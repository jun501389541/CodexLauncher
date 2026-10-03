namespace CodexLauncher.Core;

public enum LaunchStatus { Launched, AlreadyRunning, Busy, Failed }

public interface IAppProcessDetector
{
    bool IsRunning(CodexInstallation app);
}

public interface IAppActivator
{
    Task<bool> ActivateAsync(CodexInstallation app, CancellationToken cancellationToken);
}

public interface IEnvironmentNotifier
{
    void NotifyChanged();
}

public sealed class AppLauncher(IEnvironmentStore environment, string journalPath, IAppProcessDetector detector, IAppActivator activator)
{
    public async Task<LaunchStatus> LaunchAsync(CodexInstallation app, RouteKind route, ProxyAddress? proxy, CancellationToken cancellationToken)
    {
        FileStream gate;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
            gate = new FileStream(journalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return LaunchStatus.Busy; }
        catch (UnauthorizedAccessException) { return LaunchStatus.Busy; }
        using (gate)
        {
            var transaction = new ProxyEnvironmentTransaction(environment, journalPath);
            transaction.Recover();
            Notify();
            if (detector.IsRunning(app)) return LaunchStatus.AlreadyRunning;
            if (route == RouteKind.Proxy)
            {
                transaction.Begin(ProxyEnvironment.ForUser(route, proxy));
                Notify();
            }
            try
            {
                return await activator.ActivateAsync(app, cancellationToken) ? LaunchStatus.Launched : LaunchStatus.Failed;
            }
            finally
            {
                if (route == RouteKind.Proxy)
                {
                    transaction.Recover();
                    Notify();
                }
            }
        }
    }

    public void Recover()
    {
        try { _ = File.GetAttributes(journalPath); }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        using var gate = new FileStream(journalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        new ProxyEnvironmentTransaction(environment, journalPath).Recover();
        Notify();
    }

    private void Notify()
    {
        if (environment is IEnvironmentNotifier notifier) notifier.NotifyChanged();
    }
}
