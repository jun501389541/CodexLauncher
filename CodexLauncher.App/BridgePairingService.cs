using CodexLauncher.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexLauncher.App;

internal sealed record BridgeInvitation(int SchemaVersion, string BridgeId, string Endpoint, string CertificateSha256, string PairToken, DateTimeOffset ExpiresAt);
internal sealed record BridgePairRequest(string PairToken, string DeviceName);
internal sealed record BridgePairAccepted(string PairId, string SessionToken, string State, DateTimeOffset ExpiresAt);
internal sealed record BridgePairStatus(string PairId, string State, string? DeviceId, string? DeviceToken, DateTimeOffset ExpiresAt);
internal sealed record BridgePendingPairing(string PairId, string DeviceName, string RemoteAddress, DateTimeOffset ExpiresAt);
internal sealed record BridgePairOperation<T>(int StatusCode, T? Value = default, string? ErrorCode = null, int? RetryAfter = null);

internal sealed class BridgePairingService : IDisposable
{
    private sealed class Session
    {
        public required string Id { get; init; }
        public required string SessionHash { get; init; }
        public required string DeviceName { get; init; }
        public required string RemoteAddress { get; init; }
        public required DateTimeOffset SessionExpiresAt { get; init; }
        public string State { get; set; } = "PendingApproval";
        public string? DeviceId { get; set; }
        public string? DeviceToken { get; set; }
        public DateTimeOffset? DeliveryExpiresAt { get; set; }
    }

    private readonly string _bridgeId;
    private readonly string _fingerprint;
    private readonly Uri _endpoint;
    private readonly BridgeDeviceStore _devices;
    private readonly BridgeAccountGrantStore _grants;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _sourceRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumedInvitations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expiredInvitations = new(StringComparer.Ordinal);
    private string? _invitationHash;
    private DateTimeOffset _invitationExpiresAt;
    private bool _disposed;

