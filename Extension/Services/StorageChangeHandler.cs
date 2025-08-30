using Microsoft.JSInterop;

namespace Extension.Services
{
    public class StorageChangeHandler
    {
        [JSInvokable]  // see storageHelper.ts
        public static Task OnStorageChanged(object changes, string areaName)
        {
            // Handle the storage change here
            // You can process the changes object and areaName as needed
            Console.WriteLine($"Storage changed in {areaName} {changes}");
            // Additional processing logic can be added here

            return Task.CompletedTask;
        }
    }
}