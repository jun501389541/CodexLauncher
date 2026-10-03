using CodexLauncher.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.NetworkInformation;

namespace CodexLauncher.App;

internal sealed record BridgeAdapterChoice(string Id,string Name)
{public override string ToString()=>Name;}

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
        return NetworkInterface.GetAllNetworkInterfaces().Where(n=>source.Resolve(n.Id,43189,null) is not null)
            .Select(n=>new BridgeAdapterChoice(n.Id,n.Name)).OrderBy(n=>n.Name).ToArray();
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
