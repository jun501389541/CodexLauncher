using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CodexLauncher.Core;

public sealed class RegistryEnvironmentStore : IEnvironmentStore, IEnvironmentNotifier
{
    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true);
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.String);
    }

    public void NotifyChanged() => SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "Environment", 0x0002, 1000, out _);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

/// <summary>
/// 依据安装包身份、规范化路径边界和进程启动时间判断桌面进程是否属于本机 Codex 安装。
/// 身份无法确认（权限不足）时按“可能正在运行”处理，避免重复启动。
/// </summary>
public sealed class CodexProcessDetector : IAppProcessDetector
{
    private readonly IDesktopProcessControl _control;

    public CodexProcessDetector() : this(new DesktopProcessControl()) { }

    public CodexProcessDetector(IDesktopProcessControl control) => _control = control;

    public CodexDesktopDiscovery Discover(CodexInstallation app) => CodexDesktopController.DiscoverFrom(app, _control);

    public bool IsRunning(CodexInstallation app) => Discover(app).PossiblyRunning;
}

public sealed class WindowsAppActivator(IAppProcessDetector detector) : IAppActivator
{
    public async Task<bool> ActivateAsync(CodexInstallation app, CancellationToken cancellationToken)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", @"shell:AppsFolder\" + app.Aumid) { UseShellExecute = true });
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (detector.IsRunning(app)) return true;
            await Task.Delay(500, cancellationToken);
        }
        return false;
    }
}
