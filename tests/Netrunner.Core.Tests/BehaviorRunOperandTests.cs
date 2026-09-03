using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused ENG-411 coverage for typed click-run declarations and the run-scoped
/// breach-access snapshot.  The execution cases use the real turn shell and
/// reducer; the small fixture only supplies one installed Runner source.
/// </summary>
public sealed class BehaviorRunOperandTests
{
    private static readonly CardDefinitionId SourceDefinition = new("test:d1-run-source");
    private static readonly CardInstanceId SourceCard = new("card:d1-run-source");

    [Fact]
    public void RunOperandIsTypedSerializableAndIncludedInDefinitionFingerprint()
    {
        BehaviorRunOperand operand = new(
            ServerKind.RAndD,
            requireNotRunThisTurn: true,
            sourceCounterName: "counter:d1-access",
            breachAccessDeltaPerCounter: 2,
            breachAccessReasonKey: "reason:d1-access");
        BehaviorInstruction start = new(BehaviorInstructionKind.StartRun, runOperand: operand);

        string instructionJson = EngineJson.Serialize(start);
        BehaviorInstruction restoredInstruction = EngineJson.Deserialize<BehaviorInstruction>(instructionJson);
        Assert.Equal(operand, restoredInstruction.RunOperand);
        Assert.Contains("runOperand", instructionJson, StringComparison.Ordinal);

        BehaviorCatalog baseline = new BehaviorCatalogBuilder("test:d1-run-fingerprint")
            .Add(new CardBehaviorDefinition(
                SourceDefinition,
                PlayerSide.Runner,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]))
            .Build();
        BehaviorCatalog withRun = new BehaviorCatalogBuilder("test:d1-run-fingerprint")
            .Add(RunDefinition(operand))
            .Build();

