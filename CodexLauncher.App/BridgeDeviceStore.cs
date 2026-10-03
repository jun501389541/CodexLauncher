using CodexLauncher.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexLauncher.App;

internal sealed record BridgeDeviceSummary(string Id, string Name, DateTimeOffset AddedAt);

/// <summary>仅持久化设备 token 哈希，原始 bearer 仅在短暂交付期保留于内存。</summary>
internal sealed class BridgeDeviceStore : IBridgeDeviceAuthenticator, IDisposable
{
    private sealed record Device(string Id, string Name, string TokenHash, DateTimeOffset AddedAt, bool DeliveryCompleted);
    private sealed record State(int Version, string BridgeId, List<Device> Devices);
    private readonly string _bridgeId;
    private readonly string _path;
    private readonly object _sync = new();
    private List<Device> _devices;
    private bool _disposed;

    internal BridgeDeviceStore(string bridgeId, string path)
    {
        _bridgeId = bridgeId; _path = path;
        SecureBridgeFiles.PrepareDirectory(Path.GetDirectoryName(path)!);
        _devices = Load();
    }
    internal int ActiveCount { get { lock (_sync) return _devices.Count; } }
    internal IReadOnlyList<BridgeDeviceSummary> List()
    { lock (_sync) { ThrowIfDisposed(); return _devices.Select(d => new BridgeDeviceSummary(d.Id, d.Name, d.AddedAt)).ToArray(); } }

    public string? Authenticate(string token)
    {
        if (!BridgeTokens.TryDecode(token, out var raw)) return null;
        var digest = SHA256.HashData(raw);
        try
        {
            lock (_sync)
            {
                if (_disposed) return null;
                foreach (var device in _devices)
                {
                    var expected = Convert.FromHexString(device.TokenHash);
                    try { if (CryptographicOperations.FixedTimeEquals(digest, expected)) return device.Id; }
                    finally { CryptographicOperations.ZeroMemory(expected); }
                }
                return null;
            }
        }
        finally { CryptographicOperations.ZeroMemory(raw); CryptographicOperations.ZeroMemory(digest); }
    }

    internal BridgeDeviceSummary AddApproved(string name, out string token)
    {
        ValidateName(name);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_devices.Count >= 20) throw new InvalidOperationException("DEVICE_LIMIT_REACHED");
            var raw = RandomNumberGenerator.GetBytes(32);
            try
            {
                token = BridgeTokens.Encode(raw);
                var item = new Device(Guid.NewGuid().ToString("D"), name.Trim(), Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(), DateTimeOffset.UtcNow, false);
                var next = _devices.Append(item).ToList(); Save(next); _devices = next;
                return new(item.Id, item.Name, item.AddedAt);
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }
    }
    internal bool CompleteDelivery(string id)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var current = _devices.FirstOrDefault(d => d.Id == id);
            if (current is null) return false;
            if (current.DeliveryCompleted) return true;
            var next = _devices.Select(d => d.Id == id ? d with { DeliveryCompleted = true } : d).ToList();
            Save(next); _devices = next; return true;
        }
    }
    internal IReadOnlyList<string> RevokePendingDeliveries()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            var revoked = _devices.Where(d => !d.DeliveryCompleted).Select(d => d.Id).ToArray();
            if (revoked.Length == 0) return revoked;
            var next = _devices.Where(d => d.DeliveryCompleted).ToList();
            Save(next); _devices = next; return revoked;
        }
    }

    internal bool Revoke(string id)
    {
        lock (_sync)
        {
            ThrowIfDisposed(); var next = _devices.Where(d => d.Id != id).ToList();
            if (next.Count == _devices.Count) return false;
            Save(next); _devices = next; return true;
        }
    }
    internal void RevokeAll() { lock (_sync) { ThrowIfDisposed(); Save([]); _devices = []; } }
    internal bool Rename(string id,string name)
    {
        ValidateName(name);
        lock(_sync)
        {
            ThrowIfDisposed();if(!_devices.Any(d=>d.Id==id))return false;
            var next=_devices.Select(d=>d.Id==id?d with{Name=name.Trim()}:d).ToList();Save(next);_devices=next;return true;
        }
    }

    private List<Device> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var clear = SecureBridgeFiles.Read(_path);
            try
            {
                var state = JsonSerializer.Deserialize<State>(clear);
                if (state is not { Version: 2 } || state.BridgeId != _bridgeId || state.Devices is null || state.Devices.Count > 20 ||
                    state.Devices.Any(d => d is null || !Guid.TryParse(d.Id, out _) || d.TokenHash is not { Length: 64 } hash ||
                        !hash.All(Uri.IsHexDigit) || d.Name is not { Length: >= 1 and <= 80 } name || name.Any(char.IsControl)))
                    throw new InvalidDataException("Invalid device store");
                if (state.Devices.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != state.Devices.Count ||
                    state.Devices.Select(d => d.TokenHash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Devices.Count)
                    throw new InvalidDataException("Duplicate device identity");
                var active = state.Devices.Where(d => d.DeliveryCompleted).ToList();
                if (active.Count != state.Devices.Count) Save(active);
                return active;
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException or ArgumentException)
        { Save([]); return []; }
    }
    private void Save(List<Device> devices)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(new State(2, _bridgeId, devices));
        try { SecureBridgeFiles.Write(_path, clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || name.Any(char.IsControl))
            throw new ArgumentException("DEVICE_NAME_INVALID", nameof(name));
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose() { lock (_sync) { _disposed = true; _devices.Clear(); } }
}

internal static class BridgeTokens
{
    internal static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static bool TryDecode(string? text, out byte[] bytes)
    {
        bytes = [];
        if (text is null || text.Length != 43 || text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
        try
        {
            bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length == 32 && Encode(bytes) == text) return true;
            CryptographicOperations.ZeroMemory(bytes); bytes = []; return false;
        }
        catch (FormatException) { return false; }
    }
}

internal sealed class BridgeAccountGrantRepository(string bridgeId, string path) : IBridgeAccountGrantPersistence
{
    private sealed record State(int Version, string BridgeId, string? AccountId, string[] DeviceIds,string DisplayName="Codex账号");
    private readonly object _sync = new();
    public BridgePersistedGrants? Read(string requestedBridgeId)
    {
        if (requestedBridgeId != bridgeId || !File.Exists(path)) return null;
        lock (_sync)
        {
            try
            {
                var clear = SecureBridgeFiles.Read(path);
                try
                {
                    var state = JsonSerializer.Deserialize<State>(clear);
                    if (state is not { Version: 1 } || state.BridgeId != bridgeId || state.DeviceIds is null ||
                        state.DeviceIds.Any(s => !Guid.TryParse(s, out _)) ||
                        state.AccountId is not null && (state.AccountId.Length != 64 || !state.AccountId.All(Uri.IsHexDigit))) return null;
                    if(string.IsNullOrWhiteSpace(state.DisplayName)||state.DisplayName.Length>80||state.DisplayName.Any(char.IsControl))return null;
                    return new(state.AccountId, state.DeviceIds.Distinct(StringComparer.Ordinal).ToArray(),state.DisplayName);
                }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException or ArgumentException)
            { return null; }
        }
    }
    public void Write(string requestedBridgeId, string? accountId, IReadOnlyCollection<string> deviceIds)
        =>Write(requestedBridgeId,accountId,deviceIds,"Codex账号");
    public void Write(string requestedBridgeId,string? accountId,IReadOnlyCollection<string> deviceIds,string displayName)
    {
        if (requestedBridgeId != bridgeId) throw new InvalidOperationException("BRIDGE_ID_MISMATCH");
        lock (_sync)
        {
            var clear = JsonSerializer.SerializeToUtf8Bytes(new State(1, bridgeId, accountId, deviceIds.ToArray(),displayName));
            try { SecureBridgeFiles.Write(path, clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
    }
}
