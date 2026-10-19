namespace Netrunner.Core;

/// <summary>
/// Shared state mutation and working-copy helpers used by the domain reducers.
/// These methods are intentionally mechanical extractions from the M2 engine;
/// they define the atomic state boundary but do not add new rules behavior.
/// </summary>
public static partial class GameEngine
{
    private static void MoveCard(
        WorkingState working,
        CardInstanceState card,
        ZoneState source,
        ZoneState destination,
        bool emit = true,
        bool cleanupCounters = true,
        bool emitMemoryAdjustment = true)
    {
        if (card.ZoneId != source.Id || !source.CardIds.Contains(card.Id))
        {
            throw new ResolutionFailureException($"Card '{card.Id}' is not in source zone '{source.Id}'.");
        }

        if (source.Id == destination.Id)
        {
            throw new ResolutionFailureException("A card move requires distinct source and destination zones.");
        }

        if (destination.ServerId is { } destinationServer
            && working.Servers.Any(server => server.Id == destinationServer && !server.Exists))
        {
            throw new ResolutionFailureException("Cards cannot enter a destroyed server.");
        }

        MarkTriggerSourcesMovedAfterStart(working, card, destination);

        ReplaceZone(working, new ZoneState(
            source.Id,
            source.Kind,
            source.OwnerSide,
            source.ServerId,
            source.CardIds.Where(candidate => candidate != card.Id).ToArray()));
        ReplaceZone(working, new ZoneState(
            destination.Id,
            destination.Kind,
            destination.OwnerSide,
            destination.ServerId,
            destination.CardIds.Append(card.Id).ToArray()));
        // CR 26.03 4.4.6/8.6.7: cards in the Runner heap are public. Keep
        // this normalization at the single card-movement boundary so damage,
        // memory cleanup, explicit move effects, and run-event cleanup all
        // agree—even when a facedown Runner card enters the heap.
        bool entersPublicFaceUpZone = (destination.Kind == ZoneKind.RunnerHeap
                && destination.OwnerSide == PlayerSide.Runner)
            || destination.Kind is ZoneKind.CorpRemovedFromGame or ZoneKind.RunnerRemovedFromGame;
        bool leavesInstalledZone = IsInstalledZone(source.Kind) && !IsInstalledZone(destination.Kind);
        bool entersHiddenZone = destination.Kind is ZoneKind.CorpDeck or ZoneKind.CorpHand
            or ZoneKind.RunnerStack or ZoneKind.RunnerGrip;
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            !entersHiddenZone && (entersPublicFaceUpZone || card.FaceUp),
            !leavesInstalledZone && card.Rezzed,
            card.AdvancementCounters, KeepsHost(source.Kind, destination.Kind, leavesInstalledZone) ? card.HostCardId : null));
        if (leavesInstalledZone)
        {
            TrashHostedDescendants(working, card.Id);
        }
        // VP-RETURN-AT-RUN-END: a remembered card that leaves play is forgotten, so a reinstalled copy isn't returned.
        if (leavesInstalledZone
            && working.RunState is { PersistentContext.RememberedCards: { } remembered } rememberingRun
            && remembered.Any(value => value.CardId == card.Id))
        {
            working.RunState = rememberingRun.WithPersistentContext(rememberingRun.PersistentContext!.WithoutRemembered(card.Id));
        }
        // VP-UNINSTALL-INTERRUPT: a card that returns its hosted bad publicity hands it to the Corp as it leaves (Luana
        // Campos); the counters are read before they are cleared.
        if (leavesInstalledZone
            && card.OwnerSide == PlayerSide.Corp
            && working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? leaving)
            && leaving.ReturnsHostedBadPublicity
            && working.RulesState?.Counters.FirstOrDefault(counter => counter.CardId == card.Id
                && string.Equals(counter.CounterName, CardBehaviorDefinition.HostedBadPublicityCounter, StringComparison.Ordinal)) is { Count: > 0 } hosted)
        {
            int returned = hosted.Count;
            ClearCardCounters(working, card.Id);
            ApplyBadPublicity(working, returned);
        }
        else if (cleanupCounters
            && leavesInstalledZone)
        {
            ClearCardCounters(working, card.Id);
        }
        if (leavesInstalledZone)
        {
            ClearRunValueAdjustments(working, card.Id);
        }

        if (emit)
        {
            CardMovedEventPayload movement = source.Kind is ZoneKind.CorpRemovedFromGame
                or ZoneKind.RunnerRemovedFromGame
                || destination.Kind is ZoneKind.CorpRemovedFromGame
                or ZoneKind.RunnerRemovedFromGame
                ? new CardMovedEventPayload(card.Id, source.Id, destination.Id, source.Kind, destination.Kind)
                : new CardMovedEventPayload(card.Id, source.Id, destination.Id);
            Emit(
                working,
                EngineEventKinds.CardMoved,
                movement);
        }

        // Installed program memory follows the physical card across every
        // uninstall path, including ICE subroutines and generic transfers.
        if (source.Kind == ZoneKind.RunnerRig
            && destination.Kind != ZoneKind.RunnerRig
            && working.RulesState?.Programs.Any(program => program.CardId == card.Id) == true)
        {
            RemoveProgramRegistration(working, card.Id, emitMemoryAdjustment);
        }

        RefreshRemoteLifecycle(working, source.ServerId);
        RefreshRemoteLifecycle(working, destination.ServerId);
    }

    /// <summary>
    /// Performs a rules-defined trash transfer and records the physical card
    /// occurrence in the active formal turn facts.  Ordinary movement remains
    /// deliberately separate: a destination discard/heap alone does not make
    /// a move a trash operation.
    /// </summary>
    private static void TrashCard(
        WorkingState working,
        CardInstanceState card,
        ZoneState source,
        ZoneState destination,
        bool emit = true,
        bool cleanupCounters = true,
        bool emitMemoryAdjustment = true)
    {
        ZoneKind expectedKind = card.OwnerSide == PlayerSide.Corp
            ? ZoneKind.CorpDiscard
            : ZoneKind.RunnerHeap;
        if (destination.Kind != expectedKind
            || destination.OwnerSide != card.OwnerSide
            || destination.ServerId is not null)
        {
            throw new ResolutionFailureException(
                $"Card '{card.Id}' must be trashed to its owner's {expectedKind} zone.");
        }

        MoveCard(working, card, source, destination, emit, cleanupCounters, emitMemoryAdjustment);
        RecordTurnTrash(working, card.Id);
    }

    /// <summary>
    /// A run strength adjustment on a card ends when that card leaves play: a
    /// breaker trashed mid-encounter (Ansel 1.0 trashing a pumped Unity, Wave 4
    /// C1) keeps no boost, and nothing else may point at a card no longer installed.
    /// </summary>
    private static void ClearRunValueAdjustments(WorkingState working, CardInstanceId cardId)
    {
        // EL.8: so do a breaker's run-long pumps (GAMEDRAGON™ Pro), sourced by the breaker itself.
        static bool Ends(RunLingeringEffect effect, CardInstanceId cardId) =>
            effect.ValueAdjustment?.TargetCardId == cardId
            || effect.ValueAdjustment is null
            && effect.SourceCardId == cardId
            && effect.Expiry == RunLingeringEffectExpiry.Run
            && effect.BehaviorId == EncounterStrength.IcebreakerPumpBehavior;
        if (working.RunState is not { } run
            || !run.LingeringEffects.Any(effect => Ends(effect, cardId))) return;
        working.RunState = run.WithLingeringEffects(run.LingeringEffects
            .Where(effect => !Ends(effect, cardId)).ToArray());
    }

    /// <summary>
    /// Whether a moved card keeps its host. A card leaving play or its console's faceup zone loses it, except into
    /// a behavior's temporary set-aside custody and back (EL.4 review): a returned selection is hosted as before.
    /// </summary>
    private static bool KeepsHost(ZoneKind source, ZoneKind destination, bool leavesInstalledZone) =>
        !leavesInstalledZone
        && (!(IsHostedFaceUpZone(source) || source == ZoneKind.BehaviorSetAside)
            || destination == ZoneKind.BehaviorSetAside
            || IsHostedFaceUpZone(destination));

    /// <summary>
    /// Records that a begun catalog trigger's source left its zone for one other than its owner's trash or
    /// removed-from-game zone (those keep their own lifecycle rules), so the trigger may finish (EL.9).
    /// </summary>
    private static void MarkTriggerSourcesMovedAfterStart(WorkingState working, CardInstanceState card, ZoneState destination)
    {
        if (IsAbilityTrashZone(destination, card) || IsAbilityRemoveZone(destination, card)) return;
        for (int index = 0; index < working.ResolutionStack.Count; index++)
        {
            if (working.ResolutionStack[index] is BehaviorResolutionFrame frame
                && frame.SourceCardId == card.Id
                && frame.SourceZoneId == card.ZoneId
                && !frame.CatalogTriggerSourceMovedAfterStart
                && CatalogTriggerBegan(frame))
            {
                working.ResolutionStack[index] = frame.With(catalogTriggerSourceMovedAfterStart: true);
            }
        }
    }

    private static bool IsInstalledZone(ZoneKind kind) =>
        kind is ZoneKind.RunnerRig or ZoneKind.ServerIce or ZoneKind.ServerRoot;

    /// <summary>
    /// Removes every typed counter attached to a card after it leaves play.
    /// This is deliberately silent: the counter entries are derived state
    /// owned by the card, and the enclosing card-move event is the public
    /// movement boundary. Cost payment can opt out temporarily and invokes
    /// this helper after its complete declaration-order loop.
    /// </summary>
    private static void ClearCardCounters(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        if (card.AdvancementCounters != 0)
        {
            ReplaceCard(working, new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId,
                card.OwnerSide, card.FaceUp, card.Rezzed, advancementCounters: 0, hostCardId: card.HostCardId));
        }

        RulesState? rules = working.RulesState;
        if (rules is null)
        {
            return;
        }

        CardCounterState[] remaining = rules.Counters
            .Where(counter => counter.CardId != cardId)
            .ToArray();
        if (remaining.Length != rules.Counters.Count)
        {
            SetRulesState(working, counters: remaining);
        }
    }

    private static void ReplacePlayer(WorkingState working, PlayerState player)
    {
        int index = working.Players.FindIndex(candidate => candidate.Id == player.Id);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Player '{player.Id}' does not exist.");
        }

        working.Players[index] = player;
    }

    private static void ReplaceCard(WorkingState working, CardInstanceState card)
    {
        int index = working.Cards.FindIndex(candidate => candidate.Id == card.Id);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Card '{card.Id}' does not exist.");
        }

        working.Cards[index] = card;
    }

    private static void ReplaceZone(WorkingState working, ZoneState zone)
    {
        int index = working.Zones.FindIndex(candidate => candidate.Id == zone.Id);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Zone '{zone.Id}' does not exist.");
        }

        working.Zones[index] = zone;
    }

    private static PlayerState GetPlayer(WorkingState working, PlayerSide side) =>
        working.Players.SingleOrDefault(player => player.Side == side)
        ?? throw new ResolutionFailureException($"Player side '{side}' does not exist.");

    private static CardInstanceState GetCard(WorkingState working, CardInstanceId cardId) =>
        working.Cards.SingleOrDefault(card => card.Id == cardId)
        ?? throw new ResolutionFailureException($"Card '{cardId}' does not exist.");

    private static ZoneState GetZone(WorkingState working, ZoneKind kind) =>
        working.Zones.SingleOrDefault(zone => zone.Kind == kind && zone.ServerId is null)
        ?? throw new ResolutionFailureException($"Zone kind '{kind}' does not exist.");

    private static ZoneState GetRemoteRoot(WorkingState working) =>
        working.Servers
            .Where(server => server.Kind == ServerKind.Remote && server.Exists)
            .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
            .Select(server => working.Zones.SingleOrDefault(zone => zone.Id == server.RootZoneId))
            .FirstOrDefault(zone => zone is not null)
        ?? throw new ResolutionFailureException("The fixture requires a Corp remote root zone.");

    private static void RequireClicks(PlayerState player, ActionKind kind)
    {
        if (player.Clicks <= 0)
        {
            throw new ResolutionFailureException($"Action '{kind}' requires at least one click.");
        }
    }

    private static void RequireNoToyTarget(CardInstanceId? target, ActionKind kind)
    {
        if (target.HasValue)
        {
            throw new ResolutionFailureException($"Action '{kind}' must not contain a card target.");
        }
    }

    private static void RequireToyTarget(CardInstanceId? target, ActionKind kind)
    {
        if (!target.HasValue)
        {
            throw new ResolutionFailureException($"Action '{kind}' requires a card target.");
        }
    }

    private static void Emit(WorkingState working, string kind, EngineEventPayload payload)
    {
        long sequence = NextSequence(working.EventSequence, "event");
        working.EventSequence = sequence;
        working.Events.Add(new EngineEvent(sequence, kind, payload));
    }

    private static void ApplyCreditDelta(WorkingState working, PlayerId target, int delta)
    {
        int index = working.Players.FindIndex(player => player.Id == target);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Credit adjustment target '{target}' does not exist.");
        }

        PlayerState player = working.Players[index];
        long nextCredits = checked((long)player.Credits + delta);
        if (nextCredits is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException($"Credit adjustment would produce invalid credits {nextCredits} for '{target}'.");
        }

        working.Players[index] = new PlayerState(
            player.Id,
            player.Side,
            (int)nextCredits,
            player.Clicks,
            player.AgendaPoints);

        long sequence = checked(working.EventSequence + 1);
        working.EventSequence = sequence;
        working.Events.Add(new EngineEvent(
            sequence,
            EngineEventKinds.CreditsAdjusted,
            new CreditsAdjustedEventPayload(target, delta, (int)nextCredits)));
    }

    private static IReadOnlyList<EngineEvent> FreezeEvents(IEnumerable<EngineEvent> events) =>
        EngineEventValidator.FreezeBatch(events);

    private static string FormatViolations(IReadOnlyList<StateInvariantViolation> violations) =>
        string.Join("; ", violations.Select(static violation => violation.ToString()));

    private sealed class WorkingState
    {
        private readonly GameState _source;

        private WorkingState(
            GameState source,
            IBehaviorCatalog behaviorCatalog,
            IRuntimeCardCatalog? runtimeCardCatalog)
        {
            _source = source;
            BehaviorCatalog = behaviorCatalog;
            RuntimeCardCatalog = runtimeCardCatalog;
            Players = source.Players.ToList();
            Cards = source.Cards.ToList();
            Zones = source.Zones.ToList();
            Servers = source.Servers.ToList();
            InitialSeed = source.InitialSeed;
            RandomState = source.RandomState;
            Allocator = new StableIdAllocator(source.Allocator);
            Turn = new TurnState(
                source.Turn.ActiveSide,
                source.Turn.Phase,
                source.Turn.TurnNumber,
                source.Turn.Step,
                source.Turn.History);
            Outcome = new GameOutcome(source.Outcome.Kind, source.Outcome.Winner, source.Outcome.Reason);
            DecisionRevision = source.DecisionRevision;
            EventSequence = source.EventSequence;
            ActionSequence = source.ActionSequence;
            ResolutionStack = source.ResolutionStack.Select(ResolutionFrameCloner.Copy).ToList();
            PendingDecision = source.PendingDecision?.Copy();
            RunState = source.RunState?.Copy();
            RulesState = source.RulesState?.Copy();
            Events = new List<EngineEvent>();
        }

        public List<PlayerState> Players { get; }

        /// <summary>Immutable catalog captured for this atomic reduction.</summary>
        public IBehaviorCatalog BehaviorCatalog { get; }

        /// <summary>
        /// Immutable printed-card metadata captured for this atomic reducer.
        /// It is deliberately separate from the executable behavior catalog.
        /// </summary>
        public IRuntimeCardCatalog? RuntimeCardCatalog { get; }

        public List<CardInstanceState> Cards { get; }

        public List<ZoneState> Zones { get; }

        public List<ServerState> Servers { get; }

        public ulong InitialSeed { get; }

        public ulong RandomState { get; set; }

        public StableIdAllocator Allocator { get; }

        public TurnState Turn { get; set; }

        public GameOutcome Outcome { get; set; }

        public long DecisionRevision { get; set; }

        public long EventSequence { get; set; }

        public long ActionSequence { get; set; }

        public List<ResolutionFrame> ResolutionStack { get; }

        public PendingDecision? PendingDecision { get; set; }

        public RunState? RunState { get; set; }

        public RulesState? RulesState { get; set; }

        /// <summary>
        /// Transient: the action this Apply consumes has started but its
        /// frames aren't on the stack yet ("finished an action yet" checks).
        /// </summary>
        public bool ActionJustStarted { get; set; }

        public List<EngineEvent> Events { get; }

        public WorkingState Fork() => From(Build(_source), BehaviorCatalog, RuntimeCardCatalog);

        public static WorkingState From(
            GameState source,
            IBehaviorCatalog? behaviorCatalog = null,
            IRuntimeCardCatalog? runtimeCardCatalog = null) =>
            new(source, behaviorCatalog ?? Netrunner.Core.BehaviorCatalog.Empty, runtimeCardCatalog);

        public GameState Build(GameState source) => new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            Players,
            Cards,
            Zones,
            Servers,
            Turn,
            Outcome,
            RandomState,
            Allocator.State,
            DecisionRevision,
            EventSequence,
            ResolutionStack,
            PendingDecision,
            ActionSequence,
            InitialSeed,
            RunState,
            RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            RuntimeCardCatalog?.Version ?? source.RuntimeCardCatalogVersion,
            RuntimeCardCatalog?.Fingerprint ?? source.RuntimeCardCatalogFingerprint);
    }
}