    internal BridgePairingService(string bridgeId, string fingerprint, Uri endpoint, BridgeDeviceStore devices,
        BridgeAccountGrantStore grants, Func<DateTimeOffset>? clock = null)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("HTTPS endpoint required", nameof(endpoint));
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)) throw new ArgumentException("SHA-256 fingerprint required", nameof(fingerprint));
        _bridgeId = bridgeId; _fingerprint = fingerprint.ToUpperInvariant(); _endpoint = endpoint;
        _devices = devices; _grants = grants; _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _cleanupTimer = new System.Threading.Timer(_ =>
        {
            try { CleanupExpired(); }
            catch { /* persistence failures fail closed on the next HTTP request */ }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
    internal event Action<BridgePendingPairing>? PairRequested;

    internal BridgePairOperation<BridgeInvitation> CreateInvitation()
    {
        lock (_sync)
        {
            ThrowIfDisposed(); Cleanup();
            if (_devices.ActiveCount >= 20) return Fail<BridgeInvitation>(409, "DEVICE_LIMIT_REACHED");
            var raw = RandomNumberGenerator.GetBytes(32);
            try
            {
                _invitationHash = Hash(raw); _invitationExpiresAt = Now.AddMinutes(2);
                return Ok(new BridgeInvitation(1, _bridgeId, _endpoint.ToString().TrimEnd('/'), _fingerprint, BridgeTokens.Encode(raw), _invitationExpiresAt));
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }
    }
    internal string DevicePageUrl(BridgeInvitation invitation) => new Uri(_endpoint, "/v1/device").AbsoluteUri + "#" +
        Uri.EscapeDataString(JsonSerializer.Serialize(invitation, BridgeJson.Options));

    internal BridgePairOperation<BridgePairAccepted> RequestPair(BridgePairRequest request, IPAddress source)
    {
        BridgePendingPairing? pending = null;
        BridgePairOperation<BridgePairAccepted> result;
        lock (_sync)
        {
            ThrowIfDisposed(); var now = Now; Cleanup(); var sourceKey = SourceKey(source);
            var retry = CountSource(sourceKey, now);
            if (retry > 0) return Fail<BridgePairAccepted>(429, "PAIR_RATE_LIMITED", retry);
            if (_devices.ActiveCount >= 20) return Fail<BridgePairAccepted>(409, "DEVICE_LIMIT_REACHED");
            if (_sessions.Values.Count(s => s.State == "PendingApproval") >= 20) return Fail<BridgePairAccepted>(409, "PAIR_CAPACITY_REACHED");
            if (request.PairToken is null || !BridgeTokens.TryDecode(request.PairToken, out var pairBytes))
                return Fail<BridgePairAccepted>(400, "PAIR_TOKEN_INVALID");
            try
            {
                var supplied = Hash(pairBytes);
                if (_invitationHash is null || !FixedHashEquals(_invitationHash, supplied))
                    return _expiredInvitations.Contains(supplied) ? Fail<BridgePairAccepted>(410, "PAIR_EXPIRED")
                        : Fail<BridgePairAccepted>(409, _consumedInvitations.Contains(supplied) ? "PAIR_TOKEN_REPLAYED" : "PAIR_TOKEN_INVALID");
                if (now >= _invitationExpiresAt)
                { _consumedInvitations.Add(supplied); _invitationHash = null; return Fail<BridgePairAccepted>(410, "PAIR_EXPIRED"); }
                try { BridgeDeviceStore.ValidateName(request.DeviceName); }
                catch (ArgumentException) { return Fail<BridgePairAccepted>(400, "DEVICE_NAME_INVALID"); }
                _consumedInvitations.Add(supplied); _invitationHash = null;
                var raw = RandomNumberGenerator.GetBytes(32);
                try
                {
                    var id = Guid.NewGuid().ToString("D"); var token = BridgeTokens.Encode(raw);
                    var session = new Session { Id = id, SessionHash = Hash(raw), DeviceName = request.DeviceName.Trim(), RemoteAddress = sourceKey,
                        SessionExpiresAt = now.AddMinutes(5) };
                    _sessions.Add(id, session); pending = new(id, session.DeviceName, sourceKey, session.SessionExpiresAt);
                    result = Ok(new BridgePairAccepted(id, token, session.State, session.SessionExpiresAt), 202);
                }
                finally { CryptographicOperations.ZeroMemory(raw); }
            }
            finally { CryptographicOperations.ZeroMemory(pairBytes); }
        }
        try { PairRequested?.Invoke(pending!); } catch { /* local UI observers do not roll back a consumed invitation */ }
        return result;
    }

    internal IReadOnlyList<BridgePendingPairing> Pending()
    {
        lock (_sync) { ThrowIfDisposed(); Cleanup(); return _sessions.Values.Where(s => s.State == "PendingApproval")
            .Select(s => new BridgePendingPairing(s.Id, s.DeviceName, s.RemoteAddress, s.SessionExpiresAt)).ToArray(); }
    }
    internal void CleanupExpired()
    {
        lock (_sync)
        {
            if (_disposed) return;
            Cleanup();
        }
    }
    internal bool Approve(string pairId)
    {
        lock (_sync)
        {
            ThrowIfDisposed(); Cleanup();
            if (!_sessions.TryGetValue(pairId, out var session) || session.State != "PendingApproval" || _devices.ActiveCount >= 20) return false;
            var device = _devices.AddApproved(session.DeviceName, out var token);
            try
            {
                _grants.ConfirmCurrentAccount(device.Id);
                session.DeviceId = device.Id; session.DeviceToken = token; session.DeliveryExpiresAt = Now.AddMinutes(2); session.State = "Approved";
                return true;
            }
            catch { _devices.Revoke(device.Id); _grants.Revoke(device.Id); throw; }
        }
    }
    internal bool Reject(string pairId)
    {
        lock (_sync)
        {
            ThrowIfDisposed(); Cleanup();
            if (!_sessions.TryGetValue(pairId, out var session) || session.State != "PendingApproval") return false;
            session.State = "Rejected"; return true;
        }
    }
    internal BridgePairOperation<BridgePairStatus> GetStatus(string pairId, string? sessionToken)
    {
        lock (_sync)
        {
            ThrowIfDisposed(); Cleanup();
            if (!_sessions.TryGetValue(pairId, out var session) || !AuthenticateSession(session, sessionToken)) return Fail<BridgePairStatus>(401, "PAIR_SESSION_INVALID");
            if (Now >= session.SessionExpiresAt || session.State == "Expired") return Fail<BridgePairStatus>(410, "PAIR_EXPIRED");
            var expires = session.State == "Approved" ? session.DeliveryExpiresAt!.Value : session.SessionExpiresAt;
            var token = session.State == "Approved" ? session.DeviceToken : null;
            return Ok(new BridgePairStatus(session.Id, session.State, session.DeviceId, token, expires));
        }
    }
    internal BridgePairOperation<bool> Acknowledge(string pairId, string? sessionToken)
    {
        lock (_sync)
        {
            ThrowIfDisposed(); Cleanup();
            if (!_sessions.TryGetValue(pairId, out var session) || !AuthenticateSession(session, sessionToken)) return Fail<bool>(401, "PAIR_SESSION_INVALID");
            if (Now >= session.SessionExpiresAt || session.State == "Expired") return Fail<bool>(410, "PAIR_EXPIRED");
            if (session.State == "Delivered") return new BridgePairOperation<bool>(204, true);
            if (session.State != "Approved" || session.DeviceToken is null) return Fail<bool>(409, "PAIR_NOT_APPROVED");
            if (Now >= session.DeliveryExpiresAt) { ExpireDelivery(session); return Fail<bool>(410, "PAIR_EXPIRED"); }
            if (session.DeviceId is null || !_devices.CompleteDelivery(session.DeviceId))
            { ExpireDelivery(session); return Fail<bool>(410, "PAIR_EXPIRED"); }
            session.DeviceToken = null; session.State = "Delivered"; return new BridgePairOperation<bool>(204, true);
        }
    }

    internal void ObserveDeviceAuthenticated(string deviceId)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (!_devices.CompleteDelivery(deviceId)) throw new InvalidOperationException("AUTHENTICATED_DEVICE_MISSING");
            foreach (var session in _sessions.Values.Where(s => s.DeviceId == deviceId && s.State == "Approved"))
            { session.DeviceToken = null; session.State = "Delivered"; }
        }
    }

    internal bool RevokeDevice(string deviceId)
    {
        lock (_sync)
        {
            ThrowIfDisposed(); var removed = _devices.Revoke(deviceId); _grants.Revoke(deviceId);
            foreach (var session in _sessions.Values.Where(s => s.DeviceId == deviceId))
            { session.DeviceToken = null; if (session.State == "Approved") session.State = "Expired"; }
            return removed;
        }
    }
    internal void RevokeAll()
    {
        lock (_sync)
        {
            ThrowIfDisposed(); _devices.RevokeAll(); _grants.RevokeAll();
            foreach (var session in _sessions.Values.Where(s => s.DeviceId is not null))
            { session.DeviceToken = null; if (session.State == "Approved") session.State = "Expired"; }
        }
    }
    internal IReadOnlyList<BridgeDeviceSummary> Devices => _devices.List();
    internal bool HasActiveInvitation
    {get{lock(_sync){if(_disposed)return false;Cleanup();return _invitationHash is not null;}}}
    internal bool HasDiscoveryDemand
    { get { lock(_sync) { if(_disposed)return false;Cleanup();return _invitationHash is not null||_sessions.Values.Any(s=>s.State is "PendingApproval" or "Approved"); } } }
    internal void CancelPending()
    {
        lock (_sync) { if (!_disposed) ClearSessions(); }
    }
    private void ClearSessions()
    {
        try { foreach (var deviceId in _devices.RevokePendingDeliveries()) _grants.Revoke(deviceId); }
        finally
        {
            foreach (var session in _sessions.Values) session.DeviceToken = null;
            _sessions.Clear(); _sourceRequests.Clear(); _consumedInvitations.Clear(); _expiredInvitations.Clear(); _invitationHash = null;
        }
    }

    private int CountSource(string key, DateTimeOffset now)
    {
        if (!_sourceRequests.TryGetValue(key, out var queue)) _sourceRequests.Add(key, queue = new Queue<DateTimeOffset>());
        while (queue.TryPeek(out var old) && now - old >= TimeSpan.FromMinutes(1)) queue.Dequeue();
        if (queue.Count >= 5) return Math.Max(1, (int)Math.Ceiling((queue.Peek().AddMinutes(1) - now).TotalSeconds));
        queue.Enqueue(now); return 0;
    }
    private void Cleanup()
    {
        var now = Now;
        if (_invitationHash is not null && now >= _invitationExpiresAt) { _expiredInvitations.Add(_invitationHash); _invitationHash = null; }
        foreach (var session in _sessions.Values.ToArray())
        {
            if (session.State == "Approved" && session.DeliveryExpiresAt is { } deliveryEnd && now >= deliveryEnd) ExpireDelivery(session);
            if (now >= session.SessionExpiresAt && session.State is "PendingApproval" or "Approved")
            {
                session.DeviceToken = null;
                if (session.DeviceId is not null) RevokeExpired(session);
                session.State = "Expired";
            }
        }
        foreach (var key in _sourceRequests.Where(p => p.Value.Count > 0 && now - p.Value.Last() >= TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray()) _sourceRequests.Remove(key);
        if (_sessions.Count > 256)
            foreach (var key in _sessions.Values.Where(s => s.State is "Expired" or "Rejected" or "Delivered").OrderBy(s => s.SessionExpiresAt).Take(_sessions.Count - 192).Select(s => s.Id).ToArray()) _sessions.Remove(key);
        if (_consumedInvitations.Count > 256) _consumedInvitations.Clear();
        if (_expiredInvitations.Count > 256) _expiredInvitations.Clear();
    }
    private bool RevokeExpired(Session session)
    { if (session.DeviceId is null) return false; var removed = _devices.Revoke(session.DeviceId); _grants.Revoke(session.DeviceId); return removed; }
    private void ExpireDelivery(Session session)
    {
        session.DeviceToken = null;
        try { RevokeExpired(session); session.State = "Expired"; }
        catch { session.State = "Approved"; throw; }
    }
    private bool AuthenticateSession(Session session, string? token)
    {
        if (!BridgeTokens.TryDecode(token, out var bytes)) return false;
        var supplied = Hash(bytes); CryptographicOperations.ZeroMemory(bytes);
        return FixedHashEquals(session.SessionHash, supplied);
    }
    private static bool FixedHashEquals(string left, string right)
    {
        var a = Convert.FromHexString(left); var b = Convert.FromHexString(right);
        try { return CryptographicOperations.FixedTimeEquals(a, b); }
        finally { CryptographicOperations.ZeroMemory(a); CryptographicOperations.ZeroMemory(b); }
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string SourceKey(IPAddress address) => (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    private DateTimeOffset Now => _clock().ToUniversalTime();
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static BridgePairOperation<T> Ok<T>(T value, int status = 200) => new(status, value);
    private static BridgePairOperation<T> Fail<T>(int status, string code, int? retry = null) => new(status, default, code, retry);
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _cleanupTimer.Dispose();
            try { ClearSessions(); }
            finally { _disposed = true; }
        }
    }
}
