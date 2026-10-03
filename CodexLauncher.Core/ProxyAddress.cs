namespace CodexLauncher.Core;

public sealed record ProxyAddress(Uri Uri)
{
    public static ProxyAddress Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("请输入本地 HTTP 代理地址。");
        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        if (!System.Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || !uri.IsLoopback
            || uri.Port is < 1 or > 65535
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || uri.AbsolutePath != "/")
            throw new ArgumentException("仅支持 127.0.0.1、localhost 或 ::1 上的 HTTP 代理端口。");
        return new ProxyAddress(uri);
    }

    public override string ToString() => Uri.AbsoluteUri;
}
