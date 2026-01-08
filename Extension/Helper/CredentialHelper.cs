namespace Extension.Helper;

/// <summary>
/// Helper methods for credential display and identification.
/// </summary>
public static class CredentialHelper
{
    /// <summary>
    /// Known vLEI credential types mapped by their schema SAID.
    /// </summary>
    public enum CredentialType
    {
        OorCredential,
        EcrAuthCredential,
        EcrCredential,
        VleiCredential,
        OorAuthCredential,
        QviCredential,
        IxbrlAttestation,
        Unknown
    }

    /// <summary>
    /// Schema SAIDs for known vLEI credential types.
    /// See Extension/Schemas/ for local copies of these schemas.
    /// </summary>
    public static class SchemaSaids
    {
        public const string Oor = "EBNaNu-M9P5cgrnfl2Fvymy4E_jvxxyjb70PRtiANlJy";
        public const string EcrAuth = "EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g";
        public const string Ecr = "EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw";
        public const string Vlei = "ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY";
        public const string OorAuth = "EKA57bKBKxr_kN7iN5i7lMUxpMG-s19dRcmov1iDxz-E";
        public const string Qvi = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao";
        public const string Ixbrl = "EMhvwOlyEJ9kN4PrwCpr9Jsv7TxPhiYveZ0oP3lJzdEi";
    }

    /// <summary>
    /// Gets the credential type from a schema SAID.
    /// </summary>
    public static CredentialType GetCredentialType(string? schemaSaid) => schemaSaid switch
    {
        SchemaSaids.Oor => CredentialType.OorCredential,
        SchemaSaids.EcrAuth => CredentialType.EcrAuthCredential,
        SchemaSaids.Ecr => CredentialType.EcrCredential,
        SchemaSaids.Vlei => CredentialType.VleiCredential,
        SchemaSaids.OorAuth => CredentialType.OorAuthCredential,
        SchemaSaids.Qvi => CredentialType.QviCredential,
        SchemaSaids.Ixbrl => CredentialType.IxbrlAttestation,
        _ => CredentialType.Unknown
    };

    /// <summary>
    /// Gets the background color for a credential based on its schema SAID.
    /// Colors have transparency to accommodate light/dark modes.
    /// </summary>
    public static string GetBackgroundColor(string? schemaSaid) => schemaSaid switch
    {
        SchemaSaids.Oor => "hsl(210 30% 82% / 0.3)",      // Soft steel blue – neutral, professional
        SchemaSaids.EcrAuth => "hsl(150 28% 80% / 0.75)", // Muted mint green – calm, positive
        SchemaSaids.Ecr => "hsl(35 35% 82% / 0.35)",      // Warm sand – friendly and readable
        SchemaSaids.Vlei => "hsl(270 25% 82% / 0.75)",    // Dusty lavender – subtle distinction
        SchemaSaids.OorAuth => "hsl(10 30% 80% / 0.75)",  // Soft clay / muted coral
        SchemaSaids.Qvi => "hsl(195 30% 80% / 0.75)",     // Desaturated teal – modern, balanced
        SchemaSaids.Ixbrl => "hsl(90 28% 82% / 0.75)",    // Pale olive – earthy, understated
        _ => "hsl(0 0% 85% / 0.75)"                        // Neutral gray – safest baseline
    };

    /// <summary>
    /// Gets the background color for a credential from a RecursiveDictionary.
    /// Extracts the schema SAID from the "sad.s" path.
    /// </summary>
    public static string GetBackgroundColor(RecursiveDictionary? credential)
    {
        var schemaSaid = credential?.GetValueByPath("sad.s")?.Value?.ToString();
        return GetBackgroundColor(schemaSaid);
    }
}
