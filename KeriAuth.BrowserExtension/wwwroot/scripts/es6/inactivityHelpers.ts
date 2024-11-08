interface DotNet {
    invokeMethodAsync<T>(assemblyName: string, methodName: string, ...args: any[]): Promise<T>;
}

declare var DotNet: DotNet;
function subscribeToUserInteractions() {
    document.addEventListener('click', resetInactivityTimer);
    document.addEventListener('keydown', resetInactivityTimer);
}

function resetInactivityTimer() {
    chrome.runtime.sendMessage({ action: 'resetInactivityTimer' });
}

function registerLockListener() {
    chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
        if (message.action === 'lockApp') {
            DotNet.invokeMethodAsync('KeriAuth.BrowserExtension', 'LockApp');
        }
    });
}
