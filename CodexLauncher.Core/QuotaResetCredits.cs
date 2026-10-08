using System.Globalization;

namespace CodexLauncher.Core;

/// <summary>总数由服务端给出；详情为 null 表示只知道总数，列表可能只返回部分卡片。</summary>
public sealed record QuotaResetCredits(long AvailableCount,IReadOnlyList<QuotaResetCredit>? Credits);

/// <summary>只读展示信息，不保留用于兑换的卡片 ID。</summary>
public sealed record QuotaResetCredit(
    string? Title,string? Description,string ResetType,string Status,
    DateTimeOffset? ExpiresAt,bool ExpiryKnown)
{
    public string TitleLabel
    {
        get
        {
            var title=Title?.Trim();
            if(string.Equals(title,"Full reset (Weekly + 5 hr)",StringComparison.OrdinalIgnoreCase))
                return "完全重置（每周 + 5 小时）";
            if(string.Equals(title,"Rate-limit reset",StringComparison.OrdinalIgnoreCase))
                return "限额重置";
            return !string.IsNullOrWhiteSpace(title)?title
                : ResetType=="codexRateLimits"?"Codex 限额重置":"限额重置卡";
        }
    }

    public string StatusLabel => Status switch
    {
        "available"=>"可用",
        "redeeming"=>"处理中",
        "redeemed"=>"已使用",
        _=>"状态未知"
    };

    public string ExpirationLabel(DateTimeOffset now,TimeZoneInfo? displayTimeZone=null)
    {
        if(!ExpiryKnown)return "到期时间未知";
        if(ExpiresAt is null)return "无到期限制";
        var local=TimeZoneInfo.ConvertTime(ExpiresAt.Value,displayTimeZone??TimeZoneInfo.Local);
        return string.Create(CultureInfo.InvariantCulture,$"{local:yyyy-MM-dd HH:mm} {(ExpiresAt<=now?"已到期":"到期")}");
    }
}
