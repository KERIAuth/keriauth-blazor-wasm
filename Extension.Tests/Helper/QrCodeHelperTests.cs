using Extension.Helper;

namespace Extension.Tests.Helper {
    public class QrCodeHelperTests {
        [Fact]
        public void ToSvgString_ReturnsValidSvg() {
            // Arrange
            var text = "http://example.com/oobi/EBfdlu8R27Fbx-ehrqwImnK-8Cm79sqbAQ4MmvEAYqao6";

            // Act
            var svg = QrCodeHelper.ToSvgString(text);

            // Assert
            Assert.StartsWith("<?xml", svg);
            Assert.Contains("<svg", svg);
            Assert.Contains("</svg>", svg);
        }

        [Fact]
        public void ToSvgString_ReturnsEmptyForNullInput() {
            // Act
            var svg = QrCodeHelper.ToSvgString(null!);

            // Assert
            Assert.Equal("", svg);
        }

        [Fact]
        public void ToSvgString_ReturnsEmptyForEmptyInput() {
            // Act
            var svg = QrCodeHelper.ToSvgString("");

            // Assert
            Assert.Equal("", svg);
        }

        [Fact]
        public void ToSvgString_DifferentInputsProduceDifferentSvgs() {
            // Arrange & Act
            var svg1 = QrCodeHelper.ToSvgString("http://example.com/oobi/abc123");
            var svg2 = QrCodeHelper.ToSvgString("http://example.com/oobi/xyz789");

            // Assert
            Assert.NotEqual(svg1, svg2);
        }
    }
}
