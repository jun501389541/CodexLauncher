namespace CodexLauncher.Core;

public interface IGatewayProcessHost
{
    bool IsOwnedRunning();
    bool ControlsPorts(GatewayPorts ports);
    void Start(string binaryPath, GatewayPorts ports);
    void StopOwned();
    bool TryStopCompatibleExternal(string binaryPath, GatewayPorts ports);
    bool IsConfigCurrent(GatewayPorts ports);
    void SaveOwnedConfig(GatewayPorts ports);
}

public interface IGatewayProxyGuard
{
    bool IsGatewaySystemProxy(int entryPort);
}

public sealed class GatewayCoordinator(
    IGatewayController controller,
    IGatewayProcessHost host,
    IGatewayProxyGuard proxyGuard,
    GatewayPorts ports)
{
    public Task<GatewaySnapshot?> StatusAsync(CancellationToken cancellationToken) => controller.ReadAsync(cancellationToken);

    public async Task<GatewaySyncStatus> SyncPortsAsync(GatewayPorts desired, CancellationToken cancellationToken)
    {
        GatewayConfig.Build(desired);
        var state = await controller.ReadAsync(cancellationToken);
        if (state is null)
        {
            ports = desired;
            return GatewaySyncStatus.NotRunning;
        }
        if (!host.ControlsPorts(desired))
        {
            ports = desired;
            return GatewaySyncStatus.ExternalProcess;
        }
        if (host.IsConfigCurrent(desired))
        {
            ports = desired;
            return GatewaySyncStatus.Current;
        }
        await controller.ReloadAsync(desired, state.Selected, cancellationToken);
        host.SaveOwnedConfig(desired);
        ports = desired;
        return GatewaySyncStatus.Updated;
    }

    public async Task<GatewayStartStatus> StartAsync(string binaryPath, CancellationToken cancellationToken)
    {
        if (await controller.ReadAsync(cancellationToken) is not null) return GatewayStartStatus.AlreadyRunning;
        host.Start(binaryPath, ports);
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(250, cancellationToken);
            if (await controller.ReadAsync(cancellationToken) is not null) return GatewayStartStatus.Started;
            if (!host.IsOwnedRunning()) break;
        }
        host.StopOwned();
        return GatewayStartStatus.Failed;
    }

    public async Task<GatewaySnapshot> SwitchAsync(GatewayUpstream upstream, CancellationToken cancellationToken)
    {
        if (await controller.ReadAsync(cancellationToken) is null)
            throw new InvalidOperationException("固定入口尚未启动。请先点“启动入口”。");
        return await controller.SwitchAsync(upstream, cancellationToken);
    }

    public async Task<GatewayStopStatus> StopAsync(string binaryPath, CancellationToken cancellationToken)
    {
        if (await controller.ReadAsync(cancellationToken) is null) return GatewayStopStatus.NotRunning;
        if (proxyGuard.IsGatewaySystemProxy(ports.Entry)) return GatewayStopStatus.WindowsProxyActive;
        if (host.IsOwnedRunning())
        {
            host.StopOwned();
            return GatewayStopStatus.Stopped;
        }
        return host.TryStopCompatibleExternal(binaryPath, ports)
            ? GatewayStopStatus.Stopped : GatewayStopStatus.ExternalProcess;
    }
}
