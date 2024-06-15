interface LoadModuleResult<T> {
    success: boolean;
    module?: T;
    error?: string;
}
export declare function loadModule<T>(url: string): Promise<LoadModuleResult<T>>;
export {};
