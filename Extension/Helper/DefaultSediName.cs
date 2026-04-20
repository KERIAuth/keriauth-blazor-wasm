namespace Extension.Helper;

/// <summary>
/// Generates a default "full legal name" for the SEDI test credential dialog.
/// Coin-flips gender, then samples a first, middle, and last name from English-pronounceable,
/// ethnically varied pools. Hardcoded (no external data file) — this is test-only data.
/// </summary>
public static class DefaultSediName
{
    private static readonly string[] MaleFirsts = {
        "Michael", "David", "James", "Rafael", "Hiro",
        "Kwame", "Omar", "Sven", "Dmitri", "Anand"
    };

    private static readonly string[] MaleMiddles = {
        "Benjamin", "Christopher", "Elias", "Lorenzo", "Takeshi",
        "Abdou", "Farid", "Erik", "Pavel", "Raghav"
    };

    private static readonly string[] FemaleFirsts = {
        "Sarah", "Priya", "Emma", "Aisha", "Mei",
        "Sofia", "Fatima", "Ingrid", "Yuki", "Amara"
    };

    private static readonly string[] FemaleMiddles = {
        "Grace", "Noelle", "Rose", "Zara", "Jade",
        "Luna", "Leila", "Astrid", "Hana", "Olivia"
    };

    private static readonly string[] LastNames = {
        "Johnson", "Garcia", "Tanaka", "Okonkwo", "Nguyen",
        "Petrov", "Al-Sayed", "Lindqvist", "Patel", "Silva"
    };

    public static string Generate(Random? random = null)
    {
        var rng = random ?? Random.Shared;
        var isMale = rng.Next(2) == 0;
        var firsts = isMale ? MaleFirsts : FemaleFirsts;
        var middles = isMale ? MaleMiddles : FemaleMiddles;
        return $"{firsts[rng.Next(firsts.Length)]} {middles[rng.Next(middles.Length)]} {LastNames[rng.Next(LastNames.Length)]}";
    }

    // Exposed for tests that verify pool invariants (distinctness, size).
    internal static IReadOnlyList<string> GetMaleFirsts() => MaleFirsts;
    internal static IReadOnlyList<string> GetMaleMiddles() => MaleMiddles;
    internal static IReadOnlyList<string> GetFemaleFirsts() => FemaleFirsts;
    internal static IReadOnlyList<string> GetFemaleMiddles() => FemaleMiddles;
    internal static IReadOnlyList<string> GetLastNames() => LastNames;
}
