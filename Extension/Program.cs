using Blazor.BrowserExtension;
using Extension;
using JsBind.Net;
using Extension.Services;
using Extension.Services.Crypto;
using Extension.Services.JsBindings;
using Extension.Services.NotificationPollingService;
using Extension.Services.Port;
using Extension.Services.ConfigureService;
using Extension.Services.PrimeDataService;
using Extension.Services.SignifyService;
using Extension.Services.Storage;
using Extension.Utilities;
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

// ==================================================================================
// KNOWN ISSUE: Error when deserializing JSON for chrome_update
// ==================================================================================
// SYMPTOM: When the extension receives the chrome_update message, it attempts to deserialize the JSON payload into a ChromeUpdateInfo object. If the JSON structure has changed
// (e.g., new fields added) and the deserialization code is not updated accordingly, it can throw a JsonException. This error would occur in the BackgroundWorker when processing the message.
// Uncaught (in promise) Error: Error when deserializing JSON: {"__jsBindAccessPath":"#5eb4e011-80c5-4c1f-9533-6d82b11ceac8","__jsBindJsRuntime":0,"reason":"chrome_update"}

// STARTUP DIAGNOSTIC: Log to console before any builder initialization
// This helps identify if SRI integrity errors prevent WASM from loading
// If you DON'T see this log, the SRI check failed and WASM never initialized
Console.WriteLine("Program.cs: Entry point reached - WASM runtime loaded successfully");

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddJsonFile("./appsettings.json", optional: false, reloadOnChange: true);
builder.Logging.AddConfiguration(
    builder.Configuration.GetSection("Logging")
);

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
            builder.Services.AddSingleton<IPrimeDataService, Extension.Services.PrimeDataService.PrimeDataService>();
            builder.Services.AddSingleton<IConfigureService, Extension.Services.ConfigureService.ConfigureService>();
            builder.Services.AddSingleton<ISchemaService, SchemaService>();
            builder.Services.AddSingleton<INotificationPollingService, NotificationPollingService>();
            builder.Services.AddSingleton<INetworkConnectivityService, NetworkConnectivityService>();
            break;
        case BrowserExtensionMode.Standard:
        case BrowserExtensionMode.Debug:
        default:
            builder.Services.AddMudServices();
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            builder.Services.AddSingleton<IUserActivityService, UserActivityService>();
            builder.Services.AddSingleton<SessionManager>(sp => new(
                sp.GetRequiredService<ILogger<SessionManager>>(),
                sp.GetRequiredService<IStorageGateway>(),
                sp.GetRequiredService<IJsRuntimeAdapter>(),
                isSessionOwner: false));
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
// StorageService removed — all consumers now use IStorageGateway directly
builder.Services.AddSingleton<IStorageGateway, StorageGateway>();
builder.Services.AddSingleton<IJsModuleLoader, JsModuleLoader>();
builder.Services.AddSingleton<IWebsiteConfigService, WebsiteConfigService>();
builder.Services.AddSingleton<ICredentialViewSpecService, CredentialViewSpecService>();
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

// Verify terms and privacy digests at startup (BackgroundWorker only, runs once).
// Moved here from App.razor to avoid blocking every App instance startup.
if (extensionMode == BrowserExtensionMode.Background) {
    try {
        using var http = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
        var termsTask = http.GetStringAsync("content/terms.html");
        var privacyTask = http.GetStringAsync("content/privacy.html");
        await Task.WhenAll(termsTask, privacyTask);

        static string Normalize(string s) => string.IsNullOrEmpty(s) ? s : s.TrimStart('\uFEFF').Replace("\r\n", "\n");
        var termsDigest = DeterministicHash.ComputeHash(Normalize(termsTask.Result));
        var privacyDigest = DeterministicHash.ComputeHash(Normalize(privacyTask.Result));

        if (termsDigest != AppConfig.ExpectedTermsDigest) {
            logger.LogError("CurrentTermsDigest {Current} does not match expected {Expected}. Needs updating!",
                termsDigest, AppConfig.ExpectedTermsDigest);
        }

        if (privacyDigest != AppConfig.ExpectedPrivacyDigest) {
            logger.LogError("CurrentPrivacyDigest {Current} does not match expected {Expected}. Needs updating!",
                privacyDigest, AppConfig.ExpectedPrivacyDigest);
        }
    }
    catch (Exception ex) {
        logger.LogError(ex, "Failed to verify terms/privacy digests");
    }
}

logger.LogInformation("{Ctx} Running WASM Host...", ctx);
// Console.WriteLine("Program.cs: Starting host.RunAsync()...");

await host.RunAsync();

// Console.WriteLine("Program.cs: host.RunAsync() completed (extension shutdown)");
