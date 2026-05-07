namespace Extension.Helper;

public static class CredentialPaths
{
    /// <summary>
    /// True when <paramref name="path"/> denotes a field inside an ACDC edges section
    /// ("e.&lt;edge&gt;..."). Used by SaidDisplay to gate the "scroll to chained credential"
    /// affordance to edge references and exclude top-level identifying SAIDs (a credential's
    /// own "d", "i", "ri").
    ///
    /// Path scheme produced by the credential view renderers uses "/" to mark chain
    /// boundaries (a chained credential rendered inline under another) and "." to nest
    /// within a credential. Because PathPrefix ends in "/" and NodePath construction
    /// inserts "." between prefix and key, paths after a chain boundary look like
    /// "chains[0]/.e.auth.n" — hence the leading "." trim before the "e." check.
    /// </summary>
    public static bool IsEdgePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var lastSlash = path.LastIndexOf('/');
        var seg = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        return seg.TrimStart('.').StartsWith("e.", System.StringComparison.Ordinal);
    }
}
