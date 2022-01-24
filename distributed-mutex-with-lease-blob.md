# Distributed mutex with lease blob

For a web service, it is very common for it to have multiple instances, in that case, if you have a scenario that only one instance is allowed to process the same entity, you will need a distributed mutex.  
There are lots of methods to create a distributed mutex in .net, but if your service leverages on the Azure, `Lease blob` will be an easy way for you to implement a distributed mutex.

## How does lease blob work

Lease blob is a feature provided by Azure blob storage, it provides the followings:
- Only one caller can successfully acquire a new lease on a blob, once the blob has been leased, the others will get an exception with "LeaseAlreadyPresent" code when they try to request a new lease.
- You need to specify the lease duration when you acquire the lease, and the duration must be between 15 to 60 seconds, or infinite(not recommended).
- For a non-infinite lease, if you don't renew the existing lease before the lease period expires, the lease blob will be released automatically.

With the above information, it will be easy to use the lease blob to implement a distribute mutex by the following process:
- Acquire a lease on blob(Create firstly if it doesn't exist)
- If the lease is acquired successfully, start your business logic, otherwise, it stops gracefully indicating a conflict error.
    - If your job cannot be finished during the lease period, you need to keep renewing the existing lease to get another lease period.
- Release the lease after you finish your job, so others can acquire a new lease immediately.

> Note: There are cases that the application terminates unexpectedly, so to avoid the lease never being released, you should not use the infinite lease.

## Mutex implementation

With the background given above, the implementation can be split to three parts:
- **AzureBlobAccessor**: which handles the communication with azure blob, including authentication, request, or release of the lease.
- **DistributedMutex**: which integrated with AzureBlobAccessor and wrap it to expose an easy interface for others to use the mutex.
- A sample about how to use the **DistributedMutex**

You can find the sample code [here](https://github.com/928PJY/928pjy.github.io/samples/Dotnet).

### AzureBlobAccessor

You will need the following functions exposed by the blob accessor:
- AcquireLeaseBlob: request a new lease on the target blob(create if not exists) with 60 seconds period.
    ```C#
    public async Task<string> AcquireLeaseBlob(string key)
    {
        var blobClient = _leaseContainerClient!.GetBlobClient(key);
        if (!await blobClient.ExistsAsync())
        {
            var content = Encoding.UTF8.GetBytes(string.Empty);
            using (var ms = new MemoryStream(content))
            {
                await blobClient.UploadAsync(ms);
            }
        }
        var blobLeaseClient = blobClient.GetBlobLeaseClient();

        return (await blobLeaseClient.AcquireAsync(TimeSpan.FromSeconds(60))).Value.LeaseId;
    }
    ```

- RenewLeaseBlob: renew the existing lease.
    ```C#
    public async Task RenewLeaseBlob(string key, string leaseId)
    {
        var blobClient = _leaseContainerClient!.GetBlobClient(key);
        var blobLeaseClient = blobClient.GetBlobLeaseClient(leaseId);
        await blobLeaseClient.RenewAsync();
    }
    ```
- ReleaseLeaseBlob: release the existing lease.
    ```C#
    public async Task ReleaseLeaseBlob(string key, string leaseId)
    {
        var blobClient = _leaseContainerClient!.GetBlobClient(key);
        var blobLeaseClient = blobClient.GetBlobLeaseClient(leaseId);
        await blobLeaseClient.ReleaseAsync();
    }
    ```

### DistributedMutex

In the distributed mutex, we wrap the integration with blob accessor, and expose an easy interface like the following:
```C#
public async Task<DelegatingDisposable> AcquireAsync(string key)
```
We expose a function that takes a key(String) as the identity of the lease and returns a disposable, if the lease is successfully acquired, the caller can directly use it with a `using` block, which handles renewing the lease regularly and release the lease after the job is done. the detailed implementation is:
```C#
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

internal class DelegatingDisposable : IDisposable
{
    private readonly Action _dispose;

    public DelegatingDisposable(Action dispose) => _dispose = dispose;

    public void Dispose() => _dispose();
}
```

### Usage sample

```C#
var azureBlobAccessor = new AzureBlobAccessor("storage-account");
var distributedMutex = new DistributedMutex.DistributedMutex(azureBlobAccessor);

Console.WriteLine("Start");

using (await distributedMutex.AcquireAsync("test-key"))
{
    // Your business logic
    await Task.Delay(TimeSpan.FromSeconds(65));
}

Console.WriteLine("Done");
```

To use the distributed mutex, you just need to call the `AcquireAsync` function and put your business logic inside the `using` block, the `DistributedMutex` will handle the lease acquire, renew before expire and release for you.

## Reference
- [Lease Blob (REST API) - Azure Storage | Microsoft Docs](https://docs.microsoft.com/en-us/rest/api/storageservices/lease-blob)