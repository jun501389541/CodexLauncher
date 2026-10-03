using Microsoft.Win32;

namespace CodexLauncher.Core;

/// <summary>
/// 开机自启的注册表读写抽象。抽出接口只是为了能在测试里验证状态机，
/// 产品代码用的是 <see cref="RegistryAutoStartRegistry"/>。
/// </summary>
public interface IAutoStartRegistry
{
    string? Read(string name);

    void Write(string name, string? value);
}

/// <summary>
/// 开机自启。判断依据永远是注册表里的真实值，而不是"我们最后一次写过的值"：
/// 用户可能从任务管理器禁用启动项、或者换了安装位置，界面必须跟着变。
/// </summary>
public sealed class AutoStartRegistration(IAutoStartRegistry registry)
{
    public const string ValueName = "CodexLauncher";

    /// <summary>当前用户的 Run 键；不需要管理员权限，也不会影响其它用户。</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>带引号的可执行文件路径。路径常含空格（Program Files），不加引号会启动失败。</summary>
    public static string Command(string executablePath) => "\"" + executablePath + "\" --background";

    /// <summary>注册表里当前的启动命令行；没有登记时为 null。</summary>
    public string? CurrentCommand
    {
        get
        {
            try
            {
                var value = registry.Read(ValueName);
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                // 读不到就当作未登记：界面显示"未启用"比抛出异常炸掉窗口更安全。
                return null;
            }
        }
    }

    public bool IsEnabled(string executablePath)
    {
        var current = CurrentCommand;
        return current is not null
            && string.Equals(Executable(current), executablePath, StringComparison.OrdinalIgnoreCase);
    }
    private static string? Executable(string command)
    {
        if(command.StartsWith('"'))
        {var end=command.IndexOf('"',1);return end>1?command[1..end]:null;}
        var space=command.IndexOfAny([' ','\t']);return space<0?command:command[..space];
    }

    /// <summary>写入或删除自启项。关闭时必须删除键值，写空字符串会让登录时报错。</summary>
    public SettingsSaveResult TryApply(bool enabled, string executablePath)
    {
        var wanted = enabled ? Command(executablePath) : null;

        // 已经是目标状态就不要重复写注册表（每次启动都写会惊动安全软件）。
        var current = CurrentCommand;
        if (string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            return new SettingsSaveResult(true);

        try
        {
            registry.Write(ValueName, wanted);
            return new SettingsSaveResult(true);
        }
        catch (Exception ex) when (IsWriteFailure(ex))
        {
            return new SettingsSaveResult(false, ex.GetType().Name);
        }
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static bool IsWriteFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}

/// <summary>HKCU\...\Run 的真实实现。</summary>
public sealed class RegistryAutoStartRegistry : IAutoStartRegistry
{
    /// <summary>当前用户的 Run 键；不需要管理员权限，也不会影响其它用户。</summary>
    public const string RunKeyPath = AutoStartRegistration.RunKeyPath;

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistration.RunKeyPath);
        return key?.GetValue(name) as string;
    }

    public void Write(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AutoStartRegistration.RunKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException($"无法打开 {AutoStartRegistration.RunKeyPath}");
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.String);
    }
}
