"use strict";
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
//# sourceMappingURL=inactivityHelpers.js.map