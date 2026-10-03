using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexLauncher.Core;

public enum CodexDesktopState
{
    /// <summary>没有发现任何 Codex 桌面进程。</summary>
    NotRunning,

    /// <summary>已按安装包身份、规范化路径和启动时间确认桌面实例。</summary>
    Running,

    /// <summary>发现了同名进程，但无法确认它是否属于本机 Codex 安装。</summary>
    Unverified,

    /// <summary>读取进程信息时权限不足，既不认定正在运行，也不结束任何进程。</summary>
    PermissionDenied
}

public sealed record CodexDesktopDiscovery(CodexDesktopState State, CodexInstance? Instance, string Detail, bool HasMainWindow)
{
    /// <summary>身份已确认且存在主窗口时才允许请求关闭。</summary>
    public bool CanClose => State == CodexDesktopState.Running && HasMainWindow && Instance is not null;

    /// <summary>身份未确认时按“可能正在运行”处理，避免重复启动第二个实例。</summary>
    public bool PossiblyRunning => State != CodexDesktopState.NotRunning;
}

public enum CodexCloseOutcome
{
    Closed,
    StillRunning,
    IdentityMismatch,
    WindowUnavailable,
    Failed
}

public sealed record CodexCloseResult(CodexCloseOutcome Outcome, string Detail)
{
    public bool Exited => Outcome == CodexCloseOutcome.Closed;
}

public sealed record CodexForceCloseResult(bool Exited, int TerminatedProcesses, string Detail);

/// <summary>一次进程枚举的只读快照。<see cref="ExecutablePath"/> 为空表示身份不可读。</summary>
public sealed record DesktopProcessSnapshot(
    int ProcessId,
    int ParentProcessId,
    string Name,
    string ExecutablePath,
    DateTimeOffset StartedAt)
{
    public bool IdentityReadable => ExecutablePath.Length > 0;
}

/// <summary>进程与窗口控制的测试接缝；实现不得结束未确认身份的目标。</summary>
public interface IDesktopProcessControl
{
    IReadOnlyList<DesktopProcessSnapshot> ListByName(string processName);

    bool TryReadMainWindow(int processId, out IntPtr handle, out string title);

    bool TryCloseWindow(IntPtr handle);

    bool TryTerminate(int processId);

    bool IsAlive(int processId);
}

public interface ICodexDesktopController
{
    Task<CodexDesktopDiscovery> DiscoverAsync(CancellationToken cancellationToken);

    Task<CodexCloseResult> RequestCloseAsync(CodexInstance instance, CancellationToken cancellationToken);

    Task<bool> WaitForExitAsync(CodexInstance instance, TimeSpan timeout, CancellationToken cancellationToken);

    Task<CodexForceCloseResult> ForceCloseAsync(CodexInstance instance, CancellationToken cancellationToken);
}

/// <summary>通过安装包身份、规范化可执行文件路径和启动时间确认桌面实例。</summary>
public sealed class CodexDesktopController : ICodexDesktopController
{
    public const string DesktopProcessName = "ChatGPT";
    public const string BackendProcessName = "codex";

    /// <summary>“继续等待”一次追加的等待时间。</summary>
    public static readonly TimeSpan GracefulWait = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan TerminateWait = TimeSpan.FromSeconds(5);

    /// <summary>启动时间比较容差；用于识别进程编号复用，不用于放宽身份要求。</summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    private readonly CodexInstallation _app;
    private readonly IDesktopProcessControl _control;
    private readonly TimeSpan _gracefulWait;

    public CodexDesktopController(CodexInstallation app, IDesktopProcessControl control, TimeSpan? gracefulWait = null)
    {
        _app = app;
        _control = control;
        _gracefulWait = gracefulWait ?? GracefulWait;
    }

