using Extension.Helper;
using Extension.Models;
using Extension.Services.SignifyService.Models;

namespace Extension.Services;

/// <summary>
/// Pure-function pipeline that transforms a credential (RecursiveDictionary) + its embedded schema
/// into a <see cref="CredentialViewTree"/> suitable for rendering by a recursive Razor component.
/// </summary>
public static class CredentialViewPipeline {

    /// <summary>
    /// Builds a complete view tree for a credential and all its chained credentials.
    /// Recursively processes the chains[] array, looking up view specs for each level.
    /// </summary>
    public static CredentialViewTree BuildFullTree(
        ClonedCredential credential,
        ICredentialViewSpecService viewSpecService,
        CredentialViewOptions options) {

        var merged = MergeAcdcAndSchema(credential);
        var spec = viewSpecService.GetViewSpec(merged.SchemaSaid);
        var pruned = Prune(merged, spec, options);

        // Recursively process chained credentials
        var chainedTrees = credential.Chains
            .Select(chain => BuildFullTree(chain, viewSpecService, options))
            .ToList();

        return pruned with { ChainedCredentials = chainedTrees };
    }

    /// <summary>
    /// Merges a credential's SAD data with its embedded schema, producing a view tree
    /// where each node carries rendering metadata (label, format, tooltip, oneOf state).
    /// </summary>
    public static CredentialViewTree MergeAcdcAndSchema(ClonedCredential credential) {
        var sad = credential.Sad;
        var schema = credential.Schema;

        var schemaSaid = credential.SchemaSaid ?? "";
        var schemaTitle = schema?.QueryPath("title")?.StringValue ?? "Credential";
        var schemaDescription = schema?.QueryPath("description")?.StringValue;
        var credentialSaid = sad.QueryPath("d")?.StringValue;

        var schemaProperties = schema?.QueryPath("properties")?.Dictionary;

        var children = MergeLevel(sad, schemaProperties, depth: 0, parentSection: null);

        return new CredentialViewTree {
            SchemaSaid = schemaSaid,
            SchemaTitle = schemaTitle,
            SchemaDescription = schemaDescription,
            CredentialSaid = credentialSaid,
            Children = children,
        };
    }

    private static List<CredentialViewNode> MergeLevel(
        RecursiveDictionary data,
        RecursiveDictionary? schemaProperties,
        int depth,
        string? parentSection) {

        var nodes = new List<CredentialViewNode>();

        foreach (var kvp in data) {
            var key = kvp.Key;
            var value = kvp.Value;

            var schemaProp = schemaProperties?.QueryPath(key)?.Dictionary;
            var node = MergeNode(key, value, schemaProp, depth, parentSection);
            nodes.Add(node);
        }

        return nodes;
    }

