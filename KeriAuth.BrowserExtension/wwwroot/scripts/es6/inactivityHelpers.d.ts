interface DotNet {
    invokeMethodAsync<T>(assemblyName: string, methodName: string, ...args: any[]): Promise<T>;
}
declare var DotNet: DotNet;
declare function subscribeToUserInteractions(): void;
declare function resetInactivityTimer(): void;
declare function registerLockListener(): void;
