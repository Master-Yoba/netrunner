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

        return ValidateRestorable(value);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json)
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

        return ValidateRestorable(value);
    }

    public static GameState DeserializeState(string json)
    {
        GameState state = Deserialize<GameState>(json);
        try
        {
            StateInvariantValidator.Validate(state);
        }
        catch (StateInvariantException exception)
        {
            throw new JsonException("The state JSON failed invariant validation.", exception);
        }

        return state;
    }

    public static GameSnapshot DeserializeSnapshot(string json)
    {
        GameSnapshot snapshot = Deserialize<GameSnapshot>(json);
        try
        {
            StateInvariantValidator.Validate(snapshot.ToState());
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

    private static T ValidateRestorable<T>(T value)
    {
        try
        {
            switch (value)
            {
                case GameState state:
                    StateInvariantValidator.Validate(state);
                    break;
                case GameSnapshot snapshot:
                    StateInvariantValidator.Validate(snapshot.ToState());
                    break;
                case EngineEvent @event:
                    EngineEventValidator.Validate(@event);
                    break;
                case ReplayArtifact replay:
                    replay.Validate();
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
