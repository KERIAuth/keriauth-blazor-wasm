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

        var children = MergeLevel(sad, schemaProperties, depth: 0, parentSection: null, parentPath: "");

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
        string? parentSection,
        string parentPath) {

        var nodes = new List<CredentialViewNode>();

        foreach (var kvp in data) {
            var key = kvp.Key;
            var value = kvp.Value;

            var schemaProp = schemaProperties?.QueryPath(key)?.Dictionary;
            var node = MergeNode(key, value, schemaProp, depth, parentSection, parentPath);
            nodes.Add(node);
        }

        return nodes;
    }

    private static CredentialViewNode MergeNode(
        string key,
        RecursiveValue value,
        RecursiveDictionary? schemaProp,
        int depth,
        string? parentSection,
        string parentPath) {

        var isOneOf = HasOneOf(schemaProp);
        var resolvedSchema = schemaProp;

        if (isOneOf) {
            resolvedSchema = ResolveOneOfSchema(schemaProp!, value);
        }

        var description = resolvedSchema?.QueryPath("description")?.StringValue;
        var format = resolvedSchema?.QueryPath("format")?.StringValue;
        var label = description ?? key;
        var path = string.IsNullOrEmpty(parentPath) ? key : $"{parentPath}.{key}";

        var rawStringValue = value.StringValue;

        if (value.Dictionary is { } dictValue) {
            if (isOneOf && rawStringValue == null) {
                // Expanded object where schema has oneOf — it's a disclosed oneOf section
                var subProperties = resolvedSchema?.QueryPath("properties")?.Dictionary;
                var children = MergeLevel(dictValue, subProperties, depth + 1, key, path);

                return new CredentialViewNode {
                    Key = key,
                    Path = path,
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
            var dictChildren = MergeLevel(dictValue, subProps, depth + 1, parentSection ?? key, path);

            return new CredentialViewNode {
                Key = key,
                Path = path,
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
                var child = MergeNode(i.ToString(System.Globalization.CultureInfo.InvariantCulture), item, itemSchema, depth + 1, parentSection, path);
                arrayChildren.Add(child);
            }

            return new CredentialViewNode {
                Key = key,
                Path = path,
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
                Path = path,
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
        return new CredentialViewNode {
            Key = key,
            Path = path,
            Label = label,
            Kind = CredentialViewNodeKind.Value,
            Depth = depth,
            RawValue = rawStringValue ?? value.Value?.ToString(),
            Format = CredentialFieldFormatNames.ParseSchemaFormat(format),
            TooltipText = description,
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

        var fieldOverrides = BuildFieldOverrideMap(spec);

        var prunedChildren = PruneNodes(tree.Children, fieldOverrides, options, prefix: "");

        return tree with {
            ShortName = spec?.ShortName ?? tree.ShortName,
            Children = prunedChildren,
        };
    }

    private static Dictionary<string, CredentialFieldSpec> BuildFieldOverrideMap(CredentialViewSpec? spec) {
        if (spec?.Fields == null) return [];
        return spec.Fields.ToDictionary(f => f.Path, f => f);
    }

    private static List<CredentialViewNode> PruneNodes(
        List<CredentialViewNode> nodes,
        Dictionary<string, CredentialFieldSpec> fieldOverrides,
        CredentialViewOptions options,
        string prefix) {

        var result = new List<CredentialViewNode>();

        foreach (var node in nodes) {
            var path = string.IsNullOrEmpty(prefix) ? node.Key : $"{prefix}.{node.Key}";
            var current = node;
            var hasOverride = fieldOverrides.TryGetValue(path, out var fieldSpec);

            // Explicit hide via spec entry
            if (hasOverride && fieldSpec!.MinDetailLevel > (int)options.DetailLevel) {
                continue;
            }

            // Apply field spec overrides
            if (hasOverride) {
                current = current with { MinDetailLevel = fieldSpec!.MinDetailLevel };
                if (fieldSpec.Label != null) {
                    current = current with { Label = fieldSpec.Label, ViewSpecLabel = fieldSpec.Label };
                }
                if (fieldSpec.Format != null) {
                    current = current with { Format = fieldSpec.Format };
                }
            }

            // Mark elision-toggleable sections in presentation mode
            if (options.IsPresentation && current.IsOneOf) {
                current = current with { IsElisionToggleable = true };
            }

            // Recurse into children FIRST (bottom-up), so container visibility
            // can be decided based on surviving children below.
            if (current.Children.Count > 0) {
                var prunedChildren = PruneNodes(current.Children, fieldOverrides, options, path);
                current = current with { Children = prunedChildren };
            }

            // Implicit visibility for unmatched paths: default MinDetailLevel 9.
            // - Value / SaidReference (leaf-like): hidden at <9.
            // - Dictionary / Array (containers): hidden if no surviving children, except
            //   IsOneOf containers in presentation mode (they're elision-toggleable and
            //   meaningful even when empty).
            if (!hasOverride && (int)options.DetailLevel < (int)CredentialDetailLevel.WithTechnicalDetails) {
                switch (current.Kind) {
                    case CredentialViewNodeKind.Value:
                    case CredentialViewNodeKind.SaidReference:
                        continue;
                    case CredentialViewNodeKind.Dictionary:
                    case CredentialViewNodeKind.Array:
                        if (current.Children.Count == 0
                            && !(options.IsPresentation && current.IsOneOf)) {
                            continue;
                        }
                        break;
                }
            }

            result.Add(current);
        }

        return result;
    }

}
