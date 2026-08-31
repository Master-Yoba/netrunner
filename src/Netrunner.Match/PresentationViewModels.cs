using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Typed presentation projection for one player-filtered observation.  The
/// mapper accepts an <see cref="Observation"/> only; it has no path to
/// authoritative <see cref="GameState"/> and therefore cannot accidentally
/// render hidden cards.
/// </summary>
public sealed record MatchViewModel(
    PlayerSide Viewer,
    PlayerViewModel Corp,
    PlayerViewModel Runner,
    IReadOnlyList<ServerViewModel> Servers,
    ZoneViewModel CorpHand,
    ZoneViewModel CorpDeck,
    ZoneViewModel RunnerHand,
    ZoneViewModel RunnerDeck,
    IReadOnlyList<CardViewModel> RunnerRig,
    IReadOnlyList<CardViewModel> CorpScore,
    IReadOnlyList<CardViewModel> RunnerScore,
    DecisionViewModel? Decision,
    RunFocusViewModel? Run,
    OutcomeViewModel Outcome,
    IReadOnlyList<LegalActionViewModel> LegalActions)
{
    public IReadOnlyList<LegalActionViewModel> Actions => LegalActions;

    public IReadOnlyList<CardViewModel> RunnerHandCards => RunnerHand.VisibleCards;

    public IReadOnlyList<CardViewModel> CorpHandCards => CorpHand.VisibleCards;
}

/// <summary>Resource and score values visible for one player.</summary>
public sealed record PlayerViewModel(
    string Id,
    PlayerSide Side,
    int Credits,
    int Clicks,
    int AgendaPoints);

/// <summary>A hand/deck projection: count is public, cards are owner-visible only.</summary>
public sealed record ZoneViewModel(
    ZoneKind Kind,
    int Count,
    IReadOnlyList<CardViewModel> VisibleCards);

/// <summary>Ordered ICE (outermost-to-innermost) and server root projection.</summary>
public sealed record ServerViewModel(
    string Id,
    ServerKind Kind,
    IReadOnlyList<CardViewModel?> Ice,
    IReadOnlyList<CardViewModel?> Root)
{
    public IReadOnlyList<CardViewModel?> OrderedIce => Ice;

    public IReadOnlyList<CardViewModel?> OrderedRoot => Root;
}

/// <summary>One known card in a player-filtered observation.</summary>
public sealed record CardViewModel(
    string Id,
    string? DefinitionId,
    string Title,
    string Type,
    bool IsKnown,
    bool Rezzed,
    int AdvancementCounters,
    int? Cost,
    int? Strength,
    int? AgendaPoints,
    int? TrashCost,
    IReadOnlyList<string> Subtypes,
    string RulesText)
{
    public IReadOnlyList<string> Counters => AdvancementCounters > 0
        ? new[] { $"Advancement ×{AdvancementCounters}" }
        : Array.Empty<string>();
}

/// <summary>Current decision prompt and the legal actions issued to this viewer.</summary>
public sealed record DecisionViewModel(
    string Id,
    long Revision,
    PlayerId Owner,
    PlayerSide OwnerSide,
    DecisionKind Kind,
    string Prompt);

/// <summary>One legal action, retaining Core's typed parameters for target UI.</summary>
public sealed record LegalActionViewModel(
    ActionId Id,
    ActionKind Kind,
    string Description,
    ActionParameters Parameters);

/// <summary>Run focus, current ICE, encounter, and breach/access state.</summary>
public sealed record RunFocusViewModel(
    string Id,
    string AttackedServerId,
    PlayerSide InitiatingSide,
    RunCrPhase Phase,
    RunTimingStep Step,
    int CurrentIceIndex,
    CardViewModel? CurrentIce,
    bool Successful,
    bool Unsuccessful,
    RunEndReason? EndReason,
    RunPriorityViewModel? Priority,
    EncounterViewModel? Encounter,
    AccessViewModel Access,
    int RunCredits,
    CardViewModel? InitiatingSource)
{
    public string FocusLabel => Encounter is not null
        ? $"Encounter · {Encounter.Ice?.Title ?? "Unknown ICE"}"
        : Access.State != RunAccessState.NotStarted
            ? $"Access · {Access.State}"
            : $"Run · {Phase}";
}

