namespace KeriAuth.BrowserExtension.Services;

using Microsoft.AspNetCore.WebUtilities;
using Models;

public class ExtensionEnvironmentService : IExtensionEnvironmentService
{
    /// <summary>
    /// The current environment this instance of the wallet in running under
    /// eg. extension, popup, iframe
    /// </summary>
    public ExtensionEnvironment ExtensionEnvironment { get; private set; }

    /// <summary>
    /// In case the wallet runs as an iframe, this Uri represents the parent window
    /// Needed for finding the correct window to post messages to and close that iframe again
    /// </summary>
    public Uri? ExtensionIframeLocation { get; private set; }

    /// <inheritdoc />
    public void Initialize(Uri uri)
    {
        var query = uri.Query;
        if (uri.AbsoluteUri.Contains("chrome-extension"))
        {
            ExtensionEnvironment = ExtensionEnvironment.Extension;  // may be updated below
        }
        else
        {
            ExtensionEnvironment = ExtensionEnvironment.None;
        }

        if (QueryHelpers.ParseQuery(query).TryGetValue("environment", out var environment))
        {
            // await JSRuntime.InvokeVoidAsync("alert", environment);
            if (environment.FirstOrDefault()!.Equals(ExtensionEnvironment.Iframe.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                ExtensionEnvironment = ExtensionEnvironment.Iframe;
            }
            else if (environment.FirstOrDefault()!.Equals(ExtensionEnvironment.Popup.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                ExtensionEnvironment = ExtensionEnvironment.Popup;
            }
            else if (environment.FirstOrDefault()!.Equals(ExtensionEnvironment.ActionPopup.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                ExtensionEnvironment = ExtensionEnvironment.ActionPopup;
            }
            else
            {
                // TODO P3 what about the case if BrowserExtensionMode.ContentScript ?
                throw new NotSupportedException($"The environment '{environment}' is not supported");
            }
        }

        // xxConsole.WriteLine($"ExtensionEnvironmentService: Set ExtensionEnvironment to {ExtensionEnvironment}");
        if (ExtensionEnvironment == ExtensionEnvironment.Iframe)
        {
            if (QueryHelpers.ParseQuery(query).TryGetValue("location", out var location))
            {
                ExtensionIframeLocation = new Uri(location!);
            }
        }
    }
}