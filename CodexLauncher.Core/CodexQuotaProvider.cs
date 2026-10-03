using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexLauncher.Core;

/// <summary>打开一个只读额度会话失败时使用的脱敏类别，不携带原始 stderr 文本。</summary>
public sealed class QuotaSessionException(string category, Exception? inner = null)
    : Exception(category, inner)
{
    public string Category { get; } = category;
}

/// <summary>请求白名单：额度查询只允许这些方法，其余一律拒绝发送。</summary>
public static class QuotaRequestWhitelist
{
    public static readonly IReadOnlyList<string> AllowedMethods =
        ["initialize", "account/read", "account/rateLimits/read"];

    /// <summary>明确禁止的方法：对话生成、登录/注销、购买/兑换、邮件与反馈上传。</summary>
    public static readonly IReadOnlyList<string> ForbiddenMethods =
    [
        "turn/start", "turn/steer", "thread/start", "review/start",
        "account/login/start", "account/login/cancel", "account/logout",
        "account/rateLimitResetCredit/consume", "account/sendAddCreditsNudgeEmail",
        "account/gatewayOAuth/login", "account/gatewayOAuth/cancel",
        "account/workspaceMessages/read", "feedback/upload"
    ];

    public static bool IsAllowed(string method) =>
        AllowedMethods.Contains(method, StringComparer.Ordinal);

    public static bool IsForbidden(string method) =>
        ForbiddenMethods.Contains(method, StringComparer.Ordinal) ||
        method.StartsWith("account/login", StringComparison.Ordinal) ||
        method.StartsWith("turn/", StringComparison.Ordinal);
}

/// <summary>一个已握手成功的只读会话；只允许发送白名单方法。</summary>
public interface IQuotaSession : IAsyncDisposable
{
    /// <summary>发送一个白名单内的只读请求，返回 result 原始 JSON 文本；无 result 时为 null。</summary>
    Task<string?> InvokeAsync(string method, string? paramsJson, CancellationToken cancellationToken);

    /// <summary>服务端主动推送的通知方法名，例如 account/rateLimits/updated。</summary>
    event Action<string>? Notification;
}

public interface IQuotaSessionFactory
{
    /// <summary>配置变化后重建查询会话；固定配置或测试替身可保留默认值。</summary>
    string? ConfigurationKey => null;
    Task<IQuotaSession> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 通过 stdio 与配套 Codex CLI 的 App Server 通信：启动器自有的查询进程，
/// 不开放网络监听、不启动对话，凭据完全由 Codex 自身处理。
/// </summary>
public sealed class CodexAppServerSession : IQuotaSession
{
    public const string DefaultArguments = "app-server";
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);
    private const string ClientInfo =
        """{"clientInfo":{"name":"codex-launcher","title":"Codex 启动器","version":"1.0.0"},"capabilities":{"experimentalApi":false}}""";

    private readonly Process _process;
    private readonly TimeSpan _requestTimeout;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<string?>> _pending = new();
    private readonly CancellationTokenSource _closed = new();
    private long _nextId;
    private int _disposed;
    private string? _lastErrorCategory;

    private CodexAppServerSession(Process process, TimeSpan requestTimeout)
    {
        _process = process;
        _requestTimeout = requestTimeout;
        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(ReadErrorLoopAsync);
    }

    public event Action<string>? Notification;

    /// <summary>最近一次失败的脱敏类别，不含 stderr 原文。</summary>
    public string? LastErrorCategory => _lastErrorCategory;

    public static async Task<CodexAppServerSession> OpenAsync(
        string executablePath,
        string arguments = DefaultArguments,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? requestTimeout = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timeout = requestTimeout ?? QuotaSchedule.RequestTimeout;
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        // 只向这个自有短命子进程注入额外环境键（缺省继承父进程其余变量），不污染启动器自身进程环境。
        if (environment is not null)
            foreach (var (key, value) in environment)
                startInfo.EnvironmentVariables[key] = value;
        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            startInfo.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new QuotaSessionException("start-returned-null");
        }
        catch (QuotaSessionException) { throw; }
        catch (Exception exception)
        {
            throw new QuotaSessionException($"start-failed:{exception.GetType().Name}", exception);
        }

