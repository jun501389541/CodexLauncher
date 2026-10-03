namespace CodexLauncher.Core;

public enum GatewayUpstream { Party, Verge }
public enum GatewayStartStatus { Started, AlreadyRunning, Failed }
public enum GatewayStopStatus { Stopped, NotRunning, ExternalProcess, WindowsProxyActive }
public enum GatewaySyncStatus { NotRunning, Current, Updated, ExternalProcess }

public sealed record GatewaySnapshot(GatewayUpstream Selected);

public sealed record GatewayPorts(int Entry, int Controller, int Party, int Verge)
{
    public static GatewayPorts Default { get; } = new(7896, 9098, 7890, 7897);
    public ProxyAddress EntryAddress => ProxyAddress.Parse($"127.0.0.1:{Entry}");
}

public static class GatewayConfig
{
    public static string Build(GatewayPorts ports)
    {
        var numbers = new[] { ports.Entry, ports.Controller, ports.Party, ports.Verge };
        if (numbers.Any(port => port is < 1 or > 65535) || numbers.Distinct().Count() != numbers.Length)
            throw new ArgumentException("固定入口和上游代理端口必须不同且在 1–65535 范围内。");
        return $"""
            mixed-port: {ports.Entry}
            allow-lan: false
            bind-address: 127.0.0.1
            mode: rule
            log-level: warning
            external-controller: 127.0.0.1:{ports.Controller}

            proxies:
              - name: Party
                type: http
                server: 127.0.0.1
                port: {ports.Party}
              - name: Verge
                type: http
                server: 127.0.0.1
                port: {ports.Verge}

            proxy-groups:
              - name: Upstream
                type: select
                default-selected: Verge
                proxies:
                  - Party
                  - Verge

            rules:
              - MATCH,Upstream
            """;
    }
}
