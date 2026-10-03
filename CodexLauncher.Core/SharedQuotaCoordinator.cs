namespace CodexLauncher.Core;

public sealed record QuotaAccountChange(QuotaAccount Previous, QuotaAccount Current, long Version);
public sealed record QuotaManualRequest(bool Accepted, TimeSpan RetryAfter, Task<QuotaSnapshot> Completion);

/// <summary>启动器后台数据源的唯一所有者；UI 和 Bridge 复用现有 provider 的循环、缓存与节流。</summary>
public sealed class SharedQuotaCoordinator : ICodexQuotaProvider, IAsyncDisposable
{
    private readonly CodexQuotaProvider? _provider;
    private readonly QuotaSnapshot _disabled;
    private readonly object _sync = new();
    private QuotaAccount _identity = QuotaAccount.None;
    private long _version;

    public SharedQuotaCoordinator(IQuotaSessionFactory? factory, bool monitoringEnabled = true,
        CancellationToken lifetimeToken = default, Func<DateTimeOffset>? clock = null)
    {
        MonitoringEnabled = monitoringEnabled;
        _disabled = QuotaSnapshot.Unavailable("额度查询已在配置中关闭。", (clock?.Invoke() ?? DateTimeOffset.UtcNow),
            "quota-monitoring-disabled");
        if (!monitoringEnabled) return;
        _provider = new CodexQuotaProvider(factory, clock: clock, lifetimeToken: lifetimeToken);
        _provider.QuotaChanged += OnChanged;
    }

    public bool MonitoringEnabled { get; }
    public QuotaSnapshot Snapshot => _provider?.Snapshot ?? _disabled;
    public long AccountVersion { get { lock (_sync) return _version; } }
    public event Action<QuotaSnapshot>? QuotaChanged;
    public event Action<QuotaAccountChange>? AccountChanged;

    public void Start() => _provider?.Start();
    public QuotaManualRequest TryRequestManual() => _provider?.TryRequestManual()
        ?? new(true, TimeSpan.Zero, Task.FromResult(_disabled));
    public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken) => _provider is null
        ? DisabledRead(cancellationToken) : _provider.ReadAsync(cancellationToken);
    public Task<QuotaSnapshot> RequestManualAsync(CancellationToken cancellationToken) => _provider is null
        ? DisabledRead(cancellationToken) : _provider.RequestManualAsync(cancellationToken);

    private Task<QuotaSnapshot> DisabledRead(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_disabled);
    }

    private void OnChanged(QuotaSnapshot snapshot)
    {
        QuotaAccountChange? change = null;
        lock (_sync)
        {
            if (!_identity.SameIdentity(snapshot.Identity))
            {
                change = new QuotaAccountChange(_identity, snapshot.Identity, ++_version);
                _identity = snapshot.Identity;
            }
        }
        if (change is not null) AccountChanged?.Invoke(change);
        QuotaChanged?.Invoke(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is null) return;
        _provider.QuotaChanged -= OnChanged;
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
