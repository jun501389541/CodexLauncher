using System.Text.Json;

namespace CodexLauncher.Core;

/// <summary>
/// 启动器配置。旧字段保留原语义；新增字段在缺失时使用计划默认值
/// （主题跟随系统、悬浮窗关闭、额度查询开启、悬浮窗位置未记录、不开机自启）。
/// </summary>
public sealed record LauncherSettings(
    string? ProxyUrl = null,
    string? MihomoPath = null,
    int? PartyPort = null,
    int? VergePort = null,
    ThemeMode Theme = ThemeMode.System,
    bool FloatingWindowEnabled = false,
    int? FloatingLeft = null,
    int? FloatingTop = null,
    bool QuotaMonitoringEnabled = true,
    bool AutoStartEnabled = false,
    bool BridgeEnabled = false,
    int? BridgePort = null);

public sealed record SettingsSaveResult(bool Succeeded, string? ErrorType = null);
public enum SettingsLoadStatus { Loaded, Missing, Corrupt, Unavailable }
public sealed record SettingsLoadResult(SettingsLoadStatus Status, LauncherSettings Settings, string? ErrorType = null, Exception? Error = null)
{
    public bool Succeeded => Status is SettingsLoadStatus.Loaded or SettingsLoadStatus.Missing or SettingsLoadStatus.Corrupt;
}

public sealed class LauncherSettingsStore(string path)
{
    public LauncherSettings Load()
    {
        var result = TryLoad();
        if (!result.Succeeded && result.Error is not null) throw result.Error;
        return result.Settings;
    }

    public SettingsLoadResult TryLoad()
    {
        string contents;
        try { contents = File.ReadAllText(path); }
        catch (FileNotFoundException) { return new(SettingsLoadStatus.Missing, new LauncherSettings()); }
        catch (DirectoryNotFoundException) { return new(SettingsLoadStatus.Missing, new LauncherSettings()); }
        catch (IOException exception) { return new(SettingsLoadStatus.Unavailable, new LauncherSettings(), exception.GetType().Name, exception); }
        catch (UnauthorizedAccessException exception) { return new(SettingsLoadStatus.Unavailable, new LauncherSettings(), exception.GetType().Name, exception); }

        try
        {
            var settings = JsonSerializer.Deserialize<LauncherSettings>(contents);
            return settings is null
                ? new(SettingsLoadStatus.Corrupt, new LauncherSettings(), nameof(JsonException))
                : new(SettingsLoadStatus.Loaded, settings);
        }
        catch (JsonException exception)
        {
            return new(SettingsLoadStatus.Corrupt, new LauncherSettings(), exception.GetType().Name, exception);
        }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings));
        File.Move(tempPath, path, true);
    }

    public SettingsSaveResult TrySave(LauncherSettings settings)
    {
        try
        {
            Save(settings);
            return new SettingsSaveResult(true);
        }
        catch (IOException ex) { return new SettingsSaveResult(false, ex.GetType().Name); }
        catch (UnauthorizedAccessException ex) { return new SettingsSaveResult(false, ex.GetType().Name); }
    }
}
