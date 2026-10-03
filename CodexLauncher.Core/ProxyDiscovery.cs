namespace CodexLauncher.Core;

public static class ProxyDiscovery
{
    public static ProxyAddress? FromWindowsProxyServer(string? server)
    {
        if (string.IsNullOrWhiteSpace(server)) return null;
        var entries = server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidate = entries.Select(entry => entry.Split('=', 2, StringSplitOptions.TrimEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals("https", StringComparison.OrdinalIgnoreCase))?[1]
            ?? entries.FirstOrDefault(entry => !entry.Contains('='));
        if (candidate is null) return null;
        try { return ProxyAddress.Parse(candidate); }
        catch (ArgumentException) { return null; }
    }
}
