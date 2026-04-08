// networkConnectivityListener.ts
// Monitors browser online/offline state in the service worker context
// and invokes C# callback via JSInterop when connectivity changes.

/** Reference to .NET object for JSInvokable callbacks */
interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<unknown>;
}

// Module state
let dotNetRef: DotNetObjectReference | null = null;

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

// Register event listeners at module top level so Chrome service worker
// sees them during initial script evaluation (required by Chrome SW spec).
self.addEventListener('online', handleOnline);
self.addEventListener('offline', handleOffline);

/**
 * Starts listening for online/offline events.
 * Provides the .NET callback reference and immediately reports current state.
 *
 * @param dotNetObjectRef - Reference to .NET object with [JSInvokable] OnNetworkStateChanged method
 */
export function startListening(dotNetObjectRef: DotNetObjectReference): void {
    dotNetRef = dotNetObjectRef;

    // Report current state immediately
    dotNetRef.invokeMethodAsync('OnNetworkStateChanged', navigator.onLine).catch((error: unknown) => {
        console.warn('networkConnectivityListener: Failed to report initial state:', error);
    });
}

/**
 * Stops forwarding online/offline events to .NET.
 * The underlying event listeners remain registered (required by Chrome SW spec)
 * but become no-ops since dotNetRef is cleared.
 */
export function stopListening(): void {
    dotNetRef = null;
    console.debug('networkConnectivityListener: Stopped listening');
}

/**
 * Returns whether the listener is currently forwarding events to .NET.
 */
export function isActive(): boolean {
    return dotNetRef !== null;
}
