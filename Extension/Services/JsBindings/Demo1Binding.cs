using Microsoft.JSInterop;
using System.Runtime.Versioning;

namespace Extension.Services.JsBindings;

/// <summary>
/// Binding for demo1 module
/// Provides strongly-typed C# API for the demo1 JavaScript module
/// </summary>
public interface IDemo1Binding {
    /// <summary>
    /// Runs the demo1 KERI vLEI workflow demonstration
    /// </summary>
    ValueTask RunDemo1Async(CancellationToken cancellationToken = default);
    ValueTask RunDemo2Async(CancellationToken cancellationToken = default);
}

[SupportedOSPlatform("browser")]
public class Demo1Binding : IDemo1Binding {

    private readonly IJsModuleLoader _moduleLoader;

    public Demo1Binding(IJsModuleLoader moduleLoader) {
        _moduleLoader = moduleLoader;
    }

    private IJSObjectReference Module => _moduleLoader.GetModule("demo1");

    public async ValueTask RunDemo1Async(CancellationToken cancellationToken = default) {
        await Module.InvokeVoidAsync("ready", cancellationToken);
        await Module.InvokeVoidAsync("runDemo1", cancellationToken);
    }

    public async ValueTask RunDemo2Async(CancellationToken cancellationToken = default) =>
        await Module.InvokeVoidAsync("runDemo2", cancellationToken);
}
