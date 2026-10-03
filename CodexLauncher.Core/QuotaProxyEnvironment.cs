using Microsoft.Win32;

namespace CodexLauncher.Core;

/// <summary>
/// 配额 CLI 子进程代理解析的真实来源编排：解析顺序与判定规则见 <see cref="ProxyEnvironment.FromSettings"/>。
/// 只产出注入子进程的环境键，不修改注册表、不进入事务、不污染启动器自身进程环境。
/// </summary>
public static class QuotaProxyEnvironment
{
    private static readonly string SettingsPath = LauncherDataPaths.SettingsPath;

    /// <summary>解析结果：来源、代理与要注入子进程的环境键；直连时环境为空字典。</summary>
    public sealed record Resolution(string? Source, ProxyAddress? Proxy, IReadOnlyDictionary<string, string> Environment)
    {
        public static Resolution Direct { get; } = new(null, null, new Dictionary<string, string>());
    }

    /// <summary>按 进程环境 → 启动器设置 → Windows 系统代理 的顺序解析，全部缺失则直连。</summary>
    public static Resolution Resolve()
    {
        var (enabled, server) = ReadWindowsProxy();
        var (source, proxy) = ProxyEnvironment.SelectProxy(
            LoadSettingsProxy(), Environment.GetEnvironmentVariable, enabled, server);
        return proxy is null
            ? Resolution.Direct
            : new Resolution(source, proxy, ProxyEnvironment.ForUser(RouteKind.Proxy, proxy));
    }

    private static string? LoadSettingsProxy()
    {
        if (!File.Exists(SettingsPath)) return null;
        try { return new LauncherSettingsStore(SettingsPath).Load().ProxyUrl; }
        catch (IOException) { return null; }
    }

    private static (int Enabled, string? Server) ReadWindowsProxy()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
        return (Convert.ToInt32(settings?.GetValue("ProxyEnable") ?? 0), settings?.GetValue("ProxyServer") as string);
    }
}
