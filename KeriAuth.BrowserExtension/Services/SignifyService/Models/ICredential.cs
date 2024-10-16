namespace KeriAuth.BrowserExtension.Services.SignifyService.Models
{
    public record A(string i);
    public record Sad(A a, string d);
    public record Schema(string title, string credentialType, string description);
    public record Status(string et);
    public record ICredential(
        string issueeName,
        string[] ancatc,
        Sad sad,
        Schema schema,
        Status status,
        string? cesr // Nullable to handle optional property
    );
}