public sealed record RunPriorityViewModel(
    RunPriorityWindowKind Window,
    PlayerSide Holder,
    int ConsecutivePasses,
    bool CorpMayRezApproachedIce,
    bool CorpMayRezNonIce);

public sealed record EncounterViewModel(
    string Id,
    CardViewModel? Ice,
    RunEncounterStatus Status,
    IReadOnlyList<SubroutineViewModel> Subroutines)
{
    /// <summary>
    /// Effective strength of the visible encountered ICE for the attacked
    /// server. This is encounter-specific presentation data; the ICE card's
    /// printed <see cref="CardViewModel.Strength"/> remains unchanged.
    /// </summary>
    public int? EffectiveStrength { get; init; }

    public int RemainingSubroutines => Subroutines.Count(value => !value.Broken && !value.Resolved);
}

public sealed record SubroutineViewModel(
    int Ordinal,
    bool Broken,
    bool Resolved);

public sealed record AccessViewModel(
    IReadOnlyList<CardViewModel?> Candidates,
    int RandomAccessLimit,
    BreachHiddenCandidateSource? HiddenCandidateSource,
    IReadOnlyList<CardViewModel?> Chosen,
    IReadOnlyList<CardViewModel?> Accessed,
    RunAccessState State);

public sealed record OutcomeViewModel(
    GameOutcomeKind Kind,
    PlayerSide? Winner,
    GameWinReason? Reason)
{
    public bool IsTerminal => Kind == GameOutcomeKind.Won;
}

/// <summary>
/// Presentation-only summary of the representative run's terminal event.
/// This is intentionally derived from the event payload, not from
/// <see cref="GameState"/>; it contains no authoritative state, hash, RNG, or
/// hidden-card data and lets a client keep a clear slice-complete screen after
/// Core has cleaned up the live run.
/// </summary>
public sealed record RunOutcomeViewModel(
    string RunId,
    PlayerSide InitiatingSide,
    RunEndReason Reason,
    bool Successful)
{
    public string Summary => Successful
        ? $"Run complete · breach succeeded · {Reason}"
        : $"Run complete · {Reason}";
}

/// <summary>Maps filtered Core observations to immutable typed UI data.</summary>
public static class ObservationViewMapper
{
    public static MatchViewModel Build(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        IReadOnlyDictionary<string, object?> publicState = observation.PublicState;
        IReadOnlyDictionary<string, object?> privateState = observation.PrivateState;
        IReadOnlyDictionary<string, object?> players = RequiredMap(publicState, "players");
        PlayerViewModel corp = MapPlayer(RequiredMap(players, "corp"), PlayerSide.Corp);
        PlayerViewModel runner = MapPlayer(RequiredMap(players, "runner"), PlayerSide.Runner);
        IReadOnlyDictionary<string, object?> hidden = RequiredMap(publicState, "hiddenZoneCounts");

        ZoneViewModel corpHand = MapZone(
            ZoneKind.CorpHand,
            ReadInt(hidden, "corpHand"),
            observation.Viewer == PlayerSide.Corp ? ReadCards(privateState, "hand") : Array.Empty<CardViewModel>());
        ZoneViewModel corpDeck = MapZone(
            ZoneKind.CorpDeck,
            ReadInt(hidden, "corpDeck"),
            observation.Viewer == PlayerSide.Corp ? ReadCards(privateState, "deck") : Array.Empty<CardViewModel>());
        ZoneViewModel runnerHand = MapZone(
            ZoneKind.RunnerGrip,
            ReadInt(hidden, "runnerGrip"),
            observation.Viewer == PlayerSide.Runner ? ReadCards(privateState, "grip") : Array.Empty<CardViewModel>());
        ZoneViewModel runnerDeck = MapZone(
            ZoneKind.RunnerStack,
            ReadInt(hidden, "runnerStack"),
            observation.Viewer == PlayerSide.Runner ? ReadCards(privateState, "stack") : Array.Empty<CardViewModel>());

        IReadOnlyList<ServerViewModel> servers = ReadList(publicState, "servers")
            .Select(MapServer)
            .ToArray();
        IReadOnlyList<CardViewModel> runnerRig = ReadCards(publicState, "runnerRig");
        IReadOnlyList<CardViewModel> corpScore = ReadCards(publicState, "corpScore");
        IReadOnlyList<CardViewModel> runnerScore = ReadCards(publicState, "runnerScore");
        DecisionViewModel? decision = MapDecision(observation);
        RunFocusViewModel? run = MapRun(publicState);
        OutcomeViewModel outcome = MapOutcome(RequiredMap(publicState, "outcome"));
        IReadOnlyList<LegalActionViewModel> actions = observation.LegalActions
            .Select(action => new LegalActionViewModel(action.Id, action.Kind, action.Description, action.Parameters))
            .ToArray();

        return new MatchViewModel(
            observation.Viewer,
            corp,
            runner,
            new ReadOnlyCollection<ServerViewModel>(servers.ToArray()),
            corpHand,
            corpDeck,
            runnerHand,
            runnerDeck,
            new ReadOnlyCollection<CardViewModel>(runnerRig.ToArray()),
            new ReadOnlyCollection<CardViewModel>(corpScore.ToArray()),
            new ReadOnlyCollection<CardViewModel>(runnerScore.ToArray()),
            decision,
            run,
            outcome,
            new ReadOnlyCollection<LegalActionViewModel>(actions.ToArray()));
    }

