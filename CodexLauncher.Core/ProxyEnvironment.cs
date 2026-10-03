namespace CodexLauncher.Core;

public static class ProxyEnvironment
{
    private static readonly string[] ProxyKeys = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy", "NO_PROXY", "no_proxy"];
    private static readonly string[] ProcessProxyKeys = ["HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy"];

    public static IReadOnlyDictionary<string, string?> ForProcess(RouteKind route, ProxyAddress? proxy)
    {
        if (route == RouteKind.Direct) return new Dictionary<string, string?>();
        var values = ProxyKeys.ToDictionary(key => key, _ => (string?)null, StringComparer.Ordinal);
        if (proxy is null) throw new ArgumentException("代理模式需要本地 HTTP 代理地址。");
        foreach (var key in ProxyKeys.Where(key => !key.EndsWith("NO_PROXY", StringComparison.OrdinalIgnoreCase))) values[key] = proxy.ToString();
        values["NO_PROXY"] = values["no_proxy"] = "localhost,127.0.0.1,::1";
        return values;
    }

    public static IReadOnlyDictionary<string, string> ForUser(RouteKind route, ProxyAddress? proxy) =>
        ForProcess(route, proxy).Where(entry => entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);

    /// <summary>
    /// 配额 CLI 子进程的代理解析核心（纯函数）：0.159.2 起的 CLI 只认环境变量代理、不吃 Windows 系统代理，
    /// 启动器拉起自有配额查询进程时按 进程已有代理（继承，不覆盖用户自己的配置）→ 启动器设置 → Windows 系统代理
    /// 的顺序选取；无效代理串跳过该来源继续下走而不是抛异常；全部缺失则空字典直连。
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromSettings(
        string? settingsProxy,
        Func<string, string?> processEnvironment,
        int proxyEnable,
        string? proxyServer)
    {
        var (_, proxy) = SelectProxy(settingsProxy, processEnvironment, proxyEnable, proxyServer);
        return proxy is null
            ? new Dictionary<string, string>()
            : ForUser(RouteKind.Proxy, proxy);
    }

    /// <summary>选取代理并标注来源（process/settings/windows）；供解析核心与真实来源编排共用，保证单一事实。</summary>
    internal static (string? Source, ProxyAddress? Proxy) SelectProxy(
        string? settingsProxy, Func<string, string?> processEnvironment, int proxyEnable, string? proxyServer)
    {
        foreach (var key in ProcessProxyKeys)
            if (TryParseProxy(processEnvironment(key)) is { } fromProcess)
                return ("process", fromProcess);
        if (TryParseProxy(settingsProxy) is { } fromSettings)
            return ("settings", fromSettings);
        if (proxyEnable == 1 && ProxyDiscovery.FromWindowsProxyServer(proxyServer) is { } fromWindows)
            return ("windows", fromWindows);
        return (null, null);
    }

    private static ProxyAddress? TryParseProxy(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        try { return ProxyAddress.Parse(candidate); }
        catch (ArgumentException) { return null; }
    }
}
