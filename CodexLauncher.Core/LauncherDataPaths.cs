namespace CodexLauncher.Core;

/// <summary>Resolves app-owned data paths for the installed and portable packages.</summary>
public static class LauncherDataPaths
{
    public const string PortableMarkerFileName = "CodexLauncher.portable";

    private const string PortableDataFolderName = "portable-data";
    private const string MigrationMarkerFileName = ".appdata-imported";
    private static readonly string ExecutableDirectory = AppContext.BaseDirectory;
    private static readonly string LegacyDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexLauncher");

    static LauncherDataPaths()
    {
        IsPortable = File.Exists(Path.Combine(ExecutableDirectory, PortableMarkerFileName));
        DataDirectory = IsPortable
            ? Path.Combine(ExecutableDirectory, PortableDataFolderName)
            : LegacyDataDirectory;
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        BridgeDirectory = Path.Combine(DataDirectory, "bridge");
    }

    public static bool IsPortable { get; }
    public static string DataDirectory { get; }
    public static string SettingsPath { get; }
    public static string BridgeDirectory { get; }

    /// <summary>Checks portable storage and imports the existing user settings once, when present.</summary>
    public static void InitializeForCurrentProcess()
    {
        if (!IsPortable) return;

        Directory.CreateDirectory(DataDirectory);
        EnsureWritable(DataDirectory);
        ImportLegacyData();
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

    private static void ImportLegacyData()
    {
        var migrationMarker = Path.Combine(DataDirectory, MigrationMarkerFileName);
        if (File.Exists(migrationMarker)) return;

        var legacySettings = Path.Combine(LegacyDataDirectory, "settings.json");
        if (!File.Exists(SettingsPath) && File.Exists(legacySettings))
            File.Copy(legacySettings, SettingsPath);

        var legacyBridgeDirectory = Path.Combine(LegacyDataDirectory, "bridge");
        if (Directory.Exists(legacyBridgeDirectory))
        {
            Directory.CreateDirectory(BridgeDirectory);
            foreach (var fileName in new[] { "identity.dat", "devices.dat", "grants.dat", "preferences.dat" })
            {
                var source = Path.Combine(legacyBridgeDirectory, fileName);
                var destination = Path.Combine(BridgeDirectory, fileName);
                if (!File.Exists(destination) && File.Exists(source)) File.Copy(source, destination);
            }
        }

        File.WriteAllText(migrationMarker, "Imported settings and bridge state from LocalAppData.");
    }
}
