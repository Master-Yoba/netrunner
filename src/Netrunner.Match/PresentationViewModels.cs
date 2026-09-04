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
    /// <summary>
    /// The public turn projection. Older M2 observations do not carry the
    /// optional fine-grained step; such observations remain usable while the
    /// step is reported as unavailable rather than inferred from a prompt.
    /// </summary>
    public TurnViewModel Turn { get; init; } = TurnViewModel.Unavailable();

    /// <summary>Viewer-safe hand counts and optional maximum-size projection.</summary>
    public HandSizeViewModel CorpHandSize { get; init; } = HandSizeViewModel.Unavailable(PlayerSide.Corp, 0);

    public HandSizeViewModel RunnerHandSize { get; init; } = HandSizeViewModel.Unavailable(PlayerSide.Runner, 0);

    /// <summary>Visible installed cards carrying advancement tokens.</summary>
    public IReadOnlyList<InstalledAdvancementViewModel> InstalledAdvancementCounters { get; init; } =
        Array.Empty<InstalledAdvancementViewModel>();

    public ScoreAreaViewModel CorpScoreArea { get; init; } = ScoreAreaViewModel.Empty(PlayerSide.Corp);

    public ScoreAreaViewModel RunnerScoreArea { get; init; } = ScoreAreaViewModel.Empty(PlayerSide.Runner);

    /// <summary>Core-issued discard decision, when the active decision is forced discard.</summary>
    public ForcedDiscardViewModel? ForcedDiscard { get; init; }

    public IReadOnlyList<LegalActionViewModel> Actions => LegalActions;

    public IReadOnlyList<CardViewModel> RunnerHandCards => RunnerHand.VisibleCards;

    public IReadOnlyList<CardViewModel> CorpHandCards => CorpHand.VisibleCards;

    /// <summary>
    /// Ability uses from the current visible turn-fact projection. The list is
    /// detached by the observation mapper and contains no authoritative state.
    /// Older observations omit this optional projection and therefore expose an
    /// empty list.
    /// </summary>
    public IReadOnlyList<AbilityUseViewModel> UsedAbilityKeys { get; init; } =
        Array.Empty<AbilityUseViewModel>();

    /// <summary>Alias used by clients that prefer an explicit current-turn name.</summary>
    public IReadOnlyList<AbilityUseViewModel> CurrentTurnUsedAbilityKeys => UsedAbilityKeys;
}

/// <summary>
/// Optional Match-layer input for a viewer-safe maximum hand-size projection.
/// This is a compatibility fallback for observations produced before Core
/// projected the viewer-safe maximum in each public player object. The mapper
/// never inspects GameState or derives a value from card text.
/// </summary>
public sealed record HandSizeProjection(
    int? CorpMaximumHandSize,
    int? RunnerMaximumHandSize,
    string? UnavailableReason = null)
{
    public static HandSizeProjection Unavailable { get; } = new(
        null,
        null,
        "Maximum hand size unavailable: observation has no viewer-safe maximum and no compatibility fallback was supplied.");

    public int? For(PlayerSide side) => side switch
    {
        PlayerSide.Corp => CorpMaximumHandSize,
        PlayerSide.Runner => RunnerMaximumHandSize,
        _ => null,
    };
}

/// <summary>Explicit public turn/timing values used by the UI-403 shell.</summary>
public sealed record TurnViewModel(
    bool IsAvailable,
    PlayerSide? ActiveSide,
    TurnPhase? Phase,
    TurnStep? Step,
    int? TurnNumber,
    string? Diagnostic)
{
    public static TurnViewModel Unavailable(string? diagnostic = null) => new(
        false,
        null,
        null,
        null,
        null,
        diagnostic ?? "Turn projection unavailable: the observation did not provide a valid turn object.");

    public string PhaseLabel => Phase is { } phase
        ? phase.ToString().ToUpperInvariant()
        : "UNKNOWN PHASE";

    public string TimingLabel => Step is { } step
        ? step.ToString().ToUpperInvariant()
        : "TIMING STEP UNAVAILABLE";
}

/// <summary>Public hand count paired with an optional observed or fallback maximum.</summary>
public sealed record HandSizeViewModel(
    PlayerSide Side,
    int Count,
    int? MaximumHandSize,
    string? MaximumHandSizeDiagnostic)
{
    public bool IsMaximumKnown => MaximumHandSize.HasValue;

    public bool IsOverLimit => MaximumHandSize is { } maximum && Count > maximum;

    public static HandSizeViewModel Unavailable(PlayerSide side, int count, string? diagnostic = null) => new(
        side,
        count,
        null,
        diagnostic ?? HandSizeProjection.Unavailable.UnavailableReason);

    public string Summary => MaximumHandSize is { } maximum
        ? $"Hand {Count} / max {maximum}"
        : $"Hand {Count} / max unavailable";
}

/// <summary>One visible installed card's advancement-token presentation.</summary>
public sealed record InstalledAdvancementViewModel(
    string CardId,
    string Title,
    int Count,
    PlayerSide Controller,
    string? ServerId)
{
    public string Summary => ServerId is { } server
        ? $"{Title} · {Count} advancement token(s) · {server}"
        : $"{Title} · {Count} advancement token(s)";
}

/// <summary>Agenda-point total and visible scored cards for one side.</summary>
public sealed record ScoreAreaViewModel(
    PlayerSide Side,
    int AgendaPoints,
    IReadOnlyList<CardViewModel> Cards)
{
    public static ScoreAreaViewModel Empty(PlayerSide side) => new(side, 0, Array.Empty<CardViewModel>());

    public string Summary => $"{Side} score · {AgendaPoints} agenda point(s) · {Cards.Count} visible agenda(s)";
}

/// <summary>Core-issued forced-discard prompt; no discard count is inferred.</summary>
public sealed record ForcedDiscardViewModel(
    PlayerSide Side,
    string Prompt,
    HandSizeViewModel Hand)
{
    public string Summary => $"FORCED DISCARD · {Prompt} · {Hand.Summary}";
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
    string RulesText,
    string Faction = "",
    int? MemoryCost = null,
    int? AdvancementRequirement = null)
{
    /// <summary>
    /// Typed rules counters projected by Core. This is intentionally separate
    /// from the legacy advancement-counter presentation so a UI cannot mistake
    /// an advancement token for a hosted credit or virus counter.
    /// </summary>
    public IReadOnlyList<CardCounterViewModel> TypedCounters { get; init; } =
        Array.Empty<CardCounterViewModel>();

    /// <summary>Compatibility alias for callers that call these card counters.</summary>
    public IReadOnlyList<CardCounterViewModel> CardCounters => TypedCounters;

    /// <summary>Non-colour counter-chip text derived from typed counter meaning.</summary>
    public IReadOnlyList<string> CounterChips => TypedCounters
        .Select(static counter => counter.ChipText)
        .ToArray();

    /// <summary>
    /// Viewer-safe ability affordance. It is populated only from an issued
    /// <see cref="ActionKind.UseAbility"/> action and current visible facts;
    /// card metadata/rules text is never consulted to infer it.
    /// </summary>
    public string? AbilityCue { get; init; }

    public bool IsAbilityReady => string.Equals(AbilityCue, AbilityCueLabels.Ready, StringComparison.Ordinal);

    public bool IsAbilityUsed => string.Equals(AbilityCue, AbilityCueLabels.Used, StringComparison.Ordinal);

    public IReadOnlyList<string> Counters => AdvancementCounters > 0
        ? new[] { $"Advancement ×{AdvancementCounters}" }
        : Array.Empty<string>();
}

/// <summary>Typed counter state retained by the Match presentation boundary.</summary>
public sealed record CardCounterViewModel(
    string CounterName,
    CardCounterPurpose Purpose,
    int Count,
    HostedCreditSpendRestriction? SpendRestriction)
{
    /// <summary>
    /// Stable non-colour label for a counter chip. Generic counters named
    /// <c>power</c> receive the familiar POWER label; other generic names are
    /// deliberately rendered as GENERIC rather than guessed from card text.
    /// </summary>
    public string Label => Purpose switch
    {
        CardCounterPurpose.HostedCredit => "HOSTED CREDIT",
        CardCounterPurpose.Virus => "VIRUS",
        CardCounterPurpose.Generic when string.Equals(CounterName, "power", StringComparison.OrdinalIgnoreCase)
            => "POWER",
        CardCounterPurpose.Generic => "GENERIC",
        _ => "UNKNOWN",
    };

    public string ChipText => $"{Label} ×{Count}";

    public string Summary => SpendRestriction is { } restriction
        ? $"{ChipText} · {restriction.ToString().ToUpperInvariant()}"
        : ChipText;
}

/// <summary>One visible current-turn use of a physical card ability.</summary>
public sealed record AbilityUseViewModel(string SourceCardId, string AbilityKey);

/// <summary>Stable text values for the derived per-card ability cue.</summary>
public static class AbilityCueLabels
{
    public const string Ready = "ABILITY READY";
    public const string Used = "USED";
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
    ActionParameters Parameters)
{
    /// <summary>
    /// Optional raw cost quote carried by a typed Core action. Match exposes
    /// the immutable reasons/provenance without formatting or re-deriving it.
    /// </summary>
    public CostQuote? CostQuote => Parameters switch
    {
        RezCardParameters rez => rez.CostQuote,
        RunIceParameters runIce => runIce.CostQuote,
        InstallCardParameters install => install.CostQuote,
        RunBreakParameters runBreak => runBreak.CostQuote,
        _ => null,
    };
}

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
    /// <summary>
    /// Raw Core breach-access terms, including their typed source kind and
    /// viewer-filtered physical provenance. Match retains these values for a
    /// renderer without formatting or re-deriving access arithmetic.
    /// </summary>
    public IReadOnlyList<RunBreachAccessModifierViewModel> BreachAccessModifiers { get; init; } =
        Array.Empty<RunBreachAccessModifierViewModel>();

    public string FocusLabel => Encounter is not null
        ? $"Encounter · {Encounter.Ice?.Title ?? "Unknown ICE"}"
        : Access.State != RunAccessState.NotStarted
            ? $"Access · {Access.State}"
            : $"Run · {Phase}";
}

public sealed record RunBreachAccessModifierViewModel(
    string? SourceCardId,
    string AttackedServerId,
    int Delta,
    string ReasonKey,
    RunBreachAccessModifierSourceKind? SourceKind);

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

    /// <summary>
    /// The authoritative typed strength calculation projected by Core. Match
    /// retains the raw terms so presentation can localize them without
    /// re-deriving rules or parsing a display string.
    /// </summary>
    public ValueQuote? StrengthQuote { get; init; }

    /// <summary>
    /// Current encounter strength of each visible installed icebreaker. Printed
    /// card strength remains immutable; encounter-scoped pumps are reflected
    /// here so clients can explain why an interface ability is or is not legal.
    /// </summary>
    public IReadOnlyList<EncounterBreakerViewModel> Breakers { get; init; } =
        Array.Empty<EncounterBreakerViewModel>();

    public int RemainingSubroutines => Subroutines.Count(value => !value.Broken && !value.Resolved);
}

