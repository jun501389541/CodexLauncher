using CodexLauncher.App;
using CodexLauncher.Core;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class BridgeStageThreeTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
    [
        ("pairing consumes an invitation once and delivers one token until acknowledged", () => Lifecycle().GetAwaiter().GetResult()),
        ("concurrent scans create exactly one pending pairing", () => ConcurrentScan().GetAwaiter().GetResult()),
        ("pair approval rejection expiry and per-source throttling are enforced", () => ExpiryAndThrottle().GetAwaiter().GetResult()),
        ("device capacity is twenty and revocation immediately frees access", () => Capacity().GetAwaiter().GetResult()),
        ("device hashes and account grants survive restart but account changes revoke", () => PersistenceAndSwitch().GetAwaiter().GetResult()),
        ("malformed device records reset to an empty fail-closed store", () => CorruptDeviceStore().GetAwaiter().GetResult()),
        ("Kestrel pairing routes enforce approval, replay, and invitation expiry", () => HttpPairing().GetAwaiter().GetResult())
    ];

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static BridgeInvitation Invite(Fixture f) => f.Pairing.CreateInvitation().Value ?? throw new Exception("invitation unavailable");
    private sealed class Source : IQuotaSessionFactory, IQuotaSession
    {
        internal string Account = "one";
        public event Action<string>? Notification { add { } remove { } }
        public Task<IQuotaSession> OpenAsync(CancellationToken token) => Task.FromResult<IQuotaSession>(this);
        public Task<string?> InvokeAsync(string method, string? parameters, CancellationToken token) => Task.FromResult<string?>(method == "account/read"
            ? JsonSerializer.Serialize(new { account = new { type = "chatgpt", id = Account } })
            : """{"rateLimits":{"primary":{"usedPercent":22}}}""");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "bridge-pair-test-" + Guid.NewGuid());
        internal string BridgeId = Guid.NewGuid().ToString("D");
        internal byte[] MappingKey = RandomNumberGenerator.GetBytes(32);
        internal DateTimeOffset Now = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
        internal Source Source = new();
        internal SharedQuotaCoordinator Quota { get; }
        internal BridgeDeviceStore Devices { get; set; }
        internal BridgeAccountGrantStore Grants { get; set; }
        internal BridgePairingService Pairing { get; set; }
        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Quota = new SharedQuotaCoordinator(Source, clock: () => Now);
            Quota.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Devices = new BridgeDeviceStore(BridgeId, Path.Combine(Root, "devices.dat"));
            Grants = NewGrants();
            Pairing = NewPairing();
        }
        internal BridgeAccountGrantStore NewGrants() => new(BridgeId, MappingKey, Quota, new BridgeAccountGrantRepository(BridgeId, Path.Combine(Root, "grants.dat")));
        internal BridgePairingService NewPairing() => new(BridgeId, "A".PadLeft(64, 'A'), new Uri("https://192.0.2.2:43189"), Devices, Grants, () => Now);
        internal void RestartStores()
        {
            Pairing.Dispose(); Grants.Dispose(); Devices.Dispose();
            Devices = new BridgeDeviceStore(BridgeId, Path.Combine(Root, "devices.dat"));
            Grants = NewGrants(); Pairing = NewPairing();
        }
        public async ValueTask DisposeAsync()
        {
            Pairing.Dispose(); Grants.Dispose(); Devices.Dispose(); CryptographicOperations.ZeroMemory(MappingKey);
            await Quota.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private static async Task Lifecycle()
    {
        await using var f = new Fixture();
        var invitation = Invite(f);
        Check(invitation.ExpiresAt == f.Now.AddMinutes(2) && invitation.Endpoint == "https://192.0.2.2:43189", "invitation lifetime or endpoint invalid");
        Check(invitation.CertificateSha256.Length == 64 && invitation.PairToken.Length == 43, "invitation did not carry cert pin/256-bit code");
        var accepted = f.Pairing.RequestPair(new BridgePairRequest(invitation.PairToken, "Test Phone"), IPAddress.Parse("192.168.1.22"));
        Check(accepted.StatusCode == 202 && accepted.Value?.State == "PendingApproval", $"scan did not enter approval state ({accepted.StatusCode}/{accepted.ErrorCode})");
        Check(f.Devices.Authenticate(RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_", 43)) is null, "device appeared before approval");
        Check(f.Pairing.RequestPair(new BridgePairRequest(invitation.PairToken, "Replay"), IPAddress.Parse("192.168.1.23")).StatusCode == 409, "invitation replay accepted");
        Check(f.Pairing.GetStatus(accepted.Value!.PairId, "wrong") .StatusCode == 401, "invalid pair session accepted");
        Check(f.Pairing.Pending().Single().DeviceName == "Test Phone", "pending device name missing from local queue");
        Check(f.Pairing.Approve(accepted.Value.PairId), "computer approval failed");
        var approved1 = f.Pairing.GetStatus(accepted.Value.PairId, accepted.Value.SessionToken);
        var approved2 = f.Pairing.GetStatus(accepted.Value.PairId, accepted.Value.SessionToken);
        Check(approved1.Value?.State == "Approved" && approved1.Value.DeviceToken == approved2.Value?.DeviceToken && approved1.Value.DeviceToken?.Length == 43, "delivery retry changed/omitted device token");
        var token = approved1.Value!.DeviceToken!;
        var deviceId = approved1.Value.DeviceId!;
        Check(f.Devices.Authenticate(token) == deviceId, "approved bearer did not authenticate");
        var clear = SecureBridgeFiles.Read(Path.Combine(f.Root, "devices.dat"));
        try { Check(!Encoding.UTF8.GetString(clear).Contains(token, StringComparison.Ordinal), "cleartext token stored on disk"); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        Check(f.Grants.Accounts(deviceId).Value?.Count == 1, "explicit computer approval did not grant current account");
        Check(f.Pairing.Acknowledge(accepted.Value.PairId, accepted.Value.SessionToken).StatusCode == 204, "delivery ack failed");
        Check(f.Pairing.Acknowledge(accepted.Value.PairId, accepted.Value.SessionToken).StatusCode == 204, "ack retry is not idempotent");
        var delivered = f.Pairing.GetStatus(accepted.Value.PairId, accepted.Value.SessionToken);
        Check(delivered.Value?.State == "Delivered" && delivered.Value.DeviceToken is null, "delivered status repeated cleartext token");
        Check(f.Pairing.RevokeDevice(deviceId) && f.Devices.Authenticate(token) is null, "revocation did not immediately invalidate token");
        Check(f.Grants.Accounts(deviceId).Error?.ErrorCode == "ACCOUNT_REAUTHORIZATION_REQUIRED", "revoked device retained account grant");
    }

    private static async Task ConcurrentScan()
    {
        await using var f = new Fixture(); var invitation = Invite(f);
        var results = await Task.WhenAll(Enumerable.Range(1, 12).Select(i => Task.Run(() => f.Pairing.RequestPair(
            new BridgePairRequest(invitation.PairToken, "Phone " + i), IPAddress.Parse("192.168.2." + i)))));
        Check(results.Count(r => r.StatusCode == 202) == 1, "invitation acceptance statuses: " + string.Join(",", results.Select(r => $"{r.StatusCode}:{r.ErrorCode}")));
        Check(results.Count(r => r.StatusCode == 409) == 11, "concurrent replay did not fail closed");
        Check(f.Pairing.Pending().Count == 1, "concurrent replay created multiple pending devices");
    }

    private static async Task ExpiryAndThrottle()
    {
        await using var f = new Fixture(); var invite = Invite(f);
        f.Now = f.Now.AddMinutes(2).AddSeconds(1);
        Check(f.Pairing.RequestPair(new BridgePairRequest(invite.PairToken, "Late"), IPAddress.Loopback).StatusCode == 410, "expired invitation accepted");
        f.Now = f.Now.AddMinutes(1).AddSeconds(1);
        f.Now = DateTimeOffset.Parse("2026-10-02T01:00:00Z");
        for (var i = 0; i < 5; i++) Check(f.Pairing.RequestPair(new BridgePairRequest(RandomToken(), "Phone"), IPAddress.Loopback).StatusCode == 409, "invalid code status wrong");
        var limited = f.Pairing.RequestPair(new BridgePairRequest(RandomToken(), "Phone"), IPAddress.Loopback);
        Check(limited.StatusCode == 429 && limited.RetryAfter is > 0, "pair source limit/Retry-After missing");
        f.Now = f.Now.AddMinutes(1).AddSeconds(1);
        invite = Invite(f);
        var pending = f.Pairing.RequestPair(new BridgePairRequest(invite.PairToken, "Unclaimed"), IPAddress.Parse("192.168.1.24"));
        Check(pending.Value is not null && f.Pairing.Approve(pending.Value.PairId), "pending pairing approval failed");
        var status = f.Pairing.GetStatus(pending.Value!.PairId, pending.Value.SessionToken);
        var deviceId = status.Value!.DeviceId!; var token = status.Value.DeviceToken!;
        f.Now = f.Now.AddMinutes(2).AddSeconds(1);
        await Task.Delay(1200);
        Check(f.Devices.Authenticate(token) is null, "delivery timeout did not asynchronously revoke the bearer");
        Check(f.Pairing.GetStatus(pending.Value.PairId, pending.Value.SessionToken).StatusCode == 410, "unacknowledged token did not expire");
        Check(f.Devices.Authenticate(token) is null && f.Devices.List().All(d => d.Id != deviceId), "unclaimed approved device was not revoked");

        var rejectInvite = Invite(f);
        var rejected = f.Pairing.RequestPair(new BridgePairRequest(rejectInvite.PairToken, "Rejected Phone"), IPAddress.Parse("192.168.1.26"));
        Check(rejected.Value is not null && f.Pairing.Reject(rejected.Value.PairId), "local rejection failed");
        Check(f.Pairing.GetStatus(rejected.Value!.PairId, rejected.Value.SessionToken).Value?.State == "Rejected", "rejection was not visible to the device");
        Check(!f.Pairing.Approve(rejected.Value.PairId) && f.Pairing.Acknowledge(rejected.Value.PairId, rejected.Value.SessionToken).StatusCode == 409,
            "rejected pairing could still be approved or acknowledged");
    }

    private static async Task Capacity()
    {
        await using var f = new Fixture();
        for (var i = 0; i < 20; i++)
        {
            f.Now = f.Now.AddMinutes(3);
            var invite = Invite(f);
            var accepted = f.Pairing.RequestPair(new BridgePairRequest(invite.PairToken, "Phone " + i), IPAddress.Parse("10.1.0." + (i + 1)));
            var value = accepted.Value;
            Check(value is not null && f.Pairing.Approve(value.PairId), $"device {i} approval failed");
            Check(f.Pairing.Acknowledge(value!.PairId, value.SessionToken).StatusCode == 204, "delivery ack failed");
        }
        Check(f.Devices.List().Count == 20, "device cap count mismatch");
        Check(f.Pairing.CreateInvitation().ErrorCode == "DEVICE_LIMIT_REACHED", "21st pairing was not refused");
        var first = f.Devices.List().First();
        Check(f.Pairing.RevokeDevice(first.Id), "device revoke failed");
        Check(f.Pairing.CreateInvitation().Value is not null, "revocation did not free a slot");
    }

    private static async Task PersistenceAndSwitch()
    {
        await using var f = new Fixture(); var invite = Invite(f);
        var accepted = f.Pairing.RequestPair(new BridgePairRequest(invite.PairToken, "Persistent"), IPAddress.Parse("192.168.1.25"));
        Check(accepted.Value is not null && f.Pairing.Approve(accepted.Value.PairId), "device approval failed");
        var delivery = f.Pairing.GetStatus(accepted.Value!.PairId, accepted.Value.SessionToken).Value!;
        var token = delivery.DeviceToken!; var id = delivery.DeviceId!;
        f.Pairing.Acknowledge(accepted.Value.PairId, accepted.Value.SessionToken);
        f.Grants.RenameCurrentAccount("Synthetic nickname");
        f.RestartStores();
        Check(f.Devices.Authenticate(token) == id, "device hash did not persist across restart");
        Check(f.Grants.Accounts(id).Value?.Count == 1, "same-account grant did not persist across restart");
        Check(f.Grants.Accounts(id).Value?.Single().DisplayName=="Synthetic nickname","account nickname lost across restart");
        f.Source.Account = "two"; await f.Quota.ReadAsync(CancellationToken.None);
        Check(f.Grants.Accounts(id).Error?.ErrorCode == "ACCOUNT_REAUTHORIZATION_REQUIRED", "account switch retained authorization");
        f.Source.Account = "one"; await f.Quota.ReadAsync(CancellationToken.None);
        Check(f.Grants.Accounts(id).Error?.ErrorCode == "ACCOUNT_REAUTHORIZATION_REQUIRED", "switching back silently restored authorization");
        Check(f.Grants.ConfirmCurrentAccount(id) && f.Grants.Accounts(id).Value?.Count == 1, "local account reauthorization failed");

        var pendingInvite = Invite(f);
        var pending = f.Pairing.RequestPair(new BridgePairRequest(pendingInvite.PairToken, "Restart before ACK"), IPAddress.Parse("192.168.1.27"));
        Check(pending.Value is not null && f.Pairing.Approve(pending.Value.PairId), "pre-restart unacknowledged pairing failed");
        var pendingToken = f.Pairing.GetStatus(pending.Value!.PairId, pending.Value.SessionToken).Value?.DeviceToken
            ?? throw new Exception("pre-restart delivery token missing");
        var crashPath = Path.Combine(f.Root, "crash-devices.dat");
        File.Copy(Path.Combine(f.Root, "devices.dat"), crashPath);
        using (var recoveredAfterCrash = new BridgeDeviceStore(f.BridgeId, crashPath))
            Check(recoveredAfterCrash.Authenticate(pendingToken) is null && recoveredAfterCrash.List().Count == 1,
                "crash recovery restored an unfinished device delivery");
        f.RestartStores();
        Check(f.Devices.Authenticate(pendingToken) is null && f.Devices.List().Count == 1,
            "unacknowledged device survived bridge stop/restart");
    }

    private static async Task CorruptDeviceStore()
    {
        await using var f = new Fixture();
        f.Pairing.Dispose();
        f.Devices.Dispose();
        var path = Path.Combine(f.Root, "devices.dat");
        var malformed = Encoding.UTF8.GetBytes($"{{\"version\":2,\"bridgeId\":\"{f.BridgeId}\",\"devices\":[null]}}");
        try { SecureBridgeFiles.Write(path, malformed); }
        finally { CryptographicOperations.ZeroMemory(malformed); }
        f.Devices = new BridgeDeviceStore(f.BridgeId, path);
        f.Pairing = f.NewPairing();
        Check(f.Devices.List().Count == 0 && f.Devices.Authenticate(RandomToken()) is null, "malformed device record did not reset closed");
    }

    private static async Task HttpPairing()
    {
        await using var f = new Fixture(); using var identity = new BridgeIdentityStore(f.Root).LoadOrCreate();
        // Fixture bridge identity/device registry are independent of the TLS test certificate identity.
        var bridgeId = identity.BridgeId;
        f.Pairing.Dispose(); f.Grants.Dispose(); f.Devices.Dispose(); f.BridgeId = bridgeId;
        f.Devices = new BridgeDeviceStore(bridgeId, Path.Combine(f.Root, "devices.dat"));
        f.Grants = new BridgeAccountGrantStore(bridgeId, identity.MappingKey, f.Quota, new BridgeAccountGrantRepository(bridgeId, Path.Combine(f.Root, "grants.dat")));
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        f.Pairing = new BridgePairingService(bridgeId, identity.Fingerprint, new Uri($"https://127.0.0.1:{port}"), f.Devices, f.Grants, () => f.Now);
        var host = new BridgeHost(identity, f.Quota, f.Grants, f.Devices, new Loopback(), null, f.Pairing);
        await using (host)
        {
            await host.StartAsync(IPAddress.Loopback, port);
            using var handler = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString(HashAlgorithmName.SHA256) == identity.Fingerprint };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(host.Address!) };
            using var invalid = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(RandomToken(), "Phone"), BridgeJson.Options);
            Check(invalid.StatusCode == HttpStatusCode.Conflict, "invalid invitation reached pairing queue");

            var rejectedInvite = f.Pairing.CreateInvitation().Value!;
            using var rejectResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(rejectedInvite.PairToken, "Rejected over HTTP"), BridgeJson.Options);
            Check(rejectResponse.StatusCode == HttpStatusCode.Accepted, "pair request did not return 202");
            var rejected = await rejectResponse.Content.ReadFromJsonAsync<BridgePairAccepted>(BridgeJson.Options) ?? throw new Exception("pair response missing");
            Check((await client.GetAsync("/v1/providers")).StatusCode == HttpStatusCode.Unauthorized, "unauthorized device endpoint became public during pairing");
            Check(f.Pairing.Reject(rejected.PairId), "local reject hook failed");
            using var rejectedStatusRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/pair/" + rejected.PairId);
            rejectedStatusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rejected.SessionToken);
            using var rejectedStatus = await client.SendAsync(rejectedStatusRequest);
            Check(rejectedStatus.StatusCode == HttpStatusCode.OK, "pair-session request was rejected: " + rejectedStatus.StatusCode + " " + await rejectedStatus.Content.ReadAsStringAsync());
            var rejectedPayload = await rejectedStatus.Content.ReadFromJsonAsync<BridgePairStatus>(BridgeJson.Options);
            Check(rejectedPayload?.State == "Rejected" && rejectedPayload.DeviceToken is null, "rejection state/token leaked incorrectly over HTTP");

            var invitation = f.Pairing.CreateInvitation().Value!;
            Check(invitation.Endpoint == host.Address, "QR invitation endpoint did not match the active TLS listener");
            using var acceptedResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(invitation.PairToken, "Approved over HTTP"), BridgeJson.Options);
            Check(acceptedResponse.StatusCode == HttpStatusCode.Accepted, "valid invitation was not accepted");
            var accepted = await acceptedResponse.Content.ReadFromJsonAsync<BridgePairAccepted>(BridgeJson.Options) ?? throw new Exception("accepted session missing");
            using var replayResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(invitation.PairToken, "Replay over HTTP"), BridgeJson.Options);
            Check(replayResponse.StatusCode == HttpStatusCode.Conflict, "HTTP pairing route accepted an invitation replay");
            async Task<BridgePairStatus> Status()
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/pair/" + accepted.PairId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accepted.SessionToken);
                using var response = await client.SendAsync(request);
                Check(response.StatusCode == HttpStatusCode.OK, "pair-session poll failed: " + response.StatusCode);
                return await response.Content.ReadFromJsonAsync<BridgePairStatus>(BridgeJson.Options) ?? throw new Exception("pair status missing");
            }
            var pending = await Status();
            Check(pending.State == "PendingApproval" && pending.DeviceToken is null && f.Devices.List().Count == 0, "device credential appeared before local approval");
            Check(f.Pairing.Pending().Single().DeviceName == "Approved over HTTP", "local approval queue did not receive the pairing request");
            Check(f.Pairing.Approve(accepted.PairId), "local approve hook failed");
            var approved = await Status();
            Check(approved.State == "Approved" && approved.DeviceToken?.Length == 43, "approved credential was not delivered over TLS");
            using var ack = new HttpRequestMessage(HttpMethod.Post, "/v1/pair/" + accepted.PairId + "/ack");
            ack.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accepted.SessionToken);
            using var ackResponse = await client.SendAsync(ack);
            Check(ackResponse.StatusCode == HttpStatusCode.NoContent, "delivery acknowledgement failed");
            var delivered = await Status();
            Check(delivered.State == "Delivered" && delivered.DeviceToken is null, "acknowledged device token was returned again");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", approved.DeviceToken);
            using var accounts = await client.GetAsync("/v1/accounts");
            Check(accounts.StatusCode == HttpStatusCode.OK && f.Devices.List().Count == 1, "issued device bearer did not authenticate against protected API");

            var unclaimedInvite = f.Pairing.CreateInvitation().Value!;
            using var unclaimedResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(unclaimedInvite.PairToken, "Will expire"), BridgeJson.Options);
            var unclaimed = await unclaimedResponse.Content.ReadFromJsonAsync<BridgePairAccepted>(BridgeJson.Options) ?? throw new Exception("unclaimed pair session missing");
            Check(f.Pairing.Approve(unclaimed.PairId), "unclaimed credential approval failed");
            var unclaimedStatus = await StatusFor(client, unclaimed);
            var unclaimedToken = unclaimedStatus.DeviceToken ?? throw new Exception("unclaimed token was not issued");
            var expiredInvitation = f.Pairing.CreateInvitation().Value!;
            f.Now = f.Now.AddMinutes(2).AddSeconds(1);
            using var expiredInvitationResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(expiredInvitation.PairToken, "Expired over HTTP"), BridgeJson.Options);
            Check(expiredInvitationResponse.StatusCode == HttpStatusCode.Gone, "HTTP pairing route accepted an expired invitation");
            using var expiredDevice = new HttpRequestMessage(HttpMethod.Get, "/v1/providers");
            expiredDevice.Headers.Authorization = new AuthenticationHeaderValue("Bearer", unclaimedToken);
            using var expiredResponse = await client.SendAsync(expiredDevice);
            Check(expiredResponse.StatusCode == HttpStatusCode.Unauthorized && f.Devices.Authenticate(unclaimedToken) is null,
                "expired device bearer remained valid at the protected API");

            var noAckInvite = f.Pairing.CreateInvitation().Value!;
            using var noAckResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(noAckInvite.PairToken, "Authenticated without ACK"), BridgeJson.Options);
            var noAck = await noAckResponse.Content.ReadFromJsonAsync<BridgePairAccepted>(BridgeJson.Options) ?? throw new Exception("no-ACK pair session missing");
            Check(f.Pairing.Approve(noAck.PairId), "no-ACK credential approval failed");
            var noAckStatus = await StatusFor(client, noAck);
            var noAckToken = noAckStatus.DeviceToken ?? throw new Exception("no-ACK token was not issued");
            using var firstDeviceRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/providers");
            firstDeviceRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noAckToken);
            using var firstDeviceResponse = await client.SendAsync(firstDeviceRequest);
            Check(firstDeviceResponse.StatusCode == HttpStatusCode.OK, "first normal device request failed");
            var deliveredWithoutAck = await StatusFor(client, noAck);
            Check(deliveredWithoutAck.State == "Delivered" && deliveredWithoutAck.DeviceToken is null,
                "first device authentication did not clear the pending token delivery");
            f.Now = f.Now.AddMinutes(1).AddSeconds(1);
            for (var i = 0; i < 5; i++)
            {
                using var attempt = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(RandomToken(), "Invalid"), BridgeJson.Options);
                Check(attempt.StatusCode == HttpStatusCode.Conflict, "source throttle counted requests incorrectly");
            }
            using var limited = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(RandomToken(), "Limited"), BridgeJson.Options);
            Check(limited.StatusCode == HttpStatusCode.TooManyRequests && limited.Headers.RetryAfter?.Delta > TimeSpan.Zero,
                "HTTP pairing limit did not include 429 and Retry-After");
            f.Now = f.Now.AddMinutes(1).AddSeconds(1);
            var stopInvite = f.Pairing.CreateInvitation().Value!;
            using var stopResponse = await client.PostAsJsonAsync("/v1/pair", new BridgePairRequest(stopInvite.PairToken, "Stop before ACK"), BridgeJson.Options);
            var stopPair = await stopResponse.Content.ReadFromJsonAsync<BridgePairAccepted>(BridgeJson.Options) ?? throw new Exception("stop pair session missing");
            Check(f.Pairing.Approve(stopPair.PairId), "pre-stop approval failed");
            var stopToken = (await StatusFor(client, stopPair)).DeviceToken!;
            await host.DisposeAsync();
            Check(f.Devices.Authenticate(stopToken) is null && f.Pairing.Pending().Count == 0,
                "stopping the HTTPS host left unfinished pairing credentials active");
        }
    }
    private static async Task<BridgePairStatus> StatusFor(HttpClient client, BridgePairAccepted accepted)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/pair/" + accepted.PairId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accepted.SessionToken);
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.OK, "pair-session poll failed: " + response.StatusCode);
        return await response.Content.ReadFromJsonAsync<BridgePairStatus>(BridgeJson.Options) ?? throw new Exception("pair status missing");
    }
    private sealed class Loopback : IBridgeListenerPolicy { public void Validate(IPAddress address, int port) { if (!IPAddress.IsLoopback(address)) throw new InvalidOperationException(); } }
    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
