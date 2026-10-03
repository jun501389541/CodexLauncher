namespace CodexLauncher.Core;

/// <summary>关闭流程当前所处的阶段。</summary>
public enum CodexCloseStage
{
    /// <summary>还没有开始关闭。</summary>
    Idle,

    /// <summary>已经有一个关闭流程在进行中；不重复发送关闭请求。</summary>
    InProgress,

    /// <summary>已请求正常关闭，等待一个周期后仍未退出，等待用户决定。</summary>
    AwaitingDecision,

    /// <summary>已确认退出。</summary>
    Closed,

    /// <summary>用户取消了后续结束动作。</summary>
    Cancelled,

    /// <summary>身份无法确认，未做任何结束动作。</summary>
    IdentityMismatch,

    /// <summary>没有可用主窗口，未做任何结束动作。</summary>
    WindowUnavailable,

    /// <summary>关闭请求失败，未做任何结束动作。</summary>
    Failed
}

/// <summary>等待超时后展示给用户的选择，顺序与界面一致。</summary>
public static class CodexCloseChoice
{
    public const string KeepWaiting = "继续等待";
    public const string Cancel = "取消";
    public const string ForceClose = "强制关闭";

    public static IReadOnlyList<string> Options { get; } = [KeepWaiting, Cancel, ForceClose];
}

public sealed record CodexCloseAttempt(CodexCloseStage Stage, string Detail)
{
    public bool AwaitingDecision => Stage == CodexCloseStage.AwaitingDecision;

    public bool Exited => Stage == CodexCloseStage.Closed;

    /// <summary>不会再有任何后续结束动作。</summary>
    public bool Finished => Stage is CodexCloseStage.Closed or CodexCloseStage.Cancelled
        or CodexCloseStage.IdentityMismatch or CodexCloseStage.WindowUnavailable or CodexCloseStage.Failed;
}

/// <summary>
/// “关闭 Codex → 等待 → 继续等待／取消／确认强退”的多轮流程。
/// 只负责编排已确认实例的关闭动作；不读取刷新闸门，因此后台采样不会阻塞关闭按钮。
/// </summary>
public sealed class CodexCloseWorkflow
{
    private const string DecisionHint = "请选择继续等待、取消或强制关闭。";

    private readonly ICodexDesktopController _controller;
    private readonly TimeSpan _gracefulWait;
    private readonly Lock _sync = new();
    private CodexInstance? _instance;
    private CodexCloseStage _stage = CodexCloseStage.Idle;
    private bool _running;

    public CodexCloseWorkflow(ICodexDesktopController controller, TimeSpan? gracefulWait = null)
    {
        _controller = controller;
        _gracefulWait = gracefulWait ?? CodexDesktopController.GracefulWait;
    }

    /// <summary>“继续等待”一次追加的等待时间。</summary>
    public TimeSpan GracefulWait => _gracefulWait;

    public bool IsBusy
    {
        get { lock (_sync) return _running; }
    }

    public CodexCloseStage Stage
    {
        get { lock (_sync) return _stage; }
    }

