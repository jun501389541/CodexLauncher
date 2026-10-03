using System.Text.Json;

namespace CodexLauncher.Core;

public sealed class ProxyEnvironmentTransaction(IEnvironmentStore store, string journalPath)
{
    public void Begin(IReadOnlyDictionary<string, string> injection)
    {
        if (File.Exists(journalPath)) Recover();
        var journal = ProxyEnvironmentJournal.Capture(store, injection);
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        var tempPath = journalPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(journal));
        File.Move(tempPath, journalPath, true);
        try { journal.Apply(store); }
        catch { journal.Restore(store); File.Delete(journalPath); throw; }
    }

    public void Recover()
    {
        if (!File.Exists(journalPath)) return;
        var journal = JsonSerializer.Deserialize<ProxyEnvironmentJournal>(File.ReadAllText(journalPath))
            ?? throw new InvalidDataException("代理环境恢复记录损坏。");
        journal.Restore(store);
        File.Delete(journalPath);
    }
}
