interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: any[]): Promise<void>;
}

const SwAppInteropModule = (() => {
    const initializeMessaging = (dotNetHelper: DotNetObjectReference, tabId: String) => {
        const port = chrome.runtime.connect({ name: "blazorAppPort" + "-tab-" + tabId });

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
