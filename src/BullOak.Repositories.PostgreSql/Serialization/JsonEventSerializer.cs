namespace BullOak.Repositories.PostgreSql.Serialization;

using System.Text.Json;

/// <summary>
/// Default event serializer using System.Text.Json.
/// Stores assembly-qualified type names for safe round-tripping.
/// </summary>
public class JsonEventSerializer : IEventSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonEventSerializer()
        : this(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        })
    {
    }

    public JsonEventSerializer(JsonSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Serialize(object @event, Type eventType)
    {
        return JsonSerializer.Serialize(@event, eventType, _options);
    }

    public object Deserialize(string json, Type eventType)
    {
        return JsonSerializer.Deserialize(json, eventType, _options)
               ?? throw new InvalidOperationException($"Deserialization of {eventType.Name} returned null.");
    }

    public string GetTypeName(Type eventType)
    {
        return eventType.AssemblyQualifiedName
               ?? throw new InvalidOperationException($"Type {eventType.Name} has no assembly-qualified name.");
    }

    public Type? GetTypeFromName(string typeName)
    {
        return Type.GetType(typeName);
    }
}
