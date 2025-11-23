using System.Text.Json;
using System.Text.Json.Serialization;

var options = new JsonSerializerOptions {
    PropertyNameCaseInsensitive = true,
    IncludeFields = true,
    PropertyNamingPolicy = null, // Preserve PascalCase from C# properties
};

record StorageChange<T>(
    [property: JsonPropertyName("newValue")] T? NewValue,
    [property: JsonPropertyName("oldValue")] T? OldValue
);

record PasscodeModel {
    public required string Passcode { get; init; }
    public required DateTime SessionExpirationUtc { get; init; }
}

var initialPasscode = new PasscodeModel {
    Passcode = "first-value",
    SessionExpirationUtc = DateTime.UtcNow.AddMinutes(5)
};
var change = new StorageChange<PasscodeModel>(initialPasscode, null);

var json = JsonSerializer.Serialize(change, options);
Console.WriteLine("Serialized JSON:");
Console.WriteLine(json);

var parsed = JsonDocument.Parse(json);
Console.WriteLine("\nParsed properties:");
foreach (var prop in parsed.RootElement.EnumerateObject()) {
    Console.WriteLine($"  {prop.Name}: {prop.Value}");
}

Console.WriteLine("\nHas newValue property: " + parsed.RootElement.TryGetProperty("newValue", out _));
Console.WriteLine("Has oldValue property: " + parsed.RootElement.TryGetProperty("oldValue", out _));
