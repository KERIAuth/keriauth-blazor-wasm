namespace Extension.Helper;

public static class TvaCredentialHelper
{
    public const string TvaSchemaSaid = CredentialHelper.SchemaSaids.Tva;
    public const string TvaRegistryName = "tva";

    public const string DefaultEmail = "alice@example.com";

    public static RecursiveDictionary BuildTvaCredentialData(string email, string? name = null, string? role = null) {
        var credData = new RecursiveDictionary();
        credData["email"] = new RecursiveValue { StringValue = email };
        if (!string.IsNullOrEmpty(name)) {
            credData["name"] = new RecursiveValue { StringValue = name };
        }
        if (!string.IsNullOrEmpty(role)) {
            credData["role"] = new RecursiveValue { StringValue = role };
        }
        return credData;
    }
}
