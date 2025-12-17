using System.Text.Json.Serialization;
using JsBind.Net;

namespace Extension.Services.JsBindings;

// TODO P3 see https://github.com/mingyaulee/Blazor.BrowserExtension/issues/55
/// <summary>
/// JsBind.Net binding for the chrome.sidePanel API.
/// See https://developer.chrome.com/docs/extensions/reference/api/sidePanel
/// </summary>
public sealed class ChromeSidePanel : ObjectBindingBase {
    public ChromeSidePanel(IJsRuntimeAdapter jsRuntime) {
        SetAccessPath("chrome.sidePanel");
        Initialize(jsRuntime);
    }

    /// <summary>
    /// Opens the side panel for the extension. This may only be called in response to a user action.
    /// </summary>
    /// <param name="options">Options specifying the context for opening the side panel</param>
    public ValueTask OpenAsync(SidePanelOpenOptions options) => InvokeVoidAsync("open", options);

    /// <summary>
    /// Opens the side panel for a specific window. This may only be called in response to a user action.
    /// </summary>
    /// <param name="windowId">The window ID to open the side panel in</param>
    public void Open(int windowId) => InvokeVoid("open", new { windowId });

    /// <summary>
    /// Retrieves the active panel configuration for a specified context.
    /// </summary>
    /// <param name="options">Options specifying which context to get options for</param>
    /// <returns>The panel options for the specified context</returns>
    public ValueTask<SidePanelOptions?> GetOptionsAsync(SidePanelGetOptions options) =>
        InvokeAsync<SidePanelOptions>("getOptions", options);

    /// <summary>
    /// Configures side panel settings with provided options.
    /// </summary>
    /// <param name="options">The panel options to set</param>
    public ValueTask SetOptionsAsync(SidePanelOptions options) =>
        InvokeVoidAsync("setOptions", options);

    /// <summary>
    /// Fetches the extension's current side panel behavior settings.
    /// </summary>
    /// <returns>The current panel behavior settings</returns>
    public ValueTask<SidePanelBehavior?> GetPanelBehaviorAsync() =>
        InvokeAsync<SidePanelBehavior>("getPanelBehavior");

    /// <summary>
    /// Configures the extension's side panel behavior. This is an upsert operation.
    /// </summary>
    /// <param name="behavior">The panel behavior settings to apply</param>
    public ValueTask SetPanelBehaviorAsync(SidePanelBehavior behavior) =>
        InvokeVoidAsync("setPanelBehavior", behavior);

    /// <summary>
    /// Returns the side panel's current layout configuration.
    /// Available in Chrome 140+.
    /// </summary>
    /// <returns>The current panel layout</returns>
    public ValueTask<SidePanelLayout?> GetLayoutAsync() =>
        InvokeAsync<SidePanelLayout>("getLayout");
}

/// <summary>
/// Options for opening the side panel.
/// </summary>
public record SidePanelOpenOptions {
    /// <summary>
    /// The window in which to open the side panel. Defaults to the current window.
    /// </summary>
    [JsonPropertyName("windowId")]
    public int? WindowId { get; init; }

    /// <summary>
    /// The tab in which to open the side panel. If specified, the side panel will only be open for this tab.
    /// </summary>
    [JsonPropertyName("tabId")]
    public int? TabId { get; init; }
}

/// <summary>
/// Options for getting panel configuration.
/// </summary>
public record SidePanelGetOptions {
    /// <summary>
    /// If specified, returns the options for the given tab. Otherwise, returns the default options.
    /// </summary>
    [JsonPropertyName("tabId")]
    public int? TabId { get; init; }
}

/// <summary>
/// Panel configuration options.
/// </summary>
public record SidePanelOptions {
    /// <summary>
    /// If specified, the options apply to the tab with this ID. Otherwise, they apply as defaults.
    /// </summary>
    [JsonPropertyName("tabId")]
    public int? TabId { get; init; }

    /// <summary>
    /// The path to the HTML file to display in the side panel.
    /// </summary>
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    /// <summary>
    /// Whether the side panel is enabled for this tab or by default.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }
}

/// <summary>
/// Panel behavior settings.
/// </summary>
public record SidePanelBehavior {
    /// <summary>
    /// Whether clicking the extension's icon will toggle showing the extension's entry in the side panel.
    /// </summary>
    [JsonPropertyName("openPanelOnActionClick")]
    public bool? OpenPanelOnActionClick { get; init; }
}

/// <summary>
/// Panel layout configuration. Available in Chrome 140+.
/// </summary>
public record SidePanelLayout {
    /// <summary>
    /// The side of the browser window where the panel is displayed.
    /// Values: "left" or "right"
    /// </summary>
    [JsonPropertyName("side")]
    public string? Side { get; init; }
}
