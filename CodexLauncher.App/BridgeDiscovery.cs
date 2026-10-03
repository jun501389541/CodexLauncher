using System.Net.NetworkInformation;
using Makaretu.Dns;

namespace CodexLauncher.App;

/// <summary>Production defaults to blocked: a filter is not proof of multicast AND legacy-unicast egress isolation.</summary>
internal sealed class MakaretuBridgeDiscovery : IBridgeDiscovery
{
    private readonly Func<BridgeNetworkBinding,bool> _isolationVerified;
    private MulticastService? _mdns;
    private ServiceDiscovery? _discovery;
    internal MakaretuBridgeDiscovery(Func<BridgeNetworkBinding,bool>? isolationVerified=null)
    { _isolationVerified=isolationVerified??(_=>false); }
    internal static ServiceProfile CreateProfile(BridgeNetworkBinding binding,string bridgeId)
    {
        if(!Guid.TryParse(bridgeId,out var id))throw new ArgumentException("BRIDGE_ID_REQUIRED");
        var profile=new ServiceProfile("aiusage-"+id.ToString("N"),"_aiusage._tcp",checked((ushort)binding.Port),[binding.Address]);
        profile.AddProperty("bridgeId",id.ToString("D"));profile.AddProperty("schemaVersion","1");
        return profile;
    }
    public void Start(BridgeNetworkBinding binding,string bridgeId)
    {
        if(_mdns is not null)throw new InvalidOperationException("DISCOVERY_ALREADY_STARTED");
        // No socket is opened before this gate. No user-facing setting can bypass it.
        if(!_isolationVerified(binding))throw new InvalidOperationException("MDNS_ISOLATION_UNVERIFIED");
        new PrivateLanListenerPolicy().Validate(binding.Address,binding.Port);
        var selected=NetworkInterface.GetAllNetworkInterfaces().Single(n=>n.Id==binding.AdapterId&&n.GetIPProperties().GetIPv4Properties().Index==binding.InterfaceIndex&&
            n.GetIPProperties().UnicastAddresses.Any(a=>a.Address.Equals(binding.Address)));
        var profile=CreateProfile(binding,bridgeId);
        _mdns=new MulticastService(nics=>nics.Where(n=>n.Id==selected.Id).ToArray()){UseIpv4=true,UseIpv6=false};
        try
        {
            _discovery=new ServiceDiscovery(_mdns);
            _mdns.Start();_discovery.Advertise(profile);_discovery.Announce(profile);
        }
        catch{Stop();throw;}
    }
    public void Stop()
    {
        // Stop sockets before disposing the catalog; don't send goodbye on an invalidated network.
        _mdns?.Stop();_discovery?.Dispose();_mdns?.Dispose();_discovery=null;_mdns=null;
    }
    public void Dispose()=>Stop();
}
