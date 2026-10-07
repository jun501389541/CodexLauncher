namespace CodexLauncher.Core;

/// <summary>A launch route is evidence for one verified desktop process identity.</summary>
public sealed record LaunchRouteEvidence(RouteKind Route, ProxyAddress? Proxy, int ProcessId, DateTimeOffset StartedAt);

public sealed class LaunchRouteTracker
{
    private LaunchRouteEvidence? _evidence;

    public void Record(CodexInstance instance, RouteKind route, ProxyAddress? proxy)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!instance.HasVerifiedIdentity) throw new ArgumentException("A verified Codex instance is required.", nameof(instance));
        if (route == RouteKind.Proxy && proxy is null) throw new ArgumentException("A proxy route requires its proxy address.", nameof(proxy));
        _evidence = new LaunchRouteEvidence(route, route == RouteKind.Proxy ? proxy : null, instance.ProcessId, instance.StartedAt);
    }

    public LaunchRouteEvidence? For(CodexInstance? instance)
    {
        if (instance is null || !instance.HasVerifiedIdentity ||
            _evidence is not { } evidence || evidence.ProcessId != instance.ProcessId || evidence.StartedAt != instance.StartedAt)
        {
            _evidence = null;
            return null;
        }
        return evidence;
    }

    public void Clear() => _evidence = null;
}
