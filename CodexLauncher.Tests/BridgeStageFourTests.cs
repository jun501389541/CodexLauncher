using CodexLauncher.App;
using System.Net;
using Makaretu.Dns;
using CodexLauncher.Core;
using System.Net.Sockets;
using System.Security.Cryptography;

internal static class BridgeStageFourTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
    [
        ("LAN lifecycle invalidates old binding and follows only the selected adapter", () => Lifecycle().GetAwaiter().GetResult()),
        ("discovery failure keeps HTTPS usable and advertising follows demand", () => DiscoveryFallback().GetAwaiter().GetResult()),
        ("disabled bridge does not resume from network events and startup failures are bounded", () => Failures().GetAwaiter().GetResult()),
        ("real HTTPS rebinding preserves identity and discards old invitations", () => HttpsRebind().GetAwaiter().GetResult()),
        ("network change during listener startup cannot publish the stale binding", () => StartupRace().GetAwaiter().GetResult()),
        ("request validation rechecks a lease invalidated during network inspection", () => ValidationRace().GetAwaiter().GetResult()),
        ("HTTPS stop errors still dispose and failed disposal remains retryable", () => ShutdownFailure().GetAwaiter().GetResult()),
        ("discovery metadata and firewall guidance expose only scoped connection information", Guidance)
    ];
    private static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    private sealed class Source : IBridgeNetworkSource
    {
        internal BridgeNetworkBinding? Binding = new("selected", IPAddress.Parse("192.168.1.10"), 7, 43189);
        public bool Suspended { get; set; }
        internal bool FailResolve;
        internal Action? OnResolve;
        public event Action<bool>? Changed;
        public BridgeNetworkBinding? Resolve(string adapterId, int port, IPAddress? preferred)
        {if(FailResolve)throw new InvalidOperationException();var value=!Suspended && Binding?.AdapterId == adapterId ? Binding with { Port = port } : null;OnResolve?.Invoke();return value;}
        internal void Signal() => Changed?.Invoke(true);
        public void Dispose() { }
    }
    private sealed class Endpoint(BridgeNetworkBinding binding, BridgeBindingLease lease, List<string> operations) : IBridgeLanEndpoint
    {
        public string Address => binding.Endpoint.ToString().TrimEnd('/');
        public bool DiscoveryNeeded { get; set; } = true;
        internal bool Fail;
        internal Task? Startup;
        internal bool Stopped;
        public Task StartAsync() { operations.Add("start:" + binding.Address); if (Fail) throw new InvalidOperationException(); return Startup ?? Task.CompletedTask; }
        public ValueTask DisposeAsync() { Stopped = true; operations.Add("stop:" + binding.Address); return ValueTask.CompletedTask; }
        internal bool CanServe => lease.IsActive && !Stopped;
    }
    private sealed class Discovery : IBridgeDiscovery
    {
        internal int Starts, Stops;
        internal bool Fail;
        internal bool FailCleanup;
        public void Start(BridgeNetworkBinding binding, string bridgeId) { Starts++; if (Fail) throw new InvalidOperationException("MDNS_ISOLATION_UNVERIFIED"); }
        public void Stop() { Stops++; if(FailCleanup)throw new InvalidOperationException(); }
        public void Dispose() { if(FailCleanup)throw new InvalidOperationException(); }
    }
    private static async Task Lifecycle()
    {
        var source = new Source(); var operations = new List<string>(); var endpoints = new List<Endpoint>();
        await using var controller = new BridgeLanCoordinator("synthetic-id", source, (b,l) => { var e = new Endpoint(b,l,operations); endpoints.Add(e); return e; }, () => new Discovery());
        await controller.StartAsync("selected", 43189);
        Check(controller.Status.State == "RUNNING" && endpoints.Single().CanServe, "initial binding unavailable");
        source.Binding = source.Binding! with { Address = IPAddress.Parse("192.168.1.11") }; source.Signal();
        Check(!endpoints[0].CanServe, "network event did not immediately invalidate old requests");
        await controller.ReconcileAsync();
        Check(controller.Status.Endpoint == "https://192.168.1.11:43189" && operations.IndexOf("stop:192.168.1.10") < operations.IndexOf("start:192.168.1.11"), "new bind occurred before old stop");
        source.Binding = new("another", IPAddress.Parse("192.168.1.12"), 8, 43189); source.Signal(); await controller.ReconcileAsync();
        Check(controller.Status.State == "PAUSED" && endpoints.All(e => !e.CanServe), "missing selected adapter fell back to another NIC");
        source.Binding = new("selected", IPAddress.Parse("192.168.1.13"), 7, 43189); source.Suspended = true; source.Signal(); await controller.ReconcileAsync();
        Check(controller.Status.ErrorCode == "NETWORK_SUSPENDED", "suspend did not pause sharing");
        source.Suspended = false; source.Signal(); await controller.ReconcileAsync();
        Check(controller.Status.State == "RUNNING", "resume did not revalidate and restart");
        source.Binding = null; source.Signal(); await controller.ReconcileAsync();
        Check(controller.Status.State == "PAUSED", "unsafe/private-profile-loss source did not pause");
    }
    private static async Task DiscoveryFallback()
    {
        var source = new Source(); var endpoint = default(Endpoint); var discovery = new Discovery { Fail = true, FailCleanup = true };
        await using var c = new BridgeLanCoordinator("synthetic", source, (b,l) => endpoint = new Endpoint(b,l,[]), () => discovery);
        await c.StartAsync("selected", 43189);
        Check(c.Status.State == "RUNNING" && c.Status.DiscoveryErrorCode == "MDNS_ISOLATION_UNVERIFIED" && endpoint!.CanServe, "discovery error stopped HTTPS or hid its reason");
        discovery.FailCleanup=false;
        endpoint!.DiscoveryNeeded = false; await c.ReconcileAsync();
        Check(c.Status.DiscoveryState == "NOT_NEEDED", "advertising continued without demand");
        discovery.Fail = false; endpoint.DiscoveryNeeded = true; await c.ReconcileAsync();
        Check(c.Status.DiscoveryState == "ACTIVE", "discovery did not retry when needed");
    }
    private static async Task StartupRace()
    {
        var source=new Source();var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var endpoints=new List<Endpoint>();
        await using var c=new BridgeLanCoordinator("synthetic",source,(b,l)=>
        {
            var e=new Endpoint(b,l,[]){DiscoveryNeeded=false};endpoints.Add(e);
            if(endpoints.Count==1){e.Startup=release.Task;ready.SetResult();}return e;
        });
        var starting=c.StartAsync("selected",43189);await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
        source.Binding=source.Binding! with{Address=IPAddress.Parse("192.168.1.20")};source.Signal();release.SetResult();
        await starting;await c.ReconcileAsync();
        Check(endpoints[0].Stopped&&!endpoints[0].CanServe&&c.Status.Endpoint=="https://192.168.1.20:43189","in-flight startup retained a stale listener");
    }
    private static async Task Failures()
    {
        var source = new Source(); var operations = new List<string>(); var fail = true;var endpoints=new List<Endpoint>();
        await using var c = new BridgeLanCoordinator("synthetic", source, (b,l) => {var e=new Endpoint(b,l,operations) { Fail = fail };endpoints.Add(e);return e;}, () => new Discovery());
        await c.StartAsync("selected", 43189);
        Check(c.Status.State == "FAILED" && operations.Any(s=>s.StartsWith("stop:")), "failed start was not cleaned up");
        fail = false; await c.ReconcileAsync(); Check(c.Status.State == "RUNNING", "retry did not recover");
        source.FailResolve=true;await c.ReconcileAsync();
        Check(c.Status.State=="PAUSED"&&endpoints.All(e=>e.Stopped),"network inspection failure retained the old listener");
        source.FailResolve=false;await c.ReconcileAsync();
        await c.StopAsync(); var starts = operations.Count(s=>s.StartsWith("start:")); source.Signal(); await c.ReconcileAsync();
        Check(c.Status.State == "STOPPED" && operations.Count(s=>s.StartsWith("start:")) == starts, "network event resurrected disabled bridge");
    }
    private static async Task HttpsRebind()
    {
        var root=Path.Combine(Path.GetTempPath(),"bridge-rebind-test-"+Guid.NewGuid());
        try
        {
            using var identity=new BridgeIdentityStore(root).LoadOrCreate();
            await using var quota=new SharedQuotaCoordinator(null,monitoringEnabled:false);
            using var grants=new BridgeAccountGrantStore(identity.BridgeId,identity.MappingKey,quota);
            using var devices=new BridgeDeviceStore(identity.BridgeId,Path.Combine(root,"devices.dat"));
            using var portSocket=new TcpListener(IPAddress.Loopback,0);portSocket.Start();var port=((IPEndPoint)portSocket.LocalEndpoint).Port;portSocket.Stop();
            var source=new Source{Binding=new("selected",IPAddress.Loopback,1,port)};BridgeLanEndpoint? current=null;
            await using var c=new BridgeLanCoordinator(identity.BridgeId,source,(b,l)=>current=new BridgeLanEndpoint(identity,quota,grants,devices,source,b,l));
            await c.StartAsync("selected",port);Check(c.Status.State=="RUNNING","real initial host failed");
            var oldInvitation=current!.Pairing.CreateInvitation().Value!;
            using var handler=new HttpClientHandler{UseProxy=false,ServerCertificateCustomValidationCallback=(_,certificate,_,_)=>certificate?.GetCertHashString(HashAlgorithmName.SHA256)==identity.Fingerprint};
            using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(3)};
            var oldAddress=c.Status.Endpoint!;
            Check((await client.GetStringAsync(oldAddress+"/v1/health")).Contains(identity.BridgeId),"bridge identity missing from actual TLS listener");
            source.Binding=source.Binding! with{Address=IPAddress.Parse("127.0.0.2")};source.Signal();
            try{using var stale=await client.GetAsync(oldAddress+"/v1/health");Check(stale.StatusCode==HttpStatusCode.ServiceUnavailable,"invalidated old host still served data");}catch(HttpRequestException){}
            await c.ReconcileAsync();var newAddress=c.Status.Endpoint!;
            Check(newAddress!=oldAddress&&(await client.GetStringAsync(newAddress+"/v1/health")).Contains(identity.BridgeId),"TLS identity changed or new listener unavailable");
            var newInvitation=current!.Pairing.CreateInvitation().Value!;
            Check(newInvitation.Endpoint==newAddress&&newInvitation.CertificateSha256==identity.Fingerprint,"QR endpoint/pin did not follow stable identity");
            Check(current.Pairing.RequestPair(new(oldInvitation.PairToken,"Old QR"),IPAddress.Loopback).StatusCode==409,"old invitation survived rebinding");
            await c.StopAsync();Check((await quota.ReadAsync(CancellationToken.None)).Availability==QuotaAvailability.Unavailable,"LAN stop disposed shared quota owner");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static async Task ValidationRace()
    {
        var source=new Source();var lease=new BridgeBindingLease();var binding=source.Binding!;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release=new ManualResetEventSlim();
        source.OnResolve=()=>{entered.SetResult();if(!release.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException();};
        var policy=new BridgeLanEndpoint.SelectedPolicy(source,binding,lease);
        var validating=Task.Run(()=>{try{policy.Validate(binding.Address,binding.Port);return true;}catch(InvalidOperationException){return false;}});
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));lease.Invalidate();release.Set();
        Check(!await validating,"network validation accepted a lease invalidated while Resolve was blocked");
    }
    private static async Task ShutdownFailure()
    {
        var root=Path.Combine(Path.GetTempPath(),"bridge-shutdown-test-"+Guid.NewGuid());
        try
        {
            using var identity=new BridgeIdentityStore(root).LoadOrCreate();
            await using var quota=new SharedQuotaCoordinator(null,monitoringEnabled:false);
            using var grants=new BridgeAccountGrantStore(identity.BridgeId,identity.MappingKey,quota);
            using var devices=new BridgeDeviceStore(identity.BridgeId,Path.Combine(root,"devices.dat"));
            using var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();var port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
            var source=new Source{Binding=new("selected",IPAddress.Loopback,1,port)};var lease=new BridgeBindingLease();var stops=0;var disposes=0;
            await using var host=new BridgeHost(identity,quota,grants,devices,new BridgeLanEndpoint.SelectedPolicy(source,source.Binding,lease),
                stopHost:app=>++stops==1?Task.FromException(new IOException("synthetic stop")):app.StopAsync(),
                disposeHost:app=>++disposes==1?ValueTask.FromException(new IOException("synthetic dispose")):app.DisposeAsync());
            await host.StartAsync(IPAddress.Loopback,port);lease.Invalidate();
            try{await host.DisposeAsync();}catch(IOException){}
            Check(stops==1&&disposes==1,"stop failure skipped disposal attempt");
            await host.DisposeAsync();Check(stops==2&&disposes==2&&host.Address is null,"failed disposal was not retried");
            using var probe=new TcpListener(IPAddress.Loopback,port);probe.Start();probe.Stop();
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Guidance()
    {
        var b = new BridgeNetworkBinding("synthetic", IPAddress.Parse("192.168.1.10"), 7, 43189);
        Check(WindowsBridgeNetworkSource.IsPrivateV4(b.Address)&&!WindowsBridgeNetworkSource.IsPrivateV4(IPAddress.Loopback)&&
            !WindowsBridgeNetworkSource.IsPrivateV4(IPAddress.Parse("169.254.1.1"))&&!WindowsBridgeNetworkSource.IsPrivateV4(IPAddress.IPv6Loopback),"production candidate address scope widened");
        var profile = MakaretuBridgeDiscovery.CreateProfile(b, "12345678-1234-1234-1234-123456789abc");
        Check(profile.QualifiedServiceName.ToString().TrimEnd('.') == "_aiusage._tcp.local", "service type wrong");
        Check(profile.Resources.OfType<ARecord>().Single().Address.Equals(b.Address), "discovery leaked another address");
        var txt = profile.Resources.OfType<TXTRecord>().Single().Strings;
        Check(txt.Count == 3 && txt.Contains("schemaVersion=1") && txt.Any(s=>s.StartsWith("bridgeId=")), "metadata privacy allowlist changed");
        var command = BridgeConnectionGuidance.FirewallCommands(@"D:\Program Files\Codex\CodexLauncher.exe",43189);
        Check(command.Contains("profile=private") && command.Contains("remoteip=localsubnet") && command.Contains("localport=43189") && !command.Contains("runas"), "firewall guidance exceeded scope");
        try { BridgeConnectionGuidance.FirewallCommands("evil\"&whoami",43189); throw new Exception("unsafe command accepted"); } catch (ArgumentException) { }
        using var blocked = new MakaretuBridgeDiscovery();
        try { blocked.Start(b,"12345678-1234-1234-1234-123456789abc"); throw new Exception("unverified multicast started"); }
        catch (InvalidOperationException e) { Check(e.Message == "MDNS_ISOLATION_UNVERIFIED", "isolation gate wrong"); }
    }
}
