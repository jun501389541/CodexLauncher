using CodexLauncher.Core;
using System.Net;

namespace CodexLauncher.App;

internal sealed class BridgeLanEndpoint : IBridgeLanEndpoint
{
    private readonly BridgeHost _host;
    private readonly BridgeNetworkBinding _binding;
    private readonly BridgeDeviceStore _devices;
    internal BridgePairingService Pairing { get; }
    public string Address=>_host.Address??_binding.Endpoint.ToString().TrimEnd('/');
    public bool DiscoveryNeeded=>_devices.ActiveCount>0||Pairing.HasDiscoveryDemand;
    internal BridgeLanEndpoint(BridgeIdentity identity,SharedQuotaCoordinator quota,BridgeAccountGrantStore grants,
        BridgeDeviceStore devices,IBridgeNetworkSource network,BridgeNetworkBinding binding,BridgeBindingLease lease,DiagnosticLogger? logger=null)
    {
        _binding=binding;_devices=devices;
        Pairing=new(identity.BridgeId,identity.Fingerprint,binding.Endpoint,devices,grants);
        _host=new(identity,quota,grants,devices,new SelectedPolicy(network,binding,lease),logger,Pairing);
    }
    public Task StartAsync()=>_host.StartAsync(_binding.Address,_binding.Port);
    public async ValueTask DisposeAsync(){try{await _host.DisposeAsync();}finally{Pairing.Dispose();}}
    internal sealed class SelectedPolicy(IBridgeNetworkSource source,BridgeNetworkBinding binding,BridgeBindingLease lease):IBridgeListenerPolicy
    {
        public void Validate(IPAddress address,int port)
        {
            if(!lease.IsActive||address!=binding.Address||port!=binding.Port||source.Suspended||source.Resolve(binding.AdapterId,port,address)!=binding)
                throw new InvalidOperationException("SELECTED_ADAPTER_UNAVAILABLE");
            if(!lease.IsActive||source.Suspended)
                throw new InvalidOperationException("SELECTED_ADAPTER_UNAVAILABLE");
        }
    }
}
