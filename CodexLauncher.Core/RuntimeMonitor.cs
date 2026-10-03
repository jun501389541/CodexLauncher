using System.Diagnostics;

namespace CodexLauncher.Core;

/// <summary>
/// 监测所需的运行期环境事实：线路、进程状态、诊断事件来源。
/// 采样计数与时间戳由 <see cref="RuntimeMonitor"/> 自己维护。
/// </summary>
public sealed record RuntimeMonitorContext
{
    public bool CodexRunning { get; init; } = true;
    public bool TunConflict { get; init; }
    public RouteKind? Route { get; init; } = RouteKind.Direct;
    public ProxyAddress? Proxy { get; init; }
    /// <summary>线路指纹；变化即表示实际线路已切换，旧证据作废。</summary>
    public string Fingerprint { get; init; } = "";
    public string RouteLabel { get; init; } = "";
    /// <summary>外部启动的 Codex 无法确认实际线路时为 false。</summary>
    public bool RouteConfirmed { get; init; } = true;
    public IReadOnlyList<NetworkDiagnosticEvent> Events { get; init; } = Array.Empty<NetworkDiagnosticEvent>();
    public bool DiagnosticSourceAvailable { get; init; }
}

/// <summary>
/// 运行期免费监测：固定周期做一次只读 HTTPS 探测，把结果交给
/// <see cref="RuntimeHealthEvaluator"/> 得出状态；不发送任何 Codex 模型请求。
/// </summary>
public sealed class RuntimeMonitor : IDisposable
{
    private readonly IHttpProbe _probe;
    private readonly Func<RuntimeMonitorContext> _context;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _sampleTimeout;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _sampling = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);

    private bool _hasSampled;
    private int _consecutiveNormal;
    private int _consecutiveFailed;
    private RuntimeSampleKind? _lastSample;
    private TimeSpan? _lastElapsed;
    private string? _lastDetail;
    private DateTimeOffset? _lastSampleAt;
    private bool _routeChanged;
    private string? _sampledFingerprint;
    private RuntimeHealthSnapshot? _snapshot;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loop;
    private bool _disposed;

    public RuntimeMonitor(
        IHttpProbe probe,
        Func<RuntimeMonitorContext> context,
        TimeSpan? interval = null,
        TimeSpan? sampleTimeout = null,
        Func<DateTimeOffset>? clock = null)
    {
        _probe = probe;
        _context = context;
        _interval = interval ?? TimeSpan.FromSeconds(5);
        _sampleTimeout = sampleTimeout ?? RuntimeHealthEvaluator.SampleTimeout;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _snapshot = Build(_context());
    }

    public event Action<RuntimeHealthSnapshot>? SnapshotChanged;

    public bool IsRunning { get; private set; }

    /// <summary>最近一次评估结果；不发起新采样。</summary>
    public RuntimeHealthSnapshot Snapshot => _snapshot ?? Build(_context());

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loop is not null) return;
        _loopCancellation = new CancellationTokenSource();
        IsRunning = true;
        var token = _loopCancellation.Token;
        _loop = Task.Run(() => RunLoopAsync(token), token);
    }

    /// <summary>请求立即采样一次，不必等待当前周期。</summary>
    public void RequestRefresh()
    {
        if (_disposed) return;
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* 已有一个待处理的刷新请求，合并即可。 */ }
    }

    /// <summary>不发起新采样，仅按当前状态重新评估；用于界面刷新和采样过期判定。</summary>
    public RuntimeHealthSnapshot Evaluate()
    {
        var snapshot = Build(_context());
        _snapshot = snapshot;
        return snapshot;
    }

    /// <summary>执行一次采样并返回最新状态。同一时刻只允许一个采样任务。</summary>
    public async Task<RuntimeHealthSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        var context = _context();
        var fingerprint = context.Fingerprint;
        await _sampling.WaitAsync(cancellationToken);
        try
        {
            ExpireIfStale();
            InvalidateOnRouteChange(fingerprint);

            var timer = Stopwatch.StartNew();
            ProbeResult result;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var sample = _probe.CheckAsync(context.Route ?? RouteKind.Direct, context.Proxy, linked.Token);
                try
                {
                    // 单次采样最多占用 sampleTimeout：超时即取消并丢弃迟到结果，保证单任务执行。
                    result = await sample.WaitAsync(_sampleTimeout, cancellationToken);
                }
                catch (TimeoutException)
                {
                    linked.Cancel();
                    Observe(sample);
                    result = new ProbeResult(ProbeStatus.Failed, null, timer.Elapsed, $"{_sampleTimeout.TotalSeconds:0} 秒超时");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                result = new ProbeResult(ProbeStatus.Failed, null, timer.Elapsed, "探测未能完成");
            }

            var current = _context();
            if (!string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                // 线路在采样期间发生变化：丢弃迟到结果，重新确认后再决定状态。
                InvalidateForRouteChange();
                return Publish(current);
            }

            Accept(fingerprint, result);
            return Publish(current);
        }
        finally
        {
            _sampling.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var loop = _loop;
        if (loop is null)
        {
            IsRunning = false;
            return;
        }
        try { _loopCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        try { await loop.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        _loop = null;
        _loopCancellation?.Dispose();
        _loopCancellation = null;
        IsRunning = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _loopCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        _loopCancellation?.Dispose();
        _loopCancellation = null;
        _loop = null;
        GC.SuppressFinalize(this);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // 本轮采样本身就满足累积的刷新请求，先清掉再采样。
                while (_wake.Wait(0)) { }
                try { await RefreshAsync(cancellationToken); }
                catch (OperationCanceledException) { break; }
                catch (Exception) { /* 单轮异常不终止监测，下一轮继续。 */ }
                try { await _wake.WaitAsync(_interval, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>采样超时后观察被放弃的任务，避免未处理的异常。</summary>
    private static void Observe(Task<ProbeResult> sample) =>
        sample.ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);

    /// <summary>超过 15 秒没有有效采样（例如系统睡眠）后，旧证据全部作废并重新取证。</summary>
    private void ExpireIfStale()
    {
        if (!_hasSampled || _lastSampleAt is not { } at) return;
        if (_clock() - at <= RuntimeHealthEvaluator.StaleThreshold) return;
        ResetCounters();
        _hasSampled = false;
        _lastSampleAt = null;
    }

    private void InvalidateOnRouteChange(string fingerprint)
    {
        if (!_hasSampled || _sampledFingerprint is null) return;
        if (string.Equals(_sampledFingerprint, fingerprint, StringComparison.Ordinal)) return;
        InvalidateForRouteChange();
    }

    private void InvalidateForRouteChange()
    {
        ResetCounters();
        _hasSampled = false;
        _lastSampleAt = null;
        _routeChanged = true;
    }

    private void ResetCounters()
    {
        _consecutiveNormal = 0;
        _consecutiveFailed = 0;
        _lastSample = null;
        _lastElapsed = null;
        _lastDetail = null;
    }

    private void Accept(string fingerprint, ProbeResult result)
    {
        var kind = RuntimeSampleClassifier.Classify(result);
        _sampledFingerprint = fingerprint;
        _hasSampled = true;
        _routeChanged = false;
        _lastSample = kind;
        _lastElapsed = result.Elapsed;
        _lastDetail = result.Detail;
        _lastSampleAt = _clock();
        switch (kind)
        {
            case RuntimeSampleKind.Normal:
                _consecutiveNormal++;
                _consecutiveFailed = 0;
                break;
            case RuntimeSampleKind.Slow:
                // 慢样本清零连续正常计数；但已经收到回应，失败计数同样清零。
                _consecutiveNormal = 0;
                _consecutiveFailed = 0;
                break;
            default:
                _consecutiveNormal = 0;
                _consecutiveFailed++;
                break;
        }
    }

    private RuntimeHealthSnapshot Build(RuntimeMonitorContext context)
    {
        var now = _clock();
        TimeSpan? since = _lastSampleAt is { } at ? now - at : null;
        if (since is { } age && age < TimeSpan.Zero) since = TimeSpan.Zero;
        return RuntimeHealthEvaluator.Evaluate(new RuntimeHealthInput
        {
            CodexRunning = context.CodexRunning,
            TunConflict = context.TunConflict,
            HasSampled = _hasSampled,
            RouteChanged = _routeChanged,
            ConsecutiveNormal = _consecutiveNormal,
            ConsecutiveFailed = _consecutiveFailed,
            LastSample = _lastSample,
            LastElapsed = _lastElapsed,
            LastDetail = _lastDetail,
            SinceLastSample = since,
            Events = context.Events,
            DiagnosticSourceAvailable = context.DiagnosticSourceAvailable,
            RouteConfirmed = context.RouteConfirmed,
            RouteLabel = context.RouteLabel,
            Now = now
        });
    }

    private RuntimeHealthSnapshot Publish(RuntimeMonitorContext context)
    {
        var snapshot = Build(context);
        _snapshot = snapshot;
        SnapshotChanged?.Invoke(snapshot);
        return snapshot;
    }
}
