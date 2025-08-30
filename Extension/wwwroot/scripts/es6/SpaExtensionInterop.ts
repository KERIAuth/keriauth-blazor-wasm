
// SpaExtensionInterop.ts
// TODO P2 unused? If so, remove this

// Define the structure of the message to be sent when unloading
interface ExtensionMessage {
    type: "extensionUnloading";
    payload: Record<string, unknown>;
}

// Attach setupUnloadListener to the global window object
// Note, the TS compiler relies on global.d.ts to avoid compiler issues
window.setupUnloadListener = function (): void {
    window.addEventListener("beforeunload", () => {
        const message: ExtensionMessage = { type: "extensionUnloading", payload: {} };

        // Send the message to the background script for handling
        chrome.runtime.sendMessage(message);
    });
};
