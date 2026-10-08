using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace CodexLauncher.App;

internal sealed record BridgeNetworkBinding(string AdapterId, IPAddress Address, int InterfaceIndex, int Port)
{
    internal Uri Endpoint => new($"https://{Address}:{Port}");
}
internal sealed record BridgeNetworkCheck(BridgeNetworkBinding? Binding,string? ErrorCode);
internal interface IBridgeNetworkSource : IDisposable
{
    event Action<bool>? Changed;
    bool Suspended { get; }
    BridgeNetworkBinding? Resolve(string adapterId, int port, IPAddress? preferred);
}
internal sealed class WindowsBridgeNetworkSource : IBridgeNetworkSource
{
    private readonly PrivateLanListenerPolicy _policy = new();
    private volatile bool _suspended;
    public event Action<bool>? Changed;
    public bool Suspended => _suspended;
    internal WindowsBridgeNetworkSource()
    {
        NetworkChange.NetworkAddressChanged += AddressChanged;
        NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
    }
    public BridgeNetworkBinding? Resolve(string adapterId, int port, IPAddress? preferred)
        =>Inspect(adapterId,port,preferred).Binding;

    internal BridgeNetworkCheck Inspect(string adapterId,int port,IPAddress? preferred)
    {
        if(_suspended)return new(null,"NETWORK_SUSPENDED");
        if(port is < 1 or > 65535)return new(null,"INVALID_PORT");
        try
        {
            var nic=NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n=>n.Id==adapterId);
            if(nic is null)return new(null,"NETWORK_ADAPTER_NOT_FOUND");
            if(nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                return new(null,"UNSUPPORTED_NETWORK_ADAPTER");
            if(nic.OperationalStatus!=OperationalStatus.Up)return new(null,"NETWORK_ADAPTER_NOT_CONNECTED");
            var properties = nic.GetIPProperties();
            var addresses = properties.UnicastAddresses.Select(a=>a.Address).Where(IsPrivateV4).OrderBy(a=>Convert.ToHexString(a.GetAddressBytes())).ToArray();
            var address = addresses.FirstOrDefault(a=>a.Equals(preferred)) ?? addresses.FirstOrDefault();
            if(address is null)return new(null,"PRIVATE_IPV4_REQUIRED");
            try{_policy.Validate(address,port);}
            catch(Exception e){return new(null,PolicyErrorCode(e));}
            return new(new(nic.Id,address,properties.GetIPv4Properties().Index,port),null);
        }
        catch(Exception e){return new(null,PolicyErrorCode(e));}
    }
    private static string PolicyErrorCode(Exception error)=>error switch
    {
        InvalidOperationException {Message:"PRIVATE_IPV4_REQUIRED"}=>"PRIVATE_IPV4_REQUIRED",
        InvalidOperationException {Message:"PHYSICAL_ADAPTER_REQUIRED"}=>"PHYSICAL_ADAPTER_REQUIRED",
        InvalidOperationException {Message:"NETWORK_PROFILE_UNAVAILABLE"}=>"NETWORK_PROFILE_UNAVAILABLE",
        InvalidOperationException {Message:"NETWORK_PROFILE_UNSUPPORTED"}=>"NETWORK_PROFILE_UNSUPPORTED",
        InvalidOperationException {Message:"NETWORK_PROFILE_CHECK_FAILED"}=>"NETWORK_PROFILE_CHECK_FAILED",
        System.ComponentModel.Win32Exception e=>$"NETWORK_HARDWARE_CHECK_FAILED_{e.NativeErrorCode}",
        UnauthorizedAccessException=>"NETWORK_POLICY_ACCESS_DENIED",
        System.Runtime.InteropServices.COMException {HResult:unchecked((int)0x80070005)}=>"NETWORK_POLICY_ACCESS_DENIED",
        _=>"NETWORK_POLICY_CHECK_FAILED"
    };
    internal static bool IsPrivateV4(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b=address.GetAddressBytes(); return b[0]==10 || b[0]==172 && b[1] is >=16 and <=31 || b[0]==192 && b[1]==168;
    }
    private void AddressChanged(object? sender, EventArgs args) => Changed?.Invoke(true);
    private void AvailabilityChanged(object? sender, NetworkAvailabilityEventArgs args) => Changed?.Invoke(true);
    private void PowerChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend) _suspended=true;
        else if (args.Mode == PowerModes.Resume) _suspended=false;
        else return;
        Changed?.Invoke(true);
    }
    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= AddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
        Changed=null;
    }
}
internal sealed class BridgeBindingLease
{
    private int _active=1;
    internal bool IsActive => Volatile.Read(ref _active)==1;
    internal void Invalidate() => Interlocked.Exchange(ref _active,0);
}
