// Define a generic type for the loadModule function's return value
interface LoadModuleResult<T> {
    success: boolean;
    module?: T;
    error?: string;
}

// Assuming you know the structure of the module you are loading, define its type
// For example, if your module exports a function named 'myFunction', define it like so:
// (Adjust this example to fit the actual module you're loading)

// for ui-utilities
interface Iuiutils {
    utils: () => void;
}

// Function to dynamically load a module
export async function loadModule<T>(url: string): Promise<LoadModuleResult<T>> {
    try {
        const module = await import(url) as T;
        return { success: true, module: module };
    } catch (error) {
        return { success: false, error: (error as Error).message };
    }
}