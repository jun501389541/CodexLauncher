using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexLauncher.Core;

public sealed record BridgePersistedGrants(string? AccountId, IReadOnlyList<string> DeviceIds,string DisplayName="Codex账号");

public interface IBridgeAccountGrantPersistence
{
    BridgePersistedGrants? Read(string bridgeId);
    void Write(string bridgeId, string? accountId, IReadOnlyCollection<string> deviceIds);
    void Write(string bridgeId,string? accountId,IReadOnlyCollection<string> deviceIds,string displayName)=>Write(bridgeId,accountId,deviceIds);
}

/// <summary>进程内账号授权边界。设备身份须由未来 HTTP 认证层验证；此处不接受设备令牌。</summary>
public sealed class BridgeAccountGrantStore : IDisposable
{
    private readonly string _bridgeId;
    private readonly byte[] _mappingKey;
    private readonly SharedQuotaCoordinator _quota;
    private readonly IBridgeAccountGrantPersistence? _persistence;
    private readonly object _sync = new();
    private readonly HashSet<string> _grants = new(StringComparer.Ordinal);
    private QuotaAccount _identity;
    private string? _accountId;
    private string _displayName = "Codex账号";
    private bool _disposed;
    private BridgePersistedGrants? _persisted;

    // 映射密钥由后续 DPAPI 身份仓库提供；不在这里生成、不写明文 settings。
    public BridgeAccountGrantStore(string bridgeId, byte[] mappingKey, SharedQuotaCoordinator quota,
        IBridgeAccountGrantPersistence? persistence = null)
    {
        if (string.IsNullOrWhiteSpace(bridgeId)) throw new ArgumentException("Bridge ID required", nameof(bridgeId));
        if (mappingKey.Length < 32) throw new ArgumentException("Mapping key requires 256 bits", nameof(mappingKey));
        _bridgeId = bridgeId;
        _mappingKey = mappingKey.ToArray();
        _quota = quota;
        _persistence = persistence;
        _persisted = persistence?.Read(bridgeId);
        _identity = QuotaAccount.None;
        lock (_sync)
        {
            _quota.QuotaChanged += OnQuotaChanged;
            ObserveIdentity(_quota.Snapshot.Identity);
        }
    }

    private void OnQuotaChanged(QuotaSnapshot snapshot)
    {
        lock (_sync) if (!_disposed) ObserveIdentity(snapshot.Identity);
    }

    private void ObserveIdentity(QuotaAccount identity)
    {
        if (_identity.SameIdentity(identity)) return;
        var firstIdentifiable = !_identity.IsIdentifiable && identity.IsIdentifiable;
        _grants.Clear();
        _displayName = "Codex账号";
        _identity = identity;
        // 有序 JSON 编码避免分隔符碰撞；每个身份字段均参与 HMAC，昵称不参与。
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] {
            identity.Kind.ToString(), identity.Email, identity.WorkspaceAccountId, identity.StableId });
        _accountId = identity.IsIdentifiable
            ? Convert.ToHexString(HMACSHA256.HashData(_mappingKey, bytes)).ToLowerInvariant() : null;
        if (_persistence is not null)
        {
            if (firstIdentifiable && _persisted is { } persisted && persisted.AccountId == _accountId)
            {
                foreach (var deviceId in persisted.DeviceIds) _grants.Add(deviceId);
                _displayName=persisted.DisplayName;
            }
            else Persist();
        }
    }

    /// <summary>仅本机审批接缝可调用；授权记录通过注入的 DPAPI 仓库跨重启保存。</summary>
    public bool ConfirmCurrentAccount(string verifiedDeviceId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var snapshot = _quota.Snapshot;
            ObserveIdentity(snapshot.Identity);
            if (_accountId is null || snapshot.Availability == QuotaAvailability.NotLoggedIn ||
                string.IsNullOrWhiteSpace(verifiedDeviceId)) return false;
            _grants.Add(verifiedDeviceId);
            try { Persist(); }
            catch { _grants.Remove(verifiedDeviceId); throw; }
            return true;
        }
    }

    public void RenameCurrentAccount(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 80 || displayName.Any(char.IsControl))
            throw new ArgumentException("Nickname requires 1–80 visible characters", nameof(displayName));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);ObserveIdentity(_quota.Snapshot.Identity);
            if(_accountId is null)throw new InvalidOperationException("ACCOUNT_UNIDENTIFIABLE");
            var previous=_displayName;_displayName=displayName.Trim();
            try{Persist();}catch{_displayName=previous;throw;}
        }
    }
    public BridgeAccount? CurrentAccount
    {get{lock(_sync){ObserveIdentity(_quota.Snapshot.Identity);return _accountId is null?null:new("codex",_accountId,_displayName);}}}

    public void Revoke(string verifiedDeviceId) { lock (_sync) { _grants.Remove(verifiedDeviceId); Persist(); } }
    public void RevokeAll() { lock (_sync) { _grants.Clear(); Persist(); } }
    public bool IsGranted(string verifiedDeviceId)
    { lock (_sync) { ObserveIdentity(_quota.Snapshot.Identity); return _grants.Contains(verifiedDeviceId); } }
    private void Persist()
    {
        if (_persistence is null) return;
        var ids = _grants.ToArray();
        _persistence.Write(_bridgeId, _accountId, ids,_displayName);
        _persisted = new BridgePersistedGrants(_accountId, ids,_displayName);
    }

    public BridgeReadResult<IReadOnlyList<BridgeAccount>> Accounts(string verifiedDeviceId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var snapshot = _quota.Snapshot;
            ObserveIdentity(snapshot.Identity);
            if (_accountId is null || snapshot.Availability == QuotaAvailability.NotLoggedIn)
                return BridgeReadResult<IReadOnlyList<BridgeAccount>>.Success(Array.Empty<BridgeAccount>());
            if (!_grants.Contains(verifiedDeviceId))
                return BridgeReadResult<IReadOnlyList<BridgeAccount>>.Failure(403, "ACCOUNT_REAUTHORIZATION_REQUIRED");
            return BridgeReadResult<IReadOnlyList<BridgeAccount>>.Success(
                new[] { new BridgeAccount("codex", _accountId, _displayName) });
        }
    }

    public BridgeReadResult<UsageResult> Usage(string verifiedDeviceId, string requestedAccountId)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var snapshot = _quota.Snapshot;
            ObserveIdentity(snapshot.Identity);
            // 监测关闭时没有身份可识别，返回无账号数据的状态回执；并不创建账号授权。
            if (!_quota.MonitoringEnabled)
                return BridgeReadResult<UsageResult>.Success(UsageResultMapper.Map(_bridgeId,
                    requestedAccountId, "Codex账号", snapshot, false));
            if (_accountId is null) return BridgeReadResult<UsageResult>.Failure(403, "ACCOUNT_UNIDENTIFIABLE");
            if (!string.Equals(_accountId, requestedAccountId, StringComparison.Ordinal))
                return BridgeReadResult<UsageResult>.Failure(409, "ACCOUNT_CHANGED");
            if (!_grants.Contains(verifiedDeviceId))
                return BridgeReadResult<UsageResult>.Failure(403, "ACCOUNT_REAUTHORIZATION_REQUIRED");
            return BridgeReadResult<UsageResult>.Success(UsageResultMapper.Map(_bridgeId, _accountId,
                _displayName, snapshot, _quota.MonitoringEnabled));
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _quota.QuotaChanged -= OnQuotaChanged;
            _grants.Clear();
            CryptographicOperations.ZeroMemory(_mappingKey);
        }
    }
}
