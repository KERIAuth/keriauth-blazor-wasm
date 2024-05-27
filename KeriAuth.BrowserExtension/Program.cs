using BlazorDB;
using KeriAuth.BrowserExtension;
using KeriAuth.BrowserExtension.Services;
using KeriAuth.BrowserExtension.Services.SignifyService;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;

// note Program and Main are implicit and static

// Intentionally using Console.WriteLine herein since ILogger isn't yet easy to inject
Console.WriteLine("Program: started");
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddJsonFile("./appsettings.json", optional: false, reloadOnChange: true);
builder.Logging.AddConfiguration(
    builder.Configuration.GetSection("Logging")
);
builder.UseBrowserExtension(browserExtension =>
{
    builder.RootComponents.Add<App>("#app");
    builder.RootComponents.Add<HeadOutlet>("head::after");
});
builder.Services.AddBrowserExtensionServices();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IExtensionEnvironmentService, ExtensionEnvironmentService>();
builder.Services.AddSingleton<IWalletService, WalletService>();
builder.Services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
builder.Services.AddSingleton<IStateService, StateService>();
builder.Services.AddSingleton<IAlarmService, AlarmService>();
builder.Services.AddSingleton<IPreferencesService, PreferencesService>();
builder.Services.AddSingleton<ISignifyClientService, SignifyClientService>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("WASM host built");

// Import JS modules for use in C# classes
Debug.Assert(OperatingSystem.IsBrowser());
try
{
    // Adding imports of modules here for use via [JSImport] attributes in C# classes
    List<(string, string)> imports = [
        // ("signify-ts", "/node_modules/signify-ts"),
        ("signify_ts_shim", "/scripts/signify_ts_shim.js"),
        ("registerInactivityEvents", "/scripts/registerInactivityEvents.js"),
        ("uiHelper", "/scripts/uiHelper.js"),
        ("storageHelper", "/scripts/storageHelper.js")
    ];
    foreach (var (moduleName, modulePath) in imports)
    {
        logger.LogInformation("Importing {moduleName}", moduleName);
        await JSHost.ImportAsync(moduleName, modulePath);
    }
}
catch (Microsoft.JSInterop.JSException e)
{
    logger.LogError("Program: Initialize: JSInterop.JSException: {e}", e.StackTrace);
    return;
}
catch (System.Runtime.InteropServices.JavaScript.JSException e)
{
    logger.LogError("Program: Initialize: JSException: {e}", e.StackTrace);
    return;
}
catch (Exception e)
{
    logger.LogError("Program: Initialize: Exception: {e}", e);
    return;
}

logger.LogInformation("Running WASM Host...");

await host.RunAsync();
