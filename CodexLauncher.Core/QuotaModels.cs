using System.Globalization;
using System.Text.Json;

namespace CodexLauncher.Core;

/// <summary>额度查询的可用性。未知数据一律使用 <see cref="Unavailable"/>，不伪造为零。</summary>
public enum QuotaAvailability
{
    /// <summary>拿到了可展示的额度窗口。</summary>
    Available,

    /// <summary>登录方式不提供订阅额度，例如 API Key。</summary>
    Unsupported,

    /// <summary>配套 CLI 尚未登录，需要用户自己处理。</summary>
    NotLoggedIn,

    /// <summary>CLI 缺失、请求失败或超时，当前没有可展示的数据。</summary>
    Unavailable
}

public enum QuotaAccountKind
{
    ChatGpt,
    ApiKey,
    Bedrock,
    Unknown
}

/// <summary>额度账号与桌面账号的一致性。启动器不读取桌面账号，因此默认未知。</summary>
public enum QuotaAccountConsistency
{
    Unconfirmed,
    Confirmed,
    Mismatch
}

public enum QuotaLevel
{
    Normal,
    Low,
    Critical,
    Unknown
}

/// <summary>一个额度窗口：长度、剩余百分比与重置时间。百分比缺失时为 null，界面显示“暂不可用”。</summary>
public sealed record QuotaWindow(
    string Name,
    string DurationLabel,
    long? DurationMinutes,
    int? RemainingPercent,
    DateTimeOffset? ResetsAt,
    string? Note = null,
    double? UsedPercent = null,
    string? Id = null)
{
    public bool IsKnown => RemainingPercent is not null;

    public string RemainingLabel => RemainingPercent is null
        ? "暂不可用"
        : string.Create(CultureInfo.InvariantCulture, $"{RemainingPercent}%");

    public string ResetLabel => ResetsAt is null
        ? "重置时间未知"
        : $"重置 {ResetsAt.Value.ToLocalTime():MM-dd HH:mm}";

    /// <summary>倒计时文案；到点后显示“等待确认重置”，不自行填成 100%。</summary>
    public string CountdownLabel(DateTimeOffset now)
    {
        if (ResetsAt is null) return "倒计时未知";
        var left = ResetsAt.Value - now;
        if (left <= TimeSpan.Zero) return "等待确认重置";
        if (left.TotalHours >= 1) return $"还剩 {(int)left.TotalHours} 小时 {left.Minutes} 分";
        if (left.TotalMinutes >= 1) return $"还剩 {left.Minutes} 分";
        return $"还剩 {Math.Max(1, (int)left.TotalSeconds)} 秒";
    }

    public string Describe(DateTimeOffset now) =>
        $"{Name}（{DurationLabel}）剩余 {RemainingLabel} · {ResetLabel} · {CountdownLabel(now)}";

    /// <summary>条状图的标题：窗口长度决定名字（“5 小时限额”“每周限额”），长度未知时退回窗口名。</summary>
    public string TitleLabel => DurationMinutes is null ? Name : $"{DurationLabel}限额";

    /// <summary>条状图右侧的剩余量；百分比缺失时明确写“未知”，不伪造成 0%。</summary>
    public string RemainingSummary => RemainingPercent is null
        ? "剩余未知"
        : string.Create(CultureInfo.InvariantCulture, $"剩余 {RemainingPercent}%");

    /// <summary>
    /// 条状图左侧的重置倒计时，措辞与 Codex 一致（“4 小时 38 分钟后重置”）。
    /// 到点后仍显示“等待确认重置”，不自行填成 100%。
    /// </summary>
    public string ResetCountdownLabel(DateTimeOffset now)
    {
        if (ResetsAt is null) return "重置时间未知";
        var left = ResetsAt.Value - now;
        if (left <= TimeSpan.Zero) return "等待确认重置";
        if (left.TotalDays >= 1) return $"{(int)left.TotalDays} 天 {left.Hours} 小时后重置";
        if (left.TotalHours >= 1) return $"{(int)left.TotalHours} 小时 {left.Minutes} 分钟后重置";
        if (left.TotalMinutes >= 1) return $"{left.Minutes} 分钟后重置";
        return $"{Math.Max(1, (int)left.TotalSeconds)} 秒后重置";
    }
}

public sealed record QuotaBucket(
    string LimitId,
    string DisplayName,
    IReadOnlyList<QuotaWindow> Windows,
    bool Restricted = false,
    string? RestrictionReason = null);

