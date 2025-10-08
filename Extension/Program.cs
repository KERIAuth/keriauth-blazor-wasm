using Blazor.BrowserExtension;
using Extension;
using Extension.Services;
using Extension.Services.JsBindings;
using Extension.Services.SignifyService;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

// Program and Main are implicit and static

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddJsonFile("./appsettings.json", optional: false, reloadOnChange: true);
builder.Logging.AddConfiguration(
    builder.Configuration.GetSection("Logging")
);

builder.UseBrowserExtension(browserExtension => {
    switch (browserExtension.Mode) {
        case BrowserExtensionMode.ContentScript:
            // Note: not implemented
            // builder.RootComponents.Add<ContentScript>("#Sample_Messaging_app");
            break;
        case BrowserExtensionMode.Background:
            builder.RootComponents.AddBackgroundWorker<BackgroundWorker>();
            break;
        case BrowserExtensionMode.Standard:
        case BrowserExtensionMode.Debug:
        default:
            builder.Services.AddMudServices();
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            break;
    }
});
builder.Services.AddBrowserExtensionServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IExtensionEnvironmentService, ExtensionEnvironmentService>();
builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
builder.Services.AddSingleton<IStateService, StateService>();
builder.Services.AddSingleton<IAlarmService, AlarmService>();
builder.Services.AddSingleton<IPreferencesService, PreferencesService>();
// JavaScript module bindings
builder.Services.AddSingleton<IJsModuleLoader, JsModuleLoader>();
builder.Services.AddSingleton<ISignifyClientBinding, SignifyClientBinding>();

// Application services
builder.Services.AddSingleton<ISignifyClientService, SignifyClientService>();
builder.Services.AddSingleton<IWebsiteConfigService, WebsiteConfigService>();
builder.Services.AddSingleton<IAppBwMessagingService, AppBwMessagingService>();
builder.Services.AddSingleton<IWebauthnService, WebauthnService>();
builder.Services.AddJsBind();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("WASM host built");

// Load all JavaScript ES modules (fail-fast pattern)
logger.LogInformation("Program: Loading JavaScript ES modules via JsModuleLoader...");
var moduleLoader = host.Services.GetRequiredService<IJsModuleLoader>();
await moduleLoader.LoadAllModulesAsync();
logger.LogInformation("Program: All ES modules loaded successfully");

// Import JavaScript modules for JSImport interop
// This must happen BEFORE host.RunAsync() to ensure modules are available when C# code uses [JSImport]
// Use absolute paths from extension root to work in all contexts (BackgroundWorker, SPA, etc.)
/*
try {
    logger.LogInformation("Program: Importing JavaScript modules for JSImport interop...");

    var modules = new[] {
        ("webauthnCredentialWithPRF", "/scripts/es6/webauthnCredentialWithPRF.js"),
        ("storageHelper", "/scripts/es6/storageHelper.js"),
        ("signifyClient", "/scripts/esbuild/signifyClient.js")
    };

    foreach (var (moduleName, modulePath) in modules) {
        try {
            logger.LogInformation("Program: Importing {ModuleName} from {ModulePath}", moduleName, modulePath);
            await JSHost.ImportAsync(moduleName, modulePath);
            logger.LogInformation("Program: Successfully imported {ModuleName}", moduleName);
        }
        catch (JSException e) {
            logger.LogError(e, "Program: javascript Failed to import {ModuleName}: {Message}", moduleName, e.Message);
            throw;
        }
        catch (Exception ex) {
            logger.LogError(ex, "Program: Failed to import {ModuleName}: {Message}", moduleName, ex.Message);
            throw;
        }
    }

    logger.LogInformation("Program: All JavaScript modules imported successfully");
    
}
catch (Exception ex) {
    logger.LogError(ex, "Program: Critical error importing JavaScript modules");
    throw;
}
*/

logger.LogInformation("Running WASM Host...");

await host.RunAsync();
