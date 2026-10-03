using System.Text.Json;

namespace CodexLauncher.Core;

public sealed class CodexAppLocator(ICommandRunner runner)
{
    private const string Query = "$p=Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1; if($p){[pscustomobject]@{PackageFamilyName=$p.PackageFamilyName;InstallLocation=$p.InstallLocation}|ConvertTo-Json -Compress}";

    public async Task<CodexInstallation?> FindAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(new CommandSpec("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", Query], null,
            new Dictionary<string, string?>(), TimeSpan.FromSeconds(15)), cancellationToken);
        return result.ExitCode == 0 && !result.TimedOut ? Parse(result.Stdout) : null;
    }

    public static CodexInstallation? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var family = root.GetProperty("PackageFamilyName").GetString();
        var location = root.GetProperty("InstallLocation").GetString();
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(location)) return null;
        var manifest = File.ReadAllText(Path.Combine(location, "AppxManifest.xml"));
        return new CodexInstallation(family, location, CodexInstallation.BuildAumid(family, manifest));
    }
}
