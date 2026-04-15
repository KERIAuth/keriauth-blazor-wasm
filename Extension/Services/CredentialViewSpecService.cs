using System.Text.Json;
using System.Text.Json.Serialization;
using Extension.Models;

namespace Extension.Services;

public record CredentialViewSpecManifest(
    [property: JsonPropertyName("viewSpecs")] CredentialViewSpecJson[] ViewSpecs
);

public record CredentialViewSpecJson(
    [property: JsonPropertyName("schemaSaid")] string SchemaSaid,
    [property: JsonPropertyName("shortName")] string ShortName,
    [property: JsonPropertyName("fields")] CredentialFieldSpecJson[] Fields
);

public record CredentialFieldSpecJson(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("minDetailLevel")] int MinDetailLevel,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("format")] string? Format = null
);

public interface ICredentialViewSpecService {
    CredentialViewSpec? GetViewSpec(string schemaSaid);
    CredentialViewSpec GetOrCreateFallback(string schemaSaid);
}

public class CredentialViewSpecService : ICredentialViewSpecService {
    private readonly Dictionary<string, CredentialViewSpec> _viewSpecs;
    private readonly ILogger<CredentialViewSpecService> _logger;

    public CredentialViewSpecService(ILogger<CredentialViewSpecService> logger) {
        _logger = logger;
        _viewSpecs = LoadViewSpecs();
    }

    private Dictionary<string, CredentialViewSpec> LoadViewSpecs() {
        try {
            var assembly = typeof(CredentialViewSpecService).Assembly;
            var resourceName = "Extension.Schemas.credentialViewSpecs.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) {
                _logger.LogWarning(nameof(LoadViewSpecs) + ": View specs resource not found: {ResourceName}", resourceName);
                return new Dictionary<string, CredentialViewSpec>();
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var manifest = JsonSerializer.Deserialize<CredentialViewSpecManifest>(json);
            if (manifest is null) {
                _logger.LogWarning(nameof(LoadViewSpecs) + ": Failed to deserialize view specs manifest");
                return new Dictionary<string, CredentialViewSpec>();
            }

            var specs = new Dictionary<string, CredentialViewSpec>();
            foreach (var specJson in manifest.ViewSpecs) {
                var fields = specJson.Fields
                    .Select(f => new CredentialFieldSpec(f.Path, f.MinDetailLevel, f.Label, f.Format))
                    .ToList();

                specs[specJson.SchemaSaid] = new CredentialViewSpec(
                    specJson.SchemaSaid,
                    specJson.ShortName,
                    fields
                );
            }

            _logger.LogInformation(nameof(LoadViewSpecs) + ": Loaded {Count} credential view specs", specs.Count);
            return specs;
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(LoadViewSpecs) + ": Error loading credential view specs");
            return new Dictionary<string, CredentialViewSpec>();
        }
    }

    public CredentialViewSpec? GetViewSpec(string schemaSaid) {
        if (string.IsNullOrEmpty(schemaSaid))
            return null;

        _viewSpecs.TryGetValue(schemaSaid, out var spec);
        return spec;
    }

    public CredentialViewSpec GetOrCreateFallback(string schemaSaid) {
        var spec = GetViewSpec(schemaSaid);
        if (spec is not null)
            return spec;

        return new CredentialViewSpec(
            SchemaSaid: schemaSaid,
            ShortName: "Credential",
            Fields: []
        );
    }
}