public sealed record EncounterBreakerViewModel(
    string CardId,
    string Title,
    int PrintedStrength,
    int EffectiveStrength)
{
    /// <summary>Authoritative typed breaker-strength calculation from Core.</summary>
    public ValueQuote? StrengthQuote { get; init; }
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
    private sealed record CardAbilityCueContext(
        IReadOnlySet<string> ReadySourceIds,
        IReadOnlySet<string> UsedSourceIds)
    {
        public string? For(string cardId) => ReadySourceIds.Contains(cardId)
            ? AbilityCueLabels.Ready
            : UsedSourceIds.Contains(cardId)
                ? AbilityCueLabels.Used
                : null;
    }

    /// <summary>
    /// Legacy M2 compatibility adapter. New composition code must provide the
    /// immutable behavior catalog explicitly through the three-argument
    /// overload below.
    /// </summary>
    public static MatchViewModel Build(Observation observation) =>
        Build(observation, CardMetadataResolvers.Default, GatewayBehaviorCatalog.Create());

    /// <summary>
    /// Builds a view using the caller's immutable metadata snapshot. Metadata
    /// is consulted only for definition IDs that the observation already
    /// reveals; hidden cards therefore remain resolver-invisible. This
    /// two-argument overload is retained as a legacy M2 compatibility adapter;
    /// composition code should use the overload that also supplies the
    /// immutable behavior catalog.
    /// </summary>
    public static MatchViewModel Build(
        Observation observation,
        ICardMetadataResolver metadataResolver)
        => Build(observation, metadataResolver, GatewayBehaviorCatalog.Create());

    /// <summary>
    /// Builds a view from the caller's immutable metadata and behavior
    /// snapshots. Both catalogs are composition inputs; no process-wide
    /// behavior registry is consulted by this production path.
    /// </summary>
    public static MatchViewModel Build(
        Observation observation,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog)
        => Build(observation, metadataResolver, behaviorCatalog, handSizeProjection: null);

    /// <summary>
    /// Builds a view with an optional viewer-safe hand-size projection. The
    /// projection is deliberately supplied by composition; this mapper still
    /// accepts only observation data and never reaches authoritative state.
    /// </summary>
    public static MatchViewModel Build(
        Observation observation,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog,
        HandSizeProjection? handSizeProjection)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(metadataResolver);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);

        handSizeProjection ??= HandSizeProjection.Unavailable;
        IReadOnlyDictionary<string, object?> publicState = observation.PublicState;
        IReadOnlyDictionary<string, object?> privateState = observation.PrivateState;
        IReadOnlyDictionary<string, object?> players = RequiredMap(publicState, "players");
        IReadOnlyDictionary<string, object?> corpPlayer = RequiredMap(players, "corp");
        IReadOnlyDictionary<string, object?> runnerPlayer = RequiredMap(players, "runner");
        PlayerViewModel corp = MapPlayer(corpPlayer, PlayerSide.Corp);
        PlayerViewModel runner = MapPlayer(runnerPlayer, PlayerSide.Runner);
        IReadOnlyDictionary<string, object?> hidden = RequiredMap(publicState, "hiddenZoneCounts");
        IReadOnlyList<AbilityUseViewModel> usedAbilityKeys = MapVisibleCurrentTurnUsedAbilityKeys(publicState);
        IReadOnlySet<string> usedAbilitySourceIds = usedAbilityKeys
            .Select(static use => use.SourceCardId)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> readyAbilitySourceIds = observation.LegalActions
            .Where(static action => action.Kind == ActionKind.UseAbility)
            .Select(static action => action.Parameters as AbilityActionParameters)
            .Where(static parameters => parameters is not null)
            .Select(static parameters => parameters!.SourceCardId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var abilityCue = new CardAbilityCueContext(readyAbilitySourceIds, usedAbilitySourceIds);

        ZoneViewModel corpHand = MapZone(
            ZoneKind.CorpHand,
            ReadInt(hidden, "corpHand"),
            observation.Viewer == PlayerSide.Corp
                ? ReadCards(privateState, "hand", metadataResolver, abilityCue)
                : Array.Empty<CardViewModel>());
        ZoneViewModel corpDeck = MapZone(
            ZoneKind.CorpDeck,
            ReadInt(hidden, "corpDeck"),
            observation.Viewer == PlayerSide.Corp
                ? ReadCards(privateState, "deck", metadataResolver, abilityCue)
                : Array.Empty<CardViewModel>());
        ZoneViewModel runnerHand = MapZone(
            ZoneKind.RunnerGrip,
            ReadInt(hidden, "runnerGrip"),
            observation.Viewer == PlayerSide.Runner
                ? ReadCards(privateState, "grip", metadataResolver, abilityCue)
                : Array.Empty<CardViewModel>());
        ZoneViewModel runnerDeck = MapZone(
            ZoneKind.RunnerStack,
            ReadInt(hidden, "runnerStack"),
            observation.Viewer == PlayerSide.Runner
                ? ReadCards(privateState, "stack", metadataResolver, abilityCue)
                : Array.Empty<CardViewModel>());

        IReadOnlyList<ServerViewModel> servers = ReadList(publicState, "servers")
            .Select(value => MapServer(value, metadataResolver, abilityCue))
            .ToArray();
        IReadOnlyList<CardViewModel> runnerRig = ReadCards(
            publicState,
            "runnerRig",
            metadataResolver,
            abilityCue);
        IReadOnlyList<CardViewModel> corpScore = ReadCards(
            publicState,
            "corpScore",
            metadataResolver,
            abilityCue);
        IReadOnlyList<CardViewModel> runnerScore = ReadCards(
            publicState,
            "runnerScore",
            metadataResolver,
            abilityCue);
        DecisionViewModel? decision = MapDecision(observation);
        RunFocusViewModel? run = MapRun(
            publicState,
            metadataResolver,
            behaviorCatalog,
            abilityCue);
        OutcomeViewModel outcome = MapOutcome(RequiredMap(publicState, "outcome"));
        IReadOnlyList<LegalActionViewModel> actions = observation.LegalActions
            .Select(action => new LegalActionViewModel(
                action.Id,
                action.Kind,
                ActionsRegionPresenter.Describe(action.Kind, action.Description, action.Parameters),
                action.Parameters))
            .ToArray();

        MatchViewModel view = new(
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

        HandSizeViewModel corpHandSize = MapHandSize(
            PlayerSide.Corp,
            corpHand,
            corpPlayer,
            handSizeProjection);
        HandSizeViewModel runnerHandSize = MapHandSize(
            PlayerSide.Runner,
            runnerHand,
            runnerPlayer,
            handSizeProjection);
        return view with
        {
            Turn = MapTurn(publicState),
            CorpHandSize = corpHandSize,
            RunnerHandSize = runnerHandSize,
            InstalledAdvancementCounters = MapInstalledAdvancement(servers, runnerRig),
            CorpScoreArea = new ScoreAreaViewModel(
                PlayerSide.Corp,
                corp.AgendaPoints,
                new ReadOnlyCollection<CardViewModel>(corpScore.ToArray())),
            RunnerScoreArea = new ScoreAreaViewModel(
                PlayerSide.Runner,
                runner.AgendaPoints,
                new ReadOnlyCollection<CardViewModel>(runnerScore.ToArray())),
            ForcedDiscard = MapForcedDiscard(decision, corpHandSize, runnerHandSize),
            UsedAbilityKeys = usedAbilityKeys,
        };
    }

    /// <summary>Legacy M2 compatibility alias; prefer explicit catalogs.</summary>
    public static MatchViewModel FromObservation(Observation observation) => Build(observation);

    /// <summary>
    /// Legacy M2 compatibility adapter; use the overload with an explicit
    /// immutable behavior catalog for composed matches.
    /// </summary>
    public static MatchViewModel FromObservation(
        Observation observation,
        ICardMetadataResolver metadataResolver) =>
        Build(observation, metadataResolver);

    /// <summary>Canonical composition alias with explicit immutable catalogs.</summary>
    public static MatchViewModel FromObservation(
        Observation observation,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog) =>
        Build(observation, metadataResolver, behaviorCatalog);

    /// <summary>Canonical composition alias with optional hand-size input.</summary>
    public static MatchViewModel FromObservation(
        Observation observation,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog,
        HandSizeProjection? handSizeProjection) =>
        Build(observation, metadataResolver, behaviorCatalog, handSizeProjection);

    private static IReadOnlyList<AbilityUseViewModel> MapVisibleCurrentTurnUsedAbilityKeys(
        IReadOnlyDictionary<string, object?> publicState)
    {
        if (!publicState.TryGetValue("turnFacts", out object? rawFacts)
            || rawFacts is null
            || rawFacts is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return Array.Empty<AbilityUseViewModel>();
        }

        IReadOnlyDictionary<string, object?> turnFacts = RequiredMap(rawFacts, "turnFacts");
        var uses = new List<AbilityUseViewModel>();
        var keys = new HashSet<(string SourceCardId, string AbilityKey)>();
        foreach (string side in new[] { "corp", "runner" })
        {
            if (!turnFacts.TryGetValue(side, out object? rawSideFacts)
                || rawSideFacts is null
                || rawSideFacts is JsonElement { ValueKind: JsonValueKind.Null })
            {
                continue;
            }

            IReadOnlyDictionary<string, object?> sideFacts = RequiredMap(rawSideFacts, $"turnFacts.{side}");
            if (!sideFacts.TryGetValue("thisTurn", out object? rawThisTurn)
                || rawThisTurn is null
                || rawThisTurn is JsonElement { ValueKind: JsonValueKind.Null })
            {
                continue;
            }

            IReadOnlyDictionary<string, object?> thisTurn = RequiredMap(rawThisTurn, $"turnFacts.{side}.thisTurn");
            if (!thisTurn.ContainsKey("usedAbilityKeys"))
            {
                continue;
            }

            if (!thisTurn.TryGetValue("usedAbilityKeys", out object? rawUses)
                || rawUses is null
                || rawUses is JsonElement { ValueKind: JsonValueKind.Null })
            {
                throw new FormatException(
                    $"Observation field 'turnFacts.{side}.thisTurn.usedAbilityKeys' must be an array when supplied.");
            }

            foreach (object? rawUse in ReadList(thisTurn, "usedAbilityKeys"))
            {
                IReadOnlyDictionary<string, object?> use = RequiredMap(
                    rawUse,
                    $"turnFacts.{side}.thisTurn.usedAbilityKeys");
                string sourceCardId = ReadString(use, "sourceCardId");
                string abilityKey = ReadString(use, "abilityKey");
                if (!IsStableCounterName(sourceCardId) || !IsStableCounterName(abilityKey))
                {
                    throw new FormatException(
                        $"Observation field 'turnFacts.{side}.thisTurn.usedAbilityKeys' contains an invalid identifier.");
                }

                if (!keys.Add((sourceCardId, abilityKey)))
                {
                    throw new FormatException(
                        $"Observation field 'turnFacts.{side}.thisTurn.usedAbilityKeys' contains duplicate use '{sourceCardId}/{abilityKey}'.");
                }

                uses.Add(new AbilityUseViewModel(sourceCardId, abilityKey));
            }
        }

        return new ReadOnlyCollection<AbilityUseViewModel>(uses.ToArray());
    }

    private static TurnViewModel MapTurn(IReadOnlyDictionary<string, object?> publicState)
    {
        if (!publicState.TryGetValue("turn", out object? rawTurn)
            || OptionalMap(rawTurn) is not { } map)
        {
            return TurnViewModel.Unavailable(
                "Turn projection unavailable: observation field 'turn' is missing or is not an object.");
        }

        if (!TryReadEnum(map, "activeSide", out PlayerSide activeSide)
            || !TryReadEnum(map, "phase", out TurnPhase phase)
            || !TryReadRequiredInt(map, "turnNumber", out int turnNumber))
        {
            return TurnViewModel.Unavailable(
                "Turn projection unavailable: observation field 'turn' contains an unsupported active side, phase, or turn number.");
        }

        TurnStep? step = null;
        if (map.ContainsKey("step"))
        {
            if (!TryReadEnum(map, "step", out TurnStep parsedStep))
            {
                return TurnViewModel.Unavailable(
                    "Turn timing unavailable: observation field 'turn.step' contains an unsupported value.");
            }

            step = parsedStep;
        }

        return new TurnViewModel(
            true,
            activeSide,
            phase,
            step,
            turnNumber,
            Diagnostic: null);
    }

    private static HandSizeViewModel MapHandSize(
        PlayerSide side,
        ZoneViewModel zone,
        IReadOnlyDictionary<string, object?> observedPlayer,
        HandSizeProjection handSizeProjection)
    {
        // Core's current observation carries this value in the already
        // viewer-safe public player projection. Prefer it over compatibility
        // injection so the UI cannot display a stale composition estimate.
        int? maximum = TryReadOptionalInt(observedPlayer, "maximumHandSize", out int observedMaximum)
            ? observedMaximum
            : handSizeProjection.For(side);
        return maximum is { } value
            ? new HandSizeViewModel(side, zone.Count, value, null)
            : HandSizeViewModel.Unavailable(side, zone.Count, handSizeProjection.UnavailableReason);
    }

    private static ForcedDiscardViewModel? MapForcedDiscard(
        DecisionViewModel? decision,
        HandSizeViewModel corpHand,
        HandSizeViewModel runnerHand)
    {
        if (decision is null
            || !Enum.IsDefined(decision.Kind)
            || !Enum.IsDefined(decision.OwnerSide)
            || decision.Kind != DecisionKind.TurnDiscard)
        {
            return null;
        }

        HandSizeViewModel hand = decision.OwnerSide == PlayerSide.Corp ? corpHand : runnerHand;
        return new ForcedDiscardViewModel(decision.OwnerSide, decision.Prompt, hand);
    }

    private static ReadOnlyCollection<InstalledAdvancementViewModel> MapInstalledAdvancement(
        IReadOnlyList<ServerViewModel> servers,
        IReadOnlyList<CardViewModel> runnerRig)
    {
        var counters = new List<InstalledAdvancementViewModel>();
        foreach (ServerViewModel server in servers)
        {
            foreach (CardViewModel? card in server.OrderedIce.Concat(server.OrderedRoot))
            {
                if (card is { AdvancementCounters: > 0 })
                {
                    counters.Add(new InstalledAdvancementViewModel(
                        card.Id,
                        card.Title,
                        card.AdvancementCounters,
                        PlayerSide.Corp,
                        server.Id));
                }
            }
        }

        foreach (CardViewModel card in runnerRig)
        {
            if (card.AdvancementCounters > 0)
            {
                counters.Add(new InstalledAdvancementViewModel(
                    card.Id,
                    card.Title,
                    card.AdvancementCounters,
                    PlayerSide.Runner,
                    ServerId: null));
            }
        }

        return new ReadOnlyCollection<InstalledAdvancementViewModel>(counters);
    }

    private static bool TryReadRequiredInt(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out int value)
    {
        if (!map.ContainsKey(key))
        {
            value = default;
            return false;
        }

        try
        {
            value = ReadInt(map, key);
            return true;
        }
        catch (FormatException)
        {
            value = default;
            return false;
        }
    }

    private static bool TryReadOptionalInt(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out int value)
    {
        if (!map.TryGetValue(key, out object? raw)
            || raw is null
            || raw is JsonElement { ValueKind: JsonValueKind.Null })
        {
            value = default;
            return false;
        }

        return TryReadRequiredInt(map, key, out value);
    }

    private static bool TryReadEnum<TEnum>(
        IReadOnlyDictionary<string, object?> map,
        string key,
        out TEnum value)
        where TEnum : struct, Enum
    {
        if (!map.ContainsKey(key))
        {
            value = default;
            return false;
        }

        try
        {
            value = ReadEnum<TEnum>(map, key);
            return true;
        }
        catch (FormatException)
        {
            value = default;
            return false;
        }
    }

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

    private static ServerViewModel MapServer(
        object? value,
        ICardMetadataResolver metadataResolver,
        CardAbilityCueContext abilityCue)
    {
        IReadOnlyDictionary<string, object?> map = RequiredMap(value, "server");
        // Core stores server ICE from innermost to outermost (the active
        // approach index starts at Count - 1). Presentation uses the spatial
        // order promised by ServerViewModel: outermost to innermost.
        IReadOnlyList<CardViewModel?> orderedIce = ReadNullableCards(map, "ice", metadataResolver, abilityCue)
            .Reverse()
            .ToArray();
        return new ServerViewModel(
            ReadString(map, "id"),
            ReadEnum<ServerKind>(map, "kind"),
            orderedIce,
            ReadNullableCards(map, "root", metadataResolver, abilityCue));
    }

    private static RunFocusViewModel? MapRun(
        IReadOnlyDictionary<string, object?> publicState,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog,
        CardAbilityCueContext abilityCue)
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
            CardViewModel? encounterIce = ReadNullableCard(encounter, "ice", metadataResolver, abilityCue);
            ValueQuote? observedIceQuote = ReadValueQuote(encounter, "strengthQuote");
            ValueQuote? iceQuote = SelectIceQuote(
                encounterIce,
                attackedServerKind,
                behaviorCatalog,
                observedIceQuote);
            encounterView = new EncounterViewModel(
                ReadString(encounter, "id"),
                encounterIce,
                ReadEnum<RunEncounterStatus>(encounter, "status"),
                new ReadOnlyCollection<SubroutineViewModel>(subroutines.ToArray()))
            {
                EffectiveStrength = iceQuote?.EffectiveValue,
                StrengthQuote = iceQuote,
                Breakers = CalculateBreakerStrengths(
                    publicState,
                    map,
                    encounter,
                    metadataResolver,
                    behaviorCatalog,
                    abilityCue),
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

        RunBreachAccessModifierViewModel[] breachModifiers = ReadList(map, "breachAccessModifiers")
            .Select(value =>
            {
                IReadOnlyDictionary<string, object?> modifier = RequiredMap(value, "breachAccessModifier");
                return new RunBreachAccessModifierViewModel(
                    ReadNullableString(modifier, "sourceCardId"),
                    ReadString(modifier, "attackedServerId"),
                    ReadInt(modifier, "delta"),
                    ReadString(modifier, "reasonKey"),
                    ReadNullableEnum<RunBreachAccessModifierSourceKind>(modifier, "sourceKind"));
            })
            .ToArray();

        return new RunFocusViewModel(
            ReadString(map, "id"),
            attackedServerId,
            ReadEnum<PlayerSide>(map, "initiatingSide"),
            ReadEnum<RunCrPhase>(map, "phase"),
            ReadEnum<RunTimingStep>(map, "step"),
            ReadInt(map, "currentIceIndex"),
            ReadNullableCard(map, "currentIce", metadataResolver, abilityCue),
            ReadBool(map, "successful"),
            ReadBool(map, "unsuccessful"),
            ReadNullableEnum<RunEndReason>(map, "endReason"),
            priorityView,
            encounterView,
            new AccessViewModel(
                ReadNullableCards(breach, "candidates", metadataResolver, abilityCue),
                ReadInt(breach, "randomAccessLimit"),
                hiddenSource,
                ReadNullableCards(breach, "chosen", metadataResolver, abilityCue),
                ReadNullableCards(breach, "accessed", metadataResolver, abilityCue),
            ReadEnum<RunAccessState>(breach, "accessState")),
            ReadInt(map, "runCredits"),
            ReadNullableCard(map, "initiatingSource", metadataResolver, abilityCue))
        {
            BreachAccessModifiers = new ReadOnlyCollection<RunBreachAccessModifierViewModel>(breachModifiers),
        };
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

    private static ValueQuote? SelectIceQuote(
        CardViewModel? encounterIce,
        ServerKind? attackedServerKind,
        IBehaviorCatalog behaviorCatalog,
        ValueQuote? observedQuote)
    {
        if (encounterIce?.DefinitionId is not { } definitionId
            || attackedServerKind is not { } serverKind
            || !CardDefinitionId.TryParse(definitionId, out CardDefinitionId parsedDefinition))
        {
            return null;
        }

        IceBehaviorSpec? spec = behaviorCatalog.TryGetIce(parsedDefinition, out IceBehaviorSpec? registered)
            ? registered
            : null;
        if (spec is null)
        {
            return null;
        }

        // An observation quote is authoritative when it is for the same
        // printed definition/base. The base check preserves the historical
        // injected-catalog compatibility test: an intentionally alternate
        // catalog can still provide its own pure quote when its printed value
        // differs from the observation.
        return observedQuote is not null && observedQuote.BaseValue == spec.Strength
            ? observedQuote
            : EncounterStrength.IceQuote(spec, serverKind);
    }

    private static ReadOnlyCollection<EncounterBreakerViewModel> CalculateBreakerStrengths(
        IReadOnlyDictionary<string, object?> publicState,
        IReadOnlyDictionary<string, object?> run,
        IReadOnlyDictionary<string, object?> encounter,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog,
        CardAbilityCueContext abilityCue)
    {
        RunLingeringEffect[] effects = ReadList(run, "lingeringEffects")
            .Select(value =>
            {
                IReadOnlyDictionary<string, object?> effect = RequiredMap(value, "lingeringEffect");
                IReadOnlyDictionary<string, object?>? source = OptionalMap(effect, "sourceCard");
                IReadOnlyDictionary<string, object?>? rawAdjustment = OptionalMap(effect, "valueAdjustment");
                RunValueAdjustment? adjustment = rawAdjustment is null
                    ? null
                    : new RunValueAdjustment(
                        ReadEnum<BehaviorValueKind>(rawAdjustment, "valueKind"),
                        ReadEnum<BehaviorValueSourceKind>(rawAdjustment, "source"),
                        ReadEnum<BehaviorValueOperation>(rawAdjustment, "operation"),
                        ReadCardInstanceId(rawAdjustment, "sourceCardId")
                            ?? throw new FormatException("A value adjustment requires a source card ID."),
                        ReadCardInstanceId(rawAdjustment, "targetCardId")
                            ?? throw new FormatException("A value adjustment requires a target card ID."),
                        ReadInt(rawAdjustment, "delta"),
                        ReadString(rawAdjustment, "reasonKey"),
                        ReadNullableString(rawAdjustment, "variableKey"),
                        ReadNullableInt(rawAdjustment, "quantity"),
                        ReadNullableInt(rawAdjustment, "unitDelta"));
                return new RunLingeringEffect(
                    new RunEffectId(ReadString(effect, "id")),
                    ReadString(effect, "behaviorId"),
                    source is null ? null : new CardInstanceId(ReadString(source, "id")),
                    ReadEnum<RunLingeringEffectExpiry>(effect, "expiry"),
                    adjustment);
            })
            .ToArray();

        Dictionary<string, ValueQuote> observedQuotes = ReadList(encounter, "breakerStrengths")
            .Select(value =>
            {
                IReadOnlyDictionary<string, object?> entry = RequiredMap(value, "breakerStrength");
                string cardId = ReadString(entry, "cardId");
                ValueQuote quote = ReadValueQuote(entry, "strengthQuote")
                    ?? throw new FormatException(
                        $"Observation field 'breakerStrength {cardId}.strengthQuote' must be a value quote.");
                return (cardId, quote);
            })
            .GroupBy(pair => pair.cardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                if (group.Count() != 1)
                {
                    throw new FormatException(
                        $"Observation field 'breakerStrengths' contains duplicate card '{group.Key}'.");
                }

                return group.Single().quote;
            }, StringComparer.Ordinal);

        var breakers = new List<EncounterBreakerViewModel>();
        foreach (CardViewModel card in ReadCards(publicState, "runnerRig", metadataResolver, abilityCue))
        {
            if (card.DefinitionId is not { } definitionId
                || !CardDefinitionId.TryParse(definitionId, out CardDefinitionId parsedDefinition))
            {
                continue;
            }

            IcebreakerBehaviorSpec? spec = behaviorCatalog.TryGetIcebreaker(
                parsedDefinition,
                out IcebreakerBehaviorSpec? registered)
                ? registered
                : null;
            if (spec is null)
            {
                continue;
            }

            ValueQuote quote = observedQuotes.TryGetValue(card.Id, out ValueQuote? observedQuote)
                && observedQuote.BaseValue == spec.BaseStrength
                ? observedQuote
                : EncounterStrength.BreakerQuote(spec, effects, new CardInstanceId(card.Id));
            breakers.Add(new EncounterBreakerViewModel(
                card.Id,
                card.Title,
                spec.BaseStrength,
                quote.EffectiveValue)
            {
                StrengthQuote = quote,
            });
        }

        return new ReadOnlyCollection<EncounterBreakerViewModel>(breakers);
    }

    private static OutcomeViewModel MapOutcome(IReadOnlyDictionary<string, object?> map) =>
        new(
            ReadEnum<GameOutcomeKind>(map, "kind"),
            ReadNullableEnum<PlayerSide>(map, "winner"),
            ReadNullableEnum<GameWinReason>(map, "reason"));

    private static ValueQuote? ReadValueQuote(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        if (!map.TryGetValue(key, out object? rawValue)
            || rawValue is null
            || rawValue is JsonElement { ValueKind: JsonValueKind.Null })
        {
            return null;
        }

        IReadOnlyDictionary<string, object?> quote = RequiredMap(rawValue, key);
        if (!quote.ContainsKey("terms"))
        {
            throw new FormatException($"Observation field '{key}.terms' is required.");
        }

        ValueCalculationTerm[] terms = ReadList(quote, "terms")
            .Select(value =>
            {
                IReadOnlyDictionary<string, object?> term = RequiredMap(value, $"{key}.terms");
                return new ValueCalculationTerm(
                    ReadEnum<BehaviorValueSourceKind>(term, "source"),
                    ReadEnum<BehaviorValueOperation>(term, "operation"),
                    ReadInt(term, "delta"),
                    ReadString(term, "reasonKey"),
                    ReadCardDefinitionId(term, "sourceDefinitionId"),
                    ReadCardInstanceId(term, "sourceCardId"),
                    ReadNullableInt(term, "quantity"),
                    ReadNullableInt(term, "unitDelta"),
                    ReadNullableString(term, "variableKey"));
            })
            .ToArray();

        return new ValueQuote(
            ReadEnum<BehaviorValueKind>(quote, "valueKind"),
            ReadInt(quote, "baseValue"),
            ReadInt(quote, "effectiveValue"),
            terms);
    }

    private static CardDefinitionId? ReadCardDefinitionId(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        string? value = ReadNullableString(map, key);
        return value is null ? null : new CardDefinitionId(value);
    }

    private static CardInstanceId? ReadCardInstanceId(
        IReadOnlyDictionary<string, object?> map,
        string key)
    {
        string? value = ReadNullableString(map, key);
        return value is null ? null : new CardInstanceId(value);
    }

    private static int? ReadNullableInt(
        IReadOnlyDictionary<string, object?> map,
        string key) => map.TryGetValue(key, out object? value)
        && value is not null
        && value is not JsonElement { ValueKind: JsonValueKind.Null }
        ? ReadInt(map, key)
        : null;

    private static CardViewModel?[] ReadNullableCards(
        IReadOnlyDictionary<string, object?> map,
        string key,
        ICardMetadataResolver metadataResolver,
        CardAbilityCueContext abilityCue) => ReadList(map, key)
            .Select(value => ReadNullableCard(value, metadataResolver, abilityCue))
            .ToArray();

    private static CardViewModel? ReadNullableCard(
        object? value,
        ICardMetadataResolver metadataResolver,
        CardAbilityCueContext abilityCue) => value is null
        || value is JsonElement { ValueKind: JsonValueKind.Null }
        ? null
        : MapCard(value, metadataResolver, abilityCue);

    private static CardViewModel? ReadNullableCard(
        IReadOnlyDictionary<string, object?> map,
        string key,
        ICardMetadataResolver metadataResolver,
        CardAbilityCueContext abilityCue) => map.TryGetValue(key, out object? value)
            ? ReadNullableCard(value, metadataResolver, abilityCue)
            : null;

    private static CardViewModel[] ReadCards(
        IReadOnlyDictionary<string, object?> map,
        string key,
        ICardMetadataResolver metadataResolver,
        CardAbilityCueContext abilityCue) => ReadList(map, key)
            .Select(value => value is null
                ? throw new FormatException($"Observation field '{key}' contains a null card.")
                : MapCard(value, metadataResolver, abilityCue))
            .ToArray();

    private static CardViewModel MapCard(
        object value,
        ICardMetadataResolver metadataResolver,
        CardAbilityCueContext abilityCue)
    {
        IReadOnlyDictionary<string, object?> map = RequiredMap(value, "card");
        string id = ReadString(map, "id");
        string? definitionId = ReadNullableString(map, "definitionId");
        CardPresentationMetadata metadata = CardPresentationMetadata.Unknown(definitionId);
        bool known = false;
        if (definitionId is not null
            && metadataResolver.TryResolve(definitionId, out CardPresentationMetadata resolved))
        {
            metadata = resolved;
            known = true;
        }
        CardViewModel mapped = new(
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
            known ? CardMarkupFormatter.Format(metadata.RulesText) : metadata.RulesText,
            metadata.Faction,
            metadata.MemoryCost,
            metadata.AdvancementRequirement);
        return mapped with
        {
            TypedCounters = MapCounters(map, id),
            AbilityCue = abilityCue.For(id),
        };
    }

    private static IReadOnlyList<CardCounterViewModel> MapCounters(
        IReadOnlyDictionary<string, object?> card,
        string cardId)
    {
        if (!card.ContainsKey("counters"))
        {
            return Array.Empty<CardCounterViewModel>();
        }

        if (!card.TryGetValue("counters", out object? rawValue)
            || rawValue is null
            || rawValue is JsonElement { ValueKind: JsonValueKind.Null })
        {
            throw new FormatException($"Observation field 'card {cardId}.counters' must be an array when supplied.");
        }

        IReadOnlyList<object?> rawCounters = ReadList(card, "counters");
        var counters = new List<CardCounterViewModel>(rawCounters.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? rawCounter in rawCounters)
        {
            IReadOnlyDictionary<string, object?> counter = RequiredMap(rawCounter, $"card '{cardId}' counter");
            string counterName = ReadString(counter, "counterName");
            if (!IsStableCounterName(counterName))
            {
                throw new FormatException(
                    $"Observation field 'card {cardId}.counters.counterName' must be a stable nonblank identifier.");
            }

            if (!names.Add(counterName))
            {
                throw new FormatException(
                    $"Observation field 'card {cardId}.counters' contains duplicate counter '{counterName}'.");
            }

            int count = ReadInt(counter, "count");
            if (count <= 0)
            {
                throw new FormatException(
                    $"Observation field 'card {cardId}.counters.count' must be positive.");
            }

            CardCounterPurpose purpose = ReadEnum<CardCounterPurpose>(counter, "purpose");
            HostedCreditSpendRestriction? restriction = ReadNullableEnum<HostedCreditSpendRestriction>(
                counter,
                "spendRestriction");
            bool validPurposeName = purpose switch
            {
                CardCounterPurpose.Generic => !string.Equals(counterName, "virus", StringComparison.Ordinal)
                    && !string.Equals(counterName, "credit", StringComparison.Ordinal),
                CardCounterPurpose.Virus => string.Equals(counterName, "virus", StringComparison.Ordinal),
                CardCounterPurpose.HostedCredit => string.Equals(counterName, "credit", StringComparison.Ordinal),
                _ => false,
            };
            if (!validPurposeName)
            {
                throw new FormatException(
                    $"Observation field 'card {cardId}.counters' has an invalid purpose/name combination.");
            }

            if (purpose == CardCounterPurpose.HostedCredit && restriction is null)
            {
                throw new FormatException(
                    $"Observation field 'card {cardId}.counters.spendRestriction' is required for hosted credits.");
            }

            if (purpose != CardCounterPurpose.HostedCredit && restriction is not null)
            {
                throw new FormatException(
                    $"Observation field 'card {cardId}.counters.spendRestriction' is only valid for hosted credits.");
            }

            counters.Add(new CardCounterViewModel(counterName, purpose, count, restriction));
        }

        return new ReadOnlyCollection<CardCounterViewModel>(counters.ToArray());
    }

    private static bool IsStableCounterName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        return value.All(character => !char.IsWhiteSpace(character) && !char.IsControl(character));
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

}

/// <summary>Short alias retained for callers that prefer a builder name.</summary>
public static class MatchViewModelBuilder
{
    /// <summary>Legacy M2 compatibility adapter; prefer the catalog overload.</summary>
    public static MatchViewModel Build(Observation observation) => ObservationViewMapper.Build(observation);

    /// <summary>Legacy M2 compatibility adapter; prefer the catalog overload.</summary>
    public static MatchViewModel Build(
        Observation observation,
        ICardMetadataResolver metadataResolver) =>
        ObservationViewMapper.Build(observation, metadataResolver);

    /// <summary>Canonical composition entry point with explicit catalogs.</summary>
    public static MatchViewModel Build(
        Observation observation,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog) =>
        ObservationViewMapper.Build(observation, metadataResolver, behaviorCatalog);

    /// <summary>Canonical composition entry point with optional hand-size input.</summary>
    public static MatchViewModel Build(
        Observation observation,
        ICardMetadataResolver metadataResolver,
        IBehaviorCatalog behaviorCatalog,
        HandSizeProjection? handSizeProjection) =>
        ObservationViewMapper.Build(
            observation,
            metadataResolver,
            behaviorCatalog,
            handSizeProjection);
}
