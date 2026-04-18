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

    /// <summary>
    /// Gets the raw JSON body of a well-known schema from the embedded resource.
    /// </summary>
    /// <param name="said">The schema SAID</param>
    /// <returns>Raw JSON string if the schema has an embedded body, null otherwise</returns>
    string? GetSchemaBody(string said);
}

/// <summary>
/// Implementation of schema service that loads from embedded schemas.json manifest.
/// </summary>
public class SchemaService : ISchemaService {
    private readonly Dictionary<string, SchemaEntry> _schemas;
    private readonly Dictionary<string, string> _schemaBodies;
    private readonly string[] _defaultOobiHosts;
    private readonly ILogger<SchemaService> _logger;

    public SchemaService(ILogger<SchemaService> logger) {
        _logger = logger;
        (_schemas, _defaultOobiHosts, _schemaBodies) = LoadManifest();
    }

    public string[] DefaultOobiHosts => _defaultOobiHosts;

    private (Dictionary<string, SchemaEntry>, string[], Dictionary<string, string>) LoadManifest() {
        var bodies = new Dictionary<string, string>();
        try {
            var assembly = typeof(SchemaService).Assembly;
            var resourceName = "Extension.Schemas.schemas.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null) {
                _logger.LogWarning(nameof(LoadManifest) + ": Schema manifest resource not found: {ResourceName}", resourceName);
                return (new Dictionary<string, SchemaEntry>(), [], bodies);
            }

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var manifest = JsonSerializer.Deserialize<SchemaManifest>(json);
            if (manifest is null) {
                _logger.LogWarning(nameof(LoadManifest) + ": Failed to deserialize schema manifest");
                return (new Dictionary<string, SchemaEntry>(), [], bodies);
            }

            var schemas = manifest.Schemas.ToDictionary(s => s.Said, s => s);

            // Load embedded schema body files
            foreach (var entry in manifest.Schemas) {
                if (string.IsNullOrEmpty(entry.File)) continue;
                var bodyResourceName = $"Extension.Schemas.{entry.File}";
                using var bodyStream = assembly.GetManifestResourceStream(bodyResourceName);
                if (bodyStream is null) {
                    _logger.LogDebug(nameof(LoadManifest) + ": No embedded body for schema {Said} ({File})", entry.Said, entry.File);
                    continue;
                }
                using var bodyReader = new StreamReader(bodyStream);
                bodies[entry.Said] = bodyReader.ReadToEnd();
            }

            _logger.LogInformation(nameof(LoadManifest) + ": Loaded {Count} schema entries, {BodyCount} embedded bodies from manifest",
                schemas.Count, bodies.Count);

            return (schemas, manifest.DefaultOobiHosts, bodies);
        }
        catch (Exception ex) {
            _logger.LogError(ex, nameof(LoadManifest) + ": Error loading schema manifest");
            return (new Dictionary<string, SchemaEntry>(), [], bodies);
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

    public IEnumerable<SchemaEntry> GetAllSchemas() => _schemas.Values.ToList();

    public string? GetSchemaBody(string said) {
        if (string.IsNullOrEmpty(said)) return null;
        _schemaBodies.TryGetValue(said, out var body);
        return body;
    }
}
