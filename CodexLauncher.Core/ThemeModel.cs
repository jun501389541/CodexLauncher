using System.Text.Json.Serialization;

namespace CodexLauncher.Core;

/// <summary>
/// 主题选择。默认跟随系统，手动选择优先于系统设置；Windows 高对比度模式优先采用系统颜色。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode
{
    System,
    Light,
    Dark
}

/// <summary>
/// 按钮在正常、悬停、按下、禁用和键盘焦点下的配色。
/// 颜色以 32 位 ARGB 整数表示，因此纯计算部分可以留在 Core 而不依赖 System.Drawing。
/// </summary>
public sealed record ButtonFace(
    int Background,
    int Foreground,
    int Hover,
    int Pressed,
    int DisabledBackground,
    int DisabledText,
    int Focus);

/// <summary>
/// 一套主题调色板。颜色为 ARGB 整数，由 App 层转换为 <c>Color</c>。
/// </summary>
public sealed record ThemePalette(
    string Name,
    int Window,
    int Card,
    int Border,
    int Text,
    int Muted,
    int Primary,
    int OnPrimary,
    int AccentText,
    int SuccessText,
    int SuccessSurface,
    int WarningText,
    int WarningSurface,
    int ErrorText,
    int ErrorSurface,
    int NeutralText,
    int NeutralSurface,
    int PrimarySurface,
    ButtonFace PrimaryButton,
    ButtonFace SecondaryButton)
{
    public static readonly ThemePalette Light = new(
        Name: "浅色",
        Window: 0xF4F6F8,
        Card: 0xFFFFFF,
        Border: 0xE0E4EA,
        Text: 0x1C2128,
        Muted: 0x5B6472,
        Primary: 0x2563EB,
        OnPrimary: 0xFFFFFF,
        AccentText: 0x1D4ED8,
        SuccessText: 0x0B6B44,
        SuccessSurface: 0xE2F7EE,
        WarningText: 0x8A5A00,
        WarningSurface: 0xFFF7E0,
        ErrorText: 0xA32222,
        ErrorSurface: 0xFEEBEB,
        NeutralText: 0x475569,
        NeutralSurface: 0xEEF2F7,
        PrimarySurface: 0xE8F1FF,
        PrimaryButton: new ButtonFace(
            Background: 0x2563EB, Foreground: 0xFFFFFF, Hover: 0x1D4ED8, Pressed: 0x1E40AF,
            DisabledBackground: 0xE3E8EF, DisabledText: 0x5A6472, Focus: 0x1E293B),
        SecondaryButton: new ButtonFace(
            Background: 0xFFFFFF, Foreground: 0x1C2128, Hover: 0xF1F5F9, Pressed: 0xE2E8F0,
            DisabledBackground: 0xE3E8EF, DisabledText: 0x5A6472, Focus: 0x1E293B));

    public static readonly ThemePalette Dark = new(
        Name: "深色",
        Window: 0x11151A,
        Card: 0x1A1F26,
        Border: 0x2E3742,
        Text: 0xE8ECF1,
        Muted: 0x9BA7B4,
        Primary: 0x2563EB,
        OnPrimary: 0xFFFFFF,
        AccentText: 0x93B4FF,
        SuccessText: 0x4ADE80,
        SuccessSurface: 0x123326,
        WarningText: 0xF0B429,
        WarningSurface: 0x33260A,
        ErrorText: 0xFF8A8A,
        ErrorSurface: 0x3A1A1A,
        NeutralText: 0xC7D0DA,
        NeutralSurface: 0x232A33,
        PrimarySurface: 0x1B2A44,
        PrimaryButton: new ButtonFace(
            Background: 0x2563EB, Foreground: 0xFFFFFF, Hover: 0x1D4ED8, Pressed: 0x1E40AF,
            DisabledBackground: 0x262C34, DisabledText: 0x9AA5B1, Focus: 0xF8FAFC),
        SecondaryButton: new ButtonFace(
            Background: 0x1A1F26, Foreground: 0xE8ECF1, Hover: 0x232A33, Pressed: 0x2E3742,
            DisabledBackground: 0x262C34, DisabledText: 0x9AA5B1, Focus: 0xF8FAFC));

    /// <summary>Windows 高对比度模式：纯色搭配，由系统颜色决定实际渲染。</summary>
    public static readonly ThemePalette HighContrast = new(
        Name: "高对比度",
        Window: 0x000000,
        Card: 0x000000,
        Border: 0xFFFFFF,
        Text: 0xFFFFFF,
        Muted: 0xFFFFFF,
        Primary: 0xFFFF00,
        OnPrimary: 0x000000,
        AccentText: 0x00FFFF,
        SuccessText: 0x00FF00,
        SuccessSurface: 0x000000,
        WarningText: 0xFFFF00,
        WarningSurface: 0x000000,
        ErrorText: 0xFF0000,
        ErrorSurface: 0x000000,
        NeutralText: 0xFFFFFF,
        NeutralSurface: 0x000000,
        PrimarySurface: 0x000000,
        PrimaryButton: new ButtonFace(
            Background: 0xFFFF00, Foreground: 0x000000, Hover: 0xFFFFFF, Pressed: 0xC0C0C0,
            DisabledBackground: 0xC0C0C0, DisabledText: 0x000000, Focus: 0x00FFFF),
        SecondaryButton: new ButtonFace(
            Background: 0x000000, Foreground: 0xFFFFFF, Hover: 0x1F1F1F, Pressed: 0x333333,
            DisabledBackground: 0x1F1F1F, DisabledText: 0xFFFFFF, Focus: 0x00FFFF));
}

