interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: any[]): Promise<void>;
}
export declare const SwAppInteropModule: {
    initializeMessaging: (dotNetHelper: DotNetObjectReference, tabId: String) => chrome.runtime.Port;
    sendMessageToServiceWorker: (port: chrome.runtime.Port, type: string, message: string) => void;
};
export {};
