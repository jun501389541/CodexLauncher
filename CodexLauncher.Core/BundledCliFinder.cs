namespace CodexLauncher.Core;

public static class BundledCliFinder
{
    public static string? Find(string binRoot)
    {
        if (!Directory.Exists(binRoot)) return null;
        return Directory.EnumerateFiles(binRoot, "codex.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
