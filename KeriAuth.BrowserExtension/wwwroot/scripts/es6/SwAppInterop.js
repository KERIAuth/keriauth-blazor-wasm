const SwAppInteropModule = (() => {
    const initializeMessaging = (dotNetHelper) => {
        const port = chrome.runtime.connect({ name: "blazorAppPort" });
        port.onMessage.addListener((message) => {
            if (message && message.type === 'fromServiceWorker') {
                dotNetHelper.invokeMethodAsync('ReceiveMessage', message.data);
            }
        });
        return port;
    };
    const sendMessageToServiceWorker = (port, message) => {
        port.postMessage({ type: 'fromBlazorApp', data: message });
    };
    return {
        initializeMessaging,
        sendMessageToServiceWorker
    };
})();
export { SwAppInteropModule };
//# sourceMappingURL=SwAppInterop.js.map