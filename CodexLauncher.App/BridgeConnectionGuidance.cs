namespace CodexLauncher.App;

internal static class BridgeConnectionGuidance
{
    internal static string ManualConnection(BridgeLanStatus status)=>status.Endpoint is { } endpoint
        ? $"手机与电脑连接同一局域网，使用 {endpoint}/v1/device 或重新扫描添加设备二维码；通过浏览器证书查看器核对证书指纹。发现不可用不影响该地址直连。"
        : "共享已暂停：检查原选定网卡是否在线并属于 Private 网络。恢复后重新扫描二维码；不要通过其他网卡或全地址监听绕过。";
    internal static string FirewallCommands(string executable,int port,bool includeMdns=false)
    {
        if(port is <1 or >65535||!Path.IsPathFullyQualified(executable)||Path.GetExtension(executable)!=".exe"||
            executable.Any(c=>char.IsControl(c)||"\"%!?&|<>^".Contains(c)))throw new ArgumentException("SAFE_EXECUTABLE_AND_PORT_REQUIRED");
        var path=Path.GetFullPath(executable);
        var tcp=$"netsh advfirewall firewall add rule name=\"CodexLauncher-AIUsage-TCP-{port}\" dir=in action=allow program=\"{path}\" protocol=TCP localport={port} profile=private remoteip=localsubnet enable=yes";
        return includeMdns?tcp+Environment.NewLine+$"netsh advfirewall firewall add rule name=\"CodexLauncher-AIUsage-MDNS\" dir=in action=allow program=\"{path}\" protocol=UDP localport=5353 profile=private remoteip=localsubnet enable=yes":tcp;
    }
}