/// <summary>解析结果：使用哪套调色板，以及是否直接采用系统颜色（高对比度）。</summary>
public sealed record ThemeResolution(ThemePalette Palette, bool UseSystemColors);

/// <summary>把主题设置解析为具体调色板。纯函数，便于在不启动界面的情况下验证。</summary>
public static class ThemeResolver
{
    public static ThemeResolution Resolve(ThemeMode mode, bool systemUsesDark, bool highContrast)
    {
        // 高对比度模式优先：此时不覆盖用户颜色，交由系统颜色决定。
        if (highContrast) return new ThemeResolution(ThemePalette.HighContrast, UseSystemColors: true);

        return mode switch
        {
            ThemeMode.Light => new ThemeResolution(ThemePalette.Light, false),
            ThemeMode.Dark => new ThemeResolution(ThemePalette.Dark, false),
            _ => new ThemeResolution(systemUsesDark ? ThemePalette.Dark : ThemePalette.Light, false)
        };
    }
}

/// <summary>一台屏幕的工作区（虚拟桌面坐标）。</summary>
public sealed record ScreenArea(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>
/// 悬浮窗位置约束：屏幕数量或缩放变化后，把窗口拉回可见工作区，而不是留在不存在的坐标上。
/// </summary>
public static class FloatingPlacement
{
    public static (int X, int Y) Resolve(int x, int y, int width, int height, IReadOnlyList<ScreenArea> screens)
    {
        if (screens.Count == 0) return (0, 0);

        var target = PickScreen(x, y, width, height, screens);
        // 窗口比工作区还大时优先保住左上角，避免出现负坐标。
        var maxX = Math.Max(target.X, target.Right - width);
        var maxY = Math.Max(target.Y, target.Bottom - height);
        return (Math.Min(Math.Max(x, target.X), maxX), Math.Min(Math.Max(y, target.Y), maxY));
    }

    private static ScreenArea PickScreen(int x, int y, int width, int height, IReadOnlyList<ScreenArea> screens)
    {
        // 先看窗口当前落在哪台屏幕上，取重叠面积最大的那台。
        var best = screens[0];
        var bestArea = long.MinValue;
        foreach (var screen in screens)
        {
            var overlapX = Math.Min(x + width, screen.Right) - Math.Max(x, screen.X);
            var overlapY = Math.Min(y + height, screen.Bottom) - Math.Max(y, screen.Y);
            var area = overlapX > 0 && overlapY > 0 ? (long)overlapX * overlapY : 0;
            if (area > bestArea)
            {
                bestArea = area;
                best = screen;
            }
        }

        return best;
    }
}

/// <summary>
/// 单实例闸门：防止重复启动多个后台监测实例，并让后启动的进程通知已有实例恢复主窗口。
/// 由命名互斥体负责归属，命名事件负责激活信号。
/// </summary>
public sealed class SingleInstanceGate : IDisposable
{
    // 命名互斥体对同一线程可重入，单靠 WaitOne(0) 挡不住同一进程里的第二次获取，
    // 因此再按名称记录本进程已经持有的实例。
    private static readonly HashSet<string> OwnedNames = new(StringComparer.Ordinal);
    private static readonly Lock OwnedLock = new();

    private readonly string _name;
    private readonly Mutex _ownership;
    private readonly EventWaitHandle _activation;
    private readonly EventWaitHandle _background;
    private bool _holdsOwnership;
    private bool _disposed;

    public SingleInstanceGate(string name)
    {
        _name = name;
        _ownership = new Mutex(initiallyOwned: false, name);
        _activation = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name + ".activate");
        _background = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, name + ".background");
    }

    /// <summary>尝试成为唯一实例。返回 false 表示已有实例在运行。</summary>
    public bool TryAcquire()
    {
        if (_holdsOwnership) return true;

        lock (OwnedLock)
        {
            if (OwnedNames.Contains(_name)) return false;
            OwnedNames.Add(_name);
        }

        try
        {
            _holdsOwnership = _ownership.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // 上一个实例异常退出，互斥体已归当前线程所有。
            _holdsOwnership = true;
        }

        if (!_holdsOwnership)
        {
            lock (OwnedLock) OwnedNames.Remove(_name);
            return false;
        }

        return true;
    }

    /// <summary>通知已有实例恢复主窗口。</summary>
    public void SignalActivation()
    {
        try { _activation.Set(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>等待其他实例发出的激活信号；超时返回 false。</summary>
    public bool WaitForActivation(TimeSpan timeout) => _activation.WaitOne(timeout);
    public void SignalBackground(){try{_background.Set();}catch(ObjectDisposedException){}}
    public bool WaitForBackground(TimeSpan timeout)=>_background.WaitOne(timeout);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_holdsOwnership)
        {
            _holdsOwnership = false;
            lock (OwnedLock) OwnedNames.Remove(_name);
            try { _ownership.ReleaseMutex(); }
            catch (ApplicationException) { }
        }

        _ownership.Dispose();
        _activation.Dispose();
        _background.Dispose();
    }
}
