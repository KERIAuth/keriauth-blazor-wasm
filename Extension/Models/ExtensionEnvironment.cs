namespace Extension.Models;

public enum ExtensionEnvironment {
    None,           // e.g. running in ASPNetCore for development
    ActionPopup,    // Via clicking on the browser action icon
    OptionPopup,
    Extension,      // Normal Tab
    BrowserPopup,   // Floating popup on top of a web page
    Iframe,         // Iframe inside a website
    Tab,
    Unknown
}
