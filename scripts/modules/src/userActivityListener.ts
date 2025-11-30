// userActivityListener.ts
// Debounced user activity listener for session extension
// Detects keydown and mouseup events and invokes C# callback via JSInterop

/**
 * Configuration options for the activity listener
 */
interface ActivityListenerOptions {
    /** Debounce interval in milliseconds. Events within this window are aggregated. Default: 1000ms */
    debounceMs?: number;
    /** DOM events to listen for. Default: ['keydown', 'mouseup'] */
    events?: string[];
}

/** Reference to .NET object for JSInvokable callbacks */
interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: unknown[]): Promise<unknown>;
}

// Module state
let lastCallbackTimeMs = 0;
let dotNetRef: DotNetObjectReference | null = null;
let isListening = false;
let boundHandler: ((event: Event) => void) | null = null;
let registeredEvents: string[] = [];

/**
 * Default configuration
 */
const DEFAULT_OPTIONS: Required<ActivityListenerOptions> = {
    debounceMs: 1000,
    events: ['keydown', 'mouseup']
};

/**
 * Internal event handler that debounces DOM events before invoking the .NET callback.
 * Called on every matching DOM event, but only forwards to C# if debounce interval has passed.
 */
function handleActivity(): void {
    if (!dotNetRef) return;

    const now = Date.now();
    if (now - lastCallbackTimeMs < DEFAULT_OPTIONS.debounceMs) {
        // Within debounce window - skip this event
        return;
    }

    lastCallbackTimeMs = now;

    // Fire-and-forget async call to C#
    dotNetRef.invokeMethodAsync('OnUserActivity').catch((error: unknown) => {
        console.warn('userActivityListener: Failed to invoke OnUserActivity:', error);
    });
}

/**
 * Starts listening for user activity events on the document.
 * Events are debounced to reduce callback frequency.
 *
 * @param dotNetObjectRef - Reference to .NET object with [JSInvokable] OnUserActivity method
 * @param options - Optional configuration for debounce interval and event types
 */
export function startListening(
    dotNetObjectRef: DotNetObjectReference,
    options?: ActivityListenerOptions
): void {
    if (isListening) {
        // console.debug('userActivityListener: Already listening, ignoring startListening call');
        return;
    }

    // Apply options
    const debounceMs = options?.debounceMs ?? DEFAULT_OPTIONS.debounceMs;
    const events = options?.events ?? DEFAULT_OPTIONS.events;

    // Store configuration
    DEFAULT_OPTIONS.debounceMs = debounceMs;
    dotNetRef = dotNetObjectRef;
    registeredEvents = [...events];

    // Create bound handler
    boundHandler = handleActivity;

    // Register event listeners with capture phase for early detection
    for (const eventType of registeredEvents) {
        document.addEventListener(eventType, boundHandler, { capture: true, passive: true });
    }

    isListening = true;
    // console.debug(`userActivityListener: Started listening for [${registeredEvents.join(', ')}] with ${debounceMs}ms debounce`);
}

/**
 * Stops listening for user activity events and cleans up resources.
 */
export function stopListening(): void {
    if (!isListening) {
        // console.debug('userActivityListener: Not listening, ignoring stopListening call');
        return;
    }

    // Remove event listeners
    if (boundHandler) {
        for (const eventType of registeredEvents) {
            document.removeEventListener(eventType, boundHandler, { capture: true });
        }
    }

    // Clear state
    boundHandler = null;
    dotNetRef = null;
    registeredEvents = [];
    isListening = false;
    lastCallbackTimeMs = 0;

    console.debug('userActivityListener: Stopped listening');
}

/**
 * Returns whether the listener is currently active.
 */
export function isActive(): boolean {
    return isListening;
}