public sealed record QuotaAccount(
    QuotaAccountKind Kind,
    string? Email,
    string? PlanType,
    bool RequiresOpenAiAuth,
    string? WorkspaceAccountId,
    string? StableId = null)
{
    public static readonly QuotaAccount None = new(QuotaAccountKind.Unknown, null, null, false, null);

    // 登录种类本身不能识别一个人（两个 API Key 都可能只有同一个 kind）。
    public bool IsIdentifiable => !string.IsNullOrWhiteSpace(Email) ||
        !string.IsNullOrWhiteSpace(WorkspaceAccountId) || !string.IsNullOrWhiteSpace(StableId);

    public bool SameIdentity(QuotaAccount other) => Kind == other.Kind &&
        string.Equals(Email, other.Email, StringComparison.Ordinal) &&
        string.Equals(WorkspaceAccountId, other.WorkspaceAccountId, StringComparison.Ordinal) &&
        string.Equals(StableId, other.StableId, StringComparison.Ordinal);
}

/// <summary>规范化后的额度快照。仅在内存中保存，配置与日志不落盘。</summary>
public sealed record QuotaSnapshot(
    QuotaAvailability Availability,
    string Title,
    string Detail,
    QuotaAccountKind AccountKind,
    string? AccountEmail,
    string? PlanType,
    QuotaAccountConsistency Consistency,
    IReadOnlyList<QuotaBucket> Buckets,
    DateTimeOffset CheckedAt,
    bool IsStale = false,
    string? FailureCategory = null,
    bool ResetPending = false,
    string? FreshnessNote = null,
    DateTimeOffset? SourceTimestamp = null,
    DateTimeOffset? DataTimestamp = null,
    DateTimeOffset? UpdatedAt = null)
{
    // 仅用于进程内账号/缓存原子关联。网络必须通过独立 UsageResult 映射。
    [System.Text.Json.Serialization.JsonIgnore]
    public QuotaAccount Identity { get; init; } = QuotaAccount.None;

    public static QuotaSnapshot Unavailable(string detail, DateTimeOffset checkedAt, string? failureCategory) =>
        new(QuotaAvailability.Unavailable, "暂不可用", detail, QuotaAccountKind.Unknown, null, null,
            QuotaAccountConsistency.Unconfirmed, Array.Empty<QuotaBucket>(), checkedAt,
            FailureCategory: failureCategory);

    public bool HasData => Availability == QuotaAvailability.Available &&
        Buckets.Any(bucket => bucket.Windows.Count > 0);

    public string AccountLabel
    {
        get
        {
            var plan = QuotaResponseParser.PlanLabel(PlanType);
            if (AccountKind == QuotaAccountKind.ApiKey) return $"API Key 登录 · {plan}";
            if (string.IsNullOrWhiteSpace(AccountEmail)) return $"账号未知 · {plan}";
            return $"{AccountEmail} · {plan}";
        }
    }

    /// <summary>无法确认与桌面账号一致时提示核对，不标为已确认。</summary>
    public string? ConsistencyHint => Consistency switch
    {
        QuotaAccountConsistency.Confirmed => null,
        QuotaAccountConsistency.Mismatch => "额度账号与桌面账号不一致，请核对桌面账号。",
        _ => "请核对桌面账号。"
    };

    public int? LowestRemainingPercent
    {
        get
        {
            int? lowest = null;
            foreach (var bucket in Buckets)
            foreach (var window in bucket.Windows)
            {
                if (window.RemainingPercent is not { } percent) continue;
                if (lowest is null || percent < lowest) lowest = percent;
            }
            return lowest;
        }
    }

    public QuotaLevel Level
    {
        get
        {
            if (!HasData) return QuotaLevel.Unknown;
            foreach (var bucket in Buckets) if (bucket.Restricted) return QuotaLevel.Critical;
            var lowest = LowestRemainingPercent;
            if (lowest is null) return QuotaLevel.Unknown;
            if (lowest <= 5) return QuotaLevel.Critical;
            if (lowest <= 20) return QuotaLevel.Low;
            return QuotaLevel.Normal;
        }
    }

    public string LevelLabel => Level switch
    {
        QuotaLevel.Normal => "额度充足",
        QuotaLevel.Low => "额度偏低",
        QuotaLevel.Critical => "额度紧张",
        _ => "额度未知"
    };

    /// <summary>悬浮窗与卡片共用的一行摘要。</summary>
    public string Summary
    {
        get
        {
            if (Availability == QuotaAvailability.Unsupported) return "额度：此登录方式不提供订阅额度";
            if (Availability == QuotaAvailability.NotLoggedIn) return "额度：需要先登录配套 CLI";
            if (!HasData) return $"额度：{Title}";
            var parts = new List<string>();
            foreach (var bucket in Buckets)
            foreach (var window in bucket.Windows)
                parts.Add($"{window.Name} {window.RemainingLabel}");
            var suffix = IsStale ? " · 数据可能已过期" : "";
            return $"额度：{string.Join(" · ", parts)}{suffix}";
        }
    }

    public string DescribeWindows(DateTimeOffset now)
    {
        if (!HasData) return Detail;
        var lines = new List<string>();
        foreach (var bucket in Buckets)
        foreach (var window in bucket.Windows)
            lines.Add(window.Describe(now));
        return string.Join("\n", lines);
    }
}

