namespace CodexLauncher.Core;

public sealed class UpstreamPortDiscovery(string partyConfigPath, string vergeConfigPath)
{
    public static UpstreamPortDiscovery ForCurrentUser()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new UpstreamPortDiscovery(
            Path.Combine(roaming, "mihomo-party", "mihomo.yaml"),
            Path.Combine(roaming, "io.github.clash-verge-rev.clash-verge-rev", "verge.yaml"));
    }

    public GatewayPorts Discover(GatewayPorts fallback)
    {
        var party = File.Exists(partyConfigPath)
            ? ReadHttpCapablePort(partyConfigPath, "mixed-port", "port")
            : fallback.Party;
        var verge = File.Exists(vergeConfigPath)
            ? ReadHttpCapablePort(vergeConfigPath, "verge_mixed_port", "verge_port")
            : fallback.Verge;
        var result = fallback with { Party = party, Verge = verge };
        GatewayConfig.Build(result);
        return result;
    }

    private static int ReadHttpCapablePort(string path, string mixedKey, string httpKey)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#') continue;
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon];
            if (key != mixedKey && key != httpKey) continue;
            var value = line[(colon + 1)..].Split('#', 2)[0].Trim().Trim('\'', '"');
            if (!int.TryParse(value, out var port) || port is < 0 or > 65535)
                throw new InvalidDataException($"{Path.GetFileName(path)} 的 {key} 端口无效。");
            values[key] = port;
        }
        if (values.TryGetValue(mixedKey, out var mixed) && mixed > 0) return mixed;
        if (values.TryGetValue(httpKey, out var http) && http > 0) return http;
        throw new InvalidDataException($"{Path.GetFileName(path)} 没有启用 HTTP 或混合代理端口。");
    }
}
