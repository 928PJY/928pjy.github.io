using Timer = System.Timers.Timer;

namespace DistributedMutex;

internal class DistributedMutex
{
    private readonly AzureBlobAccessor _azureBlobAccessor;

    public DistributedMutex(AzureBlobAccessor azureBlobAccessor)
    {
        _azureBlobAccessor = azureBlobAccessor;
    }

    public async Task<DelegatingDisposable> AcquireAsync(string key)
    {
        Timer? timer = null;

        var leaseId = await _azureBlobAccessor.AcquireLeaseBlob(key);

        // Renew the lease every 55 seconds before it expires at 60 seconds.
        timer = new Timer(55000);
        timer.Elapsed += async (sender, e) =>
        {
            await _azureBlobAccessor.RenewLeaseBlob(key, leaseId);
        };
        timer.Start();

        return new DelegatingDisposable(async () =>
        {
            timer.Dispose();
            await _azureBlobAccessor.ReleaseLeaseBlob(key, leaseId);
        });
    }
}
