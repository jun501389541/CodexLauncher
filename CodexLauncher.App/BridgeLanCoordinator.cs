using System.Net;
using System.Threading.Channels;

namespace CodexLauncher.App;

internal interface IBridgeLanEndpoint : IAsyncDisposable
{
    string Address { get; }
    bool DiscoveryNeeded { get; }
    Task StartAsync();
}
internal interface IBridgeDiscovery : IDisposable
{
    void Start(BridgeNetworkBinding binding,string bridgeId);
    void Stop();
}
internal sealed record BridgeLanStatus(string State,string? Endpoint=null,string? ErrorCode=null,string DiscoveryState="NOT_NEEDED",string? DiscoveryErrorCode=null);

internal sealed class BridgeLanCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan ReconcileInterval=TimeSpan.FromSeconds(2);
    private readonly string _bridgeId;
    private readonly IBridgeNetworkSource _network;
    private readonly Func<BridgeNetworkBinding,BridgeBindingLease,IBridgeLanEndpoint> _factory;
    private readonly Func<IBridgeDiscovery> _discoveryFactory;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Channel<bool> _signals=Channel.CreateBounded<bool>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
    private readonly Task _worker;
    private readonly System.Threading.Timer _timer;
    private volatile BridgeBindingLease? _lease;
    private IBridgeLanEndpoint? _endpoint;
    private IBridgeDiscovery? _discovery;
    private IBridgeDiscovery? _failedDiscovery;
    private BridgeNetworkBinding? _binding;
    private string? _adapter;
    private int _port=43189, _generation;
    private IPAddress? _preferred;
    private bool _enabled,_disposed;
    public BridgeLanStatus Status { get; private set; }=new("STOPPED");
    internal event Action<BridgeLanStatus>? StatusChanged;
    internal IBridgeLanEndpoint? CurrentEndpoint=>_lease?.IsActive==true?_endpoint:null;
    internal BridgeLanCoordinator(string bridgeId,IBridgeNetworkSource network,
        Func<BridgeNetworkBinding,BridgeBindingLease,IBridgeLanEndpoint> factory,Func<IBridgeDiscovery>? discoveryFactory=null)
    {
        _bridgeId=bridgeId;_network=network;_factory=factory;_discoveryFactory=discoveryFactory??(()=>new MakaretuBridgeDiscovery());
        network.Changed+=OnChanged;
        _worker=ProcessAsync();
        _timer=new(_=>_signals.Writer.TryWrite(false),null,System.Threading.Timeout.InfiniteTimeSpan,System.Threading.Timeout.InfiniteTimeSpan);
    }
    private void OnChanged(bool invalidate)
    {
        if(invalidate){Interlocked.Increment(ref _generation);_lease?.Invalidate();}
        _signals.Writer.TryWrite(invalidate);
    }
    private async Task ProcessAsync()
    {
        await foreach(var _ in _signals.Reader.ReadAllAsync())
        {
            try { await ReconcileAsync(); }
            catch { /* status and invalidated lease retain the failure boundary */ }
        }
    }
    internal async Task StartAsync(string adapterId,int port)
    {
        if(string.IsNullOrWhiteSpace(adapterId)||port is <1 or >65535)throw new ArgumentException("SELECTED_ADAPTER_AND_PORT_REQUIRED");
        await _gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if(_adapter!=adapterId||_port!=port)_lease?.Invalidate();
            _adapter=adapterId;_port=port;_enabled=true;SetPeriodicReconciliation(true);
            await ReconcileLocked();
        }
        finally{_gate.Release();}
    }
    internal async Task ReconcileAsync()
    {
        await _gate.WaitAsync();
        try{if(!_disposed)await ReconcileLocked();}
        finally{_gate.Release();}
    }
    private async Task ReconcileLocked()
    {
        try
        {
            var generation=Volatile.Read(ref _generation);
            BridgeNetworkBinding? next=null;
            if(_enabled&&!_network.Suspended)
            {
                try{next=_network.Resolve(_adapter!,_port,_preferred);}
                catch{ /* An unverified adapter is unavailable; close the old listener below. */ }
            }
            if(!_enabled||next is null)
            {
                await CloseEndpoint();
                Publish(new(_enabled?"PAUSED":"STOPPED",ErrorCode:_enabled?(_network.Suspended?"NETWORK_SUSPENDED":"PRIVATE_ADAPTER_UNAVAILABLE"):null));
                return;
            }
            // Reject a buggy source attempting to substitute another adapter or port.
            if(next.AdapterId!=_adapter||next.Port!=_port)throw new InvalidOperationException("BINDING_MISMATCH");
            if(_endpoint is null||_binding!=next||_lease?.IsActive!=true)
            {
                await CloseEndpoint();
                _lease=new BridgeBindingLease();_binding=next;_preferred=next.Address;
                _endpoint=_factory(next,_lease);
                try{await _endpoint.StartAsync();}
                catch{await CloseEndpoint();Publish(new("FAILED",ErrorCode:"BRIDGE_LISTEN_FAILED"));return;}
                if(generation!=Volatile.Read(ref _generation)||_network.Suspended)
                {await CloseEndpoint();Publish(new("PAUSED",ErrorCode:"NETWORK_CHANGED"));_signals.Writer.TryWrite(false);return;}
            }
            if(_failedDiscovery is not null)
            {
                if(!TryCloseDiscovery(_failedDiscovery))
                {Publish(new("RUNNING",_endpoint.Address,DiscoveryState:"BLOCKED",DiscoveryErrorCode:"DISCOVERY_CLEANUP_FAILED"));return;}
                _failedDiscovery=null;
            }
            if(!_endpoint.DiscoveryNeeded)
            {CloseDiscovery();Publish(new("RUNNING",_endpoint.Address));return;}
            if(_discovery is null)
            {
                var candidate=_discoveryFactory();
                try{candidate.Start(next,_bridgeId);_discovery=candidate;}
                catch(Exception ex)
                {
                    if(!TryCloseDiscovery(candidate))_failedDiscovery=candidate;
                    Publish(new("RUNNING",_endpoint.Address,DiscoveryState:"BLOCKED",DiscoveryErrorCode:ex.Message=="MDNS_ISOLATION_UNVERIFIED"?ex.Message:"DISCOVERY_UNAVAILABLE"));return;
                }
            }
            Publish(new("RUNNING",_endpoint.Address,DiscoveryState:"ACTIVE"));
        }
        catch
        {
            _lease?.Invalidate();
            Publish(new("FAILED",ErrorCode:"NETWORK_LIFECYCLE_FAILED"));
        }
    }
    private void CloseDiscovery()
    {
        if(_discovery is not null)
        {
            if(!TryCloseDiscovery(_discovery))_failedDiscovery=_discovery;
            _discovery=null;
        }
        if(_failedDiscovery is not null&&TryCloseDiscovery(_failedDiscovery))_failedDiscovery=null;
    }
    private static bool TryCloseDiscovery(IBridgeDiscovery discovery)
    {
        var closed=true;
        try{discovery.Stop();}catch{closed=false;}
        try{discovery.Dispose();}catch{closed=false;}
        return closed;
    }
    private async Task CloseEndpoint()
    {
        _lease?.Invalidate();CloseDiscovery();
        if(_endpoint is not null){await _endpoint.DisposeAsync();_endpoint=null;}
        _binding=null;_lease=null;
    }
    private void Publish(BridgeLanStatus status)
    {
        if(Status==status)return;
        Status=status;try{StatusChanged?.Invoke(status);}catch{}
    }
    private void SetPeriodicReconciliation(bool enabled)
    {
        var due=enabled?ReconcileInterval:System.Threading.Timeout.InfiniteTimeSpan;
        try{_timer.Change(due,due);}catch(ObjectDisposedException){}
    }
    internal async Task StopAsync()
    {
        Interlocked.Increment(ref _generation);_lease?.Invalidate();await _gate.WaitAsync();
        try{_enabled=false;SetPeriodicReconciliation(false);await CloseEndpoint();Publish(new("STOPPED"));}
        finally{_gate.Release();}
    }
    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _generation);_lease?.Invalidate();
        _network.Changed-=OnChanged;_timer.Dispose();_signals.Writer.TryComplete();
        await _gate.WaitAsync();
        try{if(_disposed)return;_enabled=false;await CloseEndpoint();_disposed=true;Publish(new("STOPPED"));}
        finally{_gate.Release();}
        await _worker;_network.Dispose();
    }
}
