interface DotNet {
    invokeMethodAsync<T>(assemblyName: string, methodName: string, ...args: any[]): Promise<T>;
}

declare var DotNet: DotNet;
function subscribeToUserInteractions() : void {
    document.addEventListener('click', resetInactivityTimer);
    document.addEventListener('keydown', resetInactivityTimer);
}

function resetInactivityTimer() : void {
    // console.log("resetInactivityTimer inactivityHelper");
    chrome.runtime.sendMessage({ action: 'resetInactivityTimer' })
        .then(() => {
            // Timer reset successfully
            // console.log('Inactivity timer reset:', response);
        })
        .catch((error) => {
            // Handle error silently - the service worker might not be available
            console.warn('Failed to reset inactivity timer:', error);
        });
}

function registerLockListener() : void {
    chrome.runtime.onMessage.addListener((message:any, _sender, sendResponse: (response?: any) => void) => {
        if (message.action === 'LockApp') {
            // Wrap in try-catch to handle synchronous errors
            try {
                // Check if DotNet is available before attempting to invoke
                if (typeof DotNet === 'undefined' || !DotNet.invokeMethodAsync) {
                    console.warn('DotNet not available for LockApp');
                    sendResponse({ success: false, error: 'DotNet runtime not available' });
                    return false; // Synchronous response
                }

                // Invoke the .NET method and send response when complete
                DotNet.invokeMethodAsync('Extension', 'LockApp')
                    .then(() => {
                        try {
                            sendResponse({ success: true });
                        } catch (e) {
                            // Channel might be closed
                            console.warn('Could not send LockApp response - channel closed', e);
                        }
                    })
                    .catch((error) => {
                        console.error('Failed to lock app:', error);
                        try {
                            sendResponse({ success: false, error: error.message });
                        } catch (e) {
                            // Channel might be closed
                            console.warn('Could not send LockApp error response - channel closed', e);
                        }
                    });
                // Return true to indicate async response
                return true;
            } catch (error) {
                console.error('LockApp listener error:', error);
                sendResponse({ success: false, error: 'Failed to process LockApp' });
                return false; // Synchronous response for errors
            }
        }
        // Don't return true if we're not handling this message
        return false;
    });
}

