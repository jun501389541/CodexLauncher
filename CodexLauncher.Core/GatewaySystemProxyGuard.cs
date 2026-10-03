using Microsoft.Win32;

namespace CodexLauncher.Core;

public sealed class GatewaySystemProxyGuard : IGatewayProxyGuard
{
    public bool IsGatewaySystemProxy(int entryPort)
    {
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return IsEnabledForGateway(Convert.ToInt32(settings?.GetValue("ProxyEnable") ?? 0),
            settings?.GetValue("ProxyServer") as string, entryPort);
    }

    public static bool IsEnabledForGateway(int enabled, string? server, int entryPort) =>
        enabled == 1 && ProxyDiscovery.FromWindowsProxyServer(server)?.Uri.Port == entryPort;
}
