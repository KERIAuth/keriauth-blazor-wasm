using Extension.Services;
using Microsoft.AspNetCore.Components;

namespace Extension.Components {
    public abstract class AppCacheReactiveComponentBase : ComponentBase, IDisposable {
        [Inject]
        protected AppCache MyAppCache { get; set; } = default!;

        protected override async Task OnInitializedAsync() {
            base.OnInitialized();
            MyAppCache.Changed += OnAppCacheChanged;
            await MyAppCache.Initialize();
        }

        private void OnAppCacheChanged() {
            // Schedule the UI update safely on the renderer.
            // marshal onto the renderer's sync context
            _ = InvokeAsync(StateHasChanged);
        }

        public void Dispose() {
            MyAppCache.Changed -= OnAppCacheChanged;
            GC.SuppressFinalize(this);
        }
    }
}
