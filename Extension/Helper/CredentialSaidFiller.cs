namespace Extension.Helper;

/// <summary>
/// Presentation-time utility: walks a credential RecursiveDictionary and, for every
/// dict-valued section whose "d" field is empty (ACDC convention: populated at issuance
/// for attributes but often omitted for edges/rules), computes the SAID via the provided
/// saidify delegate and writes it into a deep-copied result. The original input is never
/// mutated. Chains[] entries are recursed through.
///
/// The returned dictionary preserves insertion order throughout, which matters for both
/// rendering stability and any downstream SAID-computation correctness.
/// </summary>
public static class CredentialSaidFiller {
    /// <summary>
    /// Fill in missing SAIDs on a deep copy. If any saidify call returns null/empty, the
    /// method returns null and the caller should treat that as a failure.
    /// </summary>
    /// <param name="credential">Source credential; NOT mutated.</param>
    /// <param name="saidifyAsync">Delegate that saidifies one block via signify-ts.</param>
    public static async Task<RecursiveDictionary?> FillMissingSaidsAsync(
        RecursiveDictionary credential,
        Func<RecursiveDictionary, Task<string?>> saidifyAsync) {
        var clone = DeepClone(credential);
        var ok = await FillCredentialAsync(clone, saidifyAsync);
        return ok ? clone : null;
    }

    private static async Task<bool> FillCredentialAsync(
        RecursiveDictionary credential,
        Func<RecursiveDictionary, Task<string?>> saidifyAsync) {
        var sad = credential.QueryPath("sad")?.Dictionary;
        if (sad is not null) {
            foreach (var kv in sad) {
                if (kv.Value.Dictionary is not { } sectionDict) continue;
                if (!sectionDict.ContainsKey("d")) continue;

                var storedSaid = sectionDict.QueryPath("d")?.StringValue;
                if (!string.IsNullOrEmpty(storedSaid)) continue;

                // Build the saidify input: the section as-is, but with d="" (canonical placeholder).
                // Since we're already in the cloned tree, we could pass sectionDict directly —
                // but constructing a fresh dict keeps the saidify input decoupled from the
                // tree we're about to write back into.
                var input = new RecursiveDictionary();
                foreach (var entry in sectionDict) {
                    input[entry.Key] = entry.Key == "d"
                        ? new RecursiveValue { StringValue = "" }
                        : entry.Value;
                }

                var said = await saidifyAsync(input);
                if (string.IsNullOrEmpty(said)) return false;

                sectionDict["d"] = new RecursiveValue { StringValue = said };
            }
        }

        var chains = credential.QueryPath("chains")?.List;
        if (chains is null) return true;
        foreach (var chainValue in chains) {
            if (chainValue.Dictionary is not { } chainDict) continue;
            if (!await FillCredentialAsync(chainDict, saidifyAsync)) return false;
        }
        return true;
    }

    /// <summary>
    /// Deep clone preserving insertion order. Scalar RecursiveValues are re-used (immutable
    /// in practice — we only ever replace dict entries whole, never in-place edit scalars).
    /// Dictionary and List containers are fresh instances so the caller can mutate safely.
    /// </summary>
    private static RecursiveDictionary DeepClone(RecursiveDictionary source) {
        var dest = new RecursiveDictionary();
        foreach (var kv in source) {
            dest[kv.Key] = DeepCloneValue(kv.Value);
        }
        return dest;
    }

    private static RecursiveValue DeepCloneValue(RecursiveValue source) => source.Type switch {
        RecursiveValueType.Dictionary => new RecursiveValue { Dictionary = DeepClone(source.Dictionary!) },
        RecursiveValueType.List => new RecursiveValue { List = [.. source.List!.Select(DeepCloneValue)] },
        _ => source,
    };
}
