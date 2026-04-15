namespace Extension.Models;

/// <summary>
/// Discriminates the kind of node in a credential view tree.
/// </summary>
public enum CredentialViewNodeKind {
    /// <summary>Leaf node: string, number, bool, date, etc.</summary>
    Value,
    /// <summary>Container with named children (keyed sub-object).</summary>
    Dictionary,
    /// <summary>Container with indexed children.</summary>
    Array,
    /// <summary>A SAID digest where an expanded object could be (collapsed oneOf).</summary>
    SaidReference,
    /// <summary>An entire chained credential (recursive pipeline output).</summary>
    ChainedCredential,
}

/// <summary>
/// Hints the Razor renderer to use a specific embedded component for this node's value.
/// </summary>
public static class ComponentHints {
    public const string AidPrefix = "AidPrefix";
    public const string LeiLink = "LeiLink";
}

/// <summary>
/// A single node in the credential view tree. Self-contained with all rendering metadata.
/// Built by the pipeline (MergeAcdcAndSchema + Prune); consumed by a recursive Razor component.
/// </summary>
public record CredentialViewNode {
    /// <summary>Field key from the credential (e.g., "LEI", "a", "auth").</summary>
    public required string Key { get; init; }

    /// <summary>
    /// Dot-separated path from this credential's <c>sad</c> root to this node, matching the
    /// convention used in <c>credentialViewSpecs.json</c> (e.g. <c>a.LEI</c>, <c>e.qvi.n</c>,
    /// <c>r.usageDisclaimer.l</c>). For inline-rendered chained credentials, paths are local
    /// to the chain's own sad — the host's <c>PathPrefix</c> handles cross-tree disambiguation.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>Display label (from schema description, view spec override, or key fallback).</summary>
    public required string Label { get; init; }

    /// <summary>
    /// View-spec label override, if one was applied during Prune. Preserved separately from
    /// <see cref="Label"/> so consumers (e.g., a label-provenance tooltip) can distinguish
    /// schema-sourced labels from explicit view-spec overrides. Null when no override exists.
    /// </summary>
    public string? ViewSpecLabel { get; init; }

    /// <summary>Discriminates how this node should be rendered.</summary>
    public required CredentialViewNodeKind Kind { get; init; }

    /// <summary>Nesting depth for indentation (0 = top-level).</summary>
    public int Depth { get; init; }

    /// <summary>Raw string value for Value nodes.</summary>
    public string? RawValue { get; init; }

    /// <summary>Formatted value after applying Format (e.g., date-time formatting).</summary>
    public string? FormattedValue { get; init; }

    /// <summary>Format hint from schema or view spec (e.g., "date-time", "ISO 17442").</summary>
    public string? Format { get; init; }

    /// <summary>Tooltip text, typically the schema description.</summary>
    public string? TooltipText { get; init; }

    /// <summary>Direct child nodes. For Dictionary: keyed children. For Array: indexed children.</summary>
    public List<CredentialViewNode> Children { get; init; } = [];

    /// <summary>True if the schema defines oneOf for this node's position.</summary>
    public bool IsOneOf { get; init; }

    /// <summary>The SAID digest when this node is a SaidReference (collapsed oneOf).</summary>
    public string? SaidDigest { get; init; }

    /// <summary>
    /// True if the user can toggle disclose/elide for this section in presentation mode.
    /// Applies to oneOf sections and chained credential sections.
    /// </summary>
    public bool IsElisionToggleable { get; init; }

    /// <summary>
    /// Hint for the Razor renderer to use a specific embedded component.
    /// See <see cref="ComponentHints"/> for constants.
    /// </summary>
    public string? ComponentHint { get; init; }

    /// <summary>Data for the embedded component (e.g., AID prefix value, LEI value).</summary>
    public string? ComponentData { get; init; }

    /// <summary>Minimum detail level for this node to be visible (from CredentialFieldSpec).</summary>
    public int MinDetailLevel { get; init; }

    /// <summary>For ChainedCredential kind: the full view tree of the chained credential.</summary>
    public CredentialViewTree? ChainedTree { get; init; }
}

/// <summary>
/// Top-level view model for a single credential. Contains schema metadata and the
/// tree of view nodes, plus any recursively-rendered chained credentials.
/// </summary>
public record CredentialViewTree {
    /// <summary>Schema SAID this credential conforms to.</summary>
    public required string SchemaSaid { get; init; }

    /// <summary>Schema title (e.g., "Legal Entity Engagement Context Role vLEI Credential").</summary>
    public required string SchemaTitle { get; init; }

    /// <summary>Abbreviated display name from CredentialViewSpec (e.g., "ECR vLEI").</summary>
    public string? ShortName { get; init; }

    /// <summary>Schema description text.</summary>
    public string? SchemaDescription { get; init; }

    /// <summary>
    /// The credential's own SAID (sad.d). Populated by the pipeline; used at render time
    /// to resolve edge `n` references to their target chained credential.
    /// </summary>
    public string? CredentialSaid { get; init; }

    /// <summary>Top-level child nodes of this credential's view tree.</summary>
    public required List<CredentialViewNode> Children { get; init; }

    /// <summary>View trees for chained credentials (from the chains[] array).</summary>
    public List<CredentialViewTree> ChainedCredentials { get; init; } = [];
}
