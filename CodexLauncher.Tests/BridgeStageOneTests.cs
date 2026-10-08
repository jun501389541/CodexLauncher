using CodexLauncher.Core;
using System.Text.Json;

internal static class BridgeStageOneTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
    [
        ("bridge preserves raw percentages, unique window ids and source UTC timestamps", ParseTruth),
        ("bridge rejects type-only identity and reads stable account ids", StableIdentity),
        ("cancelling first quota waiter does not cancel other consumers or poison future reads", () => CancellationIsolation().GetAwaiter().GetResult()),
        ("provider disposal cancels an upstream read without publishing late data", () => DisposalCancelsUpstream().GetAwaiter().GetResult()),
        ("bridge contracts redact identities and preserve timestamps across failures", Contracts),
        ("bridge publishes reset-credit fields in the Android-compatible wire shape", ResetCreditContract),
        ("bridge invalidates grants on account changes including switching back", () => Grants().GetAwaiter().GetResult()),
        ("disabled shared coordinator does not open an upstream session", () => DisabledCoordinator().GetAwaiter().GetResult()),
        ("stable rate-response identity clears old quota before a failed new-account query", () => StableAccountSwitch().GetAwaiter().GetResult())
        ,("shutdown ignores a session error caused by teardown rather than publishing late data", () => ShutdownSessionError().GetAwaiter().GetResult())
        ,("usage JSON fields match the independent published schema", SchemaContract)
        ,("owned cancellation promptly kills a stalled real stdio handshake", () => HandshakeCancellation().GetAwaiter().GetResult())
        ,("stopping a provider cancels an opening session and prevents late publication", () => StopOpeningSession().GetAwaiter().GetResult())
        ,("conflicting account and rate identities cannot expose another account quota", () => ConflictingIdentities().GetAwaiter().GetResult())
        ,("account switch between quota reads is detected without a rate-root identity", () => InterleavedAccountSwitch().GetAwaiter().GetResult())
    ];

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static T? Property<T>(object value, string name)
    {
        var property = value.GetType().GetProperty(name);
        Check(property is not null, $"missing {value.GetType().Name}.{name}");
        return (T?)property!.GetValue(value);
    }

    private static void ParseTruth()
    {
        var now = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        var account = QuotaResponseParser.ReadAccount("""{"account":{"type":"chatgpt","email":"synthetic@example.invalid"}}""");
        var snapshot = QuotaResponseParser.Build(account, """
            {"sourceTimestamp":"2026-10-02T15:59:00+08:00","rateLimitsByLimitId":{
              "bucket-a":{"primary":{"usedPercent":125,"windowDurationMins":300,"resetsAt":1790928000},"secondary":{"usedPercent":null}},
              "bucket-b":{"primary":{"usedPercent":-12},"secondary":{"usedPercent":12.5}}
            }}
            """, now);
        var windows = snapshot.Buckets.SelectMany(b => b.Windows).ToArray();
        Check(Property<double?>(windows[0], "UsedPercent") == 125, "raw overage lost");
        Check(windows[0].RemainingPercent == 0, "UI remaining must stay clamped");
        Check(Property<double?>(windows[1], "UsedPercent") is null, "null must stay unknown");
        Check(Property<double?>(windows[2], "UsedPercent") == -12, "negative raw data lost");
        Check(Property<double?>(windows[3], "UsedPercent") == 12.5, "fractional raw data lost");
        Check(windows.Select(w => Property<string>(w, "Id")).Distinct().Count() == 4, "window ids collide");
        Check(windows[0].ResetsAt?.Offset == TimeSpan.Zero, "reset must remain UTC");
        Check(Property<DateTimeOffset?>(snapshot, "SourceTimestamp") == DateTimeOffset.Parse("2026-10-02T07:59:00Z"), "source timestamp lost");
        Check(Property<DateTimeOffset?>(snapshot, "SourceTimestamp")?.Offset == TimeSpan.Zero, "source must be UTC");
        Check(Property<DateTimeOffset?>(QuotaResponseParser.Build(account, """{"rateLimits":{}}""", now), "SourceTimestamp") is null, "source time invented");
        Check(windows[0].ResetLabel.Contains(windows[0].ResetsAt!.Value.ToLocalTime().ToString("MM-dd HH:mm")), "desktop reset label regressed to UTC");
    }

    private static void StableIdentity()
    {
        var account = QuotaResponseParser.ReadAccount("""{"account":{"type":"apiKey","id":"stable-one"}}""");
        Check(Property<string>(account, "StableId") == "stable-one", "stable id missing");
        Check(Property<bool>(account, "IsIdentifiable"), "stable identity not recognized");
        Check(!Property<bool>(QuotaResponseParser.ReadAccount("""{"account":{"type":"apiKey"}}"""), "IsIdentifiable"), "type alone is not a user identity");
        Check(!Property<bool>(QuotaAccount.None, "IsIdentifiable"), "unknown identity accepted");
    }

    private static async Task CancellationIsolation()
    {
        var factory = new BlockingQuotaFactory();
        await using var provider = new CodexQuotaProvider(factory);
        using var client = new CancellationTokenSource();
        var first = provider.ReadAsync(client.Token);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = provider.ReadAsync(CancellationToken.None);
        client.Cancel();
        try { await first.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("cancelled waiter returned data"); }
        catch (OperationCanceledException) { }
        Check(!factory.UpstreamToken.IsCancellationRequested, "HTTP cancellation reached upstream");
        factory.Release.TrySetResult();
        Check((await second.WaitAsync(TimeSpan.FromSeconds(3))).HasData, "second consumer lost data");
        Check(factory.Reads == 2, "consumers must share one query with two identity checks");
        Check((await provider.ReadAsync(CancellationToken.None)).HasData, "next request poisoned");
        Check(factory.Reads == 4, "completed flight not replaced");
    }

    private static async Task DisposalCancelsUpstream()
    {
        var factory = new BlockingQuotaFactory();
        var provider = new CodexQuotaProvider(factory);
        var published = 0;
        provider.QuotaChanged += _ => published++;
        var read = provider.ReadAsync(CancellationToken.None);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await provider.DisposeAsync();
        Check(factory.UpstreamToken.IsCancellationRequested, "owned lifecycle did not cancel upstream");
        try { await read; throw new Exception("shutdown read returned data"); }
        catch (OperationCanceledException) { }
        Check(published == 0, "late data published after disposal");
    }

    private static void Contracts()
    {
        var now = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        var identity = new QuotaAccount(QuotaAccountKind.ChatGpt, "secret@example.invalid", "plus", true, "platform-workspace", "platform-id");
        var snapshot = QuotaResponseParser.Build(identity, """{"rateLimits":{"primary":{"usedPercent":12.5}}}""", now);
        var dto = UsageResultMapper.Map("synthetic-bridge", "anonymous-id", "Codex账号", snapshot);
        var json = JsonSerializer.Serialize(dto, BridgeJson.Options);
        Check(!json.Contains("secret") && !json.Contains("platform") && !json.Contains("AccountEmail"), "wire leaked identity");
        Check(dto.Status == "OK" && dto.SourceTimestamp is null && dto.DataTimestamp == now, "timestamps/status wrong");
        Check(dto.QuotaWindows.Single().RemainingPercent == 87.5, "fractional remaining lost");
        Check(UsageResultMapper.Map("b", "a", "n", snapshot with { IsStale = true }).Status == "STALE", "stale not mapped");
        foreach (var (availability, category, status) in new[] {
            (QuotaAvailability.NotLoggedIn, (string?)null, "AUTH_REQUIRED"),
            (QuotaAvailability.Unsupported, (string?)null, "UNSUPPORTED"),
            (QuotaAvailability.Unavailable, "timeout", "NETWORK_ERROR"),
            (QuotaAvailability.Unavailable, "HttpRequestException", "NETWORK_ERROR"),
            (QuotaAvailability.Unavailable, "secret raw diagnostic", "NO_DATA") })
        {
            var value = UsageResultMapper.Map("b", "a", "n", snapshot with { Availability = availability, FailureCategory = category });
            Check(value.Status == status, $"bad {status} mapping");
            Check(value.QuotaWindows.Count == 0 && value.DataTimestamp is null, "unavailable status carried data");
            Check(!JsonSerializer.Serialize(value, BridgeJson.Options).Contains("secret"), "raw diagnostic leaked");
        }
        var disabled = UsageResultMapper.Map("b", "a", "n", snapshot, monitoringEnabled: false);
        Check(disabled.Status == "NO_DATA" && disabled.ErrorCode == "QUOTA_MONITORING_DISABLED" && disabled.QuotaWindows.Count == 0, "disabled monitoring leaked cache");
    }

    private static void ResetCreditContract()
    {
        var now = DateTimeOffset.Parse("2026-10-08T12:00:00Z");
        var expiry = DateTimeOffset.Parse("2026-11-07T06:40:00Z").ToUnixTimeSeconds();
        var account = new QuotaAccount(QuotaAccountKind.ChatGpt, null, "plus", true, null, "synthetic");
        var source = "{\"rateLimits\":{\"primary\":{\"usedPercent\":25}},\"rateLimitResetCredits\":{\"availableCount\":3,\"credits\":["
            + "{\"title\":\"Full reset\",\"description\":\"Weekly + 5 hr\",\"resetType\":\"codexRateLimits\",\"status\":\"available\",\"expiresAt\":"
            + expiry.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
            + "{\"title\":\"Unknown expiry\",\"description\":null,\"resetType\":\"codexRateLimits\",\"status\":\"available\"},"
            + "{\"title\":\"No expiry\",\"description\":null,\"resetType\":\"codexRateLimits\",\"status\":\"available\",\"expiresAt\":null}]}}";
        var snapshot = QuotaResponseParser.Build(account, source, now);
        var mapped = UsageResultMapper.Map("bridge", "account", "Codex账号", snapshot);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(mapped, BridgeJson.Options));
        var root = wire.RootElement;
        Check(root.GetProperty("rateLimitResetCreditsStatus").GetString() == "AVAILABLE", "available reset cards did not carry their status");
        var cards = root.GetProperty("rateLimitResetCredits");
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge-contracts", "usage-result.schema.json")));
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        Check(!required.Contains("rateLimitResetCredits") && !required.Contains("rateLimitResetCreditsStatus"), "new reset-card fields must remain optional for existing v1 clients");
        Check(schema.RootElement.GetProperty("properties").TryGetProperty("rateLimitResetCredits", out _), "reset-card payload is missing from the published schema");
        var cardShape = schema.RootElement.GetProperty("$defs").GetProperty("RateLimitResetCredit");
        var cardAllowed = cardShape.GetProperty("properties").EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var cardRequired = cardShape.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
        Check(cardRequired.SetEquals(["resetType", "status", "title", "description"]) && cardAllowed.SetEquals(["resetType", "status", "title", "description", "expiresAt"]),
            "reset-card detail schema does not match the Android reader fields");
        Check(cards.GetProperty("availableCount").GetInt64() == 3, "authoritative reset-card count was lost");
        var rows = cards.GetProperty("credits");
        Check(rows.GetArrayLength() == 3, "reset-card details were not forwarded");
        var knownExpiry = rows[0];
        Check(knownExpiry.GetProperty("expiresAt").GetString() == "2026-11-07T06:40:00.0000000Z", "reset-card expiry was not serialized as UTC ISO-8601");
        Check(!knownExpiry.TryGetProperty("titleLabel", out _) && !knownExpiry.TryGetProperty("id", out _), "UI helpers or redeemable identifiers leaked into the Bridge response");
        Check(!rows[1].TryGetProperty("expiresAt", out _), "unknown expiry was serialized as no-expiry");
        Check(rows[2].GetProperty("expiresAt").ValueKind == JsonValueKind.Null, "known no-expiry was confused with unknown expiry");

        var countOnly = QuotaResponseParser.Build(account,
            """{"rateLimits":{"primary":{"usedPercent":25}},"rateLimitResetCredits":{"availableCount":4}}""", now);
        using var countOnlyWire = JsonDocument.Parse(JsonSerializer.Serialize(UsageResultMapper.Map("b", "a", "n", countOnly), BridgeJson.Options));
        Check(countOnlyWire.RootElement.GetProperty("rateLimitResetCredits").GetProperty("credits").ValueKind == JsonValueKind.Null,
            "count-only reset-card data was changed into an empty list");
        var cardsWithoutWindows = QuotaResponseParser.Build(account,
            """{"rateLimitsByLimitId":{},"rateLimitResetCredits":{"availableCount":3,"credits":[]}}""", now);
        var cardsOnlyResult = UsageResultMapper.Map("b", "a", "n", cardsWithoutWindows);
        Check(cardsOnlyResult.RateLimitResetCredits?.AvailableCount == 3 && cardsOnlyResult.RateLimitResetCreditsStatus == "AVAILABLE",
            "valid reset cards disappeared when quota windows were absent");
        Check(UsageResultMapper.Map("b", "a", "n", cardsWithoutWindows, false).RateLimitResetCredits is null,
            "disabled monitoring exposed reset cards");
        var zero = QuotaResponseParser.Build(account,
            """{"rateLimits":{"primary":{"usedPercent":25}},"rateLimitResetCredits":{"availableCount":0,"credits":[]}}""", now);
        using var zeroWire = JsonDocument.Parse(JsonSerializer.Serialize(UsageResultMapper.Map("b", "a", "n", zero), BridgeJson.Options));
        var zeroCredits = zeroWire.RootElement.GetProperty("rateLimitResetCredits");
        Check(zeroCredits.GetProperty("availableCount").GetInt64() == 0 && zeroCredits.GetProperty("credits").GetArrayLength() == 0,
            "known zero reset cards were confused with unknown detail");

        var missing = QuotaResponseParser.Build(account, """{"rateLimits":{"primary":{"usedPercent":25}}}""", now);
        using var missingWire = JsonDocument.Parse(JsonSerializer.Serialize(UsageResultMapper.Map("b", "a", "n", missing), BridgeJson.Options));
        Check(missingWire.RootElement.GetProperty("rateLimitResetCreditsStatus").GetString() == "NOT_RETURNED",
            "missing reset-card data was not distinguished from an older Bridge");
        Check(!missingWire.RootElement.TryGetProperty("rateLimitResetCredits", out _),
            "missing reset-card details were emitted as a malformed null object");
        var malformed = QuotaResponseParser.Build(account,
            """{"rateLimits":{"primary":{"usedPercent":25}},"rateLimitResetCredits":{"availableCount":"many"}}""", now);
        using var malformedWire = JsonDocument.Parse(JsonSerializer.Serialize(UsageResultMapper.Map("b", "a", "n", malformed), BridgeJson.Options));
        Check(malformedWire.RootElement.GetProperty("rateLimitResetCreditsStatus").GetString() == "INVALID_FORMAT",
            "malformed reset-card data was not reported to Android");

        const string oldPayload = """{"schemaVersion":1,"bridgeId":"b","providerId":"codex","accountId":"a","displayName":"n","status":"NO_DATA","quotaWindows":[],"updatedAt":"2026-10-08T12:00:00Z","dataTimestamp":null,"sourceTimestamp":null,"isStale":false,"errorCode":null}""";
        var legacy = JsonSerializer.Deserialize<UsageResult>(oldPayload, BridgeJson.Options);
        Check(legacy is not null && typeof(UsageResult).GetProperty("RateLimitResetCredits")?.GetValue(legacy) is null &&
            typeof(UsageResult).GetProperty("RateLimitResetCreditsStatus")?.GetValue(legacy) is null,
            "the additive reset-card fields broke deserialization of a v1 payload");
    }

    private static async Task Grants()
    {
        var factory = new MutableQuotaFactory();
        await using var coordinator = new SharedQuotaCoordinator(factory);
        using var grants = new BridgeAccountGrantStore("synthetic-bridge", Enumerable.Repeat((byte)7, 32).ToArray(), coordinator);
        await coordinator.ReadAsync(CancellationToken.None);
        Check(grants.ConfirmCurrentAccount("phone-1"), "identifiable account not grantable");
        var a = grants.Accounts("phone-1").Value!.Single().AccountId;
        Check(grants.Usage("phone-1", a).StatusCode == 200, "approved device denied");
        Check(grants.Usage("phone-2", a).StatusCode == 403, "unapproved device allowed");
        factory.Account = """{"account":{"type":"chatgpt","id":"account-b"}}""";
        await coordinator.ReadAsync(CancellationToken.None);
        Check(grants.Usage("phone-1", a).StatusCode == 409, "stale account id not rejected");
        Check(grants.Accounts("phone-1").StatusCode == 403, "account switch silently reauthorized");
        Check(grants.ConfirmCurrentAccount("phone-1"), "new account not grantable");
        factory.Account = """{"account":{"type":"chatgpt","id":"account-a"}}""";
        await coordinator.ReadAsync(CancellationToken.None);
        Check(grants.Accounts("phone-1").StatusCode == 403, "switching back restored old grant against D-18");
        Check(grants.ConfirmCurrentAccount("phone-1"), "reconfirmation failed");
        Check(grants.Accounts("phone-1").Value!.Single().AccountId == a, "HMAC mapping unstable");
        grants.Revoke("phone-1");
        Check(grants.Usage("phone-1", a).StatusCode == 403, "revoked grant remains active");
        factory.Account = """{"account":{"type":"apiKey"}}""";
        await coordinator.ReadAsync(CancellationToken.None);
        Check(!grants.ConfirmCurrentAccount("phone-1"), "type-only account grant accepted");
        Check(grants.Accounts("phone-1").Value!.Count == 0, "unknown identity returned account");
    }

    private static async Task DisabledCoordinator()
    {
        var factory = new MutableQuotaFactory();
        await using var coordinator = new SharedQuotaCoordinator(factory, monitoringEnabled: false);
        coordinator.Start();
        await coordinator.ReadAsync(CancellationToken.None);
        await coordinator.RequestManualAsync(CancellationToken.None);
        Check(factory.Opens == 0, "disabled coordinator opened session");
        Check(coordinator.Snapshot.FailureCategory == "quota-monitoring-disabled", "disabled reason missing");
    }

    private static async Task StableAccountSwitch()
    {
        var factory = new MutableQuotaFactory { Account = """{"account":{"type":"chatgpt"}}""", Limits = """{"accountId":"a","rateLimits":{"primary":{"usedPercent":15}}}""" };
        await using var provider = new CodexQuotaProvider(factory);
        var first = await provider.ReadAsync(CancellationToken.None);
        Check(first.HasData && first.Identity.StableId == "a", "rate-root identity missing");
        var changed = 0;
        provider.QuotaChanged += q => { if (q.FailureCategory == "account-changed") changed++; };
        await provider.ReadAsync(CancellationToken.None);
        Check(changed == 0, "unchanged rate-root identity repeatedly invalidated");
        factory.Limits = """{"accountId":"b","rateLimits":{"primary":{"usedPercent":80}}}""";
        await provider.ReadAsync(CancellationToken.None);
        Check(changed == 1 && provider.Snapshot.Identity.StableId == "b", "stable-id-only switch missed");
        factory.Account = """{"account":{"type":"chatgpt","id":"c"}}""";
        factory.FailLimits = true;
        var failed = await provider.ReadAsync(CancellationToken.None);
        Check(!failed.HasData && failed.Identity.StableId == "c", "old account quota leaked after failed switch");
    }

    private static async Task ShutdownSessionError()
    {
        var factory = new BlockingQuotaFactory { ErrorOnCancel = true };
        var provider = new CodexQuotaProvider(factory);
        var published = 0;
        provider.QuotaChanged += _ => published++;
        var read = provider.ReadAsync(CancellationToken.None);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await provider.DisposeAsync();
        Check(published == 0, "session teardown published a late failure");
        try { await read; throw new Exception("teardown returned snapshot rather than cancellation"); }
        catch (OperationCanceledException) { }
    }

    private static void SchemaContract()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge-contracts", "usage-result.schema.json")));
        var now = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        var snapshot = QuotaResponseParser.Build(new QuotaAccount(QuotaAccountKind.ChatGpt, null, null, true, null, "synthetic"),
            """{"sourceTimestamp":"2026-10-02T07:59:00Z","rateLimits":{"primary":{"usedPercent":125}}}""", now);
        var value = UsageResultMapper.Map("synthetic", "anonymous", "Codex账号", snapshot);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(value, BridgeJson.Options));
        CheckFields(schema.RootElement, wire.RootElement);
        CheckFields(schema.RootElement.GetProperty("$defs").GetProperty("UsageWindow"), wire.RootElement.GetProperty("quotaWindows")[0]);
        Check(wire.RootElement.GetProperty("updatedAt").GetString()!.EndsWith('Z'), "wire timestamp not UTC RFC3339");
        Check(wire.RootElement.GetProperty("sourceTimestamp").GetString()!.EndsWith('Z'), "source wire timestamp not UTC RFC3339");
        Check(wire.RootElement.GetProperty("quotaWindows")[0].GetProperty("usedPercent").GetDouble() == 125, "wire raw overage lost");
        static void CheckFields(JsonElement shape, JsonElement value)
        {
            var actual = value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var required = shape.GetProperty("required").EnumerateArray().Select(p => p.GetString()!).ToHashSet(StringComparer.Ordinal);
            var allowed = shape.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            Check(required.IsSubsetOf(actual) && actual.IsSubsetOf(allowed), "serialized fields diverge from published contract");
        }
    }

    private static async Task HandshakeCancellation()
    {
        var executable = Environment.ProcessPath!;
        var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? System.Reflection.Assembly.GetExecutingAssembly().Location + " " : "";
        using var lifecycle = new CancellationTokenSource();
        var factory = new CodexAppServerSessionFactory(executable, prefix + "--fake-app-server --mode handshake-timeout",
            handshakeTimeout: TimeSpan.FromSeconds(10));
        var opening = factory.OpenAsync(lifecycle.Token);
        lifecycle.Cancel();
        try
        {
            var session = await opening.WaitAsync(TimeSpan.FromSeconds(3));
            await session.DisposeAsync();
            throw new Exception("cancelled handshake completed normally");
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException)
        {
            // 不遗留失败测试的子进程，等待其内置握手超时完成清理。
            try { await opening; } catch (Exception) { }
            throw new Exception("owned cancellation did not stop handshake within 3 seconds");
        }
    }

    private static async Task ConflictingIdentities()
    {
        var factory = new MutableQuotaFactory();
        await using var coordinator = new SharedQuotaCoordinator(factory);
        using var grants = new BridgeAccountGrantStore("b", Enumerable.Repeat((byte)8, 32).ToArray(), coordinator);
        await coordinator.ReadAsync(CancellationToken.None);
        Check(grants.ConfirmCurrentAccount("phone"), "initial grant failed");
        var id = grants.Accounts("phone").Value!.Single().AccountId;
        factory.Limits = """{"accountId":"account-b","rateLimits":{"primary":{"usedPercent":80}}}""";
        var mixed = await coordinator.ReadAsync(CancellationToken.None);
        Check(!mixed.HasData && mixed.FailureCategory == "account-identity-conflict", "quota from B accepted under identity A");
        Check(grants.Usage("phone", id).StatusCode == 403, "conflicting response retained A's authorization/cache");
        Check(!grants.ConfirmCurrentAccount("phone"), "conflicting identity remained grantable");
    }

    private static async Task InterleavedAccountSwitch()
    {
        var factory = new MutableQuotaFactory();
        await using var coordinator = new SharedQuotaCoordinator(factory);
        using var grants = new BridgeAccountGrantStore("b", Enumerable.Repeat((byte)9, 32).ToArray(), coordinator);
        await coordinator.ReadAsync(CancellationToken.None);
        Check(grants.ConfirmCurrentAccount("phone"), "initial grant failed");
        var id = grants.Accounts("phone").Value!.Single().AccountId;
        factory.SwitchDuringLimits = true;
        var mixed = await coordinator.ReadAsync(CancellationToken.None);
        Check(!mixed.HasData && mixed.FailureCategory == "account-changed", "mid-read account switch went undetected");
        Check(grants.Usage("phone", id).StatusCode == 409, "old account request survived interleaved switch");
        Check(grants.Accounts("phone").StatusCode == 403, "new account automatically authorized");
    }

    private static async Task StopOpeningSession()
    {
        var factory = new OpeningQuotaFactory();
        await using var provider = new CodexQuotaProvider(factory);
        var published = 0;
        provider.QuotaChanged += _ => published++;
        var reading = provider.ReadAsync(CancellationToken.None);
        await factory.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await provider.StopAsync(CancellationToken.None);
        factory.Release.TrySetResult();
        try { await reading; } catch (OperationCanceledException) { }
        Check(factory.Token.IsCancellationRequested && factory.Disposals == 1 && published == 0,
            "stop left an opening session or published late data");
    }

    private sealed class OpeningQuotaFactory : IQuotaSessionFactory, IQuotaSession
    {
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token;
        internal int Disposals;
        public event Action<string>? Notification { add { } remove { } }
        public async Task<IQuotaSession> OpenAsync(CancellationToken token)
        {
            Token = token;
            Entered.TrySetResult();
            try { await Release.Task.WaitAsync(token); }
            catch (OperationCanceledException) { }
            // 模拟实际会话在取消竞态中仍然返回，所有者必须主动清理。
            return this;
        }
        public Task<string?> InvokeAsync(string method, string? parameters, CancellationToken token) => Task.FromResult<string?>(
            method == "account/read" ? """{"account":{"type":"chatgpt","id":"late"}}""" : """{"rateLimits":{"primary":{"usedPercent":20}}}""");
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }

    private sealed class MutableQuotaFactory : IQuotaSessionFactory, IQuotaSession
    {
        internal string Account = """{"account":{"type":"chatgpt","id":"account-a"}}""";
        internal string Limits = """{"rateLimits":{"primary":{"usedPercent":25}}}""";
        internal bool FailLimits;
        internal bool SwitchDuringLimits;
        internal int Opens;
        public event Action<string>? Notification { add { } remove { } }
        public Task<IQuotaSession> OpenAsync(CancellationToken token) { Opens++; return Task.FromResult<IQuotaSession>(this); }
        public Task<string?> InvokeAsync(string method, string? parameters, CancellationToken token)
        {
            if (method != "account/read" && SwitchDuringLimits)
                Account = """{"account":{"type":"chatgpt","id":"account-b"}}""";
            return Task.FromResult<string?>(method == "account/read" ? Account : FailLimits ? throw new QuotaSessionException("synthetic-failure") : Limits);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingQuotaFactory : IQuotaSessionFactory, IQuotaSession
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken UpstreamToken;
        internal int Reads;
        internal bool ErrorOnCancel;
        public event Action<string>? Notification { add { } remove { } }
        public Task<IQuotaSession> OpenAsync(CancellationToken token) => Task.FromResult<IQuotaSession>(this);
        public async Task<string?> InvokeAsync(string method, string? parameters, CancellationToken token)
        {
            if (method == "account/read")
            {
                Reads++;
                UpstreamToken = token;
                Entered.TrySetResult();
                try { await Release.Task.WaitAsync(token); }
                catch (OperationCanceledException) when (ErrorOnCancel) { throw new QuotaSessionException("session-disposed"); }
                return """{"account":{"type":"chatgpt","id":"synthetic-account"}}""";
            }
            return """{"rateLimits":{"primary":{"usedPercent":25}}}""";
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