    /// <summary>Convenience alias used by presentation integrations.</summary>
    public static MatchViewModel FromObservation(Observation observation) => Build(observation);

    private static DecisionViewModel? MapDecision(Observation observation)
    {
        ObservationDecision? decision = observation.Decision;
        return decision is null
            ? null
            : new DecisionViewModel(
                decision.Id.Value,
                decision.Revision,
                decision.Owner,
                decision.OwnerSide,
                decision.Kind,
                decision.Prompt);
    }

    private static PlayerViewModel MapPlayer(IReadOnlyDictionary<string, object?> map, PlayerSide side) =>
        new(
            ReadString(map, "id"),
            side,
            ReadInt(map, "credits"),
            ReadInt(map, "clicks"),
            ReadInt(map, "agendaPoints"));

    private static ZoneViewModel MapZone(ZoneKind kind, int count, IReadOnlyList<CardViewModel> visibleCards) =>
        new(kind, count, new ReadOnlyCollection<CardViewModel>(visibleCards.ToArray()));

    private static ServerViewModel MapServer(object? value)
    {
        IReadOnlyDictionary<string, object?> map = RequiredMap(value, "server");
        // Core stores server ICE from innermost to outermost (the active
        // approach index starts at Count - 1). Presentation uses the spatial
        // order promised by ServerViewModel: outermost to innermost.
        IReadOnlyList<CardViewModel?> orderedIce = ReadNullableCards(map, "ice")
            .Reverse()
            .ToArray();
        return new ServerViewModel(
            ReadString(map, "id"),
            ReadEnum<ServerKind>(map, "kind"),
            orderedIce,
            ReadNullableCards(map, "root"));
    }

