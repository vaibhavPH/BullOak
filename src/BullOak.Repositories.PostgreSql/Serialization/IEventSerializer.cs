namespace BullOak.Repositories.PostgreSql.Serialization;

/// <summary>
/// Defines how events are serialized/deserialized for PostgreSQL storage.
/// Implement this interface to use a custom serialization strategy (e.g., Newtonsoft.Json, MessagePack).
/// </summary>
public interface IEventSerializer
{
    /// <summary>Serialize an event to a JSON string for JSONB storage.</summary>
    string Serialize(object @event, Type eventType);

    /// <summary>Deserialize a JSON string back to an event object.</summary>
    object Deserialize(string json, Type eventType);

    /// <summary>Get a storable type name for the event type.</summary>
    string GetTypeName(Type eventType);

    /// <summary>Resolve a type from its stored name.</summary>
    Type? GetTypeFromName(string typeName);
}
