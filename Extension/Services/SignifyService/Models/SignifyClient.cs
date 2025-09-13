namespace Extension.Services.SignifyService.Models {
    public record SignifyClient(
        Controller Controller,
        string Url,
        string Bran,
        int Pidx,
        Tier Tier,
        string BootUrl,
        Agent? Agent = null, // Default values for nullable properties can be specified in the parameter list
        Authenticater? Authn = null,
        KeyManager? Manager = null,
        List<ExternalModule>? ExteralModules = null // Assuming it's okay to start with null, or you could use a new List<ExternalModule>() here
    );
}
