namespace CodexLauncher.Core;

public static class CodexIconLocator
{
    private static readonly string[] Candidates =
    [
        Path.Combine("assets", "icon.png"),
        Path.Combine("assets", "Square150x150Logo.png"),
        Path.Combine("assets", "Square44x44Logo.png")
    ];

    public static string? FindPath(string installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return null;
        foreach (var candidate in Candidates)
        {
            var path = Path.Combine(installLocation, candidate);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
