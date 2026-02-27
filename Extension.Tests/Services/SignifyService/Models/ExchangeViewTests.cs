using Extension.Helper;
using Extension.Services.SignifyService.Models;
using Xunit;

namespace Extension.Tests.Services.SignifyService.Models;

public class ExchangeViewTests {
    [Fact]
    public void FromRecursiveDictionary_WithWrappedExn_ExtractsScalarFields() {
        // Arrange — simulates { exn: { d, i, rp, dt, r, p } }
        var exn = new RecursiveDictionary {
            ["d"] = new RecursiveValue { StringValue = "SAID123" },
            ["i"] = new RecursiveValue { StringValue = "EABCSender" },
            ["rp"] = new RecursiveValue { StringValue = "EABCRecipient" },
            ["dt"] = new RecursiveValue { StringValue = "2026-01-15T10:00:00Z" },
            ["r"] = new RecursiveValue { StringValue = "/ipex/grant" },
            ["p"] = new RecursiveValue { StringValue = "PriorSAID" },
        };
        var wrapper = new RecursiveDictionary {
            ["exn"] = new RecursiveValue { Dictionary = exn },
        };

        // Act
        var view = ExchangeView.FromRecursiveDictionary(wrapper);

        // Assert
        Assert.Equal("SAID123", view.D);
        Assert.Equal("EABCSender", view.I);
        Assert.Equal("EABCRecipient", view.Rp);
        Assert.Equal("2026-01-15T10:00:00Z", view.Dt);
        Assert.Equal("/ipex/grant", view.R);
        Assert.Equal("PriorSAID", view.P);
        Assert.Same(exn, view.RawExn);
    }

    [Fact]
    public void FromRecursiveDictionary_WithoutWrapper_FallsBackToTopLevel() {
        // Arrange — no exn wrapper, fields at top level
        var dict = new RecursiveDictionary {
            ["d"] = new RecursiveValue { StringValue = "TopSAID" },
            ["i"] = new RecursiveValue { StringValue = "TopSender" },
        };

        // Act
        var view = ExchangeView.FromRecursiveDictionary(dict);

        // Assert
        Assert.Equal("TopSAID", view.D);
        Assert.Equal("TopSender", view.I);
        Assert.Same(dict, view.RawExn);
    }

    [Fact]
    public void FromRecursiveDictionary_MissingOptionalFields_ReturnsNulls() {
        // Arrange — only sender present
        var exn = new RecursiveDictionary {
            ["i"] = new RecursiveValue { StringValue = "EABCSender" },
        };
        var wrapper = new RecursiveDictionary {
            ["exn"] = new RecursiveValue { Dictionary = exn },
        };

        // Act
        var view = ExchangeView.FromRecursiveDictionary(wrapper);

        // Assert
        Assert.Equal("EABCSender", view.I);
        Assert.Null(view.D);
        Assert.Null(view.Rp);
        Assert.Null(view.Dt);
        Assert.Null(view.R);
        Assert.Null(view.P);
        Assert.Null(view.A);
        Assert.Null(view.E);
        Assert.Null(view.Q);
    }

    [Fact]
    public void FromRecursiveDictionary_PreservesOrderedDictFields() {
        // Arrange
        var aDict = new RecursiveDictionary {
            ["i"] = new RecursiveValue { StringValue = "AttribId" },
        };
        var eDict = new RecursiveDictionary {
            ["acdc"] = new RecursiveValue { StringValue = "EmbeddedCred" },
        };
        var qDict = new RecursiveDictionary {
            ["filter"] = new RecursiveValue { StringValue = "SomeFilter" },
        };
        var exn = new RecursiveDictionary {
            ["d"] = new RecursiveValue { StringValue = "SAID" },
            ["a"] = new RecursiveValue { Dictionary = aDict },
            ["e"] = new RecursiveValue { Dictionary = eDict },
            ["q"] = new RecursiveValue { Dictionary = qDict },
        };
        var wrapper = new RecursiveDictionary {
            ["exn"] = new RecursiveValue { Dictionary = exn },
        };

        // Act
        var view = ExchangeView.FromRecursiveDictionary(wrapper);

        // Assert — same instances preserved (no copy)
        Assert.Same(aDict, view.A);
        Assert.Same(eDict, view.E);
        Assert.Same(qDict, view.Q);
    }
}
