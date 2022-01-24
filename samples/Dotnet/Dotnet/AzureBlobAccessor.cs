using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using System.Text;

namespace DistributedMutex;

internal class AzureBlobAccessor
{
    private const string LeaseBlobContainer = "leaseblobcontainer";
    private BlobContainerClient _leaseContainerClient;

    public AzureBlobAccessor(string storageAccountName)
    {
        var blobServiceClient = new BlobServiceClient(
                    new Uri($"https://{storageAccountName}.blob.core.windows.net/"),
                    new DefaultAzureCredential(),
                    new BlobClientOptions() { GeoRedundantSecondaryUri = new Uri($"https://{storageAccountName}-secondary.blob.core.windows.net/") });

        _leaseContainerClient = blobServiceClient.GetBlobContainerClient(LeaseBlobContainer);
    }

    public virtual async Task<string> AcquireLeaseBlob(string key)
    {
        try
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
        catch (RequestFailedException e) when (e.ErrorCode == "LeaseAlreadyPresent")
        {
            Console.WriteLine($"The blob with key {key} has already been leased");
            throw;
        }
    }

    public virtual async Task RenewLeaseBlob(string key, string leaseId)
    {
        var blobClient = _leaseContainerClient!.GetBlobClient(key);
        var blobLeaseClient = blobClient.GetBlobLeaseClient(leaseId);
        await blobLeaseClient.RenewAsync();
    }

    public virtual async Task ReleaseLeaseBlob(string key, string leaseId)
    {
        var blobClient = _leaseContainerClient!.GetBlobClient(key);
        var blobLeaseClient = blobClient.GetBlobLeaseClient(leaseId);
        await blobLeaseClient.ReleaseAsync();
    }
}

