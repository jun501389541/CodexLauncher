namespace CodexLauncher.Core;

/// <summary>Resolves app-owned data paths for installed, portable, and framework-dependent packages.</summary>
public static class LauncherDataPaths
{
    public const string PortableMarkerFileName = "CodexLauncher.portable";
    public const string InstalledMarkerFileName = "CodexLauncher.installed";

    private const string PortableDataFolderName = "portable-data";
    private const string MigrationMarkerFileName = ".appdata-imported";
    private static readonly LauncherDataLayout Current = ResolveForDirectory(
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static bool IsPortable => Current.IsPortable;
    public static string DataDirectory => Current.DataDirectory;
    public static string SettingsPath => Current.SettingsPath;
    public static string BridgeDirectory => Current.BridgeDirectory;

    internal static LauncherDataLayout ResolveForDirectory(string executableDirectory, string localAppDataDirectory)
    {
        var isPortable = File.Exists(Path.Combine(executableDirectory, PortableMarkerFileName));
        var isInstalled = File.Exists(Path.Combine(executableDirectory, InstalledMarkerFileName));
        var usesInstallLocalData = isPortable || isInstalled;
        var legacyDataDirectory = Path.Combine(localAppDataDirectory, "CodexLauncher");
        var dataDirectory = usesInstallLocalData
            ? Path.Combine(executableDirectory, PortableDataFolderName)
            : legacyDataDirectory;

        return new LauncherDataLayout(
            isPortable,
            usesInstallLocalData,
            dataDirectory,
            Path.Combine(dataDirectory, "settings.json"),
            Path.Combine(dataDirectory, "bridge"),
            legacyDataDirectory);
    }

    /// <summary>Checks app-local storage and imports existing user settings once, when present.</summary>
    public static void InitializeForCurrentProcess() => InitializeForLayout(Current);

    internal static void InitializeForLayout(LauncherDataLayout layout)
    {
        if (!layout.UsesInstallLocalData) return;

        Directory.CreateDirectory(layout.DataDirectory);
        EnsureWritable(layout.DataDirectory);
        ImportLegacyData(layout.DataDirectory, layout.LegacyDataDirectory);
    }

    private static void EnsureWritable(string directory)
    {
        var probePath = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
                stream.Flush(true);
            }
            File.Delete(probePath);
        }
        finally
        {
            if (File.Exists(probePath)) File.Delete(probePath);
        }
    }

    private static void ImportLegacyData(string dataDirectory, string legacyDataDirectory)
    {
        var migrationMarker = Path.Combine(dataDirectory, MigrationMarkerFileName);
        if (File.Exists(migrationMarker)) return;

        var settingsPath = Path.Combine(dataDirectory, "settings.json");
        var legacySettings = Path.Combine(legacyDataDirectory, "settings.json");
        if (!File.Exists(settingsPath) && File.Exists(legacySettings))
            File.Copy(legacySettings, settingsPath);

        var bridgeDirectory = Path.Combine(dataDirectory, "bridge");
        var legacyBridgeDirectory = Path.Combine(legacyDataDirectory, "bridge");
        if (Directory.Exists(legacyBridgeDirectory))
        {
            Directory.CreateDirectory(bridgeDirectory);
            foreach (var fileName in new[] { "identity.dat", "devices.dat", "grants.dat", "preferences.dat" })
            {
                var source = Path.Combine(legacyBridgeDirectory, fileName);
                var destination = Path.Combine(bridgeDirectory, fileName);
                if (!File.Exists(destination) && File.Exists(source)) File.Copy(source, destination);
            }
        }

        File.WriteAllText(migrationMarker, "Imported settings and bridge state from LocalAppData.");
    }
}

internal sealed record LauncherDataLayout(
    bool IsPortable,
    bool UsesInstallLocalData,
    string DataDirectory,
    string SettingsPath,
    string BridgeDirectory,
    string LegacyDataDirectory);
