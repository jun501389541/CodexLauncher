namespace CodexLauncher.Core;

public interface IEnvironmentStore
{
    string? Get(string name);
    void Set(string name, string? value);
}

public sealed record EnvironmentChange(string Name, string? Previous, string Injected);

public sealed record ProxyEnvironmentJournal(IReadOnlyList<EnvironmentChange> Changes)
{
    public static ProxyEnvironmentJournal Capture(IEnvironmentStore store, IReadOnlyDictionary<string, string> injection) =>
        new(injection.Select(item => new EnvironmentChange(item.Key, store.Get(item.Key), item.Value)).ToArray());

    public void Apply(IEnvironmentStore store)
    {
        foreach (var change in Changes) store.Set(change.Name, change.Injected);
    }

    public void Restore(IEnvironmentStore store)
    {
        foreach (var change in Changes)
            if (store.Get(change.Name) == change.Injected) store.Set(change.Name, change.Previous);
    }
}
