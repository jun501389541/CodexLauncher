namespace CodexLauncher.Core;

public sealed record TunModeState(bool? PartyEnabled, bool? VergeEnabled)
{
    public bool HasConflict => PartyEnabled == true && VergeEnabled == true;
    public bool IsKnown => PartyEnabled.HasValue && VergeEnabled.HasValue;

    public TunModeState MergeKnown(TunModeState latest) => new(
        latest.PartyEnabled ?? PartyEnabled,
        latest.VergeEnabled ?? VergeEnabled);
}

public sealed class TunModeDiscovery(string partyConfigPath, string vergeConfigPath)
{
    public static TunModeDiscovery ForCurrentUser()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new TunModeDiscovery(
            Path.Combine(roaming, "mihomo-party", "mihomo.yaml"),
            Path.Combine(roaming, "io.github.clash-verge-rev.clash-verge-rev", "verge.yaml"));
    }

    public TunModeState Read() => new(
        ReadSectionBoolean(partyConfigPath, "tun", "enable"),
        ReadTopLevelBoolean(vergeConfigPath, "enable_tun_mode"));

    private static bool? ReadTopLevelBoolean(string path, string key)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] == '#') continue;
                if (TryReadBoolean(line, key, out var value)) return value;
            }
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool? ReadSectionBoolean(string path, string section, string key)
    {
        var inside = false;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
                if (!char.IsWhiteSpace(line[0]))
                {
                    inside = line.Trim().Equals(section + ":", StringComparison.Ordinal);
                    continue;
                }
                if (inside && TryReadBoolean(line.Trim(), key, out var value)) return value;
            }
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool TryReadBoolean(string line, string key, out bool value)
    {
        value = false;
        var colon = line.IndexOf(':');
        if (colon < 0 || line[..colon].Trim() != key) return false;
        return bool.TryParse(line[(colon + 1)..].Split('#', 2)[0].Trim(), out value);
    }
}
