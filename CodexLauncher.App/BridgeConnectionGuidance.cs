using System.Net;

namespace CodexLauncher.App;

internal static class BridgeConnectionGuidance
{
    internal static string ManualConnection(BridgeLanStatus status)=>status.Endpoint is { } endpoint
        ? $"手机与电脑连接同一局域网，使用 {endpoint}/v1/device 或重新扫描添加设备二维码；通过浏览器证书查看器核对证书指纹。发现不可用不影响该地址直连。"
        : "共享已暂停：检查选定的实体 Wi-Fi / 以太网是否在线并有局域网 IPv4 地址；公用、专用网络均支持。恢复后重新扫描二维码。手机无法连接时，检查防火墙是否允许当前网络类型的连接。";
    internal static string FirewallCommands(string executable,int port,IPAddress address,bool includeMdns=false)
    {
        if(!WindowsBridgeNetworkSource.IsPrivateV4(address)||port is <1 or >65535||!Path.IsPathFullyQualified(executable)||Path.GetExtension(executable)!=".exe"||
            executable.Any(c=>char.IsControl(c)||"\"%!?&|<>^".Contains(c)))throw new ArgumentException("SAFE_EXECUTABLE_AND_PORT_REQUIRED");
        var path=Path.GetFullPath(executable);
        var tcp=$"netsh advfirewall firewall add rule name=\"CodexLauncher-AIUsage-TCP-{port}\" dir=in action=allow program=\"{path}\" protocol=TCP localip={address} localport={port} profile=public,private remoteip=localsubnet enable=yes";
        return includeMdns?tcp+Environment.NewLine+$"netsh advfirewall firewall add rule name=\"CodexLauncher-AIUsage-MDNS\" dir=in action=allow program=\"{path}\" protocol=UDP localip={address} localport=5353 profile=public,private remoteip=localsubnet enable=yes":tcp;
    }
}
