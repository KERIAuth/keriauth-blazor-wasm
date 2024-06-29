interface DotNetObjectReference {
    invokeMethodAsync(methodName: string, ...args: any[]): Promise<void>;
}
declare const SwAppInteropModule: {
    initializeMessaging: (dotNetHelper: DotNetObjectReference) => chrome.runtime.Port;
    sendMessageToServiceWorker: (port: chrome.runtime.Port, message: string) => void;
};
export { SwAppInteropModule };
