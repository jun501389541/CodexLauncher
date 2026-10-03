using CodexLauncher.App;
using CodexLauncher.Core;
using System.Reflection;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

internal static class BridgeStageTwoTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
    [
        ("bridge settings default disabled with nullable port", Settings),
        ("bridge identity persists and corruption explicitly resets its identity", Identity),
        ("bridge HTTPS enforces auth, cache-only reads, immediate refresh and account isolation", () => Https().GetAwaiter().GetResult()),
        ("bridge disabled monitoring serves empty status without opening an upstream session", () => Disabled().GetAwaiter().GetResult()),
        ("bridge refuses unsafe production bindings and an occupied port", () => Binding().GetAwaiter().GetResult()),
        ("bridge identity detects structurally corrupt protected payloads", CorruptPayload)
    ];
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Settings()
    {
        var settings = new LauncherSettings();
        Check(typeof(LauncherSettings).GetProperty("BridgeEnabled")?.GetValue(settings) is false, "BridgeEnabled default missing");
        Check(typeof(LauncherSettings).GetProperty("BridgePort") is not null, "nullable BridgePort missing");
    }
    private static void Identity()
    {
        var type = typeof(MainForm).Assembly.GetType("CodexLauncher.App.BridgeIdentityStore");
        Check(type is not null, "persistent bridge identity missing");
        var directory = Path.Combine(Path.GetTempPath(), "bridge-identity-test-" + Guid.NewGuid());
        try
        {
            var store = Activator.CreateInstance(type!, directory)!;
            object Load() => type!.GetMethod("LoadOrCreate")!.Invoke(store, null)!;
            string Id(object identity) => (string)identity.GetType().GetProperty("BridgeId")!.GetValue(identity)!;
            using var first = (IDisposable)Load();
            using var second = (IDisposable)Load();
            Check(Id(first) == Id(second), "identity regenerated across loads");
            var raw = File.ReadAllBytes(Path.Combine(directory, "identity.dat"));
            Check(!System.Text.Encoding.UTF8.GetString(raw).Contains(Id(first)), "identity stored plaintext");
            File.WriteAllBytes(Path.Combine(directory, "identity.dat"), [1, 2, 3]);
            using var reset = (IDisposable)Load();
            Check(Id(reset) != Id(first), "corrupted identity retained");
            Check(reset.GetType().GetProperty("WasReset")!.GetValue(reset) is true, "reset not reported");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class SyntheticDevices : IBridgeDeviceAuthenticator
    {
        public string? Authenticate(string token) => token == "synthetic-device-secret" ? "synthetic-device" : null;
    }
    private sealed class ChunkedBody : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[16 * 1024 + 1]).AsTask();
    }
    private sealed class SyntheticListener : IBridgeListenerPolicy
    {
        public void Validate(IPAddress address, int port) { Check(IPAddress.IsLoopback(address) && port >= 0, "test must use loopback port"); }
    }
    private sealed class SyntheticQuota : IQuotaSessionFactory, IQuotaSession
    {
        internal string Id = "synthetic-a";
        internal int Reads;
        internal bool Fail;
        internal string? AccountError;
        internal int Opens;
        internal TaskCompletionSource? Block;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event Action<string>? Notification { add { } remove { } }
        public Task<IQuotaSession> OpenAsync(CancellationToken token) { Opens++; return Task.FromResult<IQuotaSession>(this); }
        public async Task<string?> InvokeAsync(string method, string? parameters, CancellationToken token)
        {
            Interlocked.Increment(ref Reads);
            if (method == "account/read") return AccountError is null ? JsonSerializer.Serialize(new { account = new { type = "chatgpt", id = Id } }) : throw new QuotaSessionException(AccountError);
            if (Block is not null) { Entered.TrySetResult(); await Block.Task.WaitAsync(token); }
            if (Fail) throw new QuotaSessionException("timeout");
            return """{"rateLimits":{"primary":{"usedPercent":25}}}""";
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static async Task Https()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bridge-host-test-" + Guid.NewGuid());
        try
        {
            using var identity = new BridgeIdentityStore(directory).LoadOrCreate();
            var factory = new SyntheticQuota();
            var now = DateTimeOffset.UtcNow;
            await using var quota = new SharedQuotaCoordinator(factory, clock: () => now);
            await quota.ReadAsync(CancellationToken.None);
            using var grants = new BridgeAccountGrantStore(identity.BridgeId, identity.MappingKey, quota);
            await using var host = new BridgeHost(identity, quota, grants, new SyntheticDevices(), new SyntheticListener());
            await host.StartAsync(IPAddress.Loopback, 0);
            using var handler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual?.GetCertHashString(HashAlgorithmName.SHA256) == identity.Fingerprint };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(host.Address!), Timeout = TimeSpan.FromSeconds(3) };
            Check((await client.GetAsync("/v1/health")).StatusCode == HttpStatusCode.OK, "health unavailable");
            Check((await client.GetAsync("/v1/accounts")).StatusCode == HttpStatusCode.Unauthorized, "unauthenticated account exposed");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-device-secret");
            Check((await client.GetAsync("/v1/accounts")).StatusCode == HttpStatusCode.Forbidden, "ungranted account exposed");
            Check(grants.ConfirmCurrentAccount("synthetic-device"), "confirmation failed");
            var account = JsonDocument.Parse(await client.GetStringAsync("/v1/accounts")).RootElement[0].GetProperty("accountId").GetString();
            var path = "/v1/accounts/" + account;
            var reads = factory.Reads;
            for (var i = 0; i < 3; i++) Check((await client.GetAsync(path + "/usage")).StatusCode == HttpStatusCode.OK, "cache read failed");
            Check(factory.Reads == reads, "GET initiated upstream query");
            factory.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Check((await client.PostAsync(path + "/refresh", null)).StatusCode == HttpStatusCode.Accepted, "refresh waits for upstream");
            await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var limited = await client.PostAsync(path + "/refresh", null);
            Check(limited.StatusCode == HttpStatusCode.TooManyRequests && limited.Headers.RetryAfter is not null, "shared refresh throttle missing");
            factory.Block.TrySetResult();
            // 独立UI等待复用正在进行的刷新，不使用HTTP生命周期。
            await quota.ReadAsync(CancellationToken.None);
            factory.Block = null;
            factory.Id = "synthetic-b";
            await quota.ReadAsync(CancellationToken.None);
            Check((await client.GetAsync(path + "/usage")).StatusCode == HttpStatusCode.Conflict, "old account URL accepted");
            Check((await client.GetAsync("/v1/accounts")).StatusCode == HttpStatusCode.Forbidden, "switch automatically granted account");
            grants.ConfirmCurrentAccount("synthetic-device");
            var newAccount = JsonDocument.Parse(await client.GetStringAsync("/v1/accounts")).RootElement[0].GetProperty("accountId").GetString();
            factory.Fail = true;
            now = now.AddMinutes(11); // 现有共享调度在数据超过10分钟时标陈旧。
            await quota.ReadAsync(CancellationToken.None);
            var stale = await client.GetStringAsync("/v1/accounts/" + newAccount + "/usage");
            Check(stale.Contains("\"isStale\":true"), "failure discarded cache staleness");
            factory.AccountError = "cli-missing";
            await quota.ReadAsync(CancellationToken.None);
            Check((await client.GetStringAsync("/v1/accounts/" + newAccount + "/usage")).Contains("CLI_MISSING"), "CLI missing error not normalized");
            factory.AccountError = null;
            using var cross = new HttpRequestMessage(HttpMethod.Get, "/v1/accounts");
            cross.Headers.Add("Origin", "https://attacker.invalid");
            Check((await client.SendAsync(cross)).StatusCode == HttpStatusCode.Forbidden, "cross-origin access accepted");
            using var oversized = new StringContent(new string('x', 16 * 1024 + 1));
            Check((await client.PostAsync("/v1/accounts/" + newAccount + "/refresh", oversized)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "oversized body accepted");
            using var chunked = new ChunkedBody();
            try { Check((await client.PostAsync("/v1/accounts/" + newAccount + "/refresh", chunked)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "oversized chunked body accepted"); }
            catch (HttpRequestException exception) { throw new Exception("Chunked HTTPS: " + exception.GetBaseException().Message, exception); }
            using var h2 = new HttpRequestMessage(HttpMethod.Post, "/v1/accounts/" + newAccount + "/refresh")
            { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = new ChunkedBody() };
            Check((await client.SendAsync(h2)).StatusCode == HttpStatusCode.RequestEntityTooLarge, "unknown-length HTTP/2 body bypassed limit");
            HttpResponseMessage? last = null;
            for (var i = 0; i < 65; i++) { last?.Dispose(); last = await client.GetAsync("/v1/providers"); if (last.StatusCode == HttpStatusCode.TooManyRequests) break; }
            Check(last?.StatusCode == HttpStatusCode.TooManyRequests && last.Headers.RetryAfter is not null, "device rate limit missing");
            last?.Dispose();
            using var wrongHandler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => false };
            using var wrong = new HttpClient(wrongHandler);
            try { await wrong.GetAsync(host.Address + "/v1/health"); throw new Exception("wrong pin accepted"); } catch (HttpRequestException) { }
            await host.DisposeAsync();
            Check((await quota.ReadAsync(CancellationToken.None)).Identity.StableId == factory.Id, "bridge stopped shared owner");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static async Task Disabled()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bridge-disabled-test-" + Guid.NewGuid());
        try
        {
            using var identity = new BridgeIdentityStore(directory).LoadOrCreate();
            var factory = new SyntheticQuota();
            await using var quota = new SharedQuotaCoordinator(factory, monitoringEnabled: false);
            using var grants = new BridgeAccountGrantStore(identity.BridgeId, identity.MappingKey, quota);
            await using var host = new BridgeHost(identity, quota, grants, new SyntheticDevices(), new SyntheticListener());
            await host.StartAsync(IPAddress.Loopback, 0);
            using var handler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, actual, _, _) => actual?.GetCertHashString(HashAlgorithmName.SHA256) == identity.Fingerprint };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(host.Address!) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-device-secret");
            Check(await client.GetStringAsync("/v1/accounts") == "[]", "disabled account identity invented");
            var path = "/v1/accounts/" + new string('a', 64);
            var value = await client.GetStringAsync(path + "/usage");
            Check(value.Contains("QUOTA_MONITORING_DISABLED") && value.Contains("NO_DATA") && !value.Contains("usedPercent"), "disabled status missing");
            Check((await client.PostAsync(path + "/refresh", null)).StatusCode == HttpStatusCode.Accepted, "disabled refresh cannot return current status");
            Check(factory.Opens == 0 && factory.Reads == 0, "disabled bridge started provider");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static async Task Binding()
    {
        var policy = new PrivateLanListenerPolicy();
        foreach (var address in new[] { IPAddress.Any, IPAddress.Loopback, IPAddress.IPv6Any, IPAddress.Parse("8.8.8.8") })
        {
            try { policy.Validate(address, 43189); throw new Exception("unsafe address accepted"); } catch (InvalidOperationException) { }
        }
        var directory = Path.Combine(Path.GetTempPath(), "bridge-port-test-" + Guid.NewGuid());
        var occupied = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        try
        {
            using var identity = new BridgeIdentityStore(directory).LoadOrCreate();
            await using var quota = new SharedQuotaCoordinator(null, monitoringEnabled: false);
            using var grants = new BridgeAccountGrantStore(identity.BridgeId, identity.MappingKey, quota);
            await using var host = new BridgeHost(identity, quota, grants, new SyntheticDevices(), new SyntheticListener());
            try { await host.StartAsync(IPAddress.Loopback, ((IPEndPoint)occupied.LocalEndpoint).Port); throw new Exception("occupied port accepted"); }
            catch (IOException) { }
            Check(host.Address is null, "failed listener left active state");
        }
        finally { occupied.Stop(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static void CorruptPayload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bridge-corrupt-test-" + Guid.NewGuid());
        try
        {
            using var first = new BridgeIdentityStore(directory).LoadOrCreate();
            SecureBridgeFiles.Write(Path.Combine(directory, "identity.dat"), System.Text.Encoding.UTF8.GetBytes("{\"Version\":1,\"BridgeId\":\"" + first.BridgeId + "\",\"MappingKey\":null,\"Pfx\":null}"));
            using var reset = new BridgeIdentityStore(directory).LoadOrCreate();
            Check(reset.WasReset && reset.BridgeId != first.BridgeId, "invalid protected structure not reset");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
