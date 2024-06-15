// Function to dynamically load a module
export async function loadModule(url) {
    try {
        const module = await import(url);
        return { success: true, module: module };
    }
    catch (error) {
        return { success: false, error: error.message };
    }
}
//# sourceMappingURL=moduleLoader.js.map