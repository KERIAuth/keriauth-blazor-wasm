using System.Text.Json;
using Extension.Helper;
using Extension.Models;
using Extension.Services;
using Extension.Services.SignifyService.Models;

namespace Extension.Tests.Services;

public class CredentialViewPipelineTests {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        Converters = { new RecursiveDictionaryConverter() }
    };

    private static ClonedCredential LoadEcrClonedCredential() {
        var testDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Helper"));
        var json = File.ReadAllText(Path.Combine(testDir, "ecr-credential-instance.json"));
        var credential = JsonSerializer.Deserialize<RecursiveDictionary>(json, JsonOptions)!;
        return ClonedCredential.FromRecursiveDictionary(credential);
    }

    private static TestViewSpecService LoadTestViewSpecService() {
        var testDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Services"));
        var json = File.ReadAllText(Path.Combine(testDir, "test-credentialViewSpecs.json"));
        return new TestViewSpecService(json);
    }

    private sealed class TestViewSpecService : ICredentialViewSpecService {
        private readonly Dictionary<string, CredentialViewSpec> _specs;

        public TestViewSpecService(string json) {
            _specs = new Dictionary<string, CredentialViewSpec>();
            var doc = JsonDocument.Parse(json);
            foreach (var specEl in doc.RootElement.GetProperty("viewSpecs").EnumerateArray()) {
                var said = specEl.GetProperty("schemaSaid").GetString()!;
                var fields = specEl.GetProperty("fields").EnumerateArray().Select(f => {
                    f.TryGetProperty("label", out var labelEl);
                    f.TryGetProperty("format", out var formatEl);
                    return new CredentialFieldSpec(
                        f.GetProperty("path").GetString()!,
                        f.GetProperty("minDetailLevel").GetInt32(),
                        labelEl.ValueKind == JsonValueKind.String ? labelEl.GetString() : null,
                        formatEl.ValueKind == JsonValueKind.String
                            ? CredentialFieldFormatNames.ParseSchemaFormat(formatEl.GetString())
                            : null);
                }).ToList();

                _specs[said] = new CredentialViewSpec(said, specEl.GetProperty("shortName").GetString()!, fields);
            }
        }

        public CredentialViewSpec? GetViewSpec(string schemaSaid) =>
            _specs.TryGetValue(schemaSaid, out var spec) ? spec : null;

        public CredentialViewSpec GetOrCreateFallback(string schemaSaid) =>
            GetViewSpec(schemaSaid) ?? new CredentialViewSpec(schemaSaid, "Credential", []);
    }

    public class MergeAcdcAndSchemaTests : CredentialViewPipelineTests {

        [Fact]
        public void ProducesTreeWithSchemaMetadata() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            Assert.Equal("EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw", tree.SchemaSaid);
            Assert.Equal("Legal Entity Engagement Context Role vLEI Credential", tree.SchemaTitle);
            Assert.NotNull(tree.SchemaDescription);
        }

        [Fact]
        public void Path_MatchesCredentialViewSpecsConvention() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            // Top-level: just the key (no sad. prefix).
            var aNode = tree.Children.First(n => n.Key == "a");
            Assert.Equal("a", aNode.Path);

            // Nested: dot-joined from the credential root.
            var leiNode = aNode.Children.First(n => n.Key == "LEI");
            Assert.Equal("a.LEI", leiNode.Path);

            // Deeper: e.qvi.n style.
            var eNode = tree.Children.First(n => n.Key == "e");
            // 'e' is a Dict (oneOf, expanded); its children include 'd' and edge subdicts.
            // Find a nested 'n' if any — otherwise just confirm e's children carry "e.<key>".
            var firstChildOfE = eNode.Children.FirstOrDefault();
            if (firstChildOfE is not null) {
                Assert.Equal($"e.{firstChildOfE.Key}", firstChildOfE.Path);
            }
        }

        [Fact]
        public void PopulatesCredentialSaidFromSadD() {
            var cloned = LoadEcrClonedCredential();
            var expected = cloned.Sad.QueryPath("d")?.StringValue;
            Assert.False(string.IsNullOrEmpty(expected), "ECR fixture should have a populated sad.d");

            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            Assert.Equal(expected, tree.CredentialSaid);
        }

        [Fact]
        public void TopLevelChildren_ContainAllSadKeys() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var keys = tree.Children.Select(n => n.Key).ToList();
            Assert.Contains("v", keys);
            Assert.Contains("d", keys);
            Assert.Contains("i", keys);
            Assert.Contains("s", keys);
            Assert.Contains("a", keys);
            Assert.Contains("e", keys);
            Assert.Contains("r", keys);
        }

        [Fact]
        public void TopLevelValues_HaveSchemaLabels() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var vNode = tree.Children.First(n => n.Key == "v");
            Assert.Equal("Version", vNode.Label);

            var dNode = tree.Children.First(n => n.Key == "d");
            Assert.Equal("Credential SAID", dNode.Label);

            var iNode = tree.Children.First(n => n.Key == "i");
            Assert.Equal("QVI or LE Issuer AID", iNode.Label);
        }

        [Fact]
        public void AttributesSection_IsOneOf_WithExpandedChildren() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var aNode = tree.Children.First(n => n.Key == "a");
            Assert.Equal(CredentialViewNodeKind.Dictionary, aNode.Kind);
            Assert.True(aNode.IsOneOf);

            var childKeys = aNode.Children.Select(n => n.Key).ToList();
            Assert.Contains("LEI", childKeys);
            Assert.Contains("personLegalName", childKeys);
            Assert.Contains("engagementContextRole", childKeys);
            Assert.Contains("i", childKeys);
            Assert.Contains("dt", childKeys);
        }

        [Fact]
        public void AttributeFields_HaveSchemaLabelsAndFormats() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var aNode = tree.Children.First(n => n.Key == "a");

            var leiNode = aNode.Children.First(n => n.Key == "LEI");
            Assert.Equal("LEI of the Legal Entity", leiNode.Label);
            Assert.Equal(CredentialFieldFormat.Lei, leiNode.Format);
            Assert.Equal("254900OPPU84GM83MG36", leiNode.RawValue);

            var dtNode = aNode.Children.First(n => n.Key == "dt");
            Assert.Equal("Issuance date time", dtNode.Label);
            Assert.Equal(CredentialFieldFormat.DateTime, dtNode.Format);

            var nameNode = aNode.Children.First(n => n.Key == "personLegalName");
            Assert.Equal("Recipient name as provided during identity assurance", nameNode.Label);
        }

        [Fact]
        public void Format_Identicon_AppliedToIssuerAndIssuee() {
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var tree = CredentialViewPipeline.BuildFullTree(cloned, service,
                new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails));

            // Top-level issuer "i" — format applied by Prune from view spec
            var issuerNode = tree.Children.First(n => n.Key == "i");
            Assert.Equal(CredentialFieldFormat.Identicon, issuerNode.Format);

            // Attribute issuee "a.i"
            var aNode = tree.Children.First(n => n.Key == "a");
            var issueeNode = aNode.Children.First(n => n.Key == "i");
            Assert.Equal(CredentialFieldFormat.Identicon, issueeNode.Format);
        }

        [Fact]
        public void Format_Lei_AppliedToLEI() {
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var tree = CredentialViewPipeline.BuildFullTree(cloned, service,
                new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails));

            var aNode = tree.Children.First(n => n.Key == "a");
            var leiNode = aNode.Children.First(n => n.Key == "LEI");
            Assert.Equal(CredentialFieldFormat.Lei, leiNode.Format);
            Assert.Equal("254900OPPU84GM83MG36", leiNode.RawValue);
        }

        [Fact]
        public void EdgesSection_IsOneOf_WithNestedEdge() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var eNode = tree.Children.First(n => n.Key == "e");
            Assert.Equal(CredentialViewNodeKind.Dictionary, eNode.Kind);
            Assert.True(eNode.IsOneOf);

            var authNode = eNode.Children.FirstOrDefault(n => n.Key == "auth");
            Assert.NotNull(authNode);
            Assert.Equal(CredentialViewNodeKind.Dictionary, authNode!.Kind);

            // auth.n is the SAID of the chained credential
            var nNode = authNode.Children.First(n => n.Key == "n");
            Assert.Equal(CredentialViewNodeKind.Value, nNode.Kind);
        }

        [Fact]
        public void RulesSection_IsOneOf_WithDisclaimers() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var rNode = tree.Children.First(n => n.Key == "r");
            Assert.Equal(CredentialViewNodeKind.Dictionary, rNode.Kind);
            Assert.True(rNode.IsOneOf);

            var usageNode = rNode.Children.FirstOrDefault(n => n.Key == "usageDisclaimer");
            Assert.NotNull(usageNode);
            Assert.Equal(CredentialViewNodeKind.Dictionary, usageNode!.Kind);
        }

        [Fact]
        public void SaidDigest_ExtractedForOneOfSections() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            var aNode = tree.Children.First(n => n.Key == "a");
            Assert.NotNull(aNode.SaidDigest);
            Assert.Equal("EGxR_dXxolc0GaL-WesgzXLcKGdSKRtY0iG1RKDc3Wjk", aNode.SaidDigest);
        }

        [Fact]
        public void MissingSchema_FallsBackToKeyLabels() {
            var sad = new RecursiveDictionary {
                ["v"] = new RecursiveValue { StringValue = "ACDC10JSON000800_" },
                ["d"] = new RecursiveValue { StringValue = "ESAID1234" },
                ["s"] = new RecursiveValue { StringValue = "EUnknownSchema" },
            };

            var cloned = new ClonedCredential {
                Sad = sad,
                Schema = null,
            };

            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            Assert.Equal("Credential", tree.SchemaTitle);
            var vNode = tree.Children.First(n => n.Key == "v");
            Assert.Equal("v", vNode.Label); // Falls back to key name
        }

        [Fact]
        public void DepthIncrementsForNestedNodes() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            // Top level = depth 0
            var aNode = tree.Children.First(n => n.Key == "a");
            Assert.Equal(0, aNode.Depth);

            // Attributes = depth 1
            var leiNode = aNode.Children.First(n => n.Key == "LEI");
            Assert.Equal(1, leiNode.Depth);

            // Edge sub-objects = depth 2
            var eNode = tree.Children.First(n => n.Key == "e");
            var authNode = eNode.Children.First(n => n.Key == "auth");
            Assert.Equal(1, authNode.Depth);
            var nNode = authNode.Children.First(n => n.Key == "n");
            Assert.Equal(2, nNode.Depth);
        }
    }

    public class PruneTests : CredentialViewPipelineTests {
        private static CredentialViewSpec LoadEcrViewSpec() {
            var service = LoadTestViewSpecService();
            return service.GetViewSpec("EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw")
                ?? throw new InvalidOperationException("ECR view spec not found in test fixture");
        }

        [Fact]
        public void FramingKeys_HiddenAtDefaultDetailLevel_VisibleAtNine() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();

            // At default detail level (WithOptionalSections), v/d/ri/s have minDetailLevel 9 → filtered out.
            var prunedDefault = CredentialViewPipeline.Prune(tree, spec, new CredentialViewOptions());
            var keysDefault = prunedDefault.Children.Select(n => n.Key).ToList();
            Assert.DoesNotContain("v", keysDefault);
            Assert.DoesNotContain("d", keysDefault);
            Assert.DoesNotContain("ri", keysDefault);
            Assert.DoesNotContain("s", keysDefault);
            // Section "a" survives because it has a spec-listed child (a.LEI at level 0).
            // Sections "e" and "r" have no spec-listed descendants in the test fixture, so
            // they end up empty after pruning and are dropped (containers with no surviving
            // children + no explicit spec entry are hidden under the default-9 rule).
            Assert.Contains("a", keysDefault);
            Assert.DoesNotContain("e", keysDefault);
            Assert.DoesNotContain("r", keysDefault);

            // At detail level 9, the framing keys appear.
            var prunedAll = CredentialViewPipeline.Prune(tree, spec, new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails));
            var keysAll = prunedAll.Children.Select(n => n.Key).ToList();
            Assert.Contains("v", keysAll);
            Assert.Contains("d", keysAll);
            Assert.Contains("ri", keysAll);
            Assert.Contains("s", keysAll);
        }

        [Fact]
        public void DetailLevel_FiltersFields() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();

            // Basic level should show minDetailLevel 0 fields but hide level 1 and 2 fields
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.Basic);
            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            var aNode = pruned.Children.First(n => n.Key == "a");
            var childKeys = aNode.Children.Select(n => n.Key).ToList();
            Assert.Contains("LEI", childKeys);          // minDetailLevel 0
            Assert.Contains("personLegalName", childKeys); // minDetailLevel 0
            Assert.DoesNotContain("i", childKeys);        // minDetailLevel 1 — hidden
            Assert.DoesNotContain("dt", childKeys);       // minDetailLevel 2 — hidden
        }

        [Fact]
        public void DetailLevel_ShowsAllAtLevel9() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();

            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);
            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            var aNode = pruned.Children.First(n => n.Key == "a");
            var childKeys = aNode.Children.Select(n => n.Key).ToList();
            Assert.Contains("LEI", childKeys);
            Assert.Contains("personLegalName", childKeys);
            Assert.Contains("i", childKeys);
            Assert.Contains("dt", childKeys);
        }

        [Fact]
        public void LabelOverrides_AppliedFromSpec() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            var aNode = pruned.Children.First(n => n.Key == "a");
            var nameNode = aNode.Children.First(n => n.Key == "personLegalName");
            Assert.Equal("Legal Name", nameNode.Label);

            var roleNode = aNode.Children.First(n => n.Key == "engagementContextRole");
            Assert.Equal("Engagement Context Role", roleNode.Label);

            var dtNode = aNode.Children.First(n => n.Key == "dt");
            Assert.Equal("Issued", dtNode.Label);
        }

        [Fact]
        public void ViewSpecLabel_PopulatedWhenOverrideExists() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            // personLegalName has a "label": "Legal Name" override in the test spec.
            var aNode = pruned.Children.First(n => n.Key == "a");
            var nameNode = aNode.Children.First(n => n.Key == "personLegalName");
            Assert.Equal("Legal Name", nameNode.ViewSpecLabel);
            Assert.Equal("Legal Name", nameNode.Label); // also the resolved label
        }

        [Fact]
        public void ViewSpecLabel_NullWhenNoOverride() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            // LEI has minDetailLevel 0 in the spec but no "label" override.
            var aNode = pruned.Children.First(n => n.Key == "a");
            var leiNode = aNode.Children.First(n => n.Key == "LEI");
            Assert.Null(leiNode.ViewSpecLabel);
        }

        [Fact]
        public void FormatOverride_AppliedFromSpec() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            var aNode = pruned.Children.First(n => n.Key == "a");
            var dtNode = aNode.Children.First(n => n.Key == "dt");
            Assert.Equal(CredentialFieldFormat.DateTimeAsUtc, dtNode.Format);
        }

        [Fact]
        public void ShortName_SetFromSpec() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions();

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);
            Assert.Equal("ECR vLEI", pruned.ShortName);
        }

        [Fact]
        public void PresentationMode_MarksOneOfSectionsAsElisionToggleable() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions(IsPresentation: true);

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            var aNode = pruned.Children.First(n => n.Key == "a");
            Assert.True(aNode.IsElisionToggleable);

            var eNode = pruned.Children.First(n => n.Key == "e");
            Assert.True(eNode.IsElisionToggleable);

            var rNode = pruned.Children.First(n => n.Key == "r");
            Assert.True(rNode.IsElisionToggleable);
        }

        [Fact]
        public void NonPresentationMode_DoesNotMarkElisionToggleable() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);
            var spec = LoadEcrViewSpec();
            var options = new CredentialViewOptions(IsPresentation: false);

            var pruned = CredentialViewPipeline.Prune(tree, spec, options);

            var aNode = pruned.Children.First(n => n.Key == "a");
            Assert.False(aNode.IsElisionToggleable);
        }

        [Fact]
        public void NullSpec_HidesEverythingByDefault_VisibleAtNine() {
            var cloned = LoadEcrClonedCredential();
            var tree = CredentialViewPipeline.MergeAcdcAndSchema(cloned);

            // With no spec, every Value leaf defaults to MinDetailLevel 9, every container
            // ends up empty after children are filtered, so containers also drop. At default
            // level (WithOptionalSections), nothing survives.
            var prunedDefault = CredentialViewPipeline.Prune(tree, null, new CredentialViewOptions());
            Assert.Empty(prunedDefault.Children);

            // At WithTechnicalDetails (level 9), every leaf is visible regardless of spec presence.
            var prunedAll = CredentialViewPipeline.Prune(tree, null,
                new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails));
            var keysAll = prunedAll.Children.Select(n => n.Key).ToList();
            Assert.Contains("v", keysAll);
            Assert.Contains("d", keysAll);
            Assert.Contains("a", keysAll);
        }
    }

    public class BuildFullTreeTests : CredentialViewPipelineTests {

        [Fact]
        public void BuildsFullTreeWith4LevelChain() {
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var tree = CredentialViewPipeline.BuildFullTree(cloned, service, options);

            // Root: ECR vLEI
            Assert.Equal("EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw", tree.SchemaSaid);
            Assert.Equal("ECR vLEI", tree.ShortName);
            Assert.Single(tree.ChainedCredentials);

            // Level 1: ECR Auth
            var ecrAuth = tree.ChainedCredentials[0];
            Assert.Equal("EH6ekLjSr8V32WyFbGe1zXjTzFs9PkTYmupJ9H65O14g", ecrAuth.SchemaSaid);
            Assert.Equal("ECR Auth", ecrAuth.ShortName);
            Assert.Single(ecrAuth.ChainedCredentials);

            // Level 2: LE vLEI
            var le = ecrAuth.ChainedCredentials[0];
            Assert.Equal("ENPXp1vQzRF6JwIuS-mp2U8Uf1MoADoP_GqQ62VsDZWY", le.SchemaSaid);
            Assert.Equal("LE vLEI", le.ShortName);
            Assert.Single(le.ChainedCredentials);

            // Level 3: QVI (leaf)
            var qvi = le.ChainedCredentials[0];
            Assert.Equal("EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao", qvi.SchemaSaid);
            Assert.Equal("QVI", qvi.ShortName);
            Assert.Empty(qvi.ChainedCredentials);
        }

        [Fact]
        public void EachChainLevel_HasPrunedChildren() {
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.Detailed);

            var tree = CredentialViewPipeline.BuildFullTree(cloned, service, options);

            // Root should have hidden details removed (v, d, ri, s hidden)
            var rootKeys = tree.Children.Select(n => n.Key).ToList();
            Assert.DoesNotContain("v", rootKeys);
            Assert.DoesNotContain("d", rootKeys);

            // ECR Auth should also be pruned
            var ecrAuth = tree.ChainedCredentials[0];
            var authKeys = ecrAuth.Children.Select(n => n.Key).ToList();
            Assert.DoesNotContain("v", authKeys);
            Assert.DoesNotContain("d", authKeys);
        }

        [Fact]
        public void EachChainLevel_HasAttributeNodes() {
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var tree = CredentialViewPipeline.BuildFullTree(cloned, service, options);

            // Root ECR has LEI attribute
            var rootA = tree.Children.First(n => n.Key == "a");
            Assert.Contains(rootA.Children, n => n.Key == "LEI");

            // ECR Auth also has LEI
            var authA = tree.ChainedCredentials[0].Children.First(n => n.Key == "a");
            Assert.Contains(authA.Children, n => n.Key == "LEI");

            // QVI has LEI
            var qviA = tree.ChainedCredentials[0].ChainedCredentials[0].ChainedCredentials[0]
                .Children.First(n => n.Key == "a");
            Assert.Contains(qviA.Children, n => n.Key == "LEI");
        }

        [Fact]
        public void UnknownSchema_GetsFallbackShortName() {
            // The QVI schema SAID may not have a view spec — depends on test fixture
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var options = new CredentialViewOptions(DetailLevel: CredentialDetailLevel.WithTechnicalDetails);

            var tree = CredentialViewPipeline.BuildFullTree(cloned, service, options);

            // All 4 levels should have non-null short names (from spec or null fallback)
            Assert.NotNull(tree.SchemaTitle);
            foreach (var chain in tree.ChainedCredentials) {
                Assert.NotNull(chain.SchemaTitle);
            }
        }

        [Fact]
        public void PresentationMode_ChainedCredentialsRendered() {
            var cloned = LoadEcrClonedCredential();
            var service = LoadTestViewSpecService();
            var options = new CredentialViewOptions(IsPresentation: true, DetailLevel: CredentialDetailLevel.Detailed);

            var tree = CredentialViewPipeline.BuildFullTree(cloned, service, options);

            // Chained credentials should still be present
            Assert.Single(tree.ChainedCredentials);

            // oneOf sections in root should be elision-toggleable
            var aNode = tree.Children.First(n => n.Key == "a");
            Assert.True(aNode.IsElisionToggleable);
        }
    }
}
