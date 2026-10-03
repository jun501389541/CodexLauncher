using CodexLauncher.Core;

namespace CodexLauncher.App;

internal static class Program
{
    /// <summary>单实例闸门名称：固定值，保证同一用户下只会有一个后台监测实例。</summary>
    internal const string SingleInstanceName = @"Local\CodexLauncher.Monitoring";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // 已有实例在运行时，本次启动只负责把主窗口叫回来，不再启动第二个后台监测。
        using var singleInstance = new SingleInstanceGate(SingleInstanceName);
        if (!singleInstance.TryAcquire())
        {
            if(LauncherStartup.IsBackground(args))singleInstance.SignalBackground();
            else singleInstance.SignalActivation();
            return;
        }

        Application.Run(new MainForm(singleInstance,LauncherStartup.IsBackground(args)));
    }
}
