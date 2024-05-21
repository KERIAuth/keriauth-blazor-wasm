namespace KeriAuth.BrowserExtension.Models;

public enum ExtensionEnvironment
{
    None,           // e.g. running in ASPNetCore for development
    ActionPopup,    // Via clicking on the browser action icon
    Extension,      // Normal Tab
    Popup,          // Floating popup on top of a web page
    Iframe          // Iframe inside a website
}