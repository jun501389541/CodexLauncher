using CodexLauncher.Core;

internal static class LauncherDataPathsTests
{
    internal static IEnumerable<(string Name, Action Run)> All =>
    [
        ("installed marker selects local data without portable mode", InstalledMarkerSelectsLocalData),
        ("portable and unmarked builds keep their existing data modes", ExistingModesRemainUnchanged),
        ("legacy migration copies only missing settings and bridge files", MigrationPreservesDestinationFiles),
        ("legacy migration retries after a failed partial copy", MigrationRetriesAfterFailure),
        ("missing install-local directory preserves legacy source on failure", FailedInitializationPreservesLegacySource)
    ];

    private static void InstalledMarkerSelectsLocalData()
    {
        WithTempRoot(root =>
        {
            var executableDirectory = Path.Combine(root, "install");
            Directory.CreateDirectory(executableDirectory);
            File.WriteAllText(Path.Combine(executableDirectory, LauncherDataPaths.InstalledMarkerFileName), "installed");

            var paths = LauncherDataPaths.ResolveForDirectory(executableDirectory, Path.Combine(root, "appdata"));

            Equal(false, paths.IsPortable);
            Equal(true, paths.UsesInstallLocalData);
            Equal(Path.Combine(executableDirectory, "portable-data"), paths.DataDirectory);
        });
    }

    private static void ExistingModesRemainUnchanged()
    {
        WithTempRoot(root =>
        {
            var executableDirectory = Path.Combine(root, "portable");
            Directory.CreateDirectory(executableDirectory);
            File.WriteAllText(Path.Combine(executableDirectory, LauncherDataPaths.PortableMarkerFileName), "portable");
            var portable = LauncherDataPaths.ResolveForDirectory(executableDirectory, Path.Combine(root, "appdata"));
            Equal(true, portable.IsPortable);
            Equal(true, portable.UsesInstallLocalData);
            Equal(Path.Combine(executableDirectory, "portable-data"), portable.DataDirectory);

            var ordinary = LauncherDataPaths.ResolveForDirectory(Path.Combine(root, "ordinary"), Path.Combine(root, "appdata"));
            Equal(false, ordinary.IsPortable);
            Equal(false, ordinary.UsesInstallLocalData);
            Equal(Path.Combine(root, "appdata", "CodexLauncher"), ordinary.DataDirectory);
        });
    }

    private static void MigrationPreservesDestinationFiles()
    {
        WithTempRoot(root =>
        {
            var paths = CreateInstalledPaths(root);
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(Path.Combine(paths.DataDirectory, "settings.json"), "destination-settings");
            Directory.CreateDirectory(paths.BridgeDirectory);
            File.WriteAllText(Path.Combine(paths.BridgeDirectory, "identity.dat"), "destination-identity");
            Directory.CreateDirectory(Path.Combine(paths.LegacyDataDirectory, "bridge"));
            File.WriteAllText(Path.Combine(paths.LegacyDataDirectory, "settings.json"), "legacy-settings");
            foreach (var name in new[] { "identity.dat", "devices.dat", "grants.dat", "preferences.dat" })
                File.WriteAllText(Path.Combine(paths.LegacyDataDirectory, "bridge", name), "legacy-" + name);

            LauncherDataPaths.InitializeForLayout(paths);

            Equal("destination-settings", File.ReadAllText(Path.Combine(paths.DataDirectory, "settings.json")));
            Equal("destination-identity", File.ReadAllText(Path.Combine(paths.BridgeDirectory, "identity.dat")));
            foreach (var name in new[] { "devices.dat", "grants.dat", "preferences.dat" })
                Equal("legacy-" + name, File.ReadAllText(Path.Combine(paths.BridgeDirectory, name)));
            Equal(true, File.Exists(Path.Combine(paths.DataDirectory, ".appdata-imported")));
        });
    }

    private static void MigrationRetriesAfterFailure()
    {
        WithTempRoot(root =>
        {
            var paths = CreateInstalledPaths(root);
            Directory.CreateDirectory(paths.LegacyDataDirectory);
            Directory.CreateDirectory(Path.Combine(paths.LegacyDataDirectory, "bridge"));
            File.WriteAllText(Path.Combine(paths.LegacyDataDirectory, "settings.json"), "legacy-settings");
            File.WriteAllText(Path.Combine(paths.LegacyDataDirectory, "bridge", "devices.dat"), "legacy-devices");
            Directory.CreateDirectory(paths.DataDirectory);
            File.WriteAllText(Path.Combine(paths.DataDirectory, "bridge"), "blocks-bridge-directory");

            Throws<IOException>(() => LauncherDataPaths.InitializeForLayout(paths));
            Equal(false, File.Exists(Path.Combine(paths.DataDirectory, ".appdata-imported")));
            Equal("legacy-settings", File.ReadAllText(Path.Combine(paths.DataDirectory, "settings.json")));

            File.Delete(Path.Combine(paths.DataDirectory, "bridge"));
            LauncherDataPaths.InitializeForLayout(paths);

            Equal("legacy-devices", File.ReadAllText(Path.Combine(paths.BridgeDirectory, "devices.dat")));
            Equal(true, File.Exists(Path.Combine(paths.DataDirectory, ".appdata-imported")));
            Equal("legacy-settings", File.ReadAllText(Path.Combine(paths.LegacyDataDirectory, "settings.json")));
        });
    }

    private static void FailedInitializationPreservesLegacySource()
    {
        WithTempRoot(root =>
        {
            var paths = CreateInstalledPaths(root);
            Directory.CreateDirectory(paths.LegacyDataDirectory);
            File.WriteAllText(Path.Combine(paths.LegacyDataDirectory, "settings.json"), "legacy-settings");
            Directory.CreateDirectory(Path.GetDirectoryName(paths.DataDirectory)!);
            File.WriteAllText(paths.DataDirectory, "blocks-data-directory");

            Throws<IOException>(() => LauncherDataPaths.InitializeForLayout(paths));

            Equal("legacy-settings", File.ReadAllText(Path.Combine(paths.LegacyDataDirectory, "settings.json")));
        });
    }

    private static LauncherDataLayout CreateInstalledPaths(string root)
    {
        var executableDirectory = Path.Combine(root, "install");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, LauncherDataPaths.InstalledMarkerFileName), "installed");
        return LauncherDataPaths.ResolveForDirectory(executableDirectory, Path.Combine(root, "appdata"));
    }

    private static void WithTempRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexLauncherDataPaths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"expected {expected}, got {actual}");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new Exception($"expected {typeof(TException).Name}");
    }
}
