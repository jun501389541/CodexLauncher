namespace CodexLauncher.Core;

public static class LauncherStartup
{
    public static bool IsBackground(IEnumerable<string> args)=>args.Contains("--background",StringComparer.OrdinalIgnoreCase);
}
