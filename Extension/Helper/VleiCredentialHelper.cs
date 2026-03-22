namespace Extension.Helper;

public static class VleiCredentialHelper
{
    public const string EcrSchemaSaid = "EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw";
    public const string EcrAuthSchemaSaid = "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g";

    public const string EcrPrivacyDisclaimer =
        "It is the sole responsibility of Holders as Issuees of an ECR vLEI Credential to present that Credential in a privacy-preserving manner using the mechanisms provided in the Issuance and Presentation Exchange (IPEX) protocol specification and the Authentic Chained Data Container (ACDC) specification. https://github.com/WebOfTrust/IETF-IPEX and https://github.com/trustoverip/tswg-acdc-specification.";

    public static RecursiveDictionary BuildVleiRules(string? privacyText = null) {
        var rules = new RecursiveDictionary();
        rules["d"] = new RecursiveValue { StringValue = "" };
        var usage = new RecursiveDictionary();
        usage["l"] = new RecursiveValue { StringValue = "Usage of a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, does not assert that the Legal Entity is trustworthy, honest, reputable in its business dealings, safe to do business with, or compliant with any laws or that an implied or expressly intended purpose will be fulfilled." };
        rules["usageDisclaimer"] = new RecursiveValue { Dictionary = usage };
        var issuance = new RecursiveDictionary();
        issuance["l"] = new RecursiveValue { StringValue = "All information in a valid, unexpired, and non-revoked vLEI Credential, as defined in the associated Ecosystem Governance Framework, is accurate as of the date the validation process was complete. The vLEI Credential has been issued to the legal entity or person named in the vLEI Credential as the subject; and the qualified vLEI Issuer exercised reasonable care to perform the validation process set forth in the vLEI Ecosystem Governance Framework." };
        rules["issuanceDisclaimer"] = new RecursiveValue { Dictionary = issuance };
        if (privacyText is not null) {
            var privacy = new RecursiveDictionary();
            privacy["l"] = new RecursiveValue { StringValue = privacyText };
            rules["privacyDisclaimer"] = new RecursiveValue { Dictionary = privacy };
        }
        return rules;
    }

    public static RecursiveDictionary BuildEcrCredentialData(string role, string lei = "254900OPPU84GM83MG36", string personLegalName = "John Smith") {
        var credData = new RecursiveDictionary();
        credData["LEI"] = new RecursiveValue { StringValue = lei };
        credData["personLegalName"] = new RecursiveValue { StringValue = personLegalName };
        credData["engagementContextRole"] = new RecursiveValue { StringValue = role };
        return credData;
    }

    public static RecursiveDictionary BuildEcrAuthEdge(string ecrAuthSaid) {
        var edge = new RecursiveDictionary();
        edge["d"] = new RecursiveValue { StringValue = "" };
        var authRef = new RecursiveDictionary();
        authRef["n"] = new RecursiveValue { StringValue = ecrAuthSaid };
        authRef["s"] = new RecursiveValue { StringValue = EcrAuthSchemaSaid };
        authRef["o"] = new RecursiveValue { StringValue = "I2I" };
        edge["auth"] = new RecursiveValue { Dictionary = authRef };
        return edge;
    }

    public static RecursiveDictionary? FindEcrAuthCredential(List<RecursiveDictionary> credentials, string holderPrefix) {
        return credentials.FirstOrDefault(c =>
            c.GetValueByPath("sad.s")?.Value?.ToString() == EcrAuthSchemaSaid &&
            c.GetValueByPath("sad.a.i")?.Value?.ToString() == holderPrefix);
    }
}
