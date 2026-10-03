using CodexLauncher.Core;
using Microsoft.Win32;

namespace CodexLauncher.App;

/// <summary>
/// 主题管理：解析主题模式（跟随系统／浅色／深色）、响应 Windows 主题变化、
/// 并把调色板递归应用到整棵控件树（主窗口、卡片、按钮、弹窗、菜单、悬浮窗）。
/// 高对比度模式优先采用系统颜色。
/// </summary>
internal sealed class ThemeManager : IDisposable
{
    private ThemeMode _mode = ThemeMode.System;
    private ThemePalette _palette = ThemePalette.Light;
    private bool _useSystemColors;
    private bool _listening;

    public event Action? Changed;

    public ThemeMode Mode => _mode;

    public ThemePalette Palette => _palette;

    /// <summary>高对比度模式下不覆盖用户颜色，直接采用系统颜色。</summary>
    public bool UseSystemColors => _useSystemColors;

    public ThemeManager(ThemeMode mode)
    {
        _mode = mode;
        Recalculate();
    }

    /// <summary>切换主题模式并通知所有已应用控件。</summary>
    public void SetMode(ThemeMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        Recalculate();
        Changed?.Invoke();
    }

    /// <summary>开始响应系统主题与高对比度变化。</summary>
    public void StartListening()
    {
        if (_listening) return;
        _listening = true;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        var previous = _palette;
        Recalculate();
        if (!ReferenceEquals(previous, _palette)) Changed?.Invoke();
    }

    private void Recalculate()
    {
        var resolution = ThemeResolver.Resolve(_mode, SystemUsesDark(), SystemInformation.HighContrast);
        _palette = resolution.Palette;
        _useSystemColors = resolution.UseSystemColors;
    }

    /// <summary>读取 Windows“应用模式”设置；读不到时按浅色处理。</summary>
    public static bool SystemUsesDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int light && light == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>把当前调色板应用到整棵控件树。</summary>
    public void Apply(Control root) => ApplyTo(root, _palette, _useSystemColors);

    public static void ApplyTo(Control root, ThemePalette palette, bool useSystemColors)
    {
        UiTheme.Use(palette, useSystemColors);
        Walk(root);
    }

    private static void Walk(Control control)
    {
        UiTheme.Style(control);
        foreach (Control child in control.Controls) Walk(child);
    }

    public void Dispose()
    {
        if (!_listening) return;
        _listening = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