    private static CredentialViewNode MergeNode(
        string key,
        RecursiveValue value,
        RecursiveDictionary? schemaProp,
        int depth,
        string? parentSection) {

        var isOneOf = HasOneOf(schemaProp);
        var resolvedSchema = schemaProp;

        if (isOneOf) {
            resolvedSchema = ResolveOneOfSchema(schemaProp!, value);
        }

        var description = resolvedSchema?.QueryPath("description")?.StringValue;
        var format = resolvedSchema?.QueryPath("format")?.StringValue;
        var label = description ?? key;

        var componentHint = DetectComponentHint(key, format, parentSection);
        var rawStringValue = value.StringValue;

        if (value.Dictionary is { } dictValue) {
            if (isOneOf && rawStringValue == null) {
                // Expanded object where schema has oneOf — it's a disclosed oneOf section
                var subProperties = resolvedSchema?.QueryPath("properties")?.Dictionary;
                var children = MergeLevel(dictValue, subProperties, depth + 1, key);

                return new CredentialViewNode {
                    Key = key,
                    Label = label,
                    Kind = CredentialViewNodeKind.Dictionary,
                    Depth = depth,
                    TooltipText = description,
                    Children = children,
                    IsOneOf = true,
                    SaidDigest = ExtractSaidFromSection(dictValue),
                };
            }

            // Regular dictionary (no oneOf, or non-oneOf sub-object)
            var subProps = resolvedSchema?.QueryPath("properties")?.Dictionary;
            var dictChildren = MergeLevel(dictValue, subProps, depth + 1, parentSection ?? key);

            return new CredentialViewNode {
                Key = key,
                Label = label,
                Kind = CredentialViewNodeKind.Dictionary,
                Depth = depth,
                TooltipText = description,
                Children = dictChildren,
            };
        }

        if (value.List is { } listValue) {
            var itemSchema = resolvedSchema?.QueryPath("items")?.Dictionary;
            var arrayChildren = new List<CredentialViewNode>();
            for (var i = 0; i < listValue.Count; i++) {
                var item = listValue[i];
                var child = MergeNode(i.ToString(System.Globalization.CultureInfo.InvariantCulture), item, itemSchema, depth + 1, parentSection);
                arrayChildren.Add(child);
            }

            return new CredentialViewNode {
                Key = key,
                Label = label,
                Kind = CredentialViewNodeKind.Array,
                Depth = depth,
                TooltipText = description,
                Children = arrayChildren,
            };
        }

        // SAID reference: string value where oneOf expected
        if (isOneOf && rawStringValue != null) {
            return new CredentialViewNode {
                Key = key,
                Label = label,
                Kind = CredentialViewNodeKind.SaidReference,
                Depth = depth,
                RawValue = rawStringValue,
                SaidDigest = rawStringValue,
                IsOneOf = true,
                TooltipText = description,
            };
        }

        // Leaf value node
        var formattedValue = FormatValue(value, format);

        return new CredentialViewNode {
            Key = key,
            Label = label,
            Kind = CredentialViewNodeKind.Value,
            Depth = depth,
            RawValue = rawStringValue ?? value.Value?.ToString(),
            FormattedValue = formattedValue,
            Format = format,
            TooltipText = description,
            ComponentHint = componentHint,
            ComponentData = componentHint != null ? (rawStringValue ?? value.Value?.ToString()) : null,
        };
    }

    /// <summary>
    /// Checks if a schema property has a "oneOf" array.
    /// </summary>
    private static bool HasOneOf(RecursiveDictionary? schemaProp) {
        if (schemaProp == null) return false;
        return schemaProp.QueryPath("oneOf")?.List != null;
    }

    /// <summary>
    /// Resolves which oneOf variant matches the actual data value.
    /// If value is a string, returns the string variant (oneOf[0]).
    /// If value is a dict, finds the object variant whose properties best match the data keys.
    /// </summary>
    private static RecursiveDictionary? ResolveOneOfSchema(
        RecursiveDictionary schemaProp,
        RecursiveValue value) {

        var oneOfList = schemaProp.QueryPath("oneOf")?.List;
        if (oneOfList == null || oneOfList.Count == 0) return schemaProp;

        if (value.StringValue != null) {
            // SAID reference — return the string variant
            foreach (var variant in oneOfList) {
                if (variant.Dictionary?.QueryPath("type")?.StringValue == "string") {
                    return variant.Dictionary;
                }
            }
            return oneOfList[0].Dictionary;
        }

        if (value.Dictionary is { } dictValue) {
            // Expanded object — find the object variant whose properties best match
            RecursiveDictionary? bestMatch = null;
            var bestScore = -1;

            foreach (var variant in oneOfList) {
                var variantDict = variant.Dictionary;
                if (variantDict?.QueryPath("type")?.StringValue != "object") continue;

                var variantProps = variantDict.QueryPath("properties")?.Dictionary;
                if (variantProps == null) continue;

                // Score by how many of the data keys match the variant's properties
                var score = 0;
                foreach (var dataKey in dictValue.Keys) {
                    if (variantProps.ContainsKey(dataKey)) score++;
                }

                if (score > bestScore) {
                    bestScore = score;
                    bestMatch = variantDict;
                }
            }

            return bestMatch ?? oneOfList[0].Dictionary;
        }

        return oneOfList[0].Dictionary;
    }

