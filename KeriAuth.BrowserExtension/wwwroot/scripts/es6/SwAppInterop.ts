interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: any[]): Promise<void>;
}

const SwAppInteropModule = (() => {
    const initializeMessaging = (dotNetHelper: DotNetObjectReference) => {
        // TODO handle non-fixed, named ports based on the tabId (when we have multiple tabs open with the same extension) or 0 for general?
        const port = chrome.runtime.connect({ name: "blazorAppPort" });

        port.onMessage.addListener((message) => {
            if (message && message.type === 'fromServiceWorker') {
                dotNetHelper.invokeMethodAsync('ReceiveMessage', message.data);
            }
        });

        return port;
    };

    const sendMessageToServiceWorker = (port: chrome.runtime.Port, message: string) => {
        port.postMessage({ type: 'fromBlazorApp', data: message });
    };

    return {
        initializeMessaging,
        sendMessageToServiceWorker
    };
})();

export { SwAppInteropModule };