        var session = new CodexAppServerSession(process, timeout);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshake.CancelAfter(handshakeTimeout ?? DefaultHandshakeTimeout);
        try
        {
            await session.InvokeAsync("initialize", ClientInfo, handshake.Token).ConfigureAwait(false);
            return session;
        }
        catch (OperationCanceledException exception)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new QuotaSessionException("timeout", exception);
        }
        catch (QuotaSessionException)
        {
            // 进程退出这类真实原因优先于「stderr 有输出」的旁证。
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw new QuotaSessionException(session.LastErrorCategory ?? "handshake-failed", exception);
        }
    }

    public async Task<string?> InvokeAsync(string method, string? paramsJson, CancellationToken cancellationToken)
    {
        if (!QuotaRequestWhitelist.IsAllowed(method))
            throw new InvalidOperationException($"方法不在只读白名单内：{method}");
        if (_disposed != 0) throw new QuotaSessionException("session-disposed");

        var id = Interlocked.Increment(ref _nextId);
        var payload = paramsJson is null
            ? $"{{\"id\":{id},\"method\":\"{method}\"}}"
            : $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await _process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _lastErrorCategory = "timeout";
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null) break;
                if (line.Length == 0) continue;
                Dispatch(line);
            }
        }
        catch (Exception exception)
        {
            _lastErrorCategory = $"read-failed:{exception.GetType().Name}";
        }
        finally
        {
            foreach (var entry in _pending)
                entry.Value.TrySetException(new QuotaSessionException("process-exited"));
        }
    }

    private void Dispatch(string line)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException)
        {
            _lastErrorCategory = "malformed-response";
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number &&
                idElement.TryGetInt64(out var id) && _pending.TryGetValue(id, out var completion))
            {
                if (root.TryGetProperty("error", out var error))
                {
                    _lastErrorCategory = ErrorCategory(error);
                    completion.TrySetException(new QuotaSessionException(_lastErrorCategory));
                    return;
                }
                var result = root.TryGetProperty("result", out var value) && value.ValueKind != JsonValueKind.Null
                    ? value.GetRawText()
                    : null;
                completion.TrySetResult(result);
                return;
            }

            if (root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
                Notification?.Invoke(method.GetString()!);
        }
    }

    /// <summary>只保留数字错误码，避免把可能含账号信息的错误文本记入类别。</summary>
    private static string ErrorCategory(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code))
        {
            if (code.ValueKind == JsonValueKind.Number && code.TryGetInt64(out var number)) return $"rpc-error:{number}";
            if (code.ValueKind == JsonValueKind.String)
            {
                var text = code.GetString();
                if (!string.IsNullOrEmpty(text) && text.Length <= 40 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                    return $"rpc-error:{text}";
            }
        }
        return "rpc-error";
    }

    /// <summary>
    /// stderr 只统计出现与否，不读取原文；进程已经给出更具体的失败原因时不覆盖它，
    /// 否则「进程退出」会被降级成含义模糊的「stderr 有输出」。
    /// </summary>
    private async Task ReadErrorLoopAsync()
    {
        try
        {
            var buffer = new char[1024];
            var seen = false;
            while (true)
            {
                var read = await _process.StandardError.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0) break;
                seen = true;
            }
            if (seen && _lastErrorCategory is null) _lastErrorCategory = "stderr-present";
        }
        catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _closed.Cancel();
        foreach (var entry in _pending) entry.Value.TrySetException(new QuotaSessionException("session-disposed"));
        try { _process.StandardInput.Close(); } catch (Exception) { }
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token).ConfigureAwait(false);
            }
        }
        catch (Exception) { }
        finally { _process.Dispose(); }
        _closed.Dispose();
    }
}

/// <summary>按需定位配套 CLI 并打开启动器自有的 App Server 会话。</summary>
public sealed class CodexAppServerSessionFactory(
    string executablePath,
    string arguments = CodexAppServerSession.DefaultArguments,
    TimeSpan? handshakeTimeout = null,
    TimeSpan? requestTimeout = null,
    IReadOnlyDictionary<string, string>? environment = null,
    Func<IReadOnlyDictionary<string, string>>? environmentResolver = null) : IQuotaSessionFactory
{
    public string ExecutablePath { get; } = executablePath;
    public string? ConfigurationKey
    {
        get
        {
            if (environmentResolver is null) return null;
            var configuration = string.Join("\n", environmentResolver().OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration)));
        }
    }

    public static string BinRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");

    public static string? LocateCli() => BundledCliFinder.Find(BinRoot);

    /// <summary>定位本机 CLI，并按进程环境 → 启动器设置 → Windows 系统代理的顺序解析出注入子进程的代理环境。</summary>
    public static IQuotaSessionFactory? ForCurrentUser(TimeSpan? requestTimeout = null)
    {
        var cli = LocateCli();
        return cli is null
            ? null
            : new CodexAppServerSessionFactory(cli, requestTimeout: requestTimeout,
                environmentResolver: () => QuotaProxyEnvironment.Resolve().Environment);
    }

    public async Task<IQuotaSession> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await CodexAppServerSession.OpenAsync(ExecutablePath, arguments, handshakeTimeout, requestTimeout,
                environmentResolver?.Invoke() ?? environment,
                cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>刷新周期、手动间隔、退避与新鲜度；时钟可注入以便测试。</summary>
public sealed class QuotaSchedule(Func<DateTimeOffset>? clock = null)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ManualInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(10);
    public static readonly IReadOnlyList<TimeSpan> Backoff =
        [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30)];

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private DateTimeOffset? _lastAttempt;
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset? _lastManual;
    private DateTimeOffset? _confirmedReset;
    private int _failures;

    public DateTimeOffset Now => _clock();
    public int FailureCount => _failures;
    public DateTimeOffset? LastSuccess => _lastSuccess;

    /// <summary>成功或首次为 5 分钟，失败按 5、10、20、30 分钟逐步退避。</summary>
    public TimeSpan CurrentInterval => _failures == 0
        ? RefreshInterval
        : Backoff[Math.Min(_failures - 1, Backoff.Count - 1)];

    public DateTimeOffset? NextDue => _lastAttempt is null ? null : _lastAttempt.Value + CurrentInterval;

    public bool CanRequestManually => _lastManual is null || Now - _lastManual.Value >= ManualInterval;

    public TimeSpan ManualRetryAfter
    {
        get
        {
            if (_lastManual is null) return TimeSpan.Zero;
            var left = ManualInterval - (Now - _lastManual.Value);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public bool IsStale => _lastSuccess is null || Now - _lastSuccess.Value > StaleThreshold;

    public TimeSpan? Age => _lastSuccess is null ? null : Now - _lastSuccess.Value;

    /// <summary>
    /// 下一次该等待多久；重置到点时提前一次查询。
    /// 已经过去的重置时间只提前一次：服务器在新结果里继续返回同一个过去的重置点时，
    /// 循环必须回到正常周期，不能变成零等待空转。
    /// </summary>
    public TimeSpan DelayUntilDue(DateTimeOffset? resetAt)
    {
        var due = NextDue ?? Now;
        if (_failures == 0 && resetAt is { } reset)
        {
            if (reset <= Now)
            {
                if (_confirmedReset != reset)
                {
                    _confirmedReset = reset;
                    return TimeSpan.Zero;
                }
            }
            else if (reset < due)
            {
                due = reset;
            }
        }
        var delay = due - Now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    public void RecordAttempt() => _lastAttempt = Now;

    public void RecordManual() => _lastManual = Now;

    public void RecordSuccess()
    {
        _failures = 0;
        _lastSuccess = Now;
        _lastAttempt = Now;
    }

    public void RecordFailure()
    {
        _failures++;
        _lastAttempt = Now;
    }
}

public interface ICodexQuotaProvider
{
    QuotaSnapshot Snapshot { get; }
    event Action<QuotaSnapshot>? QuotaChanged;
    Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 额度提供者：定位配套 CLI、stdio 握手、只读账号请求与规范化快照，
/// 并负责 5 分钟刷新、失败退避、新鲜度与重置到点查询。
/// </summary>
public sealed class CodexQuotaProvider : ICodexQuotaProvider, IAsyncDisposable
{
    private readonly IQuotaSessionFactory? _factory;
    private readonly QuotaSchedule _schedule;
    private readonly TimeSpan _requestTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime;
    private IQuotaSession? _session;
    private string? _sessionConfigurationKey;
    private string _queryStage = "查询";
    private QuotaSnapshot? _snapshot;
    private QuotaAccount? _account;
    private CancellationTokenSource? _loop;
    private Task? _loopTask;
    private Task<QuotaSnapshot>? _inflight;
    private DateTimeOffset? _nextResetAt;
    private int _refreshRequested;
    private bool _disposed;
    private bool _stopped;
    private Task? _shutdownTask;

    public CodexQuotaProvider(
        IQuotaSessionFactory? factory,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? requestTimeout = null,
        CancellationToken lifetimeToken = default)
    {
        _factory = factory;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _schedule = new QuotaSchedule(clock);
        _requestTimeout = requestTimeout ?? QuotaSchedule.RequestTimeout;
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
        _snapshot = QuotaSnapshot.Unavailable("尚未查询额度。", _schedule.Now, null);
    }

    public QuotaSnapshot Snapshot
    {
        get
        {
            lock (_sync) return _snapshot!.HasData && _schedule.IsStale
                ? _snapshot with { IsStale = true } : _snapshot;
        }
    }

    public event Action<QuotaSnapshot>? QuotaChanged;

    public QuotaSchedule Schedule => _schedule;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new InvalidOperationException("Quota provider is stopped; create a new owner to restart.");
            if (_loopTask is not null) return;
            _loop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _loopTask = Task.Run(() => RunLoopAsync(_loop.Token));
        }
    }

    /// <summary>手动刷新；10 秒内重复请求会被拒绝并返回当前快照。</summary>
    public async Task<QuotaSnapshot> RequestManualAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = TryRequestManual();
        return await request.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public QuotaManualRequest TryRequestManual()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new InvalidOperationException("Quota provider is stopped.");
            if (!_schedule.CanRequestManually)
                return new(false, _schedule.ManualRetryAfter, Task.FromResult(Snapshot));
            _schedule.RecordManual();
        }
        return new(true, TimeSpan.Zero, CompleteManualAsync());
    }

    private async Task<QuotaSnapshot> CompleteManualAsync()
    {
        var snapshot = await ReadAsync(CancellationToken.None).ConfigureAwait(false);
        Interlocked.Exchange(ref _refreshRequested, 0);
        return snapshot;
    }

    public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_stopped) throw new InvalidOperationException("Quota provider is stopped.");
            _lifetime.Token.ThrowIfCancellationRequested();
            Task<QuotaSnapshot> running;
            if (_inflight is null || _inflight.IsCompleted)
            {
                // 跟踪 promise 先安装，再运行查询，避免同步完成/事件重入覆盖 flight。
                var completion = new TaskCompletionSource<QuotaSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inflight = completion.Task;
                running = completion.Task;
                Observe(_inflight);
                // 不在 _sync 内执行用户事件，避免与授权仓库/UI 的锁顺序倒置。
                _ = Task.Run(() => CompleteReadAsync(completion));
            }
            else running = _inflight;
            return running.WaitAsync(cancellationToken);
        }
    }

    private async Task CompleteReadAsync(TaskCompletionSource<QuotaSnapshot> completion)
    {
        try { completion.TrySetResult(await ReadCoreAsync(_lifetime.Token).ConfigureAwait(false)); }
        catch (OperationCanceledException) { completion.TrySetCanceled(_lifetime.Token); }
        catch (Exception exception) { completion.TrySetException(exception); }
        finally
        {
            lock (_sync)
                if (ReferenceEquals(_inflight, completion.Task)) _inflight = null;
        }
    }

    private async Task<QuotaSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _schedule.RecordAttempt();
            QuotaSnapshot result;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_requestTimeout);
                try { result = await QueryAsync(timeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new TimeoutException(); }
                cancellationToken.ThrowIfCancellationRequested();
                _schedule.RecordSuccess();
                result = result with { CheckedAt = _schedule.Now, UpdatedAt = _schedule.Now.ToUniversalTime(), IsStale = false, FreshnessNote = null };
                _nextResetAt = EarliestReset(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _schedule.RecordFailure();
                result = Degrade(exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Publish(result);
            return result;
        }
        finally { _gate.Release(); }
    }

    private async Task<QuotaSnapshot> QueryAsync(CancellationToken cancellationToken)
    {
        _queryStage = "连接 Codex 查询服务";
        if (_factory is null)
            throw new QuotaSessionException("cli-missing");

        var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        _queryStage = "读取登录账号";
        var accountJson = await session.InvokeAsync("account/read", """{"refreshToken":false}""", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new QuotaSessionException("account-unavailable");
        var account = QuotaResponseParser.ReadAccount(accountJson);

        // 若 StableId 仅来自额度响应，先检查 account/read 中已经可知的字段；
        // 未变时等到 rateLimits 返回才能比较完整身份。查询失败则清除不再可确认的缓存。
        var previous = _account;
        var needsRateIdentity = account.StableId is null && previous?.StableId is not null &&
            (account with { StableId = previous.StableId }).SameIdentity(previous);
        if (!needsRateIdentity) SetAccount(account);

        // 只有 ChatGPT 登录才有订阅额度窗口；其余登录方式如实说明不支持或未登录。
        if (account.Kind != QuotaAccountKind.ChatGpt)
            return QuotaResponseParser.Build(account, null, _schedule.Now);

        string? rateLimits;
        try
        {
            _queryStage = "读取额度";
            rateLimits = await session.InvokeAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
            account = QuotaResponseParser.ReadAccount(accountJson, rateLimits);
            // 额度响应不保证带账号 ID；用第二次只读 account/read 封住两次调用间的换号窗口。
            _queryStage = "核对账号";
            var confirmedJson = await session.InvokeAsync("account/read", """{"refreshToken":false}""", cancellationToken)
                .ConfigureAwait(false) ?? throw new QuotaSessionException("account-unavailable");
            var confirmed = QuotaResponseParser.ReadAccount(confirmedJson, rateLimits);
            if (!account.SameIdentity(confirmed))
            {
                SetAccount(confirmed);
                throw new QuotaSessionException("account-changed");
            }
            SetAccount(account);
        }
        catch (QuotaSessionException exception) when (exception.Category == "account-identity-conflict")
        {
            SetAccount(QuotaAccount.None);
            throw;
        }
        catch (QuotaSessionException exception) when (exception.Category == "account-changed") { throw; }
        catch
        {
            if (needsRateIdentity) SetAccount(account);
            throw;
        }
        return QuotaResponseParser.Build(account, rateLimits, _schedule.Now);
    }

    private void SetAccount(QuotaAccount account)
    {
        var previous = _account;
        _account = account;
        if (previous is not null && (!account.IsIdentifiable || !previous.SameIdentity(account)))
        {
            _nextResetAt = null;
            Publish(QuotaSnapshot.Unavailable("检测到额度账号已切换，正在重新查询。", _schedule.Now, "account-changed")
                with { Identity = account, UpdatedAt = _schedule.Now.ToUniversalTime() });
        }
    }

    private async Task<IQuotaSession> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        var configurationKey = _factory!.ConfigurationKey;
        if (_session is not null && !string.Equals(_sessionConfigurationKey, configurationKey, StringComparison.Ordinal))
        {
            var previous = _session;
            _session = null;
            previous.Notification -= OnNotification;
            await previous.DisposeAsync().ConfigureAwait(false);
        }
        if (_session is { } existing) return existing;
        var opened = await _factory!.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            await opened.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        opened.Notification += OnNotification;
        _session = opened;
        _sessionConfigurationKey = configurationKey;
        return opened;
    }

    private void OnNotification(string method)
    {
        if (!string.Equals(method, "account/rateLimits/updated", StringComparison.Ordinal)) return;
        // 另一进程推送的额度变化只表示“可能已更新”，触发一次到期查询，不假设推送包含全部用量。
        if (Interlocked.Exchange(ref _refreshRequested, 1) == 0) TryWake();
    }

    private void TryWake()
    {
        try { if (_wake.CurrentCount == 0) _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    /// <summary>失败保留当前账号的上次结果并标明新鲜度；没有历史数据时如实显示暂不可用。</summary>
    private QuotaSnapshot Degrade(Exception exception)
    {
        var category = exception switch
        {
            QuotaSessionException session => session.Category,
            TimeoutException => "timeout",
            _ => exception.GetType().Name
        };
        DropSession();

        var now = _schedule.Now;
        var previous = Snapshot;
        if (previous.HasData)
        {
            var age = _schedule.Age;
            var stale = _schedule.IsStale;
            var note = stale
                ? $"数据可能已过期 · 上次更新 {previous.CheckedAt:HH:mm}"
                : $"数据未更新 · 上次更新 {previous.CheckedAt:HH:mm}";
            if (age is not null) note += $"（{age.Value.TotalMinutes:0} 分钟前）";
            return previous with { IsStale = stale, FreshnessNote = note, FailureCategory = category,
                UpdatedAt = now.ToUniversalTime() };
        }

        var reason = category switch
        {
            "cli-missing" => "未找到配套 Codex CLI",
            "timeout" => "请求超时（单次最多 10 秒），可点击刷新重试",
            "process-exited" or "session-disposed" => "查询进程已退出，可点击刷新重试",
            "account-identity-conflict" or "account-changed" => "账号信息发生变化，请核对配套 CLI 的登录账号后重试",
            _ when category.StartsWith("rpc-error", StringComparison.Ordinal) => "额度接口返回错误，可点击刷新重试",
            _ => "查询失败，可点击刷新重试"
        };
        var detail = $"{_queryStage}失败：{reason}。诊断码：{category}";
        return QuotaSnapshot.Unavailable(detail, now, category) with
        { Identity = _account ?? QuotaAccount.None, UpdatedAt = now.ToUniversalTime() };
    }

    private void DropSession()
    {
        var session = _session;
        _session = null;
        if (session is null) return;
        session.Notification -= OnNotification;
        _ = Task.Run(async () =>
        {
            try { await session.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
        });
    }

    private static DateTimeOffset? EarliestReset(QuotaSnapshot snapshot)
    {
        DateTimeOffset? earliest = null;
        foreach (var bucket in snapshot.Buckets)
        foreach (var window in bucket.Windows)
        {
            if (window.ResetsAt is not { } reset) continue;
            if (earliest is null || reset < earliest) earliest = reset;
        }
        return earliest;
    }

    private void Publish(QuotaSnapshot snapshot)
    {
        lock (_sync)
        {
            if (_disposed || _stopped || _lifetime.IsCancellationRequested) return;
            _snapshot = snapshot;
        }
        QuotaChanged?.Invoke(snapshot);
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReadAsync(cancellationToken).ConfigureAwait(false);
                Interlocked.Exchange(ref _refreshRequested, 0);
                var delay = _schedule.DelayUntilDue(_nextResetAt);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = _delay(delay, linked.Token);
                var wakeTask = _wake.WaitAsync(linked.Token);
                try
                {
                    var first = await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);
                    if (first != wakeTask) await delayTask.ConfigureAwait(false);
                }
                finally
                {
                    linked.Cancel();
                    Observe(delayTask);
                    Observe(wakeTask);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception) { }
        }
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);

    /// <summary>终止所有自有上游工作；不是暂停轮询。重启需建立新 provider。等待取消不撤回停止。</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _stopped = true;
            _shutdownTask ??= Task.Run(StopCoreAsync);
            return _shutdownTask.WaitAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        CancellationTokenSource? loop;
        Task? loopTask;
        Task<QuotaSnapshot>? inflight;
        lock (_sync)
        {
            loop = _loop;
            loopTask = _loopTask;
            inflight = _inflight;
            _loop = null;
            _loopTask = null;
        }
        if (loop is not null) await loop.CancelAsync().ConfigureAwait(false);
        if (loopTask is not null) { try { await loopTask.ConfigureAwait(false); } catch (Exception) { } }
        if (inflight is not null) { try { await inflight.ConfigureAwait(false); } catch (Exception) { } }
        loop?.Dispose();

        var session = _session;
        _session = null;
        if (session is not null)
        {
            session.Notification -= OnNotification;
            try { await session.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _wake.Dispose();
        _gate.Dispose();
        _lifetime.Dispose();
    }
}
