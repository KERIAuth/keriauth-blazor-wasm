using Extension.Services;
using Microsoft.AspNetCore.Components;
using System.Runtime.CompilerServices;

namespace Extension.Components {
    /// <summary>
    /// Extension methods for ComponentBase to enable reactive AppCache subscriptions
    /// without requiring inheritance from AppCacheReactiveComponentBase.
    ///
    /// Usage:
    /// <code>
    /// protected override async Task OnInitializedAsync() {
    ///     await this.SubscribeToAppCache(appCache, MyCustomCallback);
    /// }
    ///
    /// public override void Dispose() {
    ///     this.UnsubscribeFromAppCache();
    ///     base.Dispose();
    /// }
    /// </code>
    /// </summary>
    public static class AppCacheComponentExtensions {
        private static readonly ConditionalWeakTable<ComponentBase, AppCacheSubscription> _subscriptions = [];

        /// <summary>
        /// Subscribe a component to AppCache changes. Call in OnInitializedAsync.
        /// The component will automatically call StateHasChanged() when AppCache changes.
        /// </summary>
        /// <param name="component">The component to subscribe</param>
        /// <param name="appCache">The AppCache instance to observe</param>
        /// <param name="onChanged">Optional callback to execute after StateHasChanged when cache changes</param>
        public static async Task SubscribeToAppCache(
            this ComponentBase component,
            AppCache appCache,
            Func<Task>? onChanged = null) {

            var subscription = new AppCacheSubscription(component, appCache, onChanged);
            _subscriptions.Add(component, subscription);
            await subscription.Initialize();
        }

        /// <summary>
        /// Unsubscribe from AppCache. Call in Dispose() method.
        /// </summary>
        /// <param name="component">The component to unsubscribe</param>
        public static void UnsubscribeFromAppCache(this ComponentBase component) {
            if (_subscriptions.TryGetValue(component, out var subscription)) {
                subscription.Dispose();
                _subscriptions.Remove(component);
            }
        }

        private sealed class AppCacheSubscription(ComponentBase component, AppCache appCache, Func<Task>? onChanged) : IDisposable {
            private readonly ComponentBase _component = component;
            private readonly AppCache _appCache = appCache;
            private readonly Func<Task>? _onChanged = onChanged;

            private readonly System.Reflection.MethodInfo? _stateHasChangedMethod = typeof(ComponentBase).GetMethod(
                    "StateHasChanged",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            private readonly System.Reflection.MethodInfo? _invokeAsyncMethod = typeof(ComponentBase).GetMethod(
                    "InvokeAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                    null,
                    [typeof(Func<Task>)],
                    null);

            public async Task Initialize() {
                _appCache.Changed += OnAppCacheChanged;
                await _appCache.Initialize();
            }

            private void OnAppCacheChanged() {
                if (_invokeAsyncMethod == null) {
                    return;
                }

                Func<Task> action = async () => {
                    _stateHasChangedMethod?.Invoke(_component, null);
                    if (_onChanged != null) {
                        await _onChanged();
                    }
                };

                // Invoke protected InvokeAsync method via reflection
                _ = _invokeAsyncMethod.Invoke(_component, [action]);
            }

            public void Dispose() {
                _appCache.Changed -= OnAppCacheChanged;
            }
        }
    }
}
