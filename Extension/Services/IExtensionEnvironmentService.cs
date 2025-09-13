namespace Extension.Services;

using Models;

public interface IExtensionEnvironmentService {
    /// <summary>
    /// The current environment this instance of the wallet in running under
    /// eg. extension, popup, iframe
    /// </summary>
    ExtensionEnvironment ExtensionEnvironment { get; }

    /// <summary>
    /// In case the wallet runs as an iframe, this Uri represents the parent window
    /// Needed for finding the correct window to post messages to and close that iframe again
    /// </summary>
    Uri? ExtensionIframeLocation { get; }

    /// <summary>
    /// Determines the current extension hosting environment (iframe, popup, extension, none) from the URL
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="contextType"></param>
    Task Initialize(Uri uri, string contextType);

    /// <summary>
    /// Get the initial query string from the URL that started this extension context instance
    /// </summary>
    string? InitialUriQuery { get; }
}
