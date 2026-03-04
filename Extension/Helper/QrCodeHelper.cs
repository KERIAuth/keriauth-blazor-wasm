namespace Extension.Helper;

using Net.Codecrete.QrCodeGenerator;

public static class QrCodeHelper {
    public static string ToSvgString(string text, int border = 2) {
        if (string.IsNullOrEmpty(text))
            return "";

        var qr = QrCode.EncodeText(text, QrCode.Ecc.Low);
        return qr.ToSvgString(border);
    }
}
