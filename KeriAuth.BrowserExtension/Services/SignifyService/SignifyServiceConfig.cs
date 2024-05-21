﻿namespace KeriAuth.BrowserExtension.Services.SignifyService
{
    public class SignifyServiceConfig
    {
        // See Signify/KERIA Request Authentication Protocol (SKRAP), https://github.com/WebOfTrust/keria/blob/development/docs/protocol.md
        public const string MessageRouterPort = "3902"; // aka KERI Protocol Interface, including oobi, and spec.yaml
        public const string BootPort = "3903";   // for boot and health
        public const string AdminPort = "3901";  // for API Handler, Agent API Requests, and spec.yaml
    }
}
