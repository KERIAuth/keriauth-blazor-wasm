using Extension.Helper;
using System.Text.Json;
using Xunit;

namespace Extension.Tests.Helper {
    public class RecursiveDictionaryJsonPathTests {
        private static readonly JsonSerializerOptions JsonOptions = new() {
            Converters = { new RecursiveDictionaryConverter() }
        };

        [Fact]
        public void SimpleKeyPath_ReturnsValue() {
            var dict = new RecursiveDictionary {
                ["a"] = new RecursiveDictionary {
                    ["LEI"] = "254900OPPU84GM83MG36"
                }
            };

            var result = dict.QueryPath("a.LEI");
            Assert.NotNull(result);
            Assert.Equal("254900OPPU84GM83MG36", result!.StringValue);
        }

        [Fact]
        public void RootPrefix_IsStripped() {
            var dict = new RecursiveDictionary {
                ["a"] = new RecursiveDictionary {
                    ["LEI"] = "254900OPPU84GM83MG36"
                }
            };

            var result = dict.QueryPath("$.a.LEI");
            Assert.NotNull(result);
            Assert.Equal("254900OPPU84GM83MG36", result!.StringValue);
        }

        [Fact]
        public void ArrayIndex_ReturnsElement() {
            var dict = new RecursiveDictionary {
                ["items"] = new RecursiveValue {
                    List = new List<RecursiveValue> {
                        new() { Dictionary = new RecursiveDictionary { ["name"] = "first" } },
                        new() { Dictionary = new RecursiveDictionary { ["name"] = "second" } }
                    }
                }
            };

            var result = dict.QueryPath("items[0].name");
            Assert.NotNull(result);
            Assert.Equal("first", result!.StringValue);

            var result2 = dict.QueryPath("items[1].name");
            Assert.NotNull(result2);
            Assert.Equal("second", result2!.StringValue);
        }

        [Fact]
        public void NestedArrays_TraverseCorrectly() {
            var dict = new RecursiveDictionary {
                ["items"] = new RecursiveValue {
                    List = new List<RecursiveValue> {
                        new() {
                            Dictionary = new RecursiveDictionary {
                                ["sub"] = new RecursiveValue {
                                    List = new List<RecursiveValue> {
                                        new() { Dictionary = new RecursiveDictionary { ["value"] = "a" } },
                                        new() { Dictionary = new RecursiveDictionary { ["value"] = "b" } }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var result = dict.QueryPath("items[0].sub[1].value");
            Assert.NotNull(result);
            Assert.Equal("b", result!.StringValue);
        }

        [Fact]
        public void OutOfBoundsIndex_ReturnsNull() {
            var dict = new RecursiveDictionary {
                ["items"] = new RecursiveValue {
                    List = new List<RecursiveValue> { "only" }
                }
            };

            Assert.Null(dict.QueryPath("items[5]"));
        }

        [Fact]
        public void KeyNotFound_ReturnsNull() {
            var dict = new RecursiveDictionary {
                ["a"] = new RecursiveDictionary { ["b"] = "value" }
            };

            Assert.Null(dict.QueryPath("a.nonexistent"));
            Assert.Null(dict.QueryPath("missing.path"));
        }

        [Fact]
        public void EmptyPath_ReturnsNull() {
            var dict = new RecursiveDictionary { ["a"] = "value" };
            Assert.Null(dict.QueryPath(""));
            Assert.Null(dict.QueryPath(null!));
        }

        [Fact]
        public void DollarOnly_ReturnsRootAsDictionary() {
            var dict = new RecursiveDictionary { ["a"] = "value" };
            var result = dict.QueryPath("$");
            Assert.NotNull(result);
            Assert.NotNull(result!.Dictionary);
            Assert.Equal("value", result.Dictionary!["a"].StringValue);
        }

        [Fact]
        public void RootLevelArrayOfStrings() {
            var dict = new RecursiveDictionary {
                ["ancatc"] = new RecursiveValue {
                    List = new List<RecursiveValue> {
                        "attachment1",
                        "attachment2"
                    }
                }
            };

            var result = dict.QueryPath("ancatc[0]");
            Assert.NotNull(result);
            Assert.Equal("attachment1", result!.StringValue);

            var result2 = dict.QueryPath("ancatc[1]");
            Assert.NotNull(result2);
            Assert.Equal("attachment2", result2!.StringValue);
        }

        [Fact]
        public void ConsecutiveBracketIndices() {
            // matrix[0][1] — array of arrays
            var dict = new RecursiveDictionary {
                ["matrix"] = new RecursiveValue {
                    List = new List<RecursiveValue> {
                        new() {
                            List = new List<RecursiveValue> { "r0c0", "r0c1" }
                        },
                        new() {
                            List = new List<RecursiveValue> { "r1c0", "r1c1" }
                        }
                    }
                }
            };

            var result = dict.QueryPath("matrix[0][1]");
            Assert.NotNull(result);
            Assert.Equal("r0c1", result!.StringValue);

            var result2 = dict.QueryPath("matrix[1][0]");
            Assert.NotNull(result2);
            Assert.Equal("r1c0", result2!.StringValue);
        }

        [Fact]
        public void QueryValueByPath_ReturnsTypedValue() {
            var dict = new RecursiveDictionary {
                ["a"] = new RecursiveDictionary {
                    ["count"] = 42
                }
            };

            var result = dict.QueryValueByPath("a.count");
            Assert.NotNull(result);
            Assert.Equal(42L, result!.Value);
        }

        [Fact]
        public void CredentialStructure_ChainsTraversal() {
            // Simulate the credential structure: top-level with chains array
            // Navigate from test bin output to test project source
            var testDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Helper"));
            var json = File.ReadAllText(Path.Combine(testDir, "ecr-credential-instance.json"));
            var credential = JsonSerializer.Deserialize<RecursiveDictionary>(json, JsonOptions);
            Assert.NotNull(credential);

            // Root credential sad fields
            var personName = credential!.QueryPath("sad.a.personLegalName");
            Assert.NotNull(personName);
            Assert.Equal("John Smith", personName!.StringValue);

            var schemaSaid = credential.QueryPath("sad.s");
            Assert.NotNull(schemaSaid);
            Assert.Equal("EEy9PkikFcANV1l7EHukCeXqrzT1hNZjGlUk7wuMO5jw", schemaSaid!.StringValue);

            // Chain[0] = ECR Auth credential
            var chain0Title = credential.QueryPath("chains[0].schema.title");
            Assert.NotNull(chain0Title);
            Assert.Equal("ECR Authorization vLEI Credential", chain0Title!.StringValue);

            // Chain[0].Chain[0] = LE credential
            var chain0_0Lei = credential.QueryPath("chains[0].chains[0].sad.a.LEI");
            Assert.NotNull(chain0_0Lei);
            Assert.Equal("254900OPPU84GM83MG36", chain0_0Lei!.StringValue);

            // Chain[0].Chain[0].Chain[0] = QVI credential
            var qviLei = credential.QueryPath("chains[0].chains[0].chains[0].sad.a.LEI");
            Assert.NotNull(qviLei);
            Assert.Equal("5493001KJTIIGC8Y1R17", qviLei!.StringValue);

            // Deepest chains is empty
            var deepChains = credential.QueryPath("chains[0].chains[0].chains[0].chains");
            Assert.NotNull(deepChains);
            Assert.NotNull(deepChains!.List);
            Assert.Empty(deepChains.List!);

            // ancatc array of strings
            var ancatc0 = credential.QueryPath("ancatc[0]");
            Assert.NotNull(ancatc0);
            Assert.StartsWith("-VAn-", ancatc0!.StringValue);
        }

        [Fact]
        public void BackwardCompatible_WithExistingViewSpecPaths() {
            // Paths from credentialViewSpecs.json should work via QueryPath
            var sad = new RecursiveDictionary {
                ["a"] = new RecursiveDictionary {
                    ["LEI"] = "254900OPPU84GM83MG36",
                    ["personLegalName"] = "John Smith",
                    ["engagementContextRole"] = "Project Manager",
                    ["i"] = "ELTKuYTbErSepJblfpyWXJ3VGFMs4NoSCzflaKnWHSug",
                    ["dt"] = "2026-03-31T03:15:59.169000+00:00"
                },
                ["e"] = new RecursiveDictionary {
                    ["qvi"] = new RecursiveDictionary {
                        ["n"] = "EP9Lt3IoomEofK2ZD0w5FaQZIXdcUl4-abb1RheFS240",
                        ["s"] = "EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao"
                    }
                },
                ["r"] = new RecursiveDictionary {
                    ["usageDisclaimer"] = new RecursiveDictionary {
                        ["l"] = "Usage disclaimer text"
                    },
                    ["issuanceDisclaimer"] = new RecursiveDictionary {
                        ["l"] = "Issuance disclaimer text"
                    }
                }
            };

            Assert.Equal("254900OPPU84GM83MG36", sad.QueryPath("a.LEI")!.StringValue);
            Assert.Equal("John Smith", sad.QueryPath("a.personLegalName")!.StringValue);
            Assert.Equal("Project Manager", sad.QueryPath("a.engagementContextRole")!.StringValue);
            Assert.Equal("EP9Lt3IoomEofK2ZD0w5FaQZIXdcUl4-abb1RheFS240", sad.QueryPath("e.qvi.n")!.StringValue);
            Assert.Equal("EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao", sad.QueryPath("e.qvi.s")!.StringValue);
            Assert.Equal("Usage disclaimer text", sad.QueryPath("r.usageDisclaimer.l")!.StringValue);
            Assert.Equal("Issuance disclaimer text", sad.QueryPath("r.issuanceDisclaimer.l")!.StringValue);
        }
    }
}