    private static RunFocusViewModel? MapRun(IReadOnlyDictionary<string, object?> publicState)
    {
        if (!publicState.TryGetValue("run", out object? rawRun) || rawRun is null)
        {
            return null;
        }

        IReadOnlyDictionary<string, object?> map = RequiredMap(rawRun, "run");
        string attackedServerId = ReadString(map, "attackedServer");
        ServerKind? attackedServerKind = FindServerKind(publicState, attackedServerId);
        IReadOnlyDictionary<string, object?>? priority = OptionalMap(map, "priority");
        IReadOnlyDictionary<string, object?>? encounter = OptionalMap(map, "encounter");
        IReadOnlyDictionary<string, object?> breach = RequiredMap(map, "breach");
        IReadOnlyList<SubroutineViewModel> subroutines = Array.Empty<SubroutineViewModel>();
        EncounterViewModel? encounterView = null;
        if (encounter is not null)
        {
            subroutines = ReadList(encounter, "subroutines")
                .Select(value =>
                {
                    IReadOnlyDictionary<string, object?> item = RequiredMap(value, "subroutine");
                    return new SubroutineViewModel(
                        ReadInt(item, "ordinal"),
                        ReadBool(item, "broken"),
                        ReadBool(item, "resolved"));
                })
                .ToArray();
            CardViewModel? encounterIce = ReadNullableCard(encounter, "ice");
            encounterView = new EncounterViewModel(
                ReadString(encounter, "id"),
                encounterIce,
                ReadEnum<RunEncounterStatus>(encounter, "status"),
                new ReadOnlyCollection<SubroutineViewModel>(subroutines.ToArray()))
            {
                EffectiveStrength = CalculateEffectiveStrength(encounterIce, attackedServerKind),
            };
        }

        RunPriorityViewModel? priorityView = priority is null
            ? null
            : new RunPriorityViewModel(
                ReadEnum<RunPriorityWindowKind>(priority, "window"),
                ReadEnum<PlayerSide>(priority, "holder"),
                ReadInt(priority, "consecutivePasses"),
                ReadBool(priority, "corpMayRezApproachedIce"),
                ReadBool(priority, "corpMayRezNonIce"));

        BreachHiddenCandidateSource? hiddenSource = null;
        // The live observation stores this enum directly; deserialized
        // observations carry a string or a JsonElement. Support both forms
        // while preserving an explicit JSON null.
        if (breach.TryGetValue("hiddenCandidateSource", out object? rawSource)
            && rawSource is not null
            && !(rawSource is JsonElement sourceElement && sourceElement.ValueKind == JsonValueKind.Null))
        {
            hiddenSource = ReadEnumValue<BreachHiddenCandidateSource>(rawSource);
        }

        return new RunFocusViewModel(
            ReadString(map, "id"),
            attackedServerId,
            ReadEnum<PlayerSide>(map, "initiatingSide"),
            ReadEnum<RunCrPhase>(map, "phase"),
            ReadEnum<RunTimingStep>(map, "step"),
            ReadInt(map, "currentIceIndex"),
            ReadNullableCard(map, "currentIce"),
            ReadBool(map, "successful"),
            ReadBool(map, "unsuccessful"),
            ReadNullableEnum<RunEndReason>(map, "endReason"),
            priorityView,
            encounterView,
            new AccessViewModel(
                ReadNullableCards(breach, "candidates"),
                ReadInt(breach, "randomAccessLimit"),
                hiddenSource,
                ReadNullableCards(breach, "chosen"),
                ReadNullableCards(breach, "accessed"),
                ReadEnum<RunAccessState>(breach, "accessState")),
            ReadInt(map, "runCredits"),
            ReadNullableCard(map, "initiatingSource"));
    }

    private static ServerKind? FindServerKind(
        IReadOnlyDictionary<string, object?> publicState,
        string attackedServerId)
    {
        foreach (object? rawServer in ReadList(publicState, "servers"))
        {
            IReadOnlyDictionary<string, object?> server = RequiredMap(rawServer, "server");
            if (string.Equals(ReadString(server, "id"), attackedServerId, StringComparison.Ordinal))
            {
                return ReadEnum<ServerKind>(server, "kind");
            }
        }

        return null;
    }

    private static int? CalculateEffectiveStrength(
        CardViewModel? encounterIce,
        ServerKind? attackedServerKind)
    {
        if (encounterIce?.DefinitionId is not { } definitionId
            || attackedServerKind is not { } serverKind
            || !CardDefinitionId.TryParse(definitionId, out CardDefinitionId parsedDefinition))
        {
            return null;
        }

        // Live observations normally find the immutable spec in the shared
        // catalog after scenario setup. The canonical Gateway list is a
        // side-effect-free fallback for standalone/deserialized observations
        // where setup has not run in this process.
        IceBehaviorSpec? spec = CardBehaviorSpecCatalog.TryGetIce(parsedDefinition, out IceBehaviorSpec? registered)
            ? registered
            : GatewayIceBehaviorSpecs.IceSpecs.FirstOrDefault(candidate => candidate.DefinitionId == parsedDefinition);
        return spec is null ? null : EncounterStrength.Ice(spec, serverKind);
    }

    private static OutcomeViewModel MapOutcome(IReadOnlyDictionary<string, object?> map) =>
        new(
            ReadEnum<GameOutcomeKind>(map, "kind"),
            ReadNullableEnum<PlayerSide>(map, "winner"),
            ReadNullableEnum<GameWinReason>(map, "reason"));

    private static CardViewModel?[] ReadNullableCards(
        IReadOnlyDictionary<string, object?> map,
        string key) => ReadList(map, key).Select(ReadNullableCard).ToArray();

    private static CardViewModel? ReadNullableCard(object? value) => value is null
        || value is JsonElement { ValueKind: JsonValueKind.Null }
        ? null
        : MapCard(value);

    private static CardViewModel? ReadNullableCard(
        IReadOnlyDictionary<string, object?> map,
        string key) => map.TryGetValue(key, out object? value)
            ? ReadNullableCard(value)
            : null;

    private static CardViewModel[] ReadCards(
        IReadOnlyDictionary<string, object?> map,
        string key) => ReadList(map, key)
            .Select(value => value is null
                ? throw new FormatException($"Observation field '{key}' contains a null card.")
                : MapCard(value))
            .ToArray();

    private static CardViewModel MapCard(object value)
    {
        IReadOnlyDictionary<string, object?> map = RequiredMap(value, "card");
        string id = ReadString(map, "id");
        string? definitionId = ReadNullableString(map, "definitionId");
        CardPresentationMetadata metadata = CardPresentationMetadata.For(definitionId);
        return new CardViewModel(
            id,
            definitionId,
            metadata.Title,
            metadata.Type,
            definitionId is not null,
            ReadBool(map, "rezzed", fallback: false),
            ReadInt(map, "advancementCounters", fallback: 0),
            metadata.Cost,
            metadata.Strength,
            metadata.AgendaPoints,
            metadata.TrashCost,
            metadata.Subtypes,
            metadata.Text);
    }

    private static IReadOnlyDictionary<string, object?> RequiredMap(object? value, string path)
    {
        IReadOnlyDictionary<string, object?>? map = OptionalMap(value);
        return map ?? throw new FormatException($"Observation field '{path}' must be an object.");
    }

    private static IReadOnlyDictionary<string, object?> RequiredMap(
        IReadOnlyDictionary<string, object?> map,
        string key) => RequiredMap(
            map.TryGetValue(key, out object? value) ? value : null,
            key);

    private static IReadOnlyDictionary<string, object?>? OptionalMap(
        IReadOnlyDictionary<string, object?> map,
        string key) => map.TryGetValue(key, out object? value) ? OptionalMap(value) : null;

    private static IReadOnlyDictionary<string, object?>? OptionalMap(object? value)
    {
        if (value is IReadOnlyDictionary<string, object?> map)
        {
            return map;
        }

        if (value is IDictionary<string, object?> mutable)
        {
            return new ReadOnlyDictionary<string, object?>(mutable);
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            return element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => (object?)property.Value.Clone(),
                StringComparer.Ordinal);
        }

