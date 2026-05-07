using Extension.Helper;

namespace Extension.Tests.Helper {
    public class CredentialPathsTests {
        [Theory]
        // Null / empty guard
        [InlineData(null, false)]
        [InlineData("", false)]
        // Top-level identifying SAIDs — never edges
        [InlineData("d", false)]
        [InlineData("i", false)]
        [InlineData("ri", false)]
        // Top-level edge fields — the intended hit
        [InlineData("e.auth.n", true)]
        [InlineData("e.le.n", true)]
        // Other fields under e.<edge> — also classified as edges; harmless because their
        // values aren't in ChainIndex.
        [InlineData("e.auth.d", true)]
        [InlineData("e.auth.s", true)]
        // Chain-boundary path-prefix construction: "chains[0]/" + "." + key
        [InlineData("chains[0]/.d", false)]
        [InlineData("chains[0]/.i", false)]
        [InlineData("chains[0]/.e.auth.n", true)]
        // SaidReference inline-render path-prefix: "e.auth/" + "." + key
        [InlineData("e.auth/.d", false)]
        [InlineData("e.auth/.e.le.n", true)]
        // Defensive cases — paths that contain or start with 'e' but not the edges segment
        [InlineData("a.e.foo", false)]
        [InlineData("expiry", false)]
        [InlineData("ee.foo", false)]
        [InlineData("e", false)]
        public void IsEdgePath_ReturnsExpected(string? path, bool expected) {
            Assert.Equal(expected, CredentialPaths.IsEdgePath(path));
        }
    }
}
