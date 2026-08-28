using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public static class EngineEventKinds
{
    public const string CreditsAdjusted = "credits-adjusted";
    public const string TurnStarted = "turn-started";
    public const string TurnEnded = "turn-ended";
    public const string ClickSpent = "click-spent";
    public const string CardMoved = "card-moved";
    public const string CardDrawn = "card-drawn";
    public const string CardInstalled = "card-installed";
    public const string AgendaAdvanced = "agenda-advanced";
    public const string AgendaScored = "agenda-scored";
    public const string AgendaStolen = "agenda-stolen";
    public const string GameWon = "game-won";
}

/// <summary>
/// Typed diagnostic/presentation payloads. Events are never replay inputs and
/// contain no dictionaries, object values, or executable references.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreditsAdjustedEventPayload), "credits-adjusted")]
[JsonDerivedType(typeof(TurnStartedEventPayload), "turn-started")]
[JsonDerivedType(typeof(TurnEndedEventPayload), "turn-ended")]
[JsonDerivedType(typeof(ClickSpentEventPayload), "click-spent")]
[JsonDerivedType(typeof(CardMovedEventPayload), "card-moved")]
[JsonDerivedType(typeof(CardDrawnEventPayload), "card-drawn")]
[JsonDerivedType(typeof(CardInstalledEventPayload), "card-installed")]
[JsonDerivedType(typeof(AgendaAdvancedEventPayload), "agenda-advanced")]
[JsonDerivedType(typeof(AgendaScoredEventPayload), "agenda-scored")]
[JsonDerivedType(typeof(AgendaStolenEventPayload), "agenda-stolen")]
[JsonDerivedType(typeof(GameWonEventPayload), "game-won")]
public abstract record EngineEventPayload
{
    protected EngineEventPayload(int version) => Version = version;

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }
}

public sealed record CreditsAdjustedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CreditsAdjustedEventPayload(int version, PlayerId target, int delta, int creditsAfter)
        : base(version)
    {
        Target = target;
        Delta = delta;
        CreditsAfter = creditsAfter;
    }

    public CreditsAdjustedEventPayload(PlayerId target, int delta, int creditsAfter)
        : this(1, target, delta, creditsAfter)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditsAfter { get; private set; }
}

public sealed record TurnStartedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public TurnStartedEventPayload(int version, PlayerSide activeSide, int turnNumber, int clicks)
        : base(version)
    {
        ActiveSide = activeSide;
        TurnNumber = turnNumber;
        Clicks = clicks;
    }

    public TurnStartedEventPayload(PlayerSide activeSide, int turnNumber, int clicks)
        : this(1, activeSide, turnNumber, clicks)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide ActiveSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Clicks { get; private set; }
}

public sealed record TurnEndedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public TurnEndedEventPayload(int version, PlayerSide side, int turnNumber)
        : base(version)
    {
        Side = side;
        TurnNumber = turnNumber;
    }

    public TurnEndedEventPayload(PlayerSide side, int turnNumber)
        : this(1, side, turnNumber)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }
}

public sealed record ClickSpentEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public ClickSpentEventPayload(int version, PlayerSide side, int clicksRemaining)
        : base(version)
    {
        Side = side;
        ClicksRemaining = clicksRemaining;
    }

    public ClickSpentEventPayload(PlayerSide side, int clicksRemaining)
        : this(1, side, clicksRemaining)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ClicksRemaining { get; private set; }
}

