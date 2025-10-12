using Microsoft.JSInterop;

namespace Extension.Services.JsBindings;

/// <summary>
/// Centralized service for loading all JavaScript ES modules at startup
/// Implements fail-fast loading pattern - all modules loaded during initialization
/// </summary>
public interface IJsModuleLoader {
    /// <summary>
    /// Load all JavaScript modules
    /// Should be called once during application startup
    /// </summary>
    ValueTask LoadAllModulesAsync();

    /// <summary>
    /// Get a loaded module by name
    /// </summary>
    IJSObjectReference GetModule(string moduleName);

    /// <summary>
    /// Check if modules have been loaded
    /// </summary>
    bool IsInitialized { get; }
}

public class JsModuleLoader : IJsModuleLoader, IAsyncDisposable {
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<JsModuleLoader> _logger;
    private readonly Dictionary<string, IJSObjectReference> _modules = new();
    private bool _isInitialized;

    // Module definitions: name -> path
    private readonly Dictionary<string, string> _moduleDefinitions = new() {
        { "signifyClient", "./scripts/esbuild/signifyClient.js" },
        { "storageHelper", "./scripts/es6/storageHelper.js" },
        { "webauthnCredentialWithPRF", "./scripts/es6/webauthnCredentialWithPRF.js" }
    };

    public bool IsInitialized => _isInitialized;

    public JsModuleLoader(IJSRuntime jsRuntime, ILogger<JsModuleLoader> logger) {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async ValueTask LoadAllModulesAsync() {
        if (_isInitialized) {
            _logger.LogWarning("JsModuleLoader: Modules already loaded, skipping");
            return;
        }

        _logger.LogInformation("JsModuleLoader: Loading {Count} JavaScript modules (fail-fast mode)", _moduleDefinitions.Count);

        var loadTasks = _moduleDefinitions.Select(async kvp => {
            var (moduleName, modulePath) = kvp;
            try {
                _logger.LogDebug("JsModuleLoader: Loading module '{ModuleName}' from '{ModulePath}'", moduleName, modulePath);
                var module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", modulePath);
                _modules[moduleName] = module;
                _logger.LogInformation("JsModuleLoader: ✓ Loaded module '{ModuleName}'", moduleName);
            }
            catch (Exception ex) {
                _logger.LogError(ex, "JsModuleLoader: ✗ FAILED to load module '{ModuleName}' from '{ModulePath}'", moduleName, modulePath);
                throw new InvalidOperationException($"Failed to load JavaScript module '{moduleName}' from '{modulePath}'", ex);
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
