/**
 * ActionClickHandler.ts
 *
 * Handles chrome.action.onClicked events in the service worker, requesting permissions
 * while the user gesture is still active, then delegating to C# BackgroundWorker.
 *
 * This module solves the user gesture context loss issue when crossing the JavaScript-C#
 * boundary in WebExtensions.Net event listeners.
 */

/**
 * Build match patterns from the tab's URL, suitable for:
 * - chrome.permissions.{contains,request}({ origins: [...] })
 * - chrome.scripting.registerContentScripts({ matches: [...] })
 */
function buildMatchPatternsFromTabUrl(tabUrl: string): string[] {
  try {
    const uri = new URL(tabUrl);
    if (uri.protocol === "http:" || uri.protocol === "https:") {
      // Exact host only - returns pattern like "https://example.com/*"
      return [`${uri.protocol}//${uri.host}/*`];
    }
  } catch (e) {
    console.debug("ActionClickHandler: Error parsing tab URL:", tabUrl, e);
  }
  return [];
}

/**
 * Request persistent host permissions for the given origin patterns.
 * This MUST be called as the first async operation to preserve user gesture.
 */
async function requestPermissions(matchPatterns: string[]): Promise<boolean> {
  try {
    const wanted = { origins: matchPatterns };
    const granted = await chrome.permissions.request(wanted);
    console.log("ActionClickHandler: Permission request result:", granted, "for", matchPatterns);
    return granted;
  } catch (e) {
    console.error("ActionClickHandler: Permission request failed:", e);
    return false;
  }
}

/**
 * Check if we already have the required permissions.
 */
async function checkPermissions(matchPatterns: string[]): Promise<boolean> {
  try {
    const wanted = { origins: matchPatterns };
    const granted = await chrome.permissions.contains(wanted);
    return granted;
  } catch (e) {
    console.error("ActionClickHandler: Permission check failed:", e);
    return false;
  }
}

/**
 * Action click handler that preserves user gesture for permission request.
 * After handling permissions, delegates to C# BackgroundWorker.
 */
async function handleActionClick(tab: chrome.tabs.Tab): Promise<void> {
  if (!tab?.id || !tab.url) {
    console.log("ActionClickHandler: Invalid tab information");
    return;
  }

  console.log("ActionClickHandler: Action button clicked on tab:", tab.id, "URL:", tab.url);

  // 1) Compute per-origin match patterns from the clicked tab
  const matchPatterns = buildMatchPatternsFromTabUrl(tab.url);
  if (matchPatterns.length === 0) {
    console.log("ActionClickHandler: Unsupported or restricted URL scheme; not requesting permissions. URL:", tab.url);
    // Still call C# handler - it can do activeTab injection without persistent permissions
    await callCSharpHandler(tab, false);
    return;
  }

  // 2) Check if we already have permissions
  let granted = await checkPermissions(matchPatterns);

  if (!granted) {
    // 3) Request persistent host permission FIRST (while user gesture is still active)
    // CRITICAL: This must be the first async call after the user gesture to preserve the context
    granted = await requestPermissions(matchPatterns);

    if (!granted) {
      console.log("ActionClickHandler: User declined persistent host permission; will use activeTab only.");
      // Still proceed with activeTab permission for one-shot injection
    }
  } else {
    console.log("ActionClickHandler: Persistent host permission already granted for", matchPatterns);
  }

  // 4) Call into C# BackgroundWorker with permission status
  await callCSharpHandler(tab, granted);
}

/**
 * Calls the C# BackgroundWorker handler.
 * We use DotNet.invokeMethodAsync to call the JSInvokable method.
 */
async function callCSharpHandler(tab: chrome.tabs.Tab, permissionsGranted: boolean): Promise<void> {
  try {
    // Call the JSInvokable C# method directly
    // DotNet is provided by Blazor framework in the service worker context
    // Note: In service worker context, use globalThis instead of window
    const DotNet = (globalThis as any).DotNet;

    if (!DotNet) {
      console.error("ActionClickHandler: DotNet object not available. Blazor may not be initialized yet.");
      return;
    }

    await DotNet.invokeMethodAsync(
      'Extension',  // Assembly name
      'OnActionClickedWithPermissionsAsync',  // Method name
      tab,
      permissionsGranted
    );
    console.log("ActionClickHandler: C# handler invoked successfully");
  } catch (e) {
    console.error("ActionClickHandler: Error calling C# handler:", e);
    throw e;
  }
}

/**
 * Initialize the action click handler.
 * This should be called from the service worker startup (from C# BackgroundWorker).
 */
export function initializeActionClickHandler(): void {
  console.log("ActionClickHandler: Initializing action click handler");

  // Register our TypeScript handler to intercept action clicks
  // This handler preserves user gesture for permission requests before calling into C#
  chrome.action.onClicked.addListener(handleActionClick);

  console.log("ActionClickHandler: Handler registered");
}