public sealed record CardMovedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardMovedEventPayload(int version, CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : base(version)
    {
        CardId = cardId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
    }

    public CardMovedEventPayload(CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : this(1, cardId, fromZoneId, toZoneId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId FromZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ToZoneId { get; private set; }
}

public sealed record CardDrawnEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardDrawnEventPayload(int version, PlayerSide side, CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : base(version)
    {
        Side = side;
        CardId = cardId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
    }

    public CardDrawnEventPayload(PlayerSide side, CardInstanceId cardId, ZoneId fromZoneId, ZoneId toZoneId)
        : this(1, side, cardId, fromZoneId, toZoneId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId FromZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ToZoneId { get; private set; }
}

public sealed record CardInstalledEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public CardInstalledEventPayload(
        int version,
        PlayerSide side,
        CardInstanceId cardId,
        ZoneId fromZoneId,
        ZoneId toZoneId,
        int cost)
        : base(version)
    {
        Side = side;
        CardId = cardId;
        FromZoneId = fromZoneId;
        ToZoneId = toZoneId;
        Cost = cost;
    }

    public CardInstalledEventPayload(
        PlayerSide side,
        CardInstanceId cardId,
        ZoneId fromZoneId,
        ZoneId toZoneId,
        int cost)
        : this(1, side, cardId, fromZoneId, toZoneId, cost)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId FromZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId ToZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Cost { get; private set; }
}

public sealed record AgendaAdvancedEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AgendaAdvancedEventPayload(int version, CardInstanceId agendaId, int advancementCounters, int requirement)
        : base(version)
    {
        AgendaId = agendaId;
        AdvancementCounters = advancementCounters;
        Requirement = requirement;
    }

    public AgendaAdvancedEventPayload(CardInstanceId agendaId, int advancementCounters, int requirement)
        : this(1, agendaId, advancementCounters, requirement)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId AgendaId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AdvancementCounters { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Requirement { get; private set; }
}

public sealed record AgendaScoredEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AgendaScoredEventPayload(int version, PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : base(version)
    {
        Winner = winner;
        AgendaId = agendaId;
        AgendaPoints = agendaPoints;
    }

    public AgendaScoredEventPayload(PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : this(1, winner, agendaId, agendaPoints)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId AgendaId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }
}

public sealed record AgendaStolenEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public AgendaStolenEventPayload(int version, PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : base(version)
    {
        Winner = winner;
        AgendaId = agendaId;
        AgendaPoints = agendaPoints;
    }

    public AgendaStolenEventPayload(PlayerSide winner, CardInstanceId agendaId, int agendaPoints)
        : this(1, winner, agendaId, agendaPoints)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId AgendaId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int AgendaPoints { get; private set; }
}

public sealed record GameWonEventPayload : EngineEventPayload
{
    [JsonConstructor]
    public GameWonEventPayload(int version, PlayerSide winner, GameWinReason reason)
        : base(version)
    {
        Winner = winner;
        Reason = reason;
    }

    public GameWonEventPayload(PlayerSide winner, GameWinReason reason)
        : this(1, winner, reason)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Winner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public GameWinReason Reason { get; private set; }
}

/// <summary>Versioned typed event envelope emitted by deterministic Core.</summary>
public sealed record EngineEvent
{
    [JsonConstructor]
    public EngineEvent(int version, long sequence, string kind, EngineEventPayload payload)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(payload);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Event envelope version must be positive.");
        }

        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Event sequence must be positive.");
        }

        Version = version;
        Sequence = sequence;
        Kind = kind;
        Payload = payload;
    }

    public EngineEvent(long sequence, string kind, EngineEventPayload payload)
        : this(1, sequence, kind, payload)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonIgnore]
    public int EventVersion => Version;

    [JsonInclude]
    [JsonRequired]
    public long Sequence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EngineEventPayload Payload { get; private set; }

    internal EngineEvent Copy() => Payload switch
    {
        CreditsAdjustedEventPayload value => new EngineEvent(Version, Sequence, Kind, new CreditsAdjustedEventPayload(value.Version, value.Target, value.Delta, value.CreditsAfter)),
        TurnStartedEventPayload value => new EngineEvent(Version, Sequence, Kind, new TurnStartedEventPayload(value.Version, value.ActiveSide, value.TurnNumber, value.Clicks)),
        TurnEndedEventPayload value => new EngineEvent(Version, Sequence, Kind, new TurnEndedEventPayload(value.Version, value.Side, value.TurnNumber)),
        ClickSpentEventPayload value => new EngineEvent(Version, Sequence, Kind, new ClickSpentEventPayload(value.Version, value.Side, value.ClicksRemaining)),
        CardMovedEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardMovedEventPayload(value.Version, value.CardId, value.FromZoneId, value.ToZoneId)),
        CardDrawnEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardDrawnEventPayload(value.Version, value.Side, value.CardId, value.FromZoneId, value.ToZoneId)),
        CardInstalledEventPayload value => new EngineEvent(Version, Sequence, Kind, new CardInstalledEventPayload(value.Version, value.Side, value.CardId, value.FromZoneId, value.ToZoneId, value.Cost)),
        AgendaAdvancedEventPayload value => new EngineEvent(Version, Sequence, Kind, new AgendaAdvancedEventPayload(value.Version, value.AgendaId, value.AdvancementCounters, value.Requirement)),
        AgendaScoredEventPayload value => new EngineEvent(Version, Sequence, Kind, new AgendaScoredEventPayload(value.Version, value.Winner, value.AgendaId, value.AgendaPoints)),
        AgendaStolenEventPayload value => new EngineEvent(Version, Sequence, Kind, new AgendaStolenEventPayload(value.Version, value.Winner, value.AgendaId, value.AgendaPoints)),
        GameWonEventPayload value => new EngineEvent(Version, Sequence, Kind, new GameWonEventPayload(value.Version, value.Winner, value.Reason)),
        _ => throw new InvalidOperationException($"No immutable clone is registered for event payload '{Payload.GetType().Name}'."),
    };
}

