namespace CodexLauncher.Core;

public sealed class AsyncOperationGate
{
    private int _entered;

    public bool IsBusy => Volatile.Read(ref _entered) != 0;

    public bool TryEnter() => Interlocked.CompareExchange(ref _entered, 1, 0) == 0;

    public void Exit() => Volatile.Write(ref _entered, 0);
}
