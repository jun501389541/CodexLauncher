using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Win32;

namespace CodexLauncher.App;

internal sealed record BridgeNetworkBinding(string AdapterId, IPAddress Address, int InterfaceIndex, int Port)
{
    internal Uri Endpoint => new($"https://{Address}:{Port}");
}
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
    {
        if (_suspended || port is < 1 or > 65535) return null;
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n => n.Id == adapterId &&
                n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211);
            if (nic is null) return null;
            var properties = nic.GetIPProperties();
            var addresses = properties.UnicastAddresses.Select(a=>a.Address).Where(IsPrivateV4).OrderBy(a=>Convert.ToHexString(a.GetAddressBytes())).ToArray();
            var address = addresses.FirstOrDefault(a=>a.Equals(preferred)) ?? addresses.FirstOrDefault();
            if (address is null) return null;
            _policy.Validate(address,port);
            return new(nic.Id,address,properties.GetIPv4Properties().Index,port);
        }
        catch { return null; } // inability to establish physical/private state never selects a fallback NIC
    }
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