public static class EngineEventValidator
{
    public static IReadOnlyList<StateInvariantViolation> Collect(EngineEvent? @event, string path = "event")
    {
        var violations = new List<StateInvariantViolation>();
        if (@event is null)
        {
            violations.Add(new StateInvariantViolation("event.null", path, "Engine events must not be null."));
            return violations.AsReadOnly();
        }

        if (@event.Version != 1)
        {
            Add(violations, "event.unsupported-version", $"{path}.version", $"Event envelope version {@event.Version} is not supported; expected 1.");
        }

        if (@event.Sequence <= 0)
        {
            Add(violations, "event.invalid-sequence", $"{path}.sequence", "Event sequence must be positive.");
        }

        if (!StableIdentifier.TryValidate(@event.Kind))
        {
            Add(violations, "event.invalid-kind", $"{path}.kind", "Event kind must be non-empty and contain no whitespace.");
        }

        if (@event.Payload is null)
        {
            Add(violations, "event.missing-payload", $"{path}.payload", "Engine events must contain a typed payload.");
            return violations.AsReadOnly();
        }

        if (@event.Payload.Version != 1)
        {
            Add(violations, "event.payload-unsupported-version", $"{path}.payload.version", $"Event payload version {@event.Payload.Version} is not supported; expected 1.");
        }

        switch (@event.Payload)
        {
            case CreditsAdjustedEventPayload value:
                RequireKind(@event, EngineEventKinds.CreditsAdjusted, path, violations);
                RequireId(value.Target.Value, $"{path}.payload.target", "player", violations);
                if (value.CreditsAfter < 0)
                {
                    Add(violations, "event.negative-credits", $"{path}.payload.creditsAfter", "Credits after an adjustment must not be negative.");
                }

                break;
            case TurnStartedEventPayload value:
                RequireKind(@event, EngineEventKinds.TurnStarted, path, violations);
                RequireSide(value.ActiveSide, $"{path}.payload.activeSide", violations);
                RequireNumber(value.TurnNumber, $"{path}.payload.turnNumber", "Turn number", violations, strictlyPositive: true);
                RequireNumber(value.Clicks, $"{path}.payload.clicks", "Clicks", violations);
                break;
            case TurnEndedEventPayload value:
                RequireKind(@event, EngineEventKinds.TurnEnded, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireNumber(value.TurnNumber, $"{path}.payload.turnNumber", "Turn number", violations, strictlyPositive: true);
                break;
            case ClickSpentEventPayload value:
                RequireKind(@event, EngineEventKinds.ClickSpent, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireNumber(value.ClicksRemaining, $"{path}.payload.clicksRemaining", "Remaining clicks", violations);
                break;
            case CardMovedEventPayload value:
                RequireKind(@event, EngineEventKinds.CardMoved, path, violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.FromZoneId.Value, $"{path}.payload.fromZoneId", "zone", violations);
                RequireId(value.ToZoneId.Value, $"{path}.payload.toZoneId", "zone", violations);
                if (value.FromZoneId == value.ToZoneId)
                {
                    Add(violations, "event.same-zone-move", $"{path}.payload", "A card move must change zones.");
                }

                break;
            case CardDrawnEventPayload value:
                RequireKind(@event, EngineEventKinds.CardDrawn, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.FromZoneId.Value, $"{path}.payload.fromZoneId", "zone", violations);
                RequireId(value.ToZoneId.Value, $"{path}.payload.toZoneId", "zone", violations);
                break;
            case CardInstalledEventPayload value:
                RequireKind(@event, EngineEventKinds.CardInstalled, path, violations);
                RequireSide(value.Side, $"{path}.payload.side", violations);
                RequireId(value.CardId.Value, $"{path}.payload.cardId", "card", violations);
                RequireId(value.FromZoneId.Value, $"{path}.payload.fromZoneId", "zone", violations);
                RequireId(value.ToZoneId.Value, $"{path}.payload.toZoneId", "zone", violations);
                RequireNumber(value.Cost, $"{path}.payload.cost", "Install cost", violations);
                break;
            case AgendaAdvancedEventPayload value:
                RequireKind(@event, EngineEventKinds.AgendaAdvanced, path, violations);
                RequireId(value.AgendaId.Value, $"{path}.payload.agendaId", "card", violations);
                RequireNumber(value.AdvancementCounters, $"{path}.payload.advancementCounters", "Advancement counters", violations);
                RequireNumber(value.Requirement, $"{path}.payload.requirement", "Advancement requirement", violations, strictlyPositive: true);
                break;
            case AgendaScoredEventPayload value:
                RequireKind(@event, EngineEventKinds.AgendaScored, path, violations);
                RequireSide(value.Winner, $"{path}.payload.winner", violations);
                RequireId(value.AgendaId.Value, $"{path}.payload.agendaId", "card", violations);
                RequireNumber(value.AgendaPoints, $"{path}.payload.agendaPoints", "Agenda points", violations, strictlyPositive: true);
                break;
            case AgendaStolenEventPayload value:
                RequireKind(@event, EngineEventKinds.AgendaStolen, path, violations);
                RequireSide(value.Winner, $"{path}.payload.winner", violations);
                RequireId(value.AgendaId.Value, $"{path}.payload.agendaId", "card", violations);
                RequireNumber(value.AgendaPoints, $"{path}.payload.agendaPoints", "Agenda points", violations, strictlyPositive: true);
                break;
            case GameWonEventPayload value:
                RequireKind(@event, EngineEventKinds.GameWon, path, violations);
                RequireSide(value.Winner, $"{path}.payload.winner", violations);
                if (!Enum.IsDefined(value.Reason))
                {
                    Add(violations, "event.invalid-reason", $"{path}.payload.reason", $"'{value.Reason}' is not a supported win reason.");
                }

                break;
            default:
                Add(violations, "event.unknown-payload", $"{path}.payload", $"Event payload type '{@event.Payload.GetType().Name}' is not registered.");
                break;
        }

        return violations.AsReadOnly();
    }

    public static void Validate(EngineEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        IReadOnlyList<StateInvariantViolation> violations = Collect(@event);
        if (violations.Count > 0)
        {
            throw new StateInvariantException(violations);
        }
    }

    public static IReadOnlyList<EngineEvent> FreezeBatch(IEnumerable<EngineEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        EngineEvent[] copy = events.Select(static @event =>
        {
            ArgumentNullException.ThrowIfNull(@event);
            Validate(@event);
            return @event.Copy();
        }).ToArray();

        long previous = 0;
        foreach (EngineEvent @event in copy)
        {
            if (@event.Sequence <= previous)
            {
                throw new StateInvariantException([new StateInvariantViolation(
                    "event.nonmonotonic-sequence",
                    "events",
                    "Event sequences must be strictly increasing within a batch.")]);
            }

            previous = @event.Sequence;
        }

        return new ReadOnlyCollection<EngineEvent>(copy);
    }

    private static void RequireKind(EngineEvent @event, string expected, string path, List<StateInvariantViolation> violations)
    {
        if (!string.Equals(@event.Kind, expected, StringComparison.Ordinal))
        {
            Add(violations, "event.kind-payload-mismatch", $"{path}.kind", $"Payload requires event kind '{expected}'.");
        }
    }

    private static void RequireId(string? value, string path, string label, List<StateInvariantViolation> violations)
    {
        if (!StableIdentifier.TryValidate(value))
        {
            Add(violations, "event.invalid-id", path, $"Event {label} ID must be non-empty and valid.");
        }
    }

    private static void RequireSide(PlayerSide side, string path, List<StateInvariantViolation> violations)
    {
        if (side is not (PlayerSide.Corp or PlayerSide.Runner))
        {
            Add(violations, "event.invalid-side", path, $"'{side}' is not a valid player side.");
        }
    }

    private static void RequireNumber(
        int value,
        string path,
        string label,
        List<StateInvariantViolation> violations,
        bool strictlyPositive = false)
    {
        if (value < 0 || strictlyPositive && value == 0)
        {
            Add(violations, "event.invalid-number", path, $"{label} must be {(strictlyPositive ? "positive" : "nonnegative")}.");
        }
    }

    private static void Add(List<StateInvariantViolation> violations, string code, string path, string message) =>
        violations.Add(new StateInvariantViolation(code, path, message));
}
