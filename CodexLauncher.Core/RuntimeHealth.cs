using System.Diagnostics;

namespace CodexLauncher.Core;

/// <summary>运行期一次采样的三种结果。</summary>
public enum RuntimeSampleKind
{
    Normal,
    Slow,
    Failed
}

/// <summary>运行期线路状态，对应计划第 3 节的蓝灰、黄、绿、红、灰五种显示。</summary>
public enum RuntimeHealthState
{
    Checking,
    Confirming,
    Fluctuating,
    Stable,
    Down,
    NotRunning
}

/// <summary>状态依据的数据来源；诊断事件不可用时降级为纯线路检测。</summary>
public enum RuntimeEvidenceSource
{
    LineOnly,
    LineAndDiagnostics
}

/// <summary>诊断适配器可以归一化的网络事件类别。</summary>
public enum NetworkEventKind
{
    NetworkSuccess,
    NetworkFailure,
    Reconnect,
    Recovery
}

public sealed record NetworkDiagnosticEvent(NetworkEventKind Kind, DateTimeOffset Timestamp, string Category = "");

public sealed record RuntimeDiagnosticState(bool SourceAvailable, IReadOnlyList<NetworkDiagnosticEvent> Events)
{
    public static readonly RuntimeDiagnosticState Unavailable = new(false, Array.Empty<NetworkDiagnosticEvent>());

    public static RuntimeDiagnosticState From(params NetworkDiagnosticEvent[] events) => new(true, events);
}

/// <summary>把探测结果归类为正常、慢或失败样本。</summary>
public static class RuntimeSampleClassifier
{
    /// <summary>收到回应且低于该耗时算正常，达到即算慢样本。</summary>
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(2000);

    public static RuntimeSampleKind Classify(ProbeResult result) => result.ReachedHttpServer
        ? result.Elapsed >= SlowThreshold ? RuntimeSampleKind.Slow : RuntimeSampleKind.Normal
        : RuntimeSampleKind.Failed;
}

public sealed record RuntimeHealthInput
{
    public bool CodexRunning { get; init; } = true;
    public bool TunConflict { get; init; }
    /// <summary>当前线路是否已完成首次采样。</summary>
    public bool HasSampled { get; init; }
    /// <summary>线路刚刚发生变化，正在重新确认。</summary>
    public bool RouteChanged { get; init; }
    public int ConsecutiveNormal { get; init; }
    public int ConsecutiveFailed { get; init; }
    public RuntimeSampleKind? LastSample { get; init; }
    public TimeSpan? LastElapsed { get; init; }
    public string? LastDetail { get; init; }
    /// <summary>距最近一次有效采样的时间，用于判定采样是否过期。</summary>
    public TimeSpan? SinceLastSample { get; init; }
    public IReadOnlyList<NetworkDiagnosticEvent> Events { get; init; } = Array.Empty<NetworkDiagnosticEvent>();
    public bool DiagnosticSourceAvailable { get; init; }
    public bool RouteConfirmed { get; init; } = true;
    public string RouteLabel { get; init; } = "";
    public DateTimeOffset Now { get; init; }
}

public sealed record RuntimeHealthSnapshot(
    RuntimeHealthState State,
    string Title,
    string ShortLabel,
    string Detail,
    string Advice,
    RuntimeEvidenceSource Evidence,
    string? Note,
    bool RouteConfirmed,
    string RouteLabel,
    int ConsecutiveNormal,
    int ConsecutiveFailed,
    RuntimeSampleKind? LastSample,
    TimeSpan? Elapsed,
    TimeSpan? SinceLastSample,
    bool Stale,
    DateTimeOffset CheckedAt)
{
    /// <summary>悬浮窗使用的短文案；绿色时按计划注明“空闲 · 线路稳定”。</summary>
    public string IdleLabel => State == RuntimeHealthState.Stable ? "空闲 · 线路稳定" : ShortLabel;

    /// <summary>④状态卡元信息四行文案的唯一来源：App 层只读不拼，保证单测覆盖与真机一致。</summary>
    public string MetaText
    {
        get
        {
            var elapsed = Elapsed is null ? "—" : $"{Elapsed.Value.TotalMilliseconds:0} ms";
            var routeLine = RouteLabel.StartsWith("固定入口 · ", StringComparison.Ordinal)
                ? $"路径：固定入口·{RouteLabel["固定入口 · ".Length..]}"
                : $"路径：{RouteLabel}";
            return $"{routeLine}\n证据：{(Evidence == RuntimeEvidenceSource.LineAndDiagnostics ? "线路 + 诊断事件" : "仅线路检测")}\n耗时：{elapsed}\n更新时间：{CheckedAt:HH:mm:ss}";
        }
    }
}

/// <summary>纯函数状态机：只看输入，不读时钟、不发请求、不写状态。</summary>
public static class RuntimeHealthEvaluator
{
    public static readonly int RequiredConsecutiveNormal = 3;
    public static readonly int RequiredConsecutiveFailed = 3;
    public static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ReconnectRedThreshold = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan SampleTimeout = TimeSpan.FromSeconds(3);

