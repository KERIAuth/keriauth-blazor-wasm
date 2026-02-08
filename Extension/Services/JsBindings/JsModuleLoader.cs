using Blazor.BrowserExtension;
using Microsoft.JSInterop;

namespace Extension.Services.JsBindings;

/// <summary>
/// Centralized service for loading all JavaScript ES modules at startup
/// Implements fail-fast loading pattern - all modules loaded during initialization
/// </summary>
public interface IJsModuleLoader {
    /// <summary>
    /// Load JavaScript modules for the specified browser extension context.
    /// Should be called once during application startup.
    /// </summary>
    ValueTask LoadAllModulesAsync(BrowserExtensionMode mode);

    /// <summary>
    /// Get a loaded module by name
    /// </summary>
    IJSObjectReference GetModule(string moduleName);

    /// <summary>
    /// Check if modules have been loaded
    /// </summary>
    bool IsInitialized { get; }
}

public class JsModuleLoader(IJSRuntime jsRuntime, ILogger<JsModuleLoader> logger) : IJsModuleLoader, IAsyncDisposable {
    private readonly IJSRuntime _jsRuntime = jsRuntime;
    private readonly ILogger<JsModuleLoader> _logger = logger;
    private readonly Dictionary<string, IJSObjectReference> _modules = [];
    private bool _isInitialized;

    // Module definitions: name -> (path, contexts)
    // Each module specifies which BrowserExtensionMode(s) require it
    private static readonly (string Name, string Path, BrowserExtensionMode[] Contexts)[] ModuleDefinitions = [
        ("signifyClient", "./scripts/esbuild/signifyClient.js", [BrowserExtensionMode.Background]),
        ("demo1", "./scripts/esbuild/demo1.js", [BrowserExtensionMode.Background]),
        ("navigatorCredentialsShim", "./scripts/es6/navigatorCredentialsShim.js", [BrowserExtensionMode.Standard, BrowserExtensionMode.Debug]),
        ("aesGcmCrypto", "./scripts/es6/aesGcmCrypto.js", [BrowserExtensionMode.Standard, BrowserExtensionMode.Debug]),
    ];

    public bool IsInitialized => _isInitialized;

    public async ValueTask LoadAllModulesAsync(BrowserExtensionMode mode) {
        if (_isInitialized) {
            _logger.LogWarning("JsModuleLoader: Modules already loaded, skipping");
            return;
        }

        var modulesToLoad = ModuleDefinitions.Where(m => m.Contexts.Contains(mode)).ToArray();
        _logger.LogInformation("JsModuleLoader: Loading {Count} of {Total} JavaScript modules for {Mode} mode (fail-fast mode)",
            modulesToLoad.Length, ModuleDefinitions.Length, mode);

        var loadTasks = modulesToLoad.Select(async def => {
            try {
                _logger.LogDebug("JsModuleLoader: Loading module '{ModuleName}' from '{ModulePath}'", def.Name, def.Path);
                var module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", def.Path);
                _modules[def.Name] = module;
                _logger.LogInformation("JsModuleLoader: ✓ Loaded module '{ModuleName}'", def.Name);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "JsModuleLoader: ✗ FAILED to load module '{ModuleName}' from '{ModulePath}'", def.Name, def.Path);
                throw new InvalidOperationException($"Failed to load JavaScript module '{def.Name}' from '{def.Path}'", ex);
            }
        });

        // Wait for all modules to load - fail fast if any fail
        await Task.WhenAll(loadTasks);

        _isInitialized = true;
        _logger.LogInformation("JsModuleLoader: ✅ All modules loaded successfully");
    }

    public IJSObjectReference GetModule(string moduleName) {
        if (!_isInitialized) {
            throw new InvalidOperationException("Modules not loaded. Call LoadAllModulesAsync() first.");
        }

        if (!_modules.TryGetValue(moduleName, out var module)) {
            throw new ArgumentException($"Module '{moduleName}' not found. Available modules: {string.Join(", ", _modules.Keys)}", nameof(moduleName));
        }

        return module;
    }

    public async ValueTask DisposeAsync() {
        _logger.LogInformation("JsModuleLoader: Disposing {Count} modules", _modules.Count);

        foreach (var (moduleName, module) in _modules) {
            try {
                await module.DisposeAsync();
                _logger.LogDebug("JsModuleLoader: Disposed module '{ModuleName}'", moduleName);
            }
            catch (Exception ex) {
                _logger.LogWarning(ex, "JsModuleLoader: Error disposing module '{ModuleName}'", moduleName);
            }
        }

        _modules.Clear();
        _isInitialized = false;
        GC.SuppressFinalize(this);
    }
}
