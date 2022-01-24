namespace DistributedMutex;

internal class DelegatingDisposable : IDisposable
{
    private readonly Action _dispose;

    public DelegatingDisposable(Action dispose) => _dispose = dispose;

    public void Dispose() => _dispose();
}

