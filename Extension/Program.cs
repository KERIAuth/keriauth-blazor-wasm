using Blazor.BrowserExtension;
using Extension;
using Extension.Services;
using Extension.Services.Crypto;
using Extension.Services.JsBindings;
using Extension.Services.Port;
using Extension.Services.SignifyService;
using Extension.Services.Storage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;

// Program and Main are implicit and static

// ==================================================================================
// KNOWN ISSUE: SRI Integrity Check Failures After Extension Refresh
// ==================================================================================
//
// SYMPTOM: After clicking "Reload" in chrome://extensions, the BackgroundWorker
// service worker may fail to start with errors like:
//   "Failed to find a valid digest in the 'integrity' attribute for resource
//    'chrome-extension://[id]/framework/dotnet.native.wasm'"
//
// WHEN IT OCCURS: The error happens BEFORE this Program.cs code executes.
// The Blazor WASM runtime fails during initialization in BackgroundWorkerRunner.js
// when attempting to fetch and verify dotnet.native.wasm with SRI checks.
//
// ROOT CAUSE: Extension reload causes cached file hashes to become stale.
// The browser's Subresource Integrity (SRI) checks fail because the extension's
// files have been replaced but the browser cache still references old hashes.
//
// DIAGNOSTIC: If you see "app.ts: Setting up Background mode event handlers" but
// NOT "Program.cs: Entry point reached", then the SRI check failed and the WASM
// runtime never initialized. This Program.cs file never executes in that case.
//
// WHY NOT FIXED: This ensures content scripts remain consistent with extension version.
// If we allowed stale WASM to load, it could interact with new content scripts,
// causing version mismatch bugs and unpredictable behavior.
//
// USER IMPACT: User must close and reopen extension pages (popup/tab/sidepanel)
// after clicking "Reload" in chrome://extensions. The BackgroundWorker will
// restart automatically when Chrome fires registered events (e.g., onStartup).
//
// DEVELOPER WORKFLOW: After extension reload:
// 1. Close all open extension pages (popup, tabs, sidepanel)
// 2. Reopen them - they will load with the new version
// 3. BackgroundWorker restarts automatically on next event
//
// TODO P3: Consider detecting SRI failures in app.ts and showing user-friendly error
// message instead of silent failure. Would require error handling in beforeStart().
//
// ==================================================================================

// STARTUP DIAGNOSTIC: Log to console before any builder initialization
// This helps identify if SRI integrity errors prevent WASM from loading
// If you DON'T see this log, the SRI check failed and WASM never initialized
Console.WriteLine("Program.cs: Entry point reached - WASM runtime loaded successfully");

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddJsonFile("./appsettings.json", optional: false, reloadOnChange: true);
builder.Logging.AddConfiguration(
    builder.Configuration.GetSection("Logging")
);

// Console.WriteLine("Program.cs: Configuring browser extension mode...");

// TODO P2: add consistent browserExtension.Mode string to logging context for better diagnostics

var extensionMode = BrowserExtensionMode.Standard;
builder.UseBrowserExtension(browserExtension => {
    extensionMode = browserExtension.Mode;
    Console.WriteLine($"Program.cs [{extensionMode}]");

    switch (browserExtension.Mode) {
        case BrowserExtensionMode.ContentScript:
            throw new NotImplementedException("ContentScript mode is not implemented in Program.cs");
        case BrowserExtensionMode.Background:
            builder.RootComponents.AddBackgroundWorker<BackgroundWorker>();
            builder.Services.AddSingleton<SessionManager>();
            builder.Services.AddSingleton<IPendingBwAppRequestService, PendingBwAppRequestService>();
            builder.Services.AddSingleton<IBwPortService, BwPortService>();
            builder.Services.AddSingleton<ISignifyClientBinding, SignifyClientBinding>();
            builder.Services.AddSingleton<ISignifyClientService, SignifyClientService>();
            builder.Services.AddSingleton<IDemo1Binding, Demo1Binding>();
            builder.Services.AddSingleton<ISchemaService, SchemaService>();
            break;
        case BrowserExtensionMode.Standard:
        case BrowserExtensionMode.Debug:
        default:
            builder.Services.AddMudServices();
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            builder.Services.AddSingleton<IUserActivityService, UserActivityService>();
            builder.Services.AddSingleton<SessionManager>();
            builder.Services.AddSingleton<IPendingBwAppRequestService, PendingBwAppRequestService>();
            builder.Services.AddSingleton<IAppBwPortService, AppBwPortService>();
            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            builder.Services.AddSingleton<AppCache>();
            builder.Services.AddSingleton<INavigatorCredentialsBinding, NavigatorCredentialsBinding>();
            builder.Services.AddSingleton<ICryptoService, SubtleCryptoService>();
            builder.Services.AddSingleton<IFidoMetadataService, FidoMetadataService>();
            builder.Services.AddSingleton<IWebauthnService, WebauthnService>();
            break;
    }
});

// Services common to both BackgroundWorker and App contexts
builder.Services.AddBrowserExtensionServices();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IJsModuleLoader, JsModuleLoader>();
builder.Services.AddSingleton<IWebsiteConfigService, WebsiteConfigService>();
builder.Services.AddJsBind();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var ctx = extensionMode == BrowserExtensionMode.Background ? "[BW]" : "[APP]";

// Load JavaScript ES modules via JsModuleLoader
// libsodium-polyfill is statically imported in app.ts before Blazor starts
// Other modules (signifyClient, navigatorCredentialsShim) are lazy-loaded to avoid initialization issues
logger.LogInformation("{Ctx} Loading JavaScript modules via JsModuleLoader...", ctx);
// Console.WriteLine("Program.cs: Loading JavaScript modules via JsModuleLoader...");

try {
    var moduleLoader = host.Services.GetRequiredService<IJsModuleLoader>();
    await moduleLoader.LoadAllModulesAsync(extensionMode);
    logger.LogInformation("{Ctx} JavaScript modules loaded successfully", ctx);
}
catch (Exception ex) {
    logger.LogError(ex, "{Ctx} Failed to load JavaScript modules via JsModuleLoader", ctx);
    throw;
}

// logger.LogInformation("All modules loaded successfully");

// Signal to app.ts that the BackgroundWorker is ready to handle port connections.
// This sets _wasmReady=true in the CLIENT_SW_HELLO handler so polling clients
// get ready=true. Called after DI is configured and modules are loaded.
// The slight delay before BackgroundWorker.Main() creates the component is handled
// by the App's Phase 2 port handshake retry mechanism.
if (extensionMode == BrowserExtensionMode.Background) {
    // var jsRuntimeForSignal = host.Services.GetRequiredService<IJSRuntime>();
    // await jsRuntimeForSignal.InvokeVoidAsync("__keriauth_setBwReady");
    // logger.LogInformation("BW readiness signaled to app.ts");
}

logger.LogInformation("{Ctx} Running WASM Host...", ctx);
// Console.WriteLine("Program.cs: Starting host.RunAsync()...");

await host.RunAsync();

// Console.WriteLine("Program.cs: host.RunAsync() completed (extension shutdown)");