/// <summary>把 App Server 的只读账号响应规范化为快照；未知字段保持未知。</summary>
public static class QuotaResponseParser
{
    public static string PlanLabel(string? planType) => planType switch
    {
        null or "" => "计划未知",
        "plus" => "Plus",
        "pro" => "Pro",
        "prolite" => "Pro Lite",
        "promax" => "Pro Max",
        "free" => "Free",
        "go" => "Go",
        "team" => "Team",
        "business" => "Business",
        "enterprise" => "Enterprise",
        "edu" => "Edu",
        _ => planType
    };

    public static QuotaAccount ReadAccount(string accountJson, string? rateLimitsJson = null)
    {
        using var document = JsonDocument.Parse(accountJson);
        var root = document.RootElement;
        var requiresAuth = root.TryGetProperty("requiresOpenaiAuth", out var requires) && requires.ValueKind == JsonValueKind.True;
        string? workspaceAccountId = null;
        if (root.TryGetProperty("workspaceRouting", out var routing) && routing.ValueKind == JsonValueKind.Object &&
            routing.TryGetProperty("chatgptAccountId", out var accountId) && accountId.ValueKind == JsonValueKind.String)
            workspaceAccountId = accountId.GetString();

        if (!root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            return new QuotaAccount(QuotaAccountKind.Unknown, null, null, requiresAuth, workspaceAccountId);

        var type = account.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;
        var email = account.TryGetProperty("email", out var emailElement) && emailElement.ValueKind == JsonValueKind.String
            ? emailElement.GetString()
            : null;
        var plan = account.TryGetProperty("planType", out var planElement) && planElement.ValueKind == JsonValueKind.String
            ? planElement.GetString()
            : null;
        var kind = type switch
        {
            "chatgpt" => QuotaAccountKind.ChatGpt,
            "apiKey" => QuotaAccountKind.ApiKey,
            "amazonBedrock" => QuotaAccountKind.Bedrock,
            _ => QuotaAccountKind.Unknown
        };
        var stableId = workspaceAccountId ?? String(account, "id") ?? String(account, "accountId") ?? String(root, "accountId");
        if (rateLimitsJson is not null)
        {
            using var limits = JsonDocument.Parse(rateLimitsJson);
            var rateAccountId = String(limits.RootElement, "accountId");
            // 本协议把根 accountId 作为同一账号的稳定身份来源；若来源不一致，
            // 不猜测二者语义相同/不同，不把额度挂到优先字段上，保守拒绝此次数据。
            if (!string.IsNullOrWhiteSpace(stableId) && !string.IsNullOrWhiteSpace(rateAccountId) &&
                !string.Equals(stableId, rateAccountId, StringComparison.Ordinal))
                throw new QuotaSessionException("account-identity-conflict");
            stableId ??= rateAccountId;
        }
        return new QuotaAccount(kind, email, plan, requiresAuth, workspaceAccountId, stableId);
    }

    public static QuotaSnapshot Build(QuotaAccount account, string? rateLimitsJson, DateTimeOffset now) =>
        BuildCore(account, rateLimitsJson, now) with { Identity = account, UpdatedAt = now.ToUniversalTime() };

    private static QuotaSnapshot BuildCore(QuotaAccount account, string? rateLimitsJson, DateTimeOffset now)
    {
        if (account.Kind == QuotaAccountKind.ApiKey)
            return new QuotaSnapshot(QuotaAvailability.Unsupported, "不支持订阅额度",
                "此登录方式不提供订阅额度，启动器不推算余额。", account.Kind, account.Email, account.PlanType,
                QuotaAccountConsistency.Unconfirmed, Array.Empty<QuotaBucket>(), now);

        if (account.Kind == QuotaAccountKind.Bedrock)
            return new QuotaSnapshot(QuotaAvailability.Unsupported, "不支持订阅额度",
                "当前登录方式不提供订阅额度。", account.Kind, account.Email, account.PlanType,
                QuotaAccountConsistency.Unconfirmed, Array.Empty<QuotaBucket>(), now);

        if (account.Kind == QuotaAccountKind.Unknown)
            return new QuotaSnapshot(QuotaAvailability.NotLoggedIn, "需要先登录配套 CLI",
                account.RequiresOpenAiAuth
                    ? "需要先登录配套 CLI，启动器不会自动登录或退出登录。"
                    : "未能读取配套 CLI 的登录信息，需要先登录配套 CLI。",
                account.Kind, account.Email, account.PlanType, QuotaAccountConsistency.Unconfirmed,
                Array.Empty<QuotaBucket>(), now);

        if (rateLimitsJson is null)
            return new QuotaSnapshot(QuotaAvailability.Unavailable, "暂不可用", "尚未读取额度窗口。",
                account.Kind, account.Email, account.PlanType, QuotaAccountConsistency.Unconfirmed,
                Array.Empty<QuotaBucket>(), now);

        var buckets = ReadBuckets(rateLimitsJson, now);
        var resetPending = buckets.Any(bucket => bucket.Windows.Any(window => window.Note == "等待确认重置"));
        var detail = buckets.Count == 0
            ? "接口未返回额度窗口，暂不可用。"
            : $"共 {buckets.Count} 个额度桶；剩余百分比按 clamp(100 - usedPercent, 0, 100) 计算。";
        return new QuotaSnapshot(QuotaAvailability.Available, "额度与重置时间", detail,
            account.Kind, account.Email, account.PlanType, QuotaAccountConsistency.Unconfirmed,
            buckets, now, ResetPending: resetPending, SourceTimestamp: ReadSourceTimestamp(rateLimitsJson),
            DataTimestamp: buckets.Any(b => b.Windows.Count > 0) ? now.ToUniversalTime() : null);
    }

    /// <summary>优先使用 rateLimitsByLimitId，缺失时兼容旧的 rateLimits 单桶字段。</summary>
    public static IReadOnlyList<QuotaBucket> ReadBuckets(string rateLimitsJson, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(rateLimitsJson);
        var root = document.RootElement;
        var buckets = new List<QuotaBucket>();
        if (root.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in byId.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                buckets.Add(ReadBucket(entry.Name, entry.Value, now));
            }
        }
        if (buckets.Count == 0 && root.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            buckets.Add(ReadBucket("rateLimits", legacy, now));
        return buckets;
    }

    private static QuotaBucket ReadBucket(string limitId, JsonElement element, DateTimeOffset now)
    {
        var id = String(element, "limitId") ?? limitId;
        var name = String(element, "limitName");
        var windows = new List<QuotaWindow>();
        var index = 0;
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (!element.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object) continue;
            windows.Add(ReadWindow(key == "primary" ? "主要窗口" : "次要窗口", window, now,
                $"{Uri.EscapeDataString(id)}:{key}"));
            index++;
        }
        var restricted = false;
        string? reason = null;
        if (element.TryGetProperty("rateLimitReachedType", out var reached) && reached.ValueKind == JsonValueKind.String)
        {
            restricted = true;
            reason = reached.GetString();
        }
        else if (element.TryGetProperty("spendControlReached", out var spend) && spend.ValueKind == JsonValueKind.True)
        {
            restricted = true;
            reason = "spend-control";
        }
        return new QuotaBucket(id, name ?? id, windows, restricted, reason);
    }

    private static QuotaWindow ReadWindow(string name, JsonElement element, DateTimeOffset now, string id)
    {
        var duration = Long(element, "windowDurationMins");
        int? remaining = null;
        double? usedPercent = null;
        if (element.TryGetProperty("usedPercent", out var used) && used.ValueKind == JsonValueKind.Number &&
            used.TryGetDouble(out var usedValue) && double.IsFinite(usedValue))
        {
            usedPercent = usedValue;
            remaining = (int)Math.Clamp(100d - usedValue, 0, 100);
        }

        DateTimeOffset? resetsAt = null;
        if (Long(element, "resetsAt") is { } seconds && seconds > 0 && seconds <= 253402300799)
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);

        var note = resetsAt is not null && resetsAt.Value <= now ? "等待确认重置" : null;
        return new QuotaWindow(name, DurationLabel(duration), duration, remaining, resetsAt, note, usedPercent, id);
    }

    private static DateTimeOffset? ReadSourceTimestamp(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("sourceTimestamp", out var value)) return null;
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(),
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            return timestamp.ToUniversalTime();
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds) &&
            seconds >= -62135596800 && seconds <= 253402300799)
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return null;
    }

    /// <summary>仅在返回长度匹配时使用易懂名称，不把示例窗口写死。</summary>
    public static string DurationLabel(long? minutes) => minutes switch
    {
        null => "窗口长度未知",
        300 => "5 小时",
        10080 => "每周",
        var value when value % 1440 == 0 => $"{value / 1440} 天",
        var value when value % 60 == 0 => $"{value / 60} 小时",
        var value => $"{value} 分钟"
    };

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
}
