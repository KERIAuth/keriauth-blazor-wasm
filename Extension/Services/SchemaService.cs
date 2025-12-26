using System.Text.Json;
using System.Text.Json.Serialization;

namespace Extension.Services;

/// <summary>
/// Represents a schema entry from the schemas.json manifest.
/// </summary>
public record SchemaEntry(
    [property: JsonPropertyName("said")] string Said,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("oobiUrls")] string[] OobiUrls
);

/// <summary>
/// Represents the schemas.json manifest structure.
/// </summary>
public record SchemaManifest(
    [property: JsonPropertyName("schemas")] SchemaEntry[] Schemas,
    [property: JsonPropertyName("defaultOobiHosts")] string[] DefaultOobiHosts
);

/// <summary>
/// Service for looking up schema OOBI URLs by SAID.
/// Loads schema metadata from embedded schemas.json manifest.
/// </summary>
public interface ISchemaService {
    /// <summary>
    /// Gets the schema entry for a given SAID.
    /// </summary>
    /// <param name="said">The schema SAID (Self-Addressing IDentifier)</param>
    /// <returns>Schema entry if found, null otherwise</returns>
    SchemaEntry? GetSchema(string said);

    /// <summary>
    /// Gets the OOBI URLs for a given schema SAID.
    /// </summary>
    /// <param name="said">The schema SAID</param>
    /// <returns>Array of OOBI URLs if found, empty array otherwise</returns>
    string[] GetOobiUrls(string said);

    /// <summary>
    /// Gets all known schema entries.
    /// </summary>
    IEnumerable<SchemaEntry> GetAllSchemas();

    /// <summary>
    /// Gets the default OOBI hosts configured in the manifest.
    /// </summary>
    string[] DefaultOobiHosts { get; }
}

/// <summary>
/// Implementation of schema service that loads from embedded schemas.json manifest.
/// </summary>
public class SchemaService : ISchemaService {
    private readonly Dictionary<string, SchemaEntry> _schemas;
    private readonly string[] _defaultOobiHosts;
    private readonly ILogger<SchemaService> _logger;

    public SchemaService(ILogger<SchemaService> logger) {
        _logger = logger;
        (_schemas, _defaultOobiHosts) = LoadManifest();
    }

    public string[] DefaultOobiHosts => _defaultOobiHosts;

    private (Dictionary<string, SchemaEntry>, string[]) LoadManifest() {
        try {
            var assembly = typeof(SchemaService).Assembly;
            var resourceName = "Extension.Schemas.schemas.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) {
                _logger.LogWarning("Schema manifest resource not found: {ResourceName}", resourceName);
                return (new Dictionary<string, SchemaEntry>(), []);
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var manifest = JsonSerializer.Deserialize<SchemaManifest>(json);
            if (manifest is null) {
                _logger.LogWarning("Failed to deserialize schema manifest");
                return (new Dictionary<string, SchemaEntry>(), []);
            }

            var schemas = manifest.Schemas.ToDictionary(s => s.Said, s => s);
            _logger.LogInformation("Loaded {Count} schema entries from manifest", schemas.Count);

            return (schemas, manifest.DefaultOobiHosts);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error loading schema manifest");
            return (new Dictionary<string, SchemaEntry>(), []);
        }
    }

    public SchemaEntry? GetSchema(string said) {
        if (string.IsNullOrEmpty(said)) {
            return null;
        }

        _schemas.TryGetValue(said, out var entry);
        return entry;
    }

    public string[] GetOobiUrls(string said) {
        var entry = GetSchema(said);
        return entry?.OobiUrls ?? [];
    }

    public IEnumerable<SchemaEntry> GetAllSchemas() => _schemas.Values;
}
