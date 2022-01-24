using DistributedMutex;

var azureBlobAccessor = new AzureBlobAccessor("multipletenantaadaf");
var distributedMutex = new DistributedMutex.DistributedMutex(azureBlobAccessor);

Console.WriteLine("Start");

using (await distributedMutex.AcquireAsync("test-key"))
{
    // Your business logic
    await Task.Delay(TimeSpan.FromSeconds(65));
}

Console.WriteLine("Done");

