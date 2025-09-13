using Microsoft.JSInterop;

namespace Extension.Helper {
    public class PreviousPage {
        public static async Task GoBack(IJSRuntime jsRuntime) {
            await jsRuntime.InvokeVoidAsync("history.back");
        }
    }
}
