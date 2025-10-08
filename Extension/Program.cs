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

// JavaScript ES modules are loaded by app.ts beforeStart() hook before Blazor starts
// The beforeStart hook runs separately in each runtime context (BackgroundWorker and App)
// and loads the appropriate modules into the browser's module cache
// Now we just need to "register" them with JsModuleLoader (instant from cache)
logger.LogInformation("Registering JavaScript modules (pre-loaded by app.ts beforeStart)...");
var moduleLoader = host.Services.GetRequiredService<IJsModuleLoader>();
await moduleLoader.LoadAllModulesAsync();
logger.LogInformation("All modules registered successfully");

logger.LogInformation("Running WASM Host...");

await host.RunAsync();
