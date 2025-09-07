interface DotNet {
    invokeMethodAsync<T>(assemblyName: string, methodName: string, ...args: any[]): Promise<T>;
}

declare var DotNet: DotNet;
function subscribeToUserInteractions() {
    document.addEventListener('click', resetInactivityTimer);
    document.addEventListener('keydown', resetInactivityTimer);
}

function resetInactivityTimer() {
    // console.log("resetInactivityTimer inactivityHelper");
    chrome.runtime.sendMessage({ action: 'resetInactivityTimer' })
        .then((response) => {
            // Timer reset successfully
            // console.log('Inactivity timer reset:', response);
        })
        .catch((error) => {
            // Handle error silently - the service worker might not be available
            console.warn('Failed to reset inactivity timer:', error);
        });
}

function registerLockListener() {
    chrome.runtime.onMessage.addListener((message:any, _sender, sendResponse: (response?: any) => void) => {
        if (message.action === 'lockApp') {
            // Invoke the .NET method and send response when complete
            DotNet.invokeMethodAsync('KeriAuth.BrowserExtension', 'LockApp')
                .then(() => {
                    sendResponse({ success: true });
                })
                .catch((error) => {
                    console.error('Failed to lock app:', error);
                    sendResponse({ success: false, error: error.message });
                });
            // Return true to indicate async response
            return true;
        }
        // Don't return true if we're not handling this message
        return false;
    });
}

