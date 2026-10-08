using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace CodexLauncher.App;

internal sealed class PrivateLanListenerPolicy : IBridgeListenerPolicy
{
    public void Validate(IPAddress address, int port)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !(bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168) ||
            port is < 1 or > 65535) throw new InvalidOperationException("PRIVATE_IPV4_REQUIRED");
        var nic = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
        if(nic is null||!IsPhysical(nic.Id))throw new InvalidOperationException("PHYSICAL_ADAPTER_REQUIRED");
        int? category;
        try { category=(int?)ReadCategory(nic.Id); }
        catch(Exception error) { throw new InvalidOperationException("NETWORK_PROFILE_CHECK_FAILED",error); }
        EnsureSupportedCategory(category);
    }
    private static bool IsPhysical(string id)
        =>BridgeAdapterHardware.IsPhysical(id);
    internal static void EnsureSupportedCategory(int? category)
    {
        if(category is null)throw new InvalidOperationException("NETWORK_PROFILE_UNAVAILABLE");
        if(category is not (0 or 1))throw new InvalidOperationException("NETWORK_PROFILE_UNSUPPORTED");
    }
    private static BridgeNetworkCategory? ReadCategory(string id)
    {
        object? manager = null, connections = null;
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), true)!);
            connections = ((dynamic)manager!).GetNetworkConnections();
            var requested=Guid.Parse(id);
            foreach (var connection in (System.Collections.IEnumerable)connections)
            {
                IBridgeNetworkProfile? network = null;
                try
                {
                    var adapter=(IBridgeNetworkConnection)connection;
                    adapter.GetAdapterId(out var adapterId);
                    if(adapterId!=requested)continue;
                    network=adapter.GetNetwork();
                    network.GetCategory(out var category);
                    return category;
                }
                finally { Release(network); Release(connection); }
            }
            return null;
        }
        finally { Release(connections); Release(manager); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

    [ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IBridgeNetworkProfile
    {
        [DispId(1)] [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        [DispId(2)] void SetName([MarshalAs(UnmanagedType.BStr)] string name);
        [DispId(3)] [return: MarshalAs(UnmanagedType.BStr)] string GetDescription();
        [DispId(4)] void SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
        [DispId(5)] Guid GetNetworkId();
        [DispId(6)] int GetDomainType();
        [DispId(7)] [return: MarshalAs(UnmanagedType.Interface)] object GetNetworkConnections();
        [DispId(8)] void GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);
        [DispId(9)] bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        [DispId(10)] bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        [DispId(11)] int GetConnectivity();
        [DispId(12)] void GetCategory(out BridgeNetworkCategory category);
        [DispId(13)] void SetCategory(BridgeNetworkCategory category);
    }

    [ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IBridgeNetworkConnection
    {
        [DispId(1)] [return: MarshalAs(UnmanagedType.Interface)] IBridgeNetworkProfile GetNetwork();
        [DispId(2)] bool IsConnectedToInternet { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        [DispId(3)] bool IsConnected { [return: MarshalAs(UnmanagedType.VariantBool)] get; }
        [DispId(4)] int GetConnectivity();
        [DispId(5)] void GetConnectionId(out Guid connectionId);
        [DispId(6)] void GetAdapterId(out Guid adapterId);
        [DispId(7)] int GetDomainType();
    }

    private enum BridgeNetworkCategory { Public=0, Private=1, DomainAuthenticated=2 }
}