        return null;
    }

    private static IReadOnlyList<object?> ReadList(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? value) || value is null)
        {
            return Array.Empty<object?>();
        }

        if (value is IReadOnlyList<object?> list)
        {
            return list;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            return enumerable.Cast<object?>().ToArray();
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(item => (object?)item.Clone()).ToArray();
        }

        throw new FormatException($"Observation field '{key}' must be an array.");
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> map, string key) =>
        ReadNullableString(map, key)
        ?? throw new FormatException($"Observation field '{key}' must be a string.");

    private static string? ReadNullableString(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out object? value)
            || value is null
            || value is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind == JsonValueKind.Null => null,
            _ => value.ToString(),
        };
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, object?> map,
        string key,
        int fallback = 0)
    {
        if (!map.TryGetValue(key, out object? value) || value is null)
        {
            return fallback;
        }

        if (value is int integer)
        {
            return integer;
        }

        if (value is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
        {
            return (int)longValue;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int jsonValue))
        {
            return jsonValue;
        }

        return int.TryParse(ReadScalarString(value), out int parsed)
            ? parsed
            : throw new FormatException($"Observation field '{key}' must be an integer.");
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, object?> map,
        string key,
        bool fallback = false)
    {
        if (!map.TryGetValue(key, out object? value) || value is null)
        {
            return fallback;
        }

        if (value is bool boolean)
        {
            return boolean;
        }

        if (value is JsonElement element && (element.ValueKind is JsonValueKind.True or JsonValueKind.False))
        {
            return element.GetBoolean();
        }

        return bool.TryParse(ReadScalarString(value), out bool parsed)
            ? parsed
            : throw new FormatException($"Observation field '{key}' must be a boolean.");
    }

    private static TEnum ReadEnum<TEnum>(
        IReadOnlyDictionary<string, object?> map,
        string key)
        where TEnum : struct, Enum =>
        ReadEnumValue<TEnum>(
            map.TryGetValue(key, out object? value) ? value : null,
            key);

    private static TEnum? ReadNullableEnum<TEnum>(
        IReadOnlyDictionary<string, object?> map,
        string key)
        where TEnum : struct, Enum
    {
        if (!map.TryGetValue(key, out object? value)
            || value is null
            || value is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        return ReadEnumValue<TEnum>(value, key);
    }

    private static TEnum ReadEnumValue<TEnum>(object? value, string key = "enum")
        where TEnum : struct, Enum
    {
        string text = ReadScalarString(value);
        if (Enum.TryParse(text, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new FormatException($"Observation field '{key}' contains unsupported {typeof(TEnum).Name} '{text}'.");
    }

    private static string ReadScalarString(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonElement element => element.ToString(),
        Enum enumValue => enumValue.ToString(),
        _ => value.ToString() ?? string.Empty,
    };

    private sealed record CardPresentationMetadata(
        string Title,
        string Type,
        int? Cost,
        int? Strength,
        int? AgendaPoints,
        int? TrashCost,
        IReadOnlyList<string> Subtypes,
        string Text)
    {
        public static CardPresentationMetadata For(string? definitionId)
        {
            return definitionId switch
            {
                "30072" => new("Palisade", "ICE", 3, 2, null, null, ["Barrier"], "End the run."),
                "30074" => new("Whitespace", "ICE", 2, 0, null, null, ["Code Gate"], "The Runner loses 3 credits. If the Runner has 6 credits or fewer, end the run."),
                "30006" => new("Cleaver", "Program", 4, 3, null, null, ["Icebreaker · Fracter"], "Break barrier subroutines."),
                "30005" => new("Buzzsaw", "Program", 5, 3, null, null, ["Icebreaker · Decoder"], "Break code gate subroutines."),
                "30029" => new("Overclock", "Event", 1, null, null, null, Array.Empty<string>(), "Put 5 credits on this event, then run any server."),
                "30067" => new("Offworld Office", "Agenda", null, null, 2, null, Array.Empty<string>(), "Agenda · 4 advancement"),
                "30037" => new("Nico Campaign", "Asset", 2, null, null, 2, Array.Empty<string>(), "Place 2 credits on Nico Campaign."),
                _ => new(definitionId ?? "Unknown card", "Card", null, null, null, null, Array.Empty<string>(), "Card details are hidden."),
            };
        }
    }
}

/// <summary>Short alias retained for callers that prefer a builder name.</summary>
public static class MatchViewModelBuilder
{
    public static MatchViewModel Build(Observation observation) => ObservationViewMapper.Build(observation);
}
