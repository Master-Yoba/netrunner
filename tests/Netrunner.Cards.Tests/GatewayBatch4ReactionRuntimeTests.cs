using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Production-bound run evidence for the three Slice 4.5 reaction bindings.
/// The executable Gateway catalog and a detached runtime metadata catalog are
/// injected into every Core reducer call. The Funhouse ICE behavior used by
/// the encounter fixture is deliberately test-local: this checkpoint binds
/// only its generic encounter trigger.
/// </summary>
public sealed partial class GatewayBatch4ReactionRuntimeTests
{
    private static readonly CardDefinitionId HqCostCardA = new("fixture:batch4-hq-a");
    private static readonly CardDefinitionId HqCostCardB = new("fixture:batch4-hq-b");

    [Fact]
    [Trait("GatewayCard", "30042")]
    public void ManegarmApproachReactionUsesRunnerAlternativesAtExactApproachBoundary()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState prompt = ReachApproach(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Manegarm,
                runnerCredits: 10,
                runnerClicks: 4),
            catalog,
            runtime,
            ServerKind.Remote);

        Assert.Equal(RunTimingStep.Success, prompt.RunState?.Step);
        Assert.Equal(DecisionKind.BehaviorChoice, prompt.PendingDecision?.Kind);
        Assert.Equal(PlayerSide.Runner, prompt.GetPlayer(prompt.PendingDecision!.Owner).Side);
        Assert.Contains(
            prompt.ResolutionStack,
            static frame => frame is RunTimingFrame);

        PendingDecision decision = prompt.PendingDecision;
        Assert.Contains(
            decision.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "spend-clicks"));
        Assert.Contains(
            decision.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "pay-credits"));
        Assert.Contains(
            decision.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "end-run"));

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog, runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        LegalAction spendClicks = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "spend-clicks"));
        ApplyResult applied = Apply(restored, spendClicks, catalog, runtime);

        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.Equal(2, applied.State.GetPlayerBySide(PlayerSide.Runner).Clicks);
        Assert.Equal(10, applied.State.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.Contains(applied.Events, static value => value.Kind == EngineEventKinds.ClicksAdjusted);
        Assert.Contains(applied.Events, static value => value.Kind == EngineEventKinds.RunSuccessful);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, applied.State.RunState?.Step);
        Assert.Empty(StateInvariantValidator.Collect(applied.State, catalog, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30042")]
    public void ManegarmPaysFiveCreditsOrFallsBackToEndRunAndDoesNotMatchAnotherServer()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();

        GameState creditPrompt = ReachApproach(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Manegarm,
                runnerCredits: 5,
                runnerClicks: 1),
            catalog,
            runtime,
            ServerKind.Remote);
        LegalAction pay = Assert.Single(
            creditPrompt.PendingDecision!.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "pay-credits"));
        ApplyResult paid = Apply(creditPrompt, pay, catalog, runtime);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.Equal(0, paid.State.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.Equal(1, paid.State.GetPlayerBySide(PlayerSide.Runner).Clicks);
        Assert.Contains(paid.Events, static value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Contains(paid.Events, static value => value.Kind == EngineEventKinds.RunSuccessful);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, paid.State.RunState?.Step);

        GameState fallbackPrompt = ReachApproach(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Manegarm,
                runnerCredits: 0,
                runnerClicks: 0),
            catalog,
            runtime,
            ServerKind.Remote);
        LegalAction fallback = Assert.Single(
            fallbackPrompt.PendingDecision!.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "end-run"));
        ApplyResult ended = Apply(fallbackPrompt, fallback, catalog, runtime);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        Assert.Null(ended.State.RunState);
        Assert.Contains(ended.Events, static value => value.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(ended.Events, static value => value.Kind == EngineEventKinds.RunSuccessful);

        GameState differentServer = BeginRun(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Manegarm,
                runnerCredits: 0,
                runnerClicks: 0),
            catalog,
            runtime,
            ServerKind.RAndD);
        ApplyResult completed = PassTwice(differentServer, catalog, runtime);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.DoesNotContain(
            completed.Events,
            static value => value.Kind is EngineEventKinds.RunEnded && value.Payload is RunEndedEventPayload payload
                && payload.Reason == RunEndReason.EndedByRule);
        Assert.DoesNotContain(
            completed.Events,
            static value => value.Kind == EngineEventKinds.ClicksAdjusted
                || value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Null(completed.State.RunState);
        Assert.Empty(completed.State.ResolutionStack);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [Trait("GatewayCard", "30042")]
    public void ManegarmCanBePaidWithOverclockCreditsBeforePersonalCredits(int startingCredits)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState prompt = ReachApproach(BuildState(catalog, runtime, GatewayBatch4CardCodes.Manegarm,
            runnerCredits: startingCredits, overclock: true), catalog, runtime, ServerKind.Remote);
        Assert.Equal(5, prompt.RunState!.RunCredits);
        Assert.Equal(startingCredits - 1, prompt.GetPlayerBySide(PlayerSide.Runner).Credits);
        LegalAction pay = Assert.Single(prompt.PendingDecision!.LegalActions,
            action => IsChoice(action, "manegarm-approach-choice", "pay-credits"));
        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(prompt), catalog, runtime);
        ApplyResult paid = Apply(prompt, pay, catalog, runtime);
        ApplyResult replay = Apply(restored, pay, catalog, runtime);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.True(replay.Accepted, replay.Rejection?.Message);
        Assert.Equal(startingCredits - 1, paid.State.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.Equal(0, paid.State.RunState!.RunCredits);
        Assert.Contains(paid.Events, value => value.Payload is RunCreditsAdjustedEventPayload { Delta: -5, RunCreditsAfter: 0 });
        Assert.DoesNotContain(paid.Events, value => value.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Equal(EngineJson.Serialize(paid.State), EngineJson.Serialize(replay.State));
        Assert.Equal(EngineJson.Serialize(paid.Events), EngineJson.Serialize(replay.Events));
        Assert.Empty(StateInvariantValidator.Collect(paid.State, catalog, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30050")]
    public void CorpCannotPayAnoeticVoidWithTheRunnersOverclockPool()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState prompt = ReachApproach(BuildState(catalog, runtime, GatewayBatch4CardCodes.AnoeticVoid,
            runnerCredits: 1, corpCredits: 1, hqCards: 2, overclock: true), catalog, runtime, ServerKind.Remote);
        Assert.Equal(5, prompt.RunState!.RunCredits);
        Assert.Equal(1, prompt.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, prompt.RunState.Step);
        Assert.NotEqual(DecisionKind.TriggerChoice, prompt.PendingDecision?.Kind);
        Assert.NotEqual(DecisionKind.BehaviorChoice, prompt.PendingDecision?.Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30050")]
    public void AnoeticVoidOptionalUsePaysAndTrashesExactlyTwoPrivateHqCardsThenEndsRun()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState triggerPrompt = ReachApproach(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.AnoeticVoid,
                corpCredits: 5,
                hqCards: 2),
            catalog,
            runtime,
            ServerKind.Remote);

        Assert.Equal(DecisionKind.TriggerChoice, triggerPrompt.PendingDecision?.Kind);
        Assert.Equal(PlayerSide.Corp, triggerPrompt.GetPlayer(triggerPrompt.PendingDecision!.Owner).Side);
        LegalAction use = Assert.Single(
            triggerPrompt.PendingDecision.LegalActions,
            static action => action.Kind == ActionKind.UseTrigger);
        ApplyResult used = Apply(triggerPrompt, use, catalog, runtime);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(DecisionKind.BehaviorChoice, used.State.PendingDecision?.Kind);
        Assert.Equal(PlayerSide.Corp, used.State.GetPlayer(used.State.PendingDecision!.Owner).Side);

        CardInstanceId[] hqCards = used.State.GetZone(ZoneKind.CorpHand).CardIds.ToArray();
        string runnerObservation = EngineJson.Serialize(
            ObservationBuilder.Build(used.State, PlayerSide.Runner, catalog));
        Assert.All(hqCards, cardId => Assert.DoesNotContain(cardId.Value, runnerObservation, StringComparison.Ordinal));
        string corpObservation = EngineJson.Serialize(
            ObservationBuilder.Build(used.State, PlayerSide.Corp, catalog));
        Assert.All(hqCards, cardId => Assert.Contains(cardId.Value, corpObservation, StringComparison.Ordinal));

        string json = EngineJson.Serialize(used.State);
        GameState restored = EngineJson.DeserializeState(json, catalog, runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(used.State), DeterministicHash.ForState(restored));

        LegalAction pay = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.ChoiceKey == "anoetic-void-approach-choice"
                && parameters.OptionKeys.SequenceEqual(["pay-and-trash-hq"]));
        BehaviorChoiceParameters payment = Assert.IsType<BehaviorChoiceParameters>(pay.Parameters);
        Assert.Equal(hqCards, payment.CardIds);
        ApplyResult ended = Apply(restored, pay, catalog, runtime);

        Assert.True(ended.Accepted, ended.Rejection?.Message);
        Assert.Equal(3, ended.State.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.Empty(ended.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Equal(
            hqCards.OrderBy(static id => id.Value),
            ended.State.GetZone(ZoneKind.CorpDiscard).CardIds
                .Where(hqCards.Contains)
                .OrderBy(static id => id.Value));
        Assert.All(
            hqCards,
            cardId => Assert.True(ended.State.GetCard(cardId).FaceUp));
        Assert.Contains(ended.Events, static value => value.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(ended.Events, static value => value.Kind == EngineEventKinds.RunSuccessful);
        Assert.Null(ended.State.RunState);
        Assert.Empty(StateInvariantValidator.Collect(ended.State, catalog, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30050")]
    public void AnoeticVoidCanBeDeclinedAndIsNotOfferedWithoutItsFullCompoundCost()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();

        GameState declinePrompt = ReachApproach(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.AnoeticVoid,
                corpCredits: 5,
                hqCards: 2),
            catalog,
            runtime,
            ServerKind.Remote);
        LegalAction decline = Assert.Single(
            declinePrompt.PendingDecision!.LegalActions,
            static action => action.Kind == ActionKind.DeclineTrigger);
        ApplyResult declined = Apply(declinePrompt, decline, catalog, runtime);
        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Equal(5, declined.State.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, declined.State.RunState?.Step);
        Assert.Contains(declined.Events, static value => value.Kind == EngineEventKinds.RunSuccessful);

        foreach ((int CorpCredits, int HqCards) in new[] { (1, 2), (2, 1), (100, 0) })
        {
            GameState state = ReachApproach(
                BuildState(
                    catalog,
                    runtime,
                    GatewayBatch4CardCodes.AnoeticVoid,
                    corpCredits: CorpCredits,
                    hqCards: HqCards),
                catalog,
                runtime,
                ServerKind.Remote);

            Assert.NotEqual(DecisionKind.TriggerChoice, state.PendingDecision?.Kind);
            Assert.NotEqual(DecisionKind.BehaviorChoice, state.PendingDecision?.Kind);
            Assert.DoesNotContain(
                state.ResolutionStack,
                static frame => frame is EffectCheckpointFrame checkpoint
                    && checkpoint.Occurrence.Kind == OccurrenceKind.ServerApproached);
        }
    }

    [Fact]
    [Trait("GatewayCard", "30050")]
    public void AnoeticVoidStateRejectsAChangedCatalogFingerprintAtSaveLoadBoundary()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState prompt = ReachApproach(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.AnoeticVoid,
                corpCredits: 5,
                hqCards: 2),
            catalog,
            runtime,
            ServerKind.Remote);
        string json = EngineJson.Serialize(prompt);

        BehaviorCatalog changed = new BehaviorCatalogBuilder(catalog.Version, catalog)
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId("fixture:changed-reaction-catalog"),
                PlayerSide.Corp))
            .Build();
        Assert.NotEqual(catalog.Fingerprint, changed.Fingerprint);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            EngineJson.DeserializeState(json, changed, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30054")]
    public void FunhouseEncounterReactionOffersRunnerTagOrEndRunAtEncounterInitialization()
    {
        BehaviorCatalog catalog = FunhouseCatalog();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState prompt = ReachEncounter(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Funhouse,
                sourceInIce: true,
                runnerCredits: 0,
                runnerClicks: 4),
            catalog,
            runtime,
            ServerKind.Remote);

        Assert.Equal(RunTimingStep.EncounterPaidWindow, prompt.RunState?.Step);
        Assert.Equal(DecisionKind.BehaviorChoice, prompt.PendingDecision?.Kind);
        Assert.Equal(PlayerSide.Runner, prompt.GetPlayer(prompt.PendingDecision!.Owner).Side);
        Assert.Equal(
            OccurrenceKind.IceEncountered,
            Assert.IsType<EffectCheckpointFrame>(prompt.ResolutionStack[^2]).Occurrence.Kind);
        Assert.Contains(
            prompt.PendingDecision.LegalActions,
            action => IsChoice(action, "funhouse-encounter-choice", "take-tag"));
        Assert.Contains(
            prompt.PendingDecision.LegalActions,
            action => IsChoice(action, "funhouse-encounter-choice", "end-run"));

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog, runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        LegalAction tag = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => IsChoice(action, "funhouse-encounter-choice", "take-tag"));
        ApplyResult tagged = Apply(restored, tag, catalog, runtime);

        Assert.True(tagged.Accepted, tagged.Rejection?.Message);
        Assert.Equal(1, Tags(tagged.State, PlayerSide.Runner));
        Assert.Equal(RunTimingStep.EncounterPaidWindow, tagged.State.RunState?.Step);
        Assert.Equal(DecisionKind.RunTiming, tagged.State.PendingDecision?.Kind);
        Assert.Empty(StateInvariantValidator.Collect(tagged.State, catalog, runtime));

        GameState fallbackPrompt = ReachEncounter(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Funhouse,
                sourceInIce: true,
                runnerCredits: 0,
                runnerClicks: 4),
            catalog,
            runtime,
            ServerKind.Remote);
        LegalAction fallback = Assert.Single(
            fallbackPrompt.PendingDecision!.LegalActions,
            action => IsChoice(action, "funhouse-encounter-choice", "end-run"));
        ApplyResult ended = Apply(fallbackPrompt, fallback, catalog, runtime);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        Assert.Null(ended.State.RunState);
        Assert.Contains(ended.Events, static value => value.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(ended.Events, static value => value.Kind == EngineEventKinds.RunSuccessful);
    }

    [Fact]
    [Trait("GatewayCard", "30054")]
    public void FunhouseSelfScopeDoesNotTriggerWhenAnotherServerIsApproached()
    {
        BehaviorCatalog catalog = FunhouseCatalog();
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        GameState state = BeginRun(
            BuildState(
                catalog,
                runtime,
                GatewayBatch4CardCodes.Funhouse,
                sourceInIce: true,
                runnerCredits: 0,
                runnerClicks: 4),
            catalog,
            runtime,
            ServerKind.RAndD);
        ApplyResult completed = PassTwice(state, catalog, runtime);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.DoesNotContain(
            completed.Events,
            static value => value.Kind == EngineEventKinds.TagsAdjusted);
        Assert.Null(completed.State.RunState);
        Assert.Empty(completed.State.ResolutionStack);
    }

    private static BehaviorCatalog FunhouseCatalog()
    {
        return GatewayBehaviorCatalog.Create();
    }

    private static RuntimeCardCatalog CreateRuntimeCatalog() =>
        new RuntimeCardCatalog(
            "fixture:batch4-reactions-runtime",
            new CardRuntimeSpec(GatewayCardCodes.Overclock, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 1),
            new CardRuntimeSpec(
                GatewayBatch4CardCodes.Manegarm,
                PlayerSide.Corp,
                RuntimeCardClass.Upgrade,
                rezCost: 0),
            new CardRuntimeSpec(
                GatewayBatch4CardCodes.AnoeticVoid,
                PlayerSide.Corp,
                RuntimeCardClass.Upgrade,
                rezCost: 0),
            new CardRuntimeSpec(
                GatewayBatch4CardCodes.Funhouse,
                PlayerSide.Corp,
                RuntimeCardClass.Ice,
                rezCost: 0,
                subtypes: ["barrier"]),
            new CardRuntimeSpec(
                HqCostCardA,
                PlayerSide.Corp,
                RuntimeCardClass.Operation,
                playCost: 0),
            new CardRuntimeSpec(
                HqCostCardB,
                PlayerSide.Corp,
                RuntimeCardClass.Operation,
                playCost: 0));

    private static GameState BuildState(
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime,
        CardDefinitionId sourceDefinition,
        ServerKind sourceServerKind = ServerKind.Remote,
        bool sourceInIce = false,
        int runnerCredits = 10,
        int runnerClicks = 4,
        int corpCredits = 5,
        int hqCards = 0,
        bool sourceRezzed = true,
        int advancementCounters = 0,
        bool overclock = false)
    {
        GameState baseline = GameState.CreateNew(
            seed: 45042,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerState[] servers = baseline.Servers.ToArray();
        ZoneState[] baseZones = baseline.Zones.ToArray();
        if (sourceServerKind == ServerKind.Remote
            && servers.All(candidate => candidate.Kind != ServerKind.Remote))
        {
            ServerId remoteId = allocator.NextServerId();
            ZoneId remoteIceId = allocator.NextZoneId();
            ZoneId remoteRootId = allocator.NextZoneId();
            servers = servers.Append(new ServerState(
                remoteId,
                ServerKind.Remote,
                remoteIceId,
                remoteRootId)).ToArray();
            baseZones = baseZones
                .Append(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId))
                .Append(new ZoneState(remoteRootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
                .ToArray();
        }

        ServerState server = servers.Single(candidate => candidate.Kind == sourceServerKind);
        ZoneState sourceZone = baseZones.Single(zone => zone.Id == (sourceInIce ? server.IceZoneId : server.RootZoneId));
        ZoneState hand = baseZones.Single(zone => zone.Kind == ZoneKind.CorpHand);
        CardInstanceId sourceId = allocator.NextCardInstanceId();
        CardInstanceId[] hqIds = Enumerable.Range(0, hqCards)
            .Select(_ => allocator.NextCardInstanceId())
            .ToArray();
        CardDefinitionId[] hqDefinitions =
        [
            HqCostCardA,
            HqCostCardB,
        ];
        CardInstanceState source = new(
            sourceId,
            sourceDefinition,
            sourceZone.Id,
            PlayerSide.Corp,
            faceUp: sourceRezzed,
            rezzed: sourceRezzed,
            advancementCounters: advancementCounters);
        CardInstanceState[] hq = hqIds
            .Select((id, index) => new CardInstanceState(
                id,
                hqDefinitions[index % hqDefinitions.Length],
                hand.Id,
                PlayerSide.Corp,
                faceUp: false))
            .ToArray();
        CardInstanceId? eventId = overclock ? allocator.NextCardInstanceId() : null;
        CardInstanceState[] cards = [source, .. hq, .. eventId is { } id
            ? new[] { new CardInstanceState(id, GatewayCardCodes.Overclock, baseZones.Single(zone => zone.Kind == ZoneKind.RunnerGrip).Id, PlayerSide.Runner, faceUp: false) } : []];
        ZoneState[] zones = baseZones
            .Select(zone => zone.Id == sourceZone.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    [sourceId])
                : zone.Id == hand.Id
                    ? new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        hqIds)
                    : zone.Kind == ZoneKind.RunnerGrip && eventId is { } runEvent
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [runEvent]) : zone)
            .ToArray();
        PlayerState baselineCorp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState baselineRunner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players =
        [
            new PlayerState(
                baselineCorp.Id,
                PlayerSide.Corp,
                corpCredits,
                clicks: 3,
                baselineCorp.AgendaPoints),
            new PlayerState(
                baselineRunner.Id,
                PlayerSide.Runner,
                runnerCredits,
                runnerClicks,
                baselineRunner.AgendaPoints),
        ];
        TurnFactsState facts = new(
            version: 1,
            side: PlayerSide.Runner,
            turnNumber: baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            baseline.Turn.TurnNumber,
            TurnStep.RunnerAction,
            new TurnHistoryState(1, null, null, facts, null));
        RulesState rules = RulesState.CreateDefault(players);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            initialSeed: baseline.InitialSeed,
            resolutionStack: [],
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
    }

    private static GameState BeginRun(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime,
        ServerKind targetServer) =>
        GameEngine.BeginRun(
            state,
            state.Servers.Single(server => server.Kind == targetServer).Id,
            catalog,
            runtime,
            initiatingSource: state.Cards.SingleOrDefault(card => card.DefinitionId == GatewayCardCodes.Overclock)?.Id);

    private static GameState ReachApproach(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime,
        ServerKind targetServer)
    {
        GameState run = BeginRun(state, catalog, runtime, targetServer);
        ApplyResult first = Pass(run, catalog, runtime);
        return Pass(first.State, catalog, runtime).State;
    }

    private static GameState ReachEncounter(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime,
        ServerKind targetServer)
    {
        GameState run = BeginRun(state, catalog, runtime, targetServer);
        for (int index = 0; index < 4; index++)
        {
            run = Pass(run, catalog, runtime).State;
        }

        return run;
    }

    private static ApplyResult PassTwice(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime)
    {
        ApplyResult first = Pass(state, catalog, runtime);
        return Pass(first.State, catalog, runtime);
    }

    private static ApplyResult Pass(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            static action => action.Kind == ActionKind.PassPriority);
        return Apply(state, pass, catalog, runtime);
    }

    private static ApplyResult Apply(
        GameState state,
        LegalAction action,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtime)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog,
            runtime);
    }

    private static bool IsChoice(LegalAction action, string key, string option) =>
        action.Kind == ActionKind.ChooseBehavior
        && action.Parameters is BehaviorChoiceParameters parameters
        && parameters.ChoiceKey == key
        && parameters.OptionKeys.SequenceEqual([option]);

    private static int Tags(GameState state, PlayerSide side)
    {
        PlayerId playerId = state.Players.Single(player => player.Side == side).Id;
        return state.RulesState!.Players.Single(player => player.PlayerId == playerId).Tags;
    }
}

internal static class GatewayBatch4ReactionRuntimeStateExtensions
{
    public static PlayerState GetPlayerBySide(this GameState state, PlayerSide side) =>
        state.Players.Single(player => player.Side == side);
}
