interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: any[]): Promise<void>;
}

export const SwAppInteropModule = (() => {
    const initializeMessaging = (dotNetHelper: DotNetObjectReference, tabId: String) => {
        const port = chrome.runtime.connect({ name: "blazorAppPort" + "-tab-" + tabId });

        port.onMessage.addListener((message) => {
            // TODO EE!
            if (message && message.type === 'fromServiceWorker') {
                dotNetHelper.invokeMethodAsync('ReceiveMessage', message.data);
            }
        });

        return port;
    };

    const sendMessageToServiceWorker = (port: chrome.runtime.Port, type: string, message: string) => {
        // TODO EE!
        port.postMessage({ type: 'fromBlazorApp', data: message });
    };

    return {
        initializeMessaging,
        sendMessageToServiceWorker
    };
})();


