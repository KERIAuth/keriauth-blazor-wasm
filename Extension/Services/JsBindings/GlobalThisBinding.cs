using System.Text.Json.Serialization;
using JsBind.Net;

namespace Extension.Services.JsBindings;

/// <summary>
/// JsBind.Net binding for globalThis object.
/// Provides access to global properties set in HTML pages.
/// </summary>
public sealed class GlobalThisBinding : ObjectBindingBase {
    public GlobalThisBinding(IJsRuntimeAdapter jsRuntime) {
        SetAccessPath("globalThis");
        Initialize(jsRuntime);
    }

    /// <summary>
    /// Checks if a property exists on globalThis.
    /// </summary>
    /// <param name="propertyName">The name of the property to check</param>
    /// <returns>True if the property exists, false otherwise</returns>
    public ValueTask<bool> HasAsync(string propertyName) =>
        InvokeAsync<bool>("hasOwnProperty", propertyName);

    /// <summary>
    /// Gets a property value from globalThis.
    /// </summary>
    /// <typeparam name="T">The expected type of the property</typeparam>
    /// <param name="propertyName">The name of the property to get</param>
    /// <returns>The property value or default if not found</returns>
    public ValueTask<T?> GetAsync<T>(string propertyName) =>
        GetPropertyAsync<T>(propertyName);
}

/// <summary>
/// Represents the extension context object set in HTML pages via globalThis.__EXT_CONTEXT__.
/// </summary>
public record ExtContext {
    /// <summary>
    /// The type of extension context (e.g., "TAB", "POPUP", "SIDEPANEL", "OPTIONS").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