    public static RuntimeHealthSnapshot Evaluate(RuntimeHealthInput input)
    {
        var evidence = input.DiagnosticSourceAvailable ? RuntimeEvidenceSource.LineAndDiagnostics : RuntimeEvidenceSource.LineOnly;
        var note = input.DiagnosticSourceAvailable ? null : "仅依据线路检测";
        var now = input.Now == default ? DateTimeOffset.Now : input.Now;

        // 诊断源失效时，无法继续确认的事件状态必须移除，只保留线路检测。
        var reconnectSince = ResolveReconnect(input.DiagnosticSourceAvailable ? input.Events : Array.Empty<NetworkDiagnosticEvent>());
        var reconnecting = reconnectSince is not null;
        var reconnectDuration = reconnectSince is { } started ? now - started : TimeSpan.Zero;
        var stale = input.HasSampled && input.SinceLastSample is { } age && age > StaleThreshold;

        RuntimeHealthSnapshot Build(RuntimeHealthState state, string title, string shortLabel, string detail, string advice) =>
            new(state, title, shortLabel, detail, advice, evidence, note, input.RouteConfirmed, input.RouteLabel,
                input.ConsecutiveNormal, input.ConsecutiveFailed, input.LastSample, input.LastElapsed,
                input.SinceLastSample, stale, now);

        if (!input.CodexRunning)
            return Build(RuntimeHealthState.NotRunning, "Codex 未启动", "未启动",
                "桌面版 Codex 没有运行，暂停线路判定。", "启动 Codex 后会自动继续监测。");

        if (input.TunConflict)
            return Build(RuntimeHealthState.Down, "连接异常，影响使用", "连接异常",
                "Party 和 Verge 的 TUN 同时开启，无法确认桌面版实际使用的线路。", "先关闭其中一个 TUN，再重新检测。");

        if (!input.HasSampled)
            return input.RouteChanged
                ? Build(RuntimeHealthState.Confirming, "正在确认线路", "波动",
                    "线路刚刚发生变化，正在重新采样。", "保持 Codex 运行，等待确认完成。")
                : Build(RuntimeHealthState.Checking, "正在检测", "波动",
                    "正在对当前线路取样。", "");

        if (stale)
            return Build(RuntimeHealthState.Fluctuating, "状态未更新", "波动",
                $"已有 {(int)input.SinceLastSample!.Value.TotalSeconds} 秒没有有效采样，显示的可能是过期结果。",
                "网络或系统恢复后会自动立即重新检测。");

        if (input.ConsecutiveFailed >= RequiredConsecutiveFailed)
            return Build(RuntimeHealthState.Down, "连接异常，影响使用", "连接异常",
                $"连续 {input.ConsecutiveFailed} 次采样失败{(input.LastDetail is { Length: > 0 } ? $"：{input.LastDetail}" : "")}。",
                "检查代理软件、端口和网络连接，然后重新检测。");

        if (reconnecting && reconnectDuration > ReconnectRedThreshold)
            return Build(RuntimeHealthState.Down, "连接异常，影响使用", "连接异常",
                $"检测到网络重连故障，已持续 {(int)reconnectDuration.TotalSeconds} 秒。",
                "检查代理软件、端口和网络连接，然后重新检测。");

        if (input.ConsecutiveNormal >= RequiredConsecutiveNormal && !reconnecting)
            return Build(RuntimeHealthState.Stable, "线路稳定", "稳定",
                $"连续 {input.ConsecutiveNormal} 次正常响应，未发现未恢复的网络故障。", "");

        if (reconnecting)
            return Build(RuntimeHealthState.Fluctuating, "网络波动", "波动",
                "检测到网络重连，正在等待恢复确认。", "如果 Codex 持续重连，可以切换线路或检查代理软件。");

        if (input.LastSample == RuntimeSampleKind.Slow)
            return Build(RuntimeHealthState.Fluctuating, "网络波动", "波动",
                $"最近一次响应耗时 {(int)(input.LastElapsed?.TotalMilliseconds ?? 0)} ms，达到 2000 ms 的波动阈值。",
                "如果 Codex 出现重连，可以切换线路或检查代理软件。");

        if (input.LastSample == RuntimeSampleKind.Failed)
            return Build(RuntimeHealthState.Fluctuating, "网络波动", "波动",
                $"最近一次采样失败{(input.LastDetail is { Length: > 0 } ? $"：{input.LastDetail}" : "")}。",
                "检查代理软件、端口和网络连接，然后重新检测。");

        if (input.RouteChanged)
            return Build(RuntimeHealthState.Confirming, "正在确认线路", "波动",
                "线路刚刚发生变化，正在重新确认。", "保持 Codex 运行，等待确认完成。");

        var remaining = Math.Max(0, RequiredConsecutiveNormal - input.ConsecutiveNormal);
        return Build(RuntimeHealthState.Confirming, "正在确认线路", "波动",
            $"已连续收到 {input.ConsecutiveNormal} 次正常响应，还需 {remaining} 次确认。",
            "保持 Codex 运行，等待确认完成。");
    }

    /// <summary>找到尚未被恢复事件抵消的最近一次重连；没有则返回 null。</summary>
    private static DateTimeOffset? ResolveReconnect(IReadOnlyList<NetworkDiagnosticEvent> events)
    {
        DateTimeOffset? since = null;
        foreach (var item in events.OrderBy(e => e.Timestamp))
        {
            if (item.Kind == NetworkEventKind.Reconnect) since = item.Timestamp;
            else if (item.Kind == NetworkEventKind.Recovery) since = null;
        }
        return since;
    }
}