        Assert.NotEqual(baseline.Fingerprint, withRun.Fingerprint);
        Assert.True(baseline.TryGet(SourceDefinition, out CardBehaviorDefinition? baselineDefinition));
        Assert.DoesNotContain("runOperand", EngineJson.Serialize(baselineDefinition), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorRunOperand(ServerKind.Remote));
        Assert.Throws<ArgumentException>(() => new BehaviorRunOperand(
            ServerKind.RAndD,
            sourceCounterName: "counter:d1-access"));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.GainCredits,
            amount: 1,
            runOperand: operand));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            new CardDefinitionId("test:d1-root-start"),
            PlayerSide.Runner,
            instructions: [start]));
        Assert.Throws<ArgumentException>(() => new BehaviorPaidAbility(
            "ability:d1-paid-start",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [],
            [],
            [start],
            BehaviorAbilityUsageLimit.Unlimited));
    }

    [Fact]
    public void MaterializationOmitsAttemptedServerAndStaleSubmissionIsAtomic()
    {
        BehaviorRunOperand operand = Operand();
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:d1-run-materialization")
            .Add(RunDefinition(operand))
            .Build();
        ServerId rAndD = Server(catalog, attempted: []);
        RunFixture fixture = CreateFixture(catalog, operand, attempted: []);

        IReadOnlyList<AbilityActionParameters> available = GameEngine.MaterializeAbilityActionParameters(
            fixture.StateWithTurnFrame,
            catalog);
        AbilityActionParameters action = Assert.Single(available);
        Assert.Equal(SourceCard, action.SourceCardId);
        Assert.Equal(BehaviorAbilityActivation.ClickAction, action.Activation);

        RunFixture attemptedFixture = CreateFixture(catalog, operand, attempted: [rAndD]);
        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(
            attemptedFixture.StateWithTurnFrame,
            catalog));

        GameState actionState = OpenRunnerAction(fixture.State, catalog);
        LegalAction issued = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);
        ServerId attacked = rAndD;
        GameState stale = ReplaceFacts(
            actionState,
            actionState.Turn.ThisTurnFacts!.MarkRunAttempted(attacked));
        string before = EngineJson.Serialize(stale);
        ApplyResult rejected = GameEngine.Apply(
            stale,
            new ActionSubmission(
                stale.PendingDecision!.Owner,
                stale.PendingDecision.Id,
                stale.PendingDecision.Revision,
                issued.Id),
            catalog);

        Assert.False(rejected.Accepted);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
        Assert.Empty(rejected.Events);
        Assert.Same(stale, rejected.State);
    }

    [Fact]
    public void InstalledClickRunRetiresAbilityFramesKeepsSourceAndSnapshotsCounter()
    {
        BehaviorRunOperand operand = Operand();
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:d1-run-runtime")
            .Add(RunDefinition(operand))
            .Build();
        RunFixture fixture = CreateFixture(
            catalog,
            operand,
            counters: [new CardCounterState(SourceCard, "counter:d1-access", 2)]);
        GameState actionState = OpenRunnerAction(fixture.State, catalog);
        LegalAction action = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult started = Apply(actionState, action, catalog);

        Assert.True(started.Accepted, started.Rejection?.Message);
        Assert.Equal(
            [EngineEventKinds.ClickSpent, EngineEventKinds.RunInitiated],
            started.Events.Select(@event => @event.Kind));
        Assert.Equal(
            [typeof(TurnFrame), typeof(RunTimingFrame)],
            started.State.ResolutionStack.Select(frame => frame.GetType()));
        Assert.Equal(
            TurnFrameStage.AwaitingAction,
            Assert.IsType<TurnFrame>(started.State.ResolutionStack[0]).Stage);
        Assert.Equal(SourceCard, started.State.RunState!.InitiatingSource);
        Assert.Null(started.State.RunState.ActiveRunEventId);
        Assert.Contains(SourceCard, started.State.GetZone(ZoneKind.RunnerRig).CardIds);
        RunBreachAccessModifier modifier = Assert.Single(started.State.RunState.BreachAccessModifiers!);
        Assert.Equal(SourceCard, modifier.SourceCardId);
        Assert.Equal(fixture.AttackedServer, modifier.AttackedServerId);
        Assert.Equal(4, modifier.Delta);
        Assert.Equal("reason:d1-access", modifier.ReasonKey);
        Assert.Equal([fixture.AttackedServer], started.State.Turn.ThisTurnFacts!.AttemptedRunServerIds);
        Assert.Empty(StateInvariantValidator.Collect(started.State, catalog));

        string json = EngineJson.Serialize(started.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(4, Assert.Single(restored.RunState!.BreachAccessModifiers!).Delta);
        Assert.Empty(StateInvariantValidator.Collect(restored, catalog));
    }

    [Fact]
    public void InstalledClickRunRemainsValidWhenSourceLeavesRigBeforeCleanup()
    {
        BehaviorRunOperand operand = Operand();
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:d1-run-source-movement")
            .Add(RunDefinition(operand))
            .Build();
        CardInstanceId deckCard = new("card:d1-source-movement-target");
        RunFixture fixture = CreateFixture(catalog, operand, corpDeckCards: [deckCard]);
        GameState actionState = OpenRunnerAction(fixture.State, catalog);
        LegalAction ability = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult started = Apply(actionState, ability, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);

        // The installed source is detached provenance once initiation commits.
        // Moving it through the normal physical card/zone representation must
        // not turn the nested run into a standalone or invalid run.
        GameState moved = MoveCardFixture(started.State, SourceCard, ZoneKind.RunnerHeap);
        Assert.Equal(ZoneKind.RunnerHeap, moved.GetZone(moved.GetCard(SourceCard).ZoneId).Kind);
        Assert.Empty(StateInvariantValidator.Collect(moved, catalog));

        List<EngineEvent> events = started.Events.ToList();
        GameState cursor = moved;
        for (int pass = 0; pass < 2; pass++)
        {
            ApplyResult result = ApplyPass(cursor, catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            cursor = result.State;
        }

        LegalAction choose = Assert.Single(
            cursor.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate);
        ApplyResult selected = Apply(cursor, choose, catalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        events.AddRange(selected.Events);

        LegalAction noAccess = Assert.Single(
            selected.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.NoAccessAction);
        ApplyResult finished = Apply(selected.State, noAccess, catalog);
        Assert.True(finished.Accepted, finished.Rejection?.Message);
        events.AddRange(finished.Events);

        Assert.Null(finished.State.RunState);
        TurnFrame turnFrame = Assert.IsType<TurnFrame>(Assert.Single(finished.State.ResolutionStack));
        Assert.Equal(PlayerSide.Runner, turnFrame.ActiveSide);
        Assert.Equal(TurnFrameStage.ActionPriority, turnFrame.Stage);
        Assert.Equal(DecisionKind.TurnTiming, finished.State.PendingDecision?.Kind);
        Assert.Contains(events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.Empty(RunTimingTraceValidator.Validate(events).Problems);
        Assert.Empty(StateInvariantValidator.Collect(finished.State, catalog));
    }

    [Fact]
    public void RAndDBreachUsesSnapshottedModifierAndObservationIsDetached()
    {
        BehaviorRunOperand operand = Operand();
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:d1-run-breach")
            .Add(RunDefinition(operand))
            .Build();
        CardInstanceId[] deckCards =
        [
            new("card:d1-rd-one"),
            new("card:d1-rd-two"),
            new("card:d1-rd-three"),
        ];
        RunFixture fixture = CreateFixture(
            catalog,
            operand,
            counters: [new CardCounterState(SourceCard, "counter:d1-access", 2)],
            corpDeckCards: deckCards);
        GameState actionState = OpenRunnerAction(fixture.State, catalog);
        LegalAction ability = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);
        ApplyResult started = Apply(actionState, ability, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        List<EngineEvent> events = started.Events.ToList();
        GameState cursor = started.State;

        // The R&D run has no ICE. Two passes close initiation; automatic run
        // success and the breach boundary then yield candidate selection.
        for (int pass = 0; pass < 2; pass++)
        {
            ApplyResult result = ApplyPass(cursor, catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            cursor = result.State;
        }

        BreachStartedEventPayload breach = Assert.IsType<BreachStartedEventPayload>(
            events.Single(@event => @event.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(fixture.AttackedServer, breach.ServerId);
        Assert.Equal(deckCards.Length, breach.RandomAccessLimit);
        Assert.Equal(deckCards.Length, cursor.RunState!.RandomAccessLimit);

        Observation runnerObservation = ObservationBuilder.Build(cursor, PlayerSide.Runner, catalog);
        Dictionary<string, object?> runView = Assert.IsType<Dictionary<string, object?>>(
            runnerObservation.PublicState["run"]);
        IReadOnlyList<object?> modifiers = Assert.IsAssignableFrom<IReadOnlyList<object?>>(
            runView["breachAccessModifiers"]);
        Dictionary<string, object?> modifierView = Assert.IsType<Dictionary<string, object?>>(Assert.Single(modifiers));
        Assert.Equal(SourceCard.Value, modifierView["sourceCardId"]);
        Assert.Equal(4, modifierView["delta"]);
        modifierView["delta"] = 999;
        Assert.Equal(4, cursor.RunState.BreachAccessModifiers!.Single().Delta);

        // Finish each selected physical top card and validate the complete
        // event stream. The modifier must not create duplicate access events.
        for (int index = 0; index < deckCards.Length; index++)
        {
            LegalAction choose = Assert.Single(
                cursor.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.ChooseBreachCandidate
                    && Assert.IsType<BreachCandidateParameters>(action.Parameters).Source == BreachHiddenCandidateSource.CorpDeck);
            ApplyResult selected = Apply(cursor, choose, catalog);
            Assert.True(selected.Accepted, selected.Rejection?.Message);
            events.AddRange(selected.Events);
            cursor = selected.State;

            LegalAction noAccess = Assert.Single(
                cursor.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.NoAccessAction);
            ApplyResult passed = Apply(cursor, noAccess, catalog);
            Assert.True(passed.Accepted, passed.Rejection?.Message);
            events.AddRange(passed.Events);
            cursor = passed.State;
        }

        Assert.Null(cursor.RunState);
        CardInstanceId[] accessed = events
            .Where(@event => @event.Kind == EngineEventKinds.CardAccessed)
            .Select(@event => Assert.IsType<CardAccessedEventPayload>(@event.Payload).CardId)
            .ToArray();
        Assert.Equal(deckCards, accessed);
        Assert.Empty(RunTimingTraceValidator.Validate(events).Problems);
        Assert.Empty(StateInvariantValidator.Collect(cursor, catalog));
    }

    [Fact]
    public void ModifierInvariantRejectsBasicRunDuplicateAndForgedDeclaration()
    {
        BehaviorRunOperand operand = Operand();
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:d1-run-invariant")
            .Add(RunDefinition(operand))
            .Build();
        RunFixture fixture = CreateFixture(
            catalog,
            operand,
            counters: [new CardCounterState(SourceCard, "counter:d1-access", 2)]);
        GameState actionState = OpenRunnerAction(fixture.State, catalog);
        LegalAction ability = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);
        ApplyResult started = Apply(actionState, ability, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        RunState valid = started.State.RunState!;
        RunBreachAccessModifier declared = Assert.Single(valid.BreachAccessModifiers!);

        RunState duplicate = CopyRun(valid, modifiers: [declared, declared]);
        GameState duplicateState = ReplaceRun(started.State, duplicate);
        Assert.Contains(
            StateInvariantValidator.Collect(duplicateState, catalog),
            violation => violation.Code == "run.duplicate-breach-modifier");

        RunState forgedBasic = CopyRun(
            valid,
            clearInitiatingSource: true,
            activeRunEventId: null,
            modifiers:
            [
                new RunBreachAccessModifier(
                    declared.SourceCardId,
                    declared.AttackedServerId,
                    declared.Delta,
                    declared.ReasonKey),
            ]);
        GameState forgedState = ReplaceRun(started.State, forgedBasic);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedState, catalog),
            violation => violation.Code == "run.breach-modifier-no-source");

        RunState forgedDeclaration = CopyRun(
            valid,
            modifiers:
            [
                new RunBreachAccessModifier(
                    declared.SourceCardId,
                    declared.AttackedServerId,
                    declared.Delta,
                    "reason:d1-forged"),
            ]);
        GameState forgedDeclarationState = ReplaceRun(started.State, forgedDeclaration);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedDeclarationState, catalog),
            violation => violation.Code == "run.breach-modifier-undeclared");
    }

    private static BehaviorRunOperand Operand() => new(
        ServerKind.RAndD,
        requireNotRunThisTurn: true,
        sourceCounterName: "counter:d1-access",
        breachAccessDeltaPerCounter: 2,
        breachAccessReasonKey: "reason:d1-access");

    private static CardBehaviorDefinition RunDefinition(BehaviorRunOperand operand) =>
        new(
            SourceDefinition,
            PlayerSide.Runner,
            paidAbilities:
            [
                new BehaviorPaidAbility(
                    "ability:d1-run",
                    BehaviorAbilityTiming.ActionPhase,
                    BehaviorAbilityActivation.ClickAction,
                    [new BehaviorCost(BehaviorCostKind.Clicks)],
                    [],
                    [],
                    [new BehaviorInstruction(BehaviorInstructionKind.StartRun, runOperand: operand)],
                    BehaviorAbilityUsageLimit.Unlimited),
            ]);

    private static RunFixture CreateFixture(
        BehaviorCatalog catalog,
        BehaviorRunOperand operand,
        IReadOnlyList<ServerId>? attempted = null,
        IReadOnlyList<CardCounterState>? counters = null,
        IReadOnlyList<CardInstanceId>? corpDeckCards = null)
    {
        GameState baseline = GameState.CreateNew(
            seed: 41011,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ServerId attackedServer = baseline.Servers.Single(server => server.Kind == operand.ServerKind).Id;
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState corpDeck = baseline.GetZone(ZoneKind.CorpDeck);
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, credits: 10, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        CardInstanceState source = new(SourceCard, SourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true);
        CardInstanceId[] deckIds = corpDeckCards?.ToArray() ?? [];
        CardInstanceState[] deck = deckIds
            .Select(cardId => new CardInstanceState(
                cardId,
                new CardDefinitionId($"test:d1-deck-{cardId.Value.Replace(':', '-')}"),
                corpDeck.Id,
                PlayerSide.Corp))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [SourceCard])
                : zone.Id == corpDeck.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, deckIds)
                    : zone)
            .ToArray();
        RulesState defaults = RulesState.CreateDefault(players);
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counters ?? [],
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false,
            attemptedRunServerIds: attempted);
        TurnHistoryState history = new(1, null, null, facts, null);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            baseline.Turn.TurnNumber,
            TurnStep.RunnerAction,
            history);
        GameState state = new(
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [source, .. deck],
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            baseline.SchemaVersion,
            resolutionStack: [],
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        GameState stateWithFrame = new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            [new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Runner,
                state.Turn.TurnNumber,
                TurnFrameStage.AwaitingAction)],
            null,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint);
        return new RunFixture(state, stateWithFrame, attackedServer);
    }

    private static ServerId Server(BehaviorCatalog catalog, IReadOnlyList<ServerId> attempted)
    {
        RunFixture fixture = CreateFixture(catalog, Operand(), attempted: attempted);
        return fixture.AttackedServer;
    }

    private static GameState OpenRunnerAction(GameState state, BehaviorCatalog catalog)
    {
        GameState cursor = GameEngine.BeginTurn(state, PlayerSide.Runner, catalog);
        for (int step = 0; cursor.PendingDecision?.Kind != DecisionKind.TurnAction; step++)
        {
            Assert.True(step < 8, "The Runner turn did not reach its action window.");
            ApplyResult result = ApplyPass(cursor, catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            cursor = result.State;
        }

        return cursor;
    }

    private static ApplyResult ApplyPass(GameState state, BehaviorCatalog catalog)
    {
        LegalAction pass = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return Apply(state, pass, catalog);
    }

    private static GameState MoveCardFixture(GameState state, CardInstanceId cardId, ZoneKind destinationKind)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.Zones.Single(zone => zone.Id == card.ZoneId);
        ZoneState destination = state.GetZone(destinationKind);
        if (source.Id == destination.Id)
        {
            return state;
        }

        CardInstanceId[] sourceIds = source.CardIds.Where(id => id != cardId).ToArray();
        CardInstanceId[] destinationIds = destination.CardIds.Append(cardId).ToArray();
        ZoneState[] zones = state.Zones.Select(zone => zone.Id == source.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, sourceIds)
            : zone.Id == destination.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, destinationIds)
                : zone).ToArray();
        bool faceUp = destination.Kind is ZoneKind.RunnerRig
            or ZoneKind.RunnerPlayArea
            or ZoneKind.RunnerHeap
            or ZoneKind.CorpDiscard;
        CardInstanceState[] cards = state.Cards.Select(value => value.Id == cardId
            ? new CardInstanceState(
                value.Id,
                value.DefinitionId,
                destination.Id,
                value.OwnerSide,
                faceUp,
                value.Rezzed && faceUp,
                value.AdvancementCounters)
            : value).ToArray();
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            cards,
            zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint);
    }

    private static ApplyResult Apply(GameState state, LegalAction action, BehaviorCatalog catalog) => GameEngine.Apply(
        state,
        new ActionSubmission(
            state.PendingDecision!.Owner,
            state.PendingDecision.Id,
            state.PendingDecision.Revision,
            action.Id),
        catalog);

    private static GameState ReplaceFacts(GameState state, TurnFactsState facts) => new(
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        new TurnState(
            state.Turn.ActiveSide,
            state.Turn.Phase,
            state.Turn.TurnNumber,
            state.Turn.Step,
            new TurnHistoryState(1, null, null, facts, null)),
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.SchemaVersion,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint);

    private static GameState ReplaceRun(GameState state, RunState run) => new(
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        state.Turn,
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.SchemaVersion,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        run,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint);

    private static RunState CopyRun(
        RunState source,
        CardInstanceId? initiatingSource = null,
        CardInstanceId? activeRunEventId = null,
        IReadOnlyList<RunBreachAccessModifier>? modifiers = null,
        bool clearInitiatingSource = false) => new(
        source.Version,
        source.Id,
        source.AttackedServer,
        source.InitiatingSide,
        source.InitiatingPlayer,
        clearInitiatingSource ? null : initiatingSource ?? source.InitiatingSource,
        source.Phase,
        source.Step,
        source.CurrentIceIndex,
        source.CurrentIceId,
        source.Successful,
        source.Unsuccessful,
        source.EndReason,
        source.Priority,
        source.Encounter,
        source.BreachCandidates,
        source.RandomAccessLimit,
        source.ChosenAccessCards,
        source.AccessedCards,
        source.AccessState,
        source.RunCredits,
        activeRunEventId ?? source.ActiveRunEventId,
        source.LingeringEffects,
        modifiers ?? source.BreachAccessModifiers);

    private sealed record RunFixture(
        GameState State,
        GameState StateWithTurnFrame,
        ServerId AttackedServer);
}
