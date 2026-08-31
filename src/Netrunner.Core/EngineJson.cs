using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// The engine's explicit JSON contract. All persisted engine data goes through
/// these options so IDs are strings, enums are names, and unknown fields fail
/// closed instead of silently changing game meaning.
/// </summary>
public static class EngineJson
{
    private static readonly JsonSerializerOptions SerializationOptions = BuildOptions();

    /// <summary>
    /// Returns a copy so callers cannot mutate the process-wide serialization
    /// contract used by <see cref="Serialize{T}(T)"/>.
    /// </summary>
    public static JsonSerializerOptions Options => new(SerializationOptions);

    public static JsonSerializerOptions CreateOptions() => new(SerializationOptions);

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, SerializationOptions);

    public static byte[] SerializeUtf8<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, SerializationOptions);

    public static T Deserialize<T>(string json)
    {
        return Deserialize<T>(json, null);
    }

    public static T Deserialize<T>(string json, IBehaviorCatalog? behaviorCatalog)
    {
        ArgumentNullException.ThrowIfNull(json);
        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializationOptions)
                ?? throw new JsonException($"The JSON payload for '{typeof(T).Name}' was null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new JsonException($"The JSON payload for '{typeof(T).Name}' is malformed: {exception.Message}", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException($"The JSON payload for '{typeof(T).Name}' is incompatible: {exception.Message}", exception);
        }

        return ValidateRestorable(value, behaviorCatalog);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        return Deserialize<T>(utf8Json, null);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json, IBehaviorCatalog? behaviorCatalog)
    {
        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(utf8Json, SerializationOptions)
                ?? throw new JsonException($"The JSON payload for '{typeof(T).Name}' was null.");
        }
        catch (JsonException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new JsonException($"The JSON payload for '{typeof(T).Name}' is malformed: {exception.Message}", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException($"The JSON payload for '{typeof(T).Name}' is incompatible: {exception.Message}", exception);
        }

        return ValidateRestorable(value, behaviorCatalog);
    }

    public static GameState DeserializeState(string json)
    {
        return DeserializeState(json, null);
    }

    public static GameState DeserializeState(string json, IBehaviorCatalog? behaviorCatalog)
    {
        GameState state = Deserialize<GameState>(json, behaviorCatalog);
        try
        {
            StateInvariantValidator.Validate(state, behaviorCatalog);
        }
        catch (StateInvariantException exception)
        {
            throw new JsonException("The state JSON failed invariant validation.", exception);
        }

        return state;
    }

    public static GameSnapshot DeserializeSnapshot(string json)
    {
        return DeserializeSnapshot(json, null);
    }

    public static GameSnapshot DeserializeSnapshot(string json, IBehaviorCatalog? behaviorCatalog)
    {
        GameSnapshot snapshot = Deserialize<GameSnapshot>(json, behaviorCatalog);
        try
        {
            StateInvariantValidator.Validate(snapshot.ToState(), behaviorCatalog);
        }
        catch (StateInvariantException exception)
        {
            throw new JsonException("The snapshot JSON failed invariant validation.", exception);
        }

        return snapshot;
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static T ValidateRestorable<T>(T value, IBehaviorCatalog? behaviorCatalog)
    {
        try
        {
            switch (value)
            {
                case GameState state:
                    StateInvariantValidator.Validate(state, behaviorCatalog);
                    break;
                case GameSnapshot snapshot:
                    StateInvariantValidator.Validate(snapshot.ToState(), behaviorCatalog);
                    break;
                case EngineEvent @event:
                    EngineEventValidator.Validate(@event);
                    break;
                case ReplayArtifact replay:
                    replay.Validate(behaviorCatalog);
                    break;
                case ScenarioArtifact scenario:
                    scenario.Validate();
                    break;
            }
        }
        catch (StateInvariantException exception)
        {
            throw new JsonException($"The JSON payload for '{typeof(T).Name}' failed invariant validation.", exception);
        }

        return value;
    }
}
