using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Common validation for engine-owned identifiers. Identifiers intentionally
/// remain opaque strings: their values are stable in save files and replays and
/// do not depend on a process-local hash code or a random GUID.
/// </summary>
internal static class StableIdentifier
{
    private const int MaximumLength = 128;

    public static string Validate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An engine identifier must not be empty.", parameterName);
        }

        if (value.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"An engine identifier must be at most {MaximumLength} characters.",
                parameterName);
        }

        foreach (char character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                throw new ArgumentException(
                    "An engine identifier must not contain whitespace or control characters.",
                    parameterName);
            }
        }

        return value;
    }

    public static bool TryValidate(string? value)
    {
        try
        {
            _ = Validate(value, nameof(value));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Stable identifier for one match.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct GameId
{
    public GameId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static GameId Parse(string value) => new(value);

    public static bool TryParse(string? value, out GameId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new GameId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for a static card definition/code.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct CardDefinitionId
{
    public CardDefinitionId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static CardDefinitionId Parse(string value) => new(value);

    public static bool TryParse(string? value, out CardDefinitionId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new CardDefinitionId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for one physical card instance in a match.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct CardInstanceId
{
    public CardInstanceId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static CardInstanceId Parse(string value) => new(value);

    public static bool TryParse(string? value, out CardInstanceId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new CardInstanceId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for a player seat.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct PlayerId
{
    public PlayerId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static PlayerId Parse(string value) => new(value);

    public static bool TryParse(string? value, out PlayerId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new PlayerId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for a Corp central or remote server.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct ServerId
{
    public ServerId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static ServerId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ServerId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new ServerId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for a physical card zone.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct ZoneId
{
    public ZoneId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static ZoneId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ZoneId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new ZoneId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for a pending decision.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct DecisionId
{
    public DecisionId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static DecisionId Parse(string value) => new(value);

    public static bool TryParse(string? value, out DecisionId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new DecisionId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for an engine-issued action.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct ActionId
{
    public ActionId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static ActionId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ActionId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new ActionId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for one active run.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct RunId
{
    public RunId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static RunId Parse(string value) => new(value);

    public static bool TryParse(string? value, out RunId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new RunId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identifier for one run encounter.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct EncounterId
{
    public EncounterId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static EncounterId Parse(string value) => new(value);

    public static bool TryParse(string? value, out EncounterId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new EncounterId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identity for one serialized encounter subroutine.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct SubroutineId
{
    public SubroutineId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static SubroutineId Parse(string value) => new(value);

    public static bool TryParse(string? value, out SubroutineId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new SubroutineId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identity for one run-scoped lingering effect.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct RunEffectId
{
    public RunEffectId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static RunEffectId Parse(string value) => new(value);

    public static bool TryParse(string? value, out RunEffectId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new RunEffectId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identity for one committed occurrence snapshot.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct OccurrenceId
{
    public OccurrenceId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static OccurrenceId Parse(string value) => new(value);

    public static bool TryParse(string? value, out OccurrenceId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new OccurrenceId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identity for one registered conditional trigger.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct TriggerId
{
    public TriggerId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static TriggerId Parse(string value) => new(value);

    public static bool TryParse(string? value, out TriggerId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new TriggerId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Stable identity for one registered prevention/replacement.</summary>
[JsonConverter(typeof(StableIdJsonConverterFactory))]
public readonly record struct InterruptId
{
    public InterruptId(string value) => Value = StableIdentifier.Validate(value, nameof(value));

    public string Value { get; }

    public static InterruptId Parse(string value) => new(value);

    public static bool TryParse(string? value, out InterruptId id)
    {
        if (StableIdentifier.TryValidate(value))
        {
            id = new InterruptId(value!);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// The only serialized mutable allocation state used by the engine. The next
/// number is consumed monotonically and can be restored with a save/replay.
/// </summary>
public sealed record IdAllocatorState
{
    [JsonConstructor]
    public IdAllocatorState(ulong nextSequence = 1)
    {
        if (nextSequence == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextSequence), "The next allocation sequence must be nonzero.");
        }

        NextSequence = nextSequence;
    }

    [JsonInclude]
    [JsonRequired]
    public ulong NextSequence { get; private set; }
}

/// <summary>
/// Deterministic typed-ID issuer backed by <see cref="IdAllocatorState"/>.
/// The issuer is intentionally not global; a match owns and serializes its
/// state so two matches cannot affect one another's identities.
/// </summary>
public sealed class StableIdAllocator
{
    private ulong _nextSequence;

    public StableIdAllocator(ulong nextSequence = 1)
        : this(new IdAllocatorState(nextSequence))
    {
    }

    public StableIdAllocator(IdAllocatorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _nextSequence = state.NextSequence;
    }

    public IdAllocatorState State => new(_nextSequence);

    public GameId NextGameId() => new(Issue("game"));

    public CardInstanceId NextCardInstanceId() => new(Issue("card"));

    public PlayerId NextPlayerId() => new(Issue("player"));

    public ServerId NextServerId() => new(Issue("server"));

    public ZoneId NextZoneId() => new(Issue("zone"));

    public DecisionId NextDecisionId() => new(Issue("decision"));

    public ActionId NextActionId() => new(Issue("action"));

    public RunId NextRunId() => new(Issue("run"));

    public OccurrenceId NextOccurrenceId() => new(Issue("occurrence"));

    public TriggerId NextTriggerId() => new(Issue("trigger"));

    public InterruptId NextInterruptId() => new(Issue("interrupt"));

    /// <summary>Issues an identity for a live generic behavior frame.</summary>
    public string NextBehaviorFrameId() => Issue("behavior-frame");

    private string Issue(string prefix) =>
        $"{prefix}:{TakeSequence().ToString(CultureInfo.InvariantCulture)}";

    private ulong TakeSequence()
    {
        ulong value = _nextSequence;
        if (value == ulong.MaxValue)
        {
            throw new InvalidOperationException("The engine identifier allocator is exhausted.");
        }

        _nextSequence++;
        return value;
    }
}

/// <summary>
/// JSON converter factory shared by all stable IDs. IDs are represented as
/// their opaque string value, rather than an implementation-detail object.
/// </summary>
public sealed class StableIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(GameId)
        || typeToConvert == typeof(CardDefinitionId)
        || typeToConvert == typeof(CardInstanceId)
        || typeToConvert == typeof(PlayerId)
        || typeToConvert == typeof(ServerId)
        || typeToConvert == typeof(ZoneId)
        || typeToConvert == typeof(DecisionId)
        || typeToConvert == typeof(ActionId)
        || typeToConvert == typeof(RunId)
        || typeToConvert == typeof(EncounterId)
        || typeToConvert == typeof(SubroutineId)
        || typeToConvert == typeof(RunEffectId)
        || typeToConvert == typeof(OccurrenceId)
        || typeToConvert == typeof(TriggerId)
        || typeToConvert == typeof(InterruptId);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        typeToConvert == typeof(GameId)
            ? new StableIdJsonConverter<GameId>(static value => GameId.Parse(value), static value => value.Value)
            : typeToConvert == typeof(CardDefinitionId)
                ? new StableIdJsonConverter<CardDefinitionId>(static value => CardDefinitionId.Parse(value), static value => value.Value)
                : typeToConvert == typeof(CardInstanceId)
                    ? new StableIdJsonConverter<CardInstanceId>(static value => CardInstanceId.Parse(value), static value => value.Value)
                    : typeToConvert == typeof(PlayerId)
                        ? new StableIdJsonConverter<PlayerId>(static value => PlayerId.Parse(value), static value => value.Value)
                        : typeToConvert == typeof(ServerId)
                            ? new StableIdJsonConverter<ServerId>(static value => ServerId.Parse(value), static value => value.Value)
                            : typeToConvert == typeof(ZoneId)
                                ? new StableIdJsonConverter<ZoneId>(static value => ZoneId.Parse(value), static value => value.Value)
                                : typeToConvert == typeof(DecisionId)
                                    ? new StableIdJsonConverter<DecisionId>(static value => DecisionId.Parse(value), static value => value.Value)
                                    : typeToConvert == typeof(ActionId)
                                        ? new StableIdJsonConverter<ActionId>(static value => ActionId.Parse(value), static value => value.Value)
                                        : typeToConvert == typeof(RunId)
                                            ? new StableIdJsonConverter<RunId>(static value => RunId.Parse(value), static value => value.Value)
                                            : typeToConvert == typeof(EncounterId)
                                                ? new StableIdJsonConverter<EncounterId>(static value => EncounterId.Parse(value), static value => value.Value)
                                                : typeToConvert == typeof(SubroutineId)
                                                    ? new StableIdJsonConverter<SubroutineId>(static value => SubroutineId.Parse(value), static value => value.Value)
                                                    : typeToConvert == typeof(RunEffectId)
                                                        ? new StableIdJsonConverter<RunEffectId>(static value => RunEffectId.Parse(value), static value => value.Value)
                                                        : typeToConvert == typeof(OccurrenceId)
                                                            ? new StableIdJsonConverter<OccurrenceId>(static value => OccurrenceId.Parse(value), static value => value.Value)
                                                            : typeToConvert == typeof(TriggerId)
                                                                ? new StableIdJsonConverter<TriggerId>(static value => TriggerId.Parse(value), static value => value.Value)
                                                                : typeToConvert == typeof(InterruptId)
                                                                    ? new StableIdJsonConverter<InterruptId>(static value => InterruptId.Parse(value), static value => value.Value)
                                                                    : throw new InvalidOperationException($"Unsupported stable ID type '{typeToConvert}'.");

    private sealed class StableIdJsonConverter<T> : JsonConverter<T>
        where T : struct
    {
        private readonly Func<string, T> _parse;
        private readonly Func<T, string> _format;

        public StableIdJsonConverter(Func<string, T> parse, Func<T, string> format)
        {
            _parse = parse;
            _format = format;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected a string for stable ID '{typeToConvert.Name}'.");
            }

            string? value = reader.GetString();
            if (value is null)
            {
                throw new JsonException($"Stable ID '{typeToConvert.Name}' must not be null.");
            }

            try
            {
                return _parse(value);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException($"Invalid stable ID '{value}' for '{typeToConvert.Name}'.", exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(_format(value));
    }
}