    /// <summary>请求正常关闭并等待一个周期。同一时间只允许一个关闭流程。</summary>
    public async Task<CodexCloseAttempt> StartAsync(CodexInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_running) return new CodexCloseAttempt(CodexCloseStage.InProgress, "已经有一个关闭流程在进行中。");
            _running = true;
            _instance = instance;
            _stage = CodexCloseStage.InProgress;
        }

        try
        {
            var result = await _controller.RequestCloseAsync(instance, cancellationToken);
            if (result.Exited) return Finish(CodexCloseStage.Closed, result.Detail);

            var stage = result.Outcome switch
            {
                CodexCloseOutcome.StillRunning => CodexCloseStage.AwaitingDecision,
                CodexCloseOutcome.IdentityMismatch => CodexCloseStage.IdentityMismatch,
                CodexCloseOutcome.WindowUnavailable => CodexCloseStage.WindowUnavailable,
                _ => CodexCloseStage.Failed
            };
            return Finish(stage, stage == CodexCloseStage.AwaitingDecision ? result.Detail + " " + DecisionHint : result.Detail);
        }
        catch (OperationCanceledException)
        {
            Finish(CodexCloseStage.Cancelled, "已取消关闭流程，不会关闭 Codex。");
            throw;
        }
    }

    /// <summary>继续等待一个周期，不重复发送关闭请求。</summary>
    public async Task<CodexCloseAttempt> KeepWaitingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CodexInstance? instance;
        lock (_sync)
        {
            if (_running) return new CodexCloseAttempt(CodexCloseStage.InProgress, "已经有一个关闭流程在进行中。");
            if (_stage != CodexCloseStage.AwaitingDecision) return new CodexCloseAttempt(_stage, DetailFor(_stage));
            instance = _instance;
            _running = true;
        }

        if (instance is null)
        {
            lock (_sync) _running = false;
            return Finish(CodexCloseStage.Failed, "尚未确认要关闭的 Codex 实例。");
        }

        try
        {
            var exited = await _controller.WaitForExitAsync(instance, _gracefulWait, cancellationToken);
            return exited
                ? Finish(CodexCloseStage.Closed, "Codex 已正常退出。")
                : Finish(CodexCloseStage.AwaitingDecision, $"Codex 仍未退出。{DecisionHint}");
        }
        catch (OperationCanceledException)
        {
            Finish(CodexCloseStage.Cancelled, "已取消关闭流程，不会关闭 Codex。");
            throw;
        }
    }

    /// <summary>取消后续结束动作，不做任何强退。</summary>
    public CodexCloseAttempt Cancel()
    {
        lock (_sync)
        {
            _stage = CodexCloseStage.Cancelled;
            _instance = null;
            return new CodexCloseAttempt(CodexCloseStage.Cancelled, "已取消后续结束动作，不会关闭 Codex。");
        }
    }

    /// <summary>仅在用户明确确认后强退；未确认时一个进程都不结束。</summary>
    public async Task<CodexForceCloseResult> ForceCloseAsync(bool confirmed, CancellationToken cancellationToken)
    {
        if (!confirmed)
            return new CodexForceCloseResult(false, 0, "强制关闭可能中断正在执行的任务，需要用户确认后才会执行。");

        cancellationToken.ThrowIfCancellationRequested();
        CodexInstance? instance;
        lock (_sync)
        {
            if (_running) return new CodexForceCloseResult(false, 0, "已经有一个关闭流程在进行中。");
            if (_stage is CodexCloseStage.Closed or CodexCloseStage.Cancelled)
                return new CodexForceCloseResult(false, 0, "关闭流程已结束，不再执行强制关闭。");
            instance = _instance;
            _running = true;
        }

        if (instance is null)
        {
            lock (_sync) _running = false;
            return new CodexForceCloseResult(false, 0, "尚未确认要关闭的 Codex 实例。");
        }

        try
        {
            var result = await _controller.ForceCloseAsync(instance, cancellationToken);
            Finish(result.Exited ? CodexCloseStage.Closed : CodexCloseStage.Failed, result.Detail);
            return result;
        }
        catch (OperationCanceledException)
        {
            Finish(CodexCloseStage.Cancelled, "已取消关闭流程，不会关闭 Codex。");
            throw;
        }
    }

    private CodexCloseAttempt Finish(CodexCloseStage stage, string detail)
    {
        lock (_sync)
        {
            _stage = stage;
            _running = false;
            if (stage is CodexCloseStage.Closed or CodexCloseStage.Cancelled) _instance = null;
            return new CodexCloseAttempt(stage, detail);
        }
    }

    private static string DetailFor(CodexCloseStage stage) => stage switch
    {
        CodexCloseStage.Closed => "Codex 已正常退出。",
        CodexCloseStage.Cancelled => "已取消后续结束动作，不会关闭 Codex。",
        CodexCloseStage.Idle => "还没有开始关闭流程。",
        _ => "当前没有等待中的关闭流程。"
    };
}

/// <summary>关闭按钮的可用性与禁用原因。</summary>
public sealed record CodexCloseButtonState(bool Enabled, string Text, string Reason);

/// <summary>
/// 关闭按钮的显示规则。只依据已确认的桌面身份和关闭进度，不读取网络采样闸门，
/// 因此后台采样进行中时按钮仍然可用。
/// </summary>
public static class CodexCloseButtonPolicy
{
    public const string Label = "关闭 Codex";

    public static CodexCloseButtonState Evaluate(CodexDesktopDiscovery? discovery, bool closeInProgress)
    {
        if (closeInProgress) return new CodexCloseButtonState(false, Label, "正在关闭 Codex，请稍候。");
        if (discovery is null) return new CodexCloseButtonState(false, Label, "尚未确认 Codex 桌面进程，暂时不能关闭。");

        return discovery.State switch
        {
            CodexDesktopState.NotRunning => new CodexCloseButtonState(false, Label, "Codex 未运行"),
            CodexDesktopState.Unverified => new CodexCloseButtonState(
                false, Label, "无法确认桌面进程是否属于本机 Codex 安装，不会结束任何进程。"),
            CodexDesktopState.PermissionDenied => new CodexCloseButtonState(
                false, Label, "读取进程信息时权限不足，不会结束任何进程。"),
            _ => discovery.CanClose
                ? new CodexCloseButtonState(true, Label, "")
                : new CodexCloseButtonState(false, Label, "已确认桌面进程，但没有找到主窗口，无法请求正常关闭。")
        };
    }
}
