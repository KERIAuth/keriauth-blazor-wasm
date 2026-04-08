// networkConnectivityListener.ts
// Monitors browser online/offline state in the service worker context
// and invokes C# callback via JSInterop when connectivity changes.

/** Reference to .NET object for JSInvokable callbacks */
interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<unknown>;
}

// Module state
let dotNetRef: DotNetObjectReference | null = null;
let isListening = false;

function handleOnline(): void {
    if (!dotNetRef) return;
    dotNetRef.invokeMethodAsync('OnNetworkStateChanged', true).catch((error: unknown) => {
        console.warn('networkConnectivityListener: Failed to invoke OnNetworkStateChanged(true):', error);
    });
}

function handleOffline(): void {
    if (!dotNetRef) return;
    dotNetRef.invokeMethodAsync('OnNetworkStateChanged', false).catch((error: unknown) => {
        console.warn('networkConnectivityListener: Failed to invoke OnNetworkStateChanged(false):', error);
    });
}

/**
 * Starts listening for online/offline events.
 * Uses `self` (globalThis) to work in both window and service worker contexts.
 * Immediately reports the current navigator.onLine state on startup.
 *
 * @param dotNetObjectRef - Reference to .NET object with [JSInvokable] OnNetworkStateChanged method
 */
export function startListening(dotNetObjectRef: DotNetObjectReference): void {
    if (isListening) {
        // Already listening — but still report current state (handles SW wake scenarios)
        dotNetObjectRef.invokeMethodAsync('OnNetworkStateChanged', navigator.onLine).catch((error: unknown) => {
            console.warn('networkConnectivityListener: Failed to report initial state on re-start:', error);
        });
        // Update ref in case it changed (new DotNetObjectReference after SW restart)
        dotNetRef = dotNetObjectRef;
        return;
    }

    dotNetRef = dotNetObjectRef;

    self.addEventListener('online', handleOnline);
    self.addEventListener('offline', handleOffline);

    isListening = true;

    // Report current state immediately
    dotNetRef.invokeMethodAsync('OnNetworkStateChanged', navigator.onLine).catch((error: unknown) => {
        console.warn('networkConnectivityListener: Failed to report initial state:', error);
    });
}

/**
 * Stops listening for online/offline events and cleans up resources.
 */
export function stopListening(): void {
    if (!isListening) return;

    self.removeEventListener('online', handleOnline);
    self.removeEventListener('offline', handleOffline);

    dotNetRef = null;
    isListening = false;

    console.debug('networkConnectivityListener: Stopped listening');
}

/**
 * Returns whether the listener is currently active.
 */
export function isActive(): boolean {
    return isListening;
}
