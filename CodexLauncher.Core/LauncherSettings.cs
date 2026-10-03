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

public sealed class LauncherSettingsStore(string path)
{
    public LauncherSettings Load()
    {
        if (!File.Exists(path)) return new LauncherSettings();
        try { return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path)) ?? new LauncherSettings(); }
        catch (JsonException) { return new LauncherSettings(); }
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