    public Task<CodexDesktopDiscovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Discover());
    }

    private CodexDesktopDiscovery Discover() => DiscoverFrom(_app, _control);

    /// <summary>
    /// 按安装包身份、规范化路径边界和进程启动时间确认桌面进程。
    /// 探测与关闭共用同一套身份规则，避免两处判断不一致。
    /// </summary>
    public static CodexDesktopDiscovery DiscoverFrom(CodexInstallation app, IDesktopProcessControl control)
    {
        IReadOnlyList<DesktopProcessSnapshot> desktops;
        try
        {
            desktops = control.ListByName(DesktopProcessName);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return new CodexDesktopDiscovery(
                CodexDesktopState.PermissionDenied,
                null,
                "无法读取 Codex 桌面进程信息（权限不足）；不会结束任何进程。",
                false);
        }

        var unreadable = desktops.Any(process => !process.IdentityReadable);
        var verified = desktops
            .Where(process => process.IdentityReadable && CodexInstance.IsPathWithin(process.ExecutablePath, app.InstallLocation))
            .ToArray();

        if (verified.Length == 0)
        {
            return unreadable
                ? new CodexDesktopDiscovery(
                    CodexDesktopState.Unverified,
                    null,
                    "发现同名进程，但无法读取它的可执行文件路径（权限不足）；不会结束任何进程。",
                    false)
                : new CodexDesktopDiscovery(CodexDesktopState.NotRunning, null, "未发现 Codex 桌面进程。", false);
        }

        var windowed = verified.FirstOrDefault(process => control.TryReadMainWindow(process.ProcessId, out _, out _));
        var target = windowed ?? verified.OrderBy(process => process.StartedAt).First();
        var hasWindow = control.TryReadMainWindow(target.ProcessId, out _, out _);

        var instance = new CodexInstance
        {
            ProcessId = target.ProcessId,
            StartedAt = target.StartedAt,
            ExecutablePath = target.ExecutablePath,
            PackageFamilyName = app.PackageFamilyName,
            BackendProcessIds = ListVerifiedBackends(control, target.ProcessId)
        };

        var detail = hasWindow
            ? $"已确认桌面进程 {target.ProcessId}。"
            : $"已确认桌面进程 {target.ProcessId}，但未找到主窗口；不会请求关闭。";

        return new CodexDesktopDiscovery(CodexDesktopState.Running, instance, detail, hasWindow);
    }

    private static int[] ListVerifiedBackends(IDesktopProcessControl control, int desktopProcessId)
    {
        IReadOnlyList<DesktopProcessSnapshot> backends;
        try
        {
            backends = control.ListByName(BackendProcessName);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            // 后端枚举失败只影响诊断关联，不影响桌面身份。
            return [];
        }

        return backends
            .Where(process => process.IdentityReadable
                && process.ParentProcessId == desktopProcessId
                && process.Name.Equals(BackendProcessName, StringComparison.OrdinalIgnoreCase))
            .Select(process => process.ProcessId)
            .ToArray();
    }

    /// <summary>执行结束动作前重新核对 PID、启动时间与路径，避免进程编号复用。</summary>
    private bool IsVerified(CodexInstance instance, out string reason)
    {
        if (!instance.HasVerifiedIdentity)
        {
            reason = "实例身份信息不完整，不执行结束动作。";
            return false;
        }

        IReadOnlyList<DesktopProcessSnapshot> desktops;
        try
        {
            desktops = _control.ListByName(DesktopProcessName);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            reason = "无法重新读取进程信息（权限不足），不执行结束动作。";
            return false;
        }

        var current = desktops.FirstOrDefault(process => process.ProcessId == instance.ProcessId);
        if (current is null)
        {
            reason = "进程已退出，不需要结束动作。";
            return false;
        }

        if (!current.IdentityReadable)
        {
            reason = "无法重新读取进程身份（权限不足），不执行结束动作。";
            return false;
        }

        if ((current.StartedAt - instance.StartedAt).Duration() > StartTimeTolerance)
        {
            reason = "进程编号已被复用，启动时间不一致；不执行结束动作。";
            return false;
        }

        if (!CodexInstance.IsPathWithin(current.ExecutablePath, _app.InstallLocation))
        {
            reason = "进程可执行文件不在 Codex 安装目录内；不执行结束动作。";
            return false;
        }

        reason = "";
        return true;
    }

    public async Task<CodexCloseResult> RequestCloseAsync(CodexInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsVerified(instance, out var reason))
            return new CodexCloseResult(CodexCloseOutcome.IdentityMismatch, reason);

        if (!_control.TryReadMainWindow(instance.ProcessId, out var handle, out _) || handle == IntPtr.Zero)
            return new CodexCloseResult(CodexCloseOutcome.WindowUnavailable, "未找到 Codex 主窗口，无法请求正常关闭；不会结束进程。");

        if (!_control.TryCloseWindow(handle))
            return new CodexCloseResult(CodexCloseOutcome.Failed, "发送关闭请求失败；不会结束进程。");

        if (await WaitForExitAsync(instance, _gracefulWait, cancellationToken))
            return new CodexCloseResult(CodexCloseOutcome.Closed, "Codex 已正常退出。");

        return new CodexCloseResult(
            CodexCloseOutcome.StillRunning,
            $"Codex 在 {_gracefulWait.TotalSeconds:0.#} 秒内没有退出。");
    }

    public async Task<bool> WaitForExitAsync(CodexInstance instance, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_control.IsAlive(instance.ProcessId)) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    public async Task<CodexForceCloseResult> ForceCloseAsync(CodexInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsVerified(instance, out var reason))
            return new CodexForceCloseResult(false, 0, reason);

        var targets = ListVerifiedChildren(instance);
        var terminated = 0;
        foreach (var processId in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_control.TryTerminate(processId)) terminated++;
        }

        if (_control.TryTerminate(instance.ProcessId)) terminated++;

        await WaitForExitAsync(instance, TerminateWait, cancellationToken);
        var exited = !_control.IsAlive(instance.ProcessId);
        var detail = exited
            ? $"已强制结束 {terminated} 个属于该实例的进程。"
            : "强制结束没有生效，进程仍在运行。";
        return new CodexForceCloseResult(exited, terminated, detail);
    }

    /// <summary>只返回父进程是目标实例、且身份可验证的子进程；先结束子进程再结束桌面进程。</summary>
    private List<int> ListVerifiedChildren(CodexInstance instance)
    {
        var targets = new List<int>();
        IReadOnlyList<DesktopProcessSnapshot> desktops;
        IReadOnlyList<DesktopProcessSnapshot> backends;
        try
        {
            desktops = _control.ListByName(DesktopProcessName);
            backends = _control.ListByName(BackendProcessName);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return targets;
        }

        foreach (var process in desktops)
        {
            if (!process.IdentityReadable) continue;
            if (process.ProcessId == instance.ProcessId) continue;
            if (process.ParentProcessId != instance.ProcessId) continue;
            if (!CodexInstance.IsPathWithin(process.ExecutablePath, _app.InstallLocation)) continue;
            targets.Add(process.ProcessId);
        }

        foreach (var process in backends)
        {
            if (!process.IdentityReadable) continue;
            if (process.ParentProcessId != instance.ProcessId) continue;
            if (!instance.BackendProcessIds.Contains(process.ProcessId)) continue;
            if (!process.Name.Equals(BackendProcessName, StringComparison.OrdinalIgnoreCase)) continue;
            targets.Add(process.ProcessId);
        }

        return targets;
    }
}

