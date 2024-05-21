using Microsoft.JSInterop;

namespace KeriAuth.BrowserExtension.Helper
{
    public class PreviousPage
    {
        public static async Task GoBack(IJSRuntime jsRuntime)
        {
            await jsRuntime.InvokeVoidAsync("history.back");
        }
    }
}
