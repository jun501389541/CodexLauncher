using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexLauncher.Core;

public static class BridgeJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new UtcTimestampConverter());
        return options;
    }

    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset().ToUniversalTime();
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    }
}

public sealed record UsageWindow(string Id, string Label, double? UsedPercent, double? RemainingPercent,
    long? WindowMinutes, DateTimeOffset? ResetAt);

public sealed record UsageResult(int SchemaVersion, string BridgeId, string ProviderId, string AccountId,
    string DisplayName, string Status, IReadOnlyList<UsageWindow> QuotaWindows,
    DateTimeOffset UpdatedAt, DateTimeOffset? DataTimestamp, DateTimeOffset? SourceTimestamp,
    bool IsStale, string? ErrorCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BridgeResetCredits? RateLimitResetCredits = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RateLimitResetCreditsStatus = null);

/// <summary>Sanitized reset-credit fields consumed by the Android Bridge parser.</summary>
public sealed record BridgeResetCredits(long AvailableCount, IReadOnlyList<BridgeResetCredit>? Credits);

/// <summary>Wire shape deliberately excludes redemption IDs and desktop-only display labels.</summary>
public sealed record BridgeResetCredit(string ResetType, string Status, string? Title, string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? ExpiresAt);

public sealed record BridgeAccount(string ProviderId, string AccountId, string DisplayName);
public sealed record BridgeError(string ErrorCode);
public sealed record BridgeReadResult<T>(int StatusCode, T? Value, BridgeError? Error) where T : class
{
    public static BridgeReadResult<T> Success(T value) => new(200, value, null);
    public static BridgeReadResult<T> Failure(int code, string error) => new(code, null, new(error));
}

/// <summary>允许列表映射：没有邮箱、平台身份、原始错误或原始响应字段。</summary>
public static class UsageResultMapper
{
    public static UsageResult Map(string bridgeId, string accountId, string displayName,
        QuotaSnapshot snapshot, bool monitoringEnabled = true)
    {
        var status = !monitoringEnabled ? "NO_DATA" : snapshot.Availability switch
        {
            QuotaAvailability.Available => snapshot.HasData ? (snapshot.IsStale ? "STALE" : "OK") : "NO_DATA",
            QuotaAvailability.NotLoggedIn => "AUTH_REQUIRED",
            QuotaAvailability.Unsupported => "UNSUPPORTED",
            _ => IsNetworkFailure(snapshot.FailureCategory) ? "NETWORK_ERROR" : "NO_DATA"
        };
        var hasData = monitoringEnabled && snapshot.HasData;
        var windows = hasData ? snapshot.Buckets.SelectMany(bucket => bucket.Windows.Select((window, index) =>
            new UsageWindow(window.Id ?? $"{Uri.EscapeDataString(bucket.LimitId)}:{(index == 0 ? "primary" : "secondary")}",
                window.DurationLabel, window.UsedPercent,
                window.UsedPercent is { } used ? Math.Clamp(100d - used, 0, 100) : window.RemainingPercent,
                window.DurationMinutes, window.ResetsAt?.ToUniversalTime()))).ToArray() : [];
        var canShareCredits = monitoringEnabled && snapshot.Availability == QuotaAvailability.Available;
        var resetCredits = canShareCredits && snapshot.ResetCredits is { } summary
            ? new BridgeResetCredits(summary.AvailableCount, summary.Credits?.Select(card =>
                new BridgeResetCredit(card.ResetType, card.Status, card.Title, card.Description,
                    ExpirationJson(card))).ToArray())
            : null;
        var resetCreditsStatus = canShareCredits
            ? snapshot.ResetCreditsStatus ?? (snapshot.ResetCredits is null ? "NOT_RETURNED" : "AVAILABLE")
            : "NOT_RETURNED";
        return new UsageResult(1, bridgeId, "codex", accountId, displayName, status, windows,
            (snapshot.UpdatedAt ?? snapshot.CheckedAt).ToUniversalTime(),
            hasData ? snapshot.DataTimestamp?.ToUniversalTime() : null,
            hasData ? snapshot.SourceTimestamp?.ToUniversalTime() : null,
            hasData && snapshot.IsStale, !monitoringEnabled ? "QUOTA_MONITORING_DISABLED" : ErrorCode(snapshot.FailureCategory),
            resetCredits, resetCreditsStatus);
    }

    private static JsonElement? ExpirationJson(QuotaResetCredit card)
    {
        if (!card.ExpiryKnown) return null;
        return card.ExpiresAt is { } expiry
            ? JsonSerializer.SerializeToElement(expiry.ToUniversalTime(), BridgeJson.Options)
            : JsonDocument.Parse("null").RootElement.Clone();
    }

    private static bool IsNetworkFailure(string? category) => category is
        "timeout" or "HttpRequestException" or "SocketException" or "network-error" or "network-unavailable";

    private static string? ErrorCode(string? category) => category switch
    {
        null => null,
        "cli-missing" => "CLI_MISSING",
        "account-changed" => "ACCOUNT_CHANGED",
        "account-unavailable" => "ACCOUNT_UNAVAILABLE",
        "account-identity-conflict" => "ACCOUNT_IDENTITY_CONFLICT",
        "quota-monitoring-disabled" => "QUOTA_MONITORING_DISABLED",
        _ when IsNetworkFailure(category) => "UPSTREAM_NETWORK_ERROR",
        _ => "UPSTREAM_UNAVAILABLE"
    };
}