/// <summary>Win32 实现；不自动提权，读取失败时保持身份不可确认。</summary>
public sealed class DesktopProcessControl : IDesktopProcessControl
{
    private const uint WmClose = 0x0010;

    public IReadOnlyList<DesktopProcessSnapshot> ListByName(string processName)
    {
        var snapshots = new List<DesktopProcessSnapshot>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                var path = "";
                var startedAt = default(DateTimeOffset);
                try
                {
                    path = process.MainModule?.FileName ?? "";
                    startedAt = new DateTimeOffset(process.StartTime);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    path = "";
                }

                snapshots.Add(new DesktopProcessSnapshot(
                    process.Id,
                    ParentProcessId(process),
                    process.ProcessName,
                    path,
                    startedAt));
            }
        }

        return snapshots;
    }

    public bool TryReadMainWindow(int processId, out IntPtr handle, out string title)
    {
        handle = IntPtr.Zero;
        title = "";
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            handle = process.MainWindowHandle;
            title = process.MainWindowTitle;
            return handle != IntPtr.Zero;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public bool TryCloseWindow(IntPtr handle) =>
        handle != IntPtr.Zero && PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);

    public bool TryTerminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(2000);
            return true;
        }
        catch (ArgumentException)
        {
            return true; // 已经退出。
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    public bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return true; // 无法确认时按仍然存在处理，不误报已退出。
        }
    }

    private static int ParentProcessId(Process process)
    {
        try
        {
            var information = default(ProcessBasicInformation);
            var status = NtQueryInformationProcess(
                process.Handle,
                0,
                ref information,
                Marshal.SizeOf<ProcessBasicInformation>(),
                out _);
            return status == 0 ? information.InheritedFromUniqueProcessId.ToInt32() : 0;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
