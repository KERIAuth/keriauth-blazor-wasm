namespace Extension.Models;

/// <summary>
/// Specifies display properties for one field within a credential view.
/// </summary>
public record CredentialFieldSpec(
    string Path,             // Dot-separated path in ACDC (e.g., "a.LEI", "e.qvi.n")
    int MinDetailLevel,      // Field shown when user's detail level >= this value (0 = always shown)
    string? Label = null,    // Override label (null = use schema description or field key)
    string? Format = null    // Override format (null = use schema format or raw string)
);

/// <summary>
/// View specification for a credential schema. Loaded from credentialViewSpecs.json.
/// </summary>
public record CredentialViewSpec(
    string SchemaSaid,                  // Schema SAID this spec applies to
    string ShortName,                   // Abbreviated display name (e.g., "LE vLEI")
    List<CredentialFieldSpec> Fields    // Field display specs, ordered by display position.
                                        // To hide framing keys (v, d, ri, s) at low detail, give them
                                        // entries here with MinDetailLevel: 9 — they'll only render at
                                        // the most-detailed setting.
);

public enum CredentialDisplayType { Card, Tree }

/// <summary>
/// Runtime display options for a CredentialComponent.
/// </summary>
public record CredentialViewOptions(
    CredentialDisplayType DisplayType = CredentialDisplayType.Card,
    int DetailLevel = 5,                          // 0 (most summary) to 9 (most detailed)
    bool IsPresentation = false,                  // When true: enables elision controls and disclosure presets
    bool IsAidPrefixDisplay = true,               // When true: AIDs shown via display component; false: raw string
    bool IsJsonShown = false,                     // When true: show raw JSON expansion panel
    List<string>? PreselectedPresentationPaths = null  // Initial oneOf paths pre-selected for disclosure
);
