using CodexLauncher.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.NetworkInformation;

namespace CodexLauncher.App;

internal sealed record BridgeAdapterChoice(string Id,string Name,bool CanListen,string? UnavailableReason)
{
    internal static BridgeAdapterChoice FromError(string id,string name,string? errorCode)
    {
        var reason=errorCode switch
        {
            null=>null,
            "NETWORK_ADAPTER_NOT_FOUND"=>"网卡不存在或已断开。",
            "NETWORK_ADAPTER_NOT_CONNECTED"=>"网卡未连接，请连接 Wi-Fi 或网线。",
            "UNSUPPORTED_NETWORK_ADAPTER"=>"只支持 Wi-Fi 或以太网网卡。",
            "PRIVATE_IPV4_REQUIRED"=>"没有局域网 IPv4 地址，请连接与手机同一网络的 Wi-Fi 或以太网。",
            "PHYSICAL_ADAPTER_REQUIRED"=>"这是虚拟网卡，请选择实际的 Wi-Fi 或以太网网卡。",
            "PRIVATE_NETWORK_REQUIRED"=>"Windows 将此网络设为“公用”；请将可信网络改为“专用”后刷新。",
            "NETWORK_POLICY_ACCESS_DENIED"=>"Windows 拒绝读取网卡权限信息（访问被拒绝）。",
            "NETWORK_POLICY_CHECK_FAILED"=>"无法读取 Windows 网卡安全信息，请稍后重试。",
            "NETWORK_SUSPENDED"=>"网络当前处于挂起状态。",
            "INVALID_PORT"=>"端口号无效。",
            _=>$"网卡检查失败：{errorCode}。"
        };
        return new(id,name,reason is null,reason);
    }

    public override string ToString()=>CanListen?Name:$"{Name} — {UnavailableReason}";
}
internal sealed record BridgeAdapterDescriptor(string Id,string Name,NetworkInterfaceType Type);
internal static class BridgeAdapterCatalog
{
    internal static IReadOnlyList<BridgeAdapterChoice> Build(IEnumerable<BridgeAdapterDescriptor> adapters,Func<string,string?> inspect)
        =>adapters.Where(a=>a.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Select(a=>BridgeAdapterChoice.FromError(a.Id,a.Name,inspect(a.Id)))
            .OrderBy(a=>a.Name).ToArray();
}

/// <summary>Owns bridge resources only; the caller owns the shared quota coordinator.</summary>
internal sealed class BridgeRuntime:IAsyncDisposable
{
    private sealed record Preferences(string? AdapterId);
    private readonly BridgeIdentity _identity;
    private readonly string _preferencesPath;
    private readonly BridgeLanCoordinator _lan;
    private bool _disposed;
    internal BridgeDeviceStore Devices{get;}
    internal BridgeAccountGrantStore Grants{get;}
    internal string? SelectedAdapter{get;private set;}
    internal bool IdentityWasReset=>_identity.WasReset;
    internal string Fingerprint=>_identity.Fingerprint;
    internal BridgeLanStatus Status=>_lan.Status;
    internal BridgePairingService? Pairing=>(_lan.CurrentEndpoint as BridgeLanEndpoint)?.Pairing;
    internal event Action<BridgeLanStatus>? StatusChanged;
    internal BridgeRuntime(string directory,SharedQuotaCoordinator quota,IBridgeNetworkSource? source=null,DiagnosticLogger? logger=null)
    {
        _identity=new BridgeIdentityStore(directory).LoadOrCreate();
        Devices=new(_identity.BridgeId,Path.Combine(directory,"devices.dat"));
        Grants=new(_identity.BridgeId,_identity.MappingKey,quota,new BridgeAccountGrantRepository(_identity.BridgeId,Path.Combine(directory,"grants.dat")));
        _preferencesPath=Path.Combine(directory,"preferences.dat");
        if(File.Exists(_preferencesPath))
        {
            try{var bytes=SecureBridgeFiles.Read(_preferencesPath);try{SelectedAdapter=JsonSerializer.Deserialize<Preferences>(bytes)?.AdapterId;}finally{CryptographicOperations.ZeroMemory(bytes);}}
            catch(Exception e) when(e is CryptographicException or JsonException or InvalidDataException){SelectedAdapter=null;}
        }
        var network=source??new WindowsBridgeNetworkSource();
        _lan=new(_identity.BridgeId,network,(b,l)=>new BridgeLanEndpoint(_identity,quota,Grants,Devices,network,b,l,logger));
        _lan.StatusChanged+=status=>{logger?.Write("bridge-serve",status.ErrorCode??status.DiscoveryErrorCode??status.State,null);StatusChanged?.Invoke(status);};
    }
    internal void SelectAdapter(string id)
    {
        if(string.IsNullOrWhiteSpace(id)||id.Length>256||id.Any(char.IsControl))throw new ArgumentException("ADAPTER_REQUIRED");
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new Preferences(id));
        try{SecureBridgeFiles.Write(_preferencesPath,bytes);SelectedAdapter=id;}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    internal static IReadOnlyList<BridgeAdapterChoice> AvailableAdapters()
    {
        using var source=new WindowsBridgeNetworkSource();
        var adapters=NetworkInterface.GetAllNetworkInterfaces().Select(n=>new BridgeAdapterDescriptor(n.Id,n.Name,n.NetworkInterfaceType));
        return BridgeAdapterCatalog.Build(adapters,id=>source.Inspect(id,43189,null).ErrorCode);
    }
    internal Task StartAsync(int port)=>string.IsNullOrWhiteSpace(SelectedAdapter)?Task.CompletedTask:_lan.StartAsync(SelectedAdapter,port);
    internal Task StopAsync()=>_lan.StopAsync();
    internal void RenameDevice(string id,string name){if(!Devices.Rename(id,name))throw new InvalidOperationException("DEVICE_INVALID");}
    internal void RevokeDevice(string id){Devices.Revoke(id);Grants.Revoke(id);Pairing?.RevokeDevice(id);}
    internal void RevokeAll(){Devices.RevokeAll();Grants.RevokeAll();Pairing?.RevokeAll();}
    internal bool ConfirmAccount(string id)=>Devices.List().Any(d=>d.Id==id)&&Grants.ConfirmCurrentAccount(id);
    public async ValueTask DisposeAsync()
    {
        if(_disposed)return;
        await _lan.DisposeAsync();Grants.Dispose();Devices.Dispose();_identity.Dispose();_disposed=true;
    }
}
