namespace CodexLauncher.Core;

public enum AccessHealthState
{
    Checking,
    Available,
    NeedsVerification,
    Unavailable
}

public sealed record AccessHealthSnapshot(
    AccessHealthState State,
    string Title,
    string Detail,
    RouteKind? Route,
    string RouteLabel,
    string? ProxyEndpoint,
    TimeSpan? Elapsed,
    DateTimeOffset CheckedAt);

/// <summary>
/// 启动前的免费线路评估：只看安装状态、TUN 冲突和一次只读 HTTPS 探测结果，
/// 不发送任何 Codex 模型请求，也不消耗用量。
/// </summary>
public static class AccessHealthEvaluator
{
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(6);

    public static AccessHealthSnapshot Evaluate(
        bool codexInstalled,
        TunModeState tun,
        DiagnosisResult? diagnosis,
        string routeLabel,
        string? proxyEndpoint,
        DateTimeOffset checkedAt)
    {
        if (!codexInstalled)
            return Snapshot(AccessHealthState.Unavailable, "未找到 Codex", "请先安装 Windows 桌面版 Codex。",
                null, routeLabel, proxyEndpoint, null, checkedAt);

        if (tun.HasConflict)
            return Snapshot(AccessHealthState.Unavailable, "代理环境冲突", "Party 和 Verge 的 TUN 同时开启，请先关闭其中一个。",
                diagnosis?.Selected, routeLabel, proxyEndpoint, SelectedResult(diagnosis)?.Elapsed, checkedAt);

        if (diagnosis is null)
            return Snapshot(AccessHealthState.Checking, "正在检测", "正在检查系统默认网络和本地代理。",
                null, routeLabel, proxyEndpoint, null, checkedAt);

        if (diagnosis.Selected is null)
        {
            var detail = "未找到可用网络路径。系统默认网络：" + diagnosis.Direct.Detail;
            if (diagnosis.Proxy is not null) detail += "；本地代理：" + diagnosis.Proxy.Detail;
            return Snapshot(AccessHealthState.Unavailable, "当前不可用", detail,
                null, routeLabel, proxyEndpoint, null, checkedAt);
        }

        var selected = SelectedResult(diagnosis)!;

        if (selected.Elapsed >= SlowThreshold)
            return Snapshot(AccessHealthState.NeedsVerification, "网络可达，但响应较慢",
                $"当前路径可以连接，耗时 {selected.Elapsed.TotalMilliseconds:0} ms。",
                diagnosis.Selected, routeLabel, proxyEndpoint, selected.Elapsed, checkedAt);

        // 收到任意 HTTP 回应即说明线路可达；账号与模型服务由运行期监测继续观察。
        return Snapshot(AccessHealthState.Available, "线路可达，可以尝试启动",
            "基础线路可达，未验证账号与模型服务。",
            diagnosis.Selected, routeLabel, proxyEndpoint, selected.Elapsed, checkedAt);
    }

    private static ProbeResult? SelectedResult(DiagnosisResult? diagnosis) => diagnosis?.Selected switch
    {
        RouteKind.Direct => diagnosis.Direct,
        RouteKind.Proxy => diagnosis.Proxy,
        _ => null
    };

    private static AccessHealthSnapshot Snapshot(
        AccessHealthState state,
        string title,
        string detail,
        RouteKind? route,
        string routeLabel,
        string? proxyEndpoint,
        TimeSpan? elapsed,
        DateTimeOffset checkedAt) =>
        new(state, title, detail, route, routeLabel, proxyEndpoint, elapsed, checkedAt);
}
