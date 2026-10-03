namespace CodexLauncher.Core;

public enum RouteKind { Direct, Proxy }
public enum ProbeStatus { Reachable, HttpRejected, ProxyAuthRequired, Failed }

public sealed record ProbeResult(ProbeStatus Status, int? HttpStatus, TimeSpan Elapsed, string Detail)
{
    public bool IsReachable => Status == ProbeStatus.Reachable;
    public bool ReachedHttpServer => Status is ProbeStatus.Reachable or ProbeStatus.HttpRejected;
}

public static class NetworkDecision
{
    public static RouteKind? Choose(ProbeResult direct, ProbeResult? proxy, bool preferProxy = false)
    {
        if (preferProxy && proxy?.ReachedHttpServer == true) return RouteKind.Proxy;
        if (direct.ReachedHttpServer) return RouteKind.Direct;
        return proxy?.ReachedHttpServer == true ? RouteKind.Proxy : null;
    }

    public static ProbeStatus ClassifyHttp(int status) => status switch
    {
        >= 200 and < 400 => ProbeStatus.Reachable,
        407 => ProbeStatus.ProxyAuthRequired,
        _ => ProbeStatus.HttpRejected
    };
}
