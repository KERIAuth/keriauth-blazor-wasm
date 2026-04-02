using System.Text.Json;
using Extension.Helper;
using Extension.Services.SignifyService.Models;

namespace Extension.Tests.Services.SignifyService.Models;

public class ClonedCredentialTests {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        Converters = { new RecursiveDictionaryConverter() }
    };

    private static RecursiveDictionary LoadEcrCredential() {
        var testDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Helper"));
        var json = File.ReadAllText(Path.Combine(testDir, "ecr-credential-instance.json"));
        return JsonSerializer.Deserialize<RecursiveDictionary>(json, JsonOptions)!;
    }

    [Fact]
    public void FromRecursiveDictionary_ExtractsRootFields() {
        var credential = LoadEcrCredential();
        var cloned = ClonedCredential.FromRecursiveDictionary(credential);

        Assert.NotNull(cloned.Sad);
        Assert.NotNull(cloned.Schema);
        Assert.NotNull(cloned.Iss);
        Assert.NotNull(cloned.Atc);
        Assert.NotNull(cloned.IssAtc);
        Assert.NotNull(cloned.Pre);
        Assert.Equal("EJ0TiqSKn_OvYWIbU14TqNPsuBzz0zgGt7NCtvbNi3n3", cloned.Pre);
    }

    [Fact]
    public void FromRecursiveDictionary_SchemaSaid_FromSad() {
        var credential = LoadEcrCredential();
        var cloned = ClonedCredential.FromRecursiveDictionary(credential);

        Assert.Equal("EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw", cloned.SchemaSaid);
    }

    [Fact]
    public void FromRecursiveDictionary_ExtractsSchemaTitle() {
        var credential = LoadEcrCredential();
        var cloned = ClonedCredential.FromRecursiveDictionary(credential);

        var title = cloned.Schema!.QueryPath("title")?.StringValue;
        Assert.Equal("Legal Entity Engagement Context Role vLEI Credential", title);
    }

    [Fact]
    public void FromRecursiveDictionary_ExtractsIssEvent() {
        var credential = LoadEcrCredential();
        var cloned = ClonedCredential.FromRecursiveDictionary(credential);

        var issType = cloned.Iss!.QueryPath("t")?.StringValue;
        Assert.Equal("iss", issType);
        var issD = cloned.Iss.QueryPath("d")?.StringValue;
        Assert.Equal("EPoGoCd7jJh19eqDMfr2GpflR3NnaskGI5XTb7RsWFr_", issD);
    }

    [Fact]
    public void FromRecursiveDictionary_Chains_Has4Levels() {
        var credential = LoadEcrCredential();
        var cloned = ClonedCredential.FromRecursiveDictionary(credential);

        // Level 0: ECR credential
        Assert.Single(cloned.Chains);

        // Level 1: ECR Auth
        var ecrAuth = cloned.Chains[0];
        Assert.NotNull(ecrAuth.Sad);
        Assert.Equal("EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g", ecrAuth.SchemaSaid);
        Assert.Single(ecrAuth.Chains);

        // Level 2: LE vLEI
        var le = ecrAuth.Chains[0];
        Assert.NotNull(le.Sad);
        Assert.Equal("ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY", le.SchemaSaid);
        Assert.Single(le.Chains);

        // Level 3: QVI (leaf)
        var qvi = le.Chains[0];
        Assert.NotNull(qvi.Sad);
        Assert.Equal("EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao", qvi.SchemaSaid);
        Assert.Empty(qvi.Chains);
    }

    [Fact]
    public void FromRecursiveDictionary_ChainedSad_HasAttributes() {
        var credential = LoadEcrCredential();
        var cloned = ClonedCredential.FromRecursiveDictionary(credential);

        var ecrAuth = cloned.Chains[0];
        var lei = ecrAuth.Sad.QueryPath("a.LEI")?.StringValue;
        Assert.Equal("254900OPPU84GM83MG36", lei);
    }

    [Fact]
    public void FromRecursiveDictionary_MissingSad_Throws() {
        var empty = new RecursiveDictionary();
        Assert.Throws<ArgumentException>(() => ClonedCredential.FromRecursiveDictionary(empty));
    }
}
