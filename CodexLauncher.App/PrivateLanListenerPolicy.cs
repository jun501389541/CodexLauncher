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
        if (nic is null || !IsPhysical(nic.Id) || !IsPrivate(nic.Id))
            throw new InvalidOperationException("PRIVATE_PHYSICAL_ADAPTER_REQUIRED");
    }
    private static bool IsPhysical(string id)
    {
        object? locator = null, services = null, rows = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true)!);
            services = ((dynamic)locator!).ConnectServer(".", "root\\cimv2");
            // 不将配置值插进WQL；逐项比较 Windows 适配器 GUID。
            rows = ((dynamic)services).ExecQuery("SELECT GUID, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE");
            foreach (var row in (System.Collections.IEnumerable)rows)
            {
                try { if (Guid.TryParse((string?)((dynamic)row).GUID, out var guid) && Guid.TryParse(id, out var requested) && guid == requested) return true; }
                finally { Marshal.FinalReleaseComObject(row); }
            }
            return false;
        }
        finally { Release(rows); Release(services); Release(locator); }
    }
    private static bool IsPrivate(string id)
    {
        object? manager = null, connections = null;
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), true)!);
            connections = ((dynamic)manager!).GetNetworkConnections();
            foreach (var connection in (System.Collections.IEnumerable)connections)
            {
                object? network = null;
                try
                {
                    if ((Guid)((dynamic)connection).GetAdapterId() != Guid.Parse(id)) continue;
                    network = ((dynamic)connection).GetNetwork();
                    return (int)((dynamic)network).GetCategory() == 1;
                }
                finally { Release(network); Release(connection); }
            }
            return false;
        }
        finally { Release(connections); Release(manager); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
