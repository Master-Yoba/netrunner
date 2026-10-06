using System.Security.Cryptography;
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

    public static string Serialize<T>(T value)
    {
        ThrowIfSimulated(value);
        return JsonSerializer.Serialize(value, SerializationOptions);
    }

    public static byte[] SerializeUtf8<T>(T value)
    {
        ThrowIfSimulated(value);
        return JsonSerializer.SerializeToUtf8Bytes(value, SerializationOptions);
    }

    /// <summary>
    /// A fingerprint of a state, simulated or not, for a search's memo of positions it has already valued (bot plan S1.5):
    /// equal states have equal fingerprints. It is not a saved form or an authoritative hash and never leaves the process.
    /// </summary>
    public static string SearchFingerprint(GameState state) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state, SerializationOptions)));

    /// <summary>Simulated states never reach saves, replays or hashes.</summary>
    private static void ThrowIfSimulated<T>(T value)
    {
        if (value is GameState state) GameEngine.ThrowIfSimulated(state);
    }

    public static T Deserialize<T>(string json)
    {
        return Deserialize<T>(json, null);
    }

    public static T Deserialize<T>(string json, IBehaviorCatalog? behaviorCatalog)
    {
        return Deserialize<T>(json, behaviorCatalog, null);
    }

    /// <summary>
    /// Deserializes and validates against the explicitly supplied executable
    /// behavior and printed-card metadata catalogs. Deserialization and both
    /// catalog checks complete as one fail-closed boundary.
    /// </summary>
    public static T Deserialize<T>(
        string json,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
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

        return ValidateRestorable(value, behaviorCatalog, runtimeCardCatalog);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        return Deserialize<T>(utf8Json, null);
    }

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json, IBehaviorCatalog? behaviorCatalog)
    {
        return Deserialize<T>(utf8Json, behaviorCatalog, null);
    }

    /// <summary>UTF-8 counterpart to the dual-catalog string overload.</summary>
    public static T Deserialize<T>(
        ReadOnlySpan<byte> utf8Json,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
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

        return ValidateRestorable(value, behaviorCatalog, runtimeCardCatalog);
    }

    public static GameState DeserializeState(string json)
    {
        return DeserializeState(json, null);
    }

    public static GameState DeserializeState(string json, IBehaviorCatalog? behaviorCatalog)
    {
        return DeserializeState(json, behaviorCatalog, null);
    }

    /// <summary>
    /// Restores a state only when both injected catalogs agree with the pins
    /// embedded in the payload. No partially restored state is returned.
    /// </summary>
    public static GameState DeserializeState(
        string json,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        // Deserialize validates the restored state against both catalogs (ValidateRestorable), so the next
        // authoritative action needn't validate it again as its input.
        GameState state = Deserialize<GameState>(json, behaviorCatalog, runtimeCardCatalog);
        state.MarkValidated(behaviorCatalog, runtimeCardCatalog);

        return state;
    }

    public static GameSnapshot DeserializeSnapshot(string json)
    {
        return DeserializeSnapshot(json, null);
    }

    public static GameSnapshot DeserializeSnapshot(string json, IBehaviorCatalog? behaviorCatalog)
    {
        return DeserializeSnapshot(json, behaviorCatalog, null);
    }

    /// <summary>Dual-catalog counterpart for serialized snapshots.</summary>
    public static GameSnapshot DeserializeSnapshot(
        string json,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        // Deserialize already validated the snapshot's state against both catalogs (ValidateRestorable).
        return Deserialize<GameSnapshot>(json, behaviorCatalog, runtimeCardCatalog);
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
        options.Converters.Add(new BehaviorInstructionKindJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static T ValidateRestorable<T>(
        T value,
        IBehaviorCatalog? behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        try
        {
            switch (value)
            {
                case GameState state:
                    StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCardCatalog);
                    break;
                case GameSnapshot snapshot:
                    StateInvariantValidator.Validate(snapshot.ToState(), behaviorCatalog, runtimeCardCatalog);
                    break;
                case EngineEvent @event:
                    EngineEventValidator.Validate(@event);
                    break;
                case ReplayArtifact replay:
                    replay.Validate(behaviorCatalog, runtimeCardCatalog);
                    break;
                case ScenarioArtifact scenario:
                    scenario.Validate();
                    break;
                case TurnScenarioArtifact turnScenario:
                    turnScenario.Validate();
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