    /// <summary>
    /// Detects if a field should use an embedded component (AidPrefixDisplay, LEI link).
    /// </summary>
    private static string? DetectComponentHint(string key, string? format, string? parentSection) {
        // "i" in the attributes section is an AID (issuee)
        // "i" at top level is the issuer AID
        if (key == "i") {
            return ComponentHints.AidPrefix;
        }

        if (key == "LEI" || format == "ISO 17442") {
            return ComponentHints.LeiLink;
        }

        return null;
    }

    private static string? ExtractSaidFromSection(RecursiveDictionary section) {
        return section.QueryPath("d")?.StringValue;
    }

    /// <summary>
    /// Prunes a merged view tree by applying a CredentialViewSpec and CredentialViewOptions.
    /// Removes hidden details, filters by detail level, applies label/format overrides,
    /// and marks elision-toggleable sections in presentation mode.
    /// </summary>
    public static CredentialViewTree Prune(
        CredentialViewTree tree,
        CredentialViewSpec? spec,
        CredentialViewOptions options) {

        var hiddenKeys = BuildHiddenKeySet(spec);
        var fieldOverrides = BuildFieldOverrideMap(spec);

        var prunedChildren = PruneNodes(tree.Children, hiddenKeys, fieldOverrides, options, prefix: "");

        return tree with {
            ShortName = spec?.ShortName ?? tree.ShortName,
            Children = prunedChildren,
        };
    }

    private static HashSet<string> BuildHiddenKeySet(CredentialViewSpec? spec) {
        if (spec?.HiddenDetails == null) return [];
        return spec.HiddenDetails
            .Select(d => d.ToString().TrimStart('_'))
            .ToHashSet();
    }

    private static Dictionary<string, CredentialFieldSpec> BuildFieldOverrideMap(CredentialViewSpec? spec) {
        if (spec?.Fields == null) return [];
        return spec.Fields.ToDictionary(f => f.Path, f => f);
    }

    private static List<CredentialViewNode> PruneNodes(
        List<CredentialViewNode> nodes,
        HashSet<string> hiddenKeys,
        Dictionary<string, CredentialFieldSpec> fieldOverrides,
        CredentialViewOptions options,
        string prefix) {

        var result = new List<CredentialViewNode>();

        foreach (var node in nodes) {
            var path = string.IsNullOrEmpty(prefix) ? node.Key : $"{prefix}.{node.Key}";

            // Remove hidden keys (top-level only, matching SchemaIndependentDetail pattern)
            if (hiddenKeys.Contains(node.Key) && node.Depth == 0) {
                continue;
            }

            var current = node;

            // Apply field spec overrides and detail level filtering
            if (fieldOverrides.TryGetValue(path, out var fieldSpec)) {
                if (fieldSpec.MinDetailLevel > options.DetailLevel) {
                    continue;
                }

                current = current with { MinDetailLevel = fieldSpec.MinDetailLevel };

                if (fieldSpec.Label != null) {
                    current = current with { Label = fieldSpec.Label };
                }
                if (fieldSpec.Format != null) {
                    var formattedValue = current.FormattedValue;
                    if (current.RawValue != null && fieldSpec.Format == "date-time" &&
                        DateTimeOffset.TryParse(current.RawValue, out var dto)) {
                        formattedValue = dto.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
                    }
                    current = current with { Format = fieldSpec.Format, FormattedValue = formattedValue };
                }
            }

            // Mark elision-toggleable sections in presentation mode
            if (options.IsPresentation && current.IsOneOf) {
                current = current with { IsElisionToggleable = true };
            }

            // Recurse into children
            if (current.Children.Count > 0) {
                var prunedChildren = PruneNodes(current.Children, hiddenKeys, fieldOverrides, options, path);
                current = current with { Children = prunedChildren };
            }

            result.Add(current);
        }

        // TODO P2 Reordering fields may break SAID integrity for presentations.
        // Clarify whether display order can differ from serialization order,
        // or whether reordering must be view-only.

        return result;
    }

    private static string? FormatValue(RecursiveValue value, string? format) {
        var raw = value.StringValue ?? value.Value?.ToString();
        if (raw == null) return null;

        if (format == "date-time" && DateTimeOffset.TryParse(raw, out var dto)) {
            return dto.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        }

        return raw;
    }
}
