﻿namespace KeriAuth.BrowserExtension.Services;

using Microsoft.AspNetCore.WebUtilities;
using Models;

public class ExtensionEnvironmentService(ILogger<ExtensionEnvironmentService> logger) : IExtensionEnvironmentService
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

    // TODO P2 refactor this entire service and instead user webExtensionsApi.Runtime.GetContexts() helpers
    public string? InitialUriQuery { get; private set; }

    /// <inheritdoc />
    public async Task Initialize(Uri uri, string contextType)
    {
        logger.LogInformation("Initialize with uri {uri}", uri);
        var query = uri.Query;
        InitialUriQuery = query;
        if (uri.AbsoluteUri.Contains("chrome-extension"))
        {
            // TODO P2 better to get this environment value from the chrome.runtime.getContexts() API, filtered by the current context.  See UIHelper.GetChromeContexts()
            if (QueryHelpers.ParseQuery(query).TryGetValue("environment", out var environment))
            {
                if (Enum.TryParse(environment.FirstOrDefault(), true, out ExtensionEnvironment extensionEnvironment))
                {
                    ExtensionEnvironment = extensionEnvironment;
                    // used?
                    if (ExtensionEnvironment == ExtensionEnvironment.Iframe)
                    {
                        if (QueryHelpers.ParseQuery(query).TryGetValue("location", out var location))
                        {
                            ExtensionIframeLocation = new Uri(location!);
                        }
                    }
                }
                else
                {
                    logger.LogWarning("Environment is not a valid ExtensionEnvironment");
                    ExtensionEnvironment = ExtensionEnvironment.Unknown;
                }
            }
            else
            {
                logger.LogInformation("No environment query parameter found");
                ExtensionEnvironment = ExtensionEnvironment.Unknown;
            }
            logger.LogInformation("ExtensionEnvironment: {ExtensionEnvironment}", ExtensionEnvironment);
        }
        else
        {
            logger.LogError("Not running in a browser extension");
        }
        await Task.Delay(0);
    }
}