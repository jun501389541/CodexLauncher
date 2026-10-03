using CodexLauncher.Core;

namespace CodexLauncher.App;

// Internal dependency boundary for synthetic lifecycle tests; production uses the defaults.
internal sealed record LauncherServices(string DataDirectory,
    Func<bool,CancellationToken,SharedQuotaCoordinator> QuotaFactory,
    Func<SharedQuotaCoordinator,BridgeRuntime> BridgeFactory,IAutoStartRegistry AutoStartRegistry);
