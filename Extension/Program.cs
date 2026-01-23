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

builder.UseBrowserExtension(browserExtension => {
    var mode = browserExtension.Mode.ToString();
    Console.WriteLine($"Program.cs [{mode}]");

    switch (browserExtension.Mode) {
        case BrowserExtensionMode.ContentScript:
            throw new NotImplementedException("ContentScript mode is not implemented in Program.cs");
        // builder.RootComponents.Add<ContentScript>("#Sample_Messaging_app");
        // break;
        case BrowserExtensionMode.Background:
            // Console.WriteLine($"Program.cs [{mode}]: Configuring Background mode - adding BackgroundWorker root component");
            builder.RootComponents.AddBackgroundWorker<BackgroundWorker>();
            builder.Services.AddSingleton<SessionManager>();
            builder.Services.AddSingleton<IBwAppMessagingService, BwAppMessagingService>();
            builder.Services.AddSingleton<IPendingBwAppRequestService, PendingBwAppRequestService>();
            builder.Services.AddSingleton<IBwPortService, BwPortService>();
            break;
        case BrowserExtensionMode.Standard:
        case BrowserExtensionMode.Debug:
        default:
            // Console.WriteLine($"Program.cs [{mode}]: Configuring {browserExtension.Mode} mode - adding App and MudBlazor");
            builder.Services.AddMudServices();
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            builder.Services.AddSingleton<IUserActivityService, UserActivityService>();
            builder.Services.AddSingleton<SessionManager>();
            builder.Services.AddSingleton<IPendingBwAppRequestService, PendingBwAppRequestService>();
            builder.Services.AddSingleton<IAppPortService, AppPortService>();
            break;
    }
});

// Console.WriteLine("Program.cs: Configuring services...");
builder.Services.AddBrowserExtensionServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
builder.Services.AddSingleton<AppCache>();
// JavaScript module bindings
builder.Services.AddSingleton<IJsModuleLoader, JsModuleLoader>();
builder.Services.AddSingleton<ISignifyClientBinding, SignifyClientBinding>();
builder.Services.AddSingleton<INavigatorCredentialsBinding, NavigatorCredentialsBinding>();
builder.Services.AddSingleton<IDemo1Binding, Demo1Binding>();

// Cryptography services
builder.Services.AddSingleton<ICryptoService, SubtleCryptoService>();

// Application services
builder.Services.AddSingleton<ISignifyClientService, SignifyClientService>();
builder.Services.AddSingleton<IWebsiteConfigService, WebsiteConfigService>();
builder.Services.AddSingleton<IAppBwMessagingService, AppBwMessagingService>();
builder.Services.AddSingleton<IFidoMetadataService, FidoMetadataService>();
builder.Services.AddSingleton<ISchemaService, SchemaService>();
builder.Services.AddSingleton<IWebauthnService, WebauthnService>();
builder.Services.AddJsBind();

// Console.WriteLine("Program.cs: Building host...");

var host = builder.Build();

// Console.WriteLine("Program.cs: Host built successfully, initializing logger...");

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("WASM host built");

// Load JavaScript ES modules via JsModuleLoader
// libsodium-polyfill is statically imported in app.ts before Blazor starts
// Other modules (signifyClient, navigatorCredentialsShim) are lazy-loaded to avoid initialization issues
logger.LogInformation("Loading JavaScript modules via JsModuleLoader...");
// Console.WriteLine("Program.cs: Loading JavaScript modules via JsModuleLoader...");

try {
    var moduleLoader = host.Services.GetRequiredService<IJsModuleLoader>();
    await moduleLoader.LoadAllModulesAsync();
    logger.LogInformation("Program.cs: JavaScript modules loaded successfully");
}
catch (Exception ex) {
    logger.LogError(ex, "Failed to load JavaScript modules via JsModuleLoader");
    throw;
}

// logger.LogInformation("All modules loaded successfully");

logger.LogInformation("Running WASM Host...");
// Console.WriteLine("Program.cs: Starting host.RunAsync()...");

await host.RunAsync();

// Console.WriteLine("Program.cs: host.RunAsync() completed (extension shutdown)");
