using Xunit;

namespace Netrunner.Core.Tests;

public sealed class FirstBasicDrawReplacementTests
{
    private static readonly CardDefinitionId SourceDefinition = new("test:first-basic-draw-source");

    [Fact]
    public void DeclarationValidatesRoundTripsAndChangesCatalogFingerprint()
    {
        BehaviorFirstBasicDrawReplacement replacement = new(2, "first-basic-draw-test");
        CardBehaviorDefinition plain = new(SourceDefinition, PlayerSide.Runner);
        CardBehaviorDefinition declared = new(
            SourceDefinition,
            PlayerSide.Runner,
            firstBasicDrawReplacements: [replacement]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BehaviorFirstBasicDrawReplacement(0, "first-basic-draw-test"));
        Assert.Throws<ArgumentException>(
            () => new BehaviorFirstBasicDrawReplacement(2, "not stable"));
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstBasicDrawReplacements: [replacement, replacement]));

        string json = EngineJson.Serialize(declared);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
        Assert.Contains("firstBasicDrawReplacements", json, StringComparison.Ordinal);
        Assert.Equal(declared.FirstBasicDrawReplacements, restored.FirstBasicDrawReplacements);

        string legacyJson = EngineJson.Serialize(plain);
        string emptyJson = EngineJson.Serialize(new CardBehaviorDefinition(
            SourceDefinition,
            PlayerSide.Runner,
            firstBasicDrawReplacements: []));
        Assert.Equal(legacyJson, emptyJson);
        Assert.DoesNotContain("firstBasicDrawReplacements", legacyJson, StringComparison.Ordinal);

        BehaviorCatalog baseline = new BehaviorCatalogBuilder("test:first-basic-draw-fingerprint")
            .Add(plain)
            .Build();
        BehaviorCatalog changed = new BehaviorCatalogBuilder("test:first-basic-draw-fingerprint")
            .Add(declared)
            .Build();
        Assert.NotEqual(baseline.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void RealFirstDrawUsesOneClickAndMandatoryReplacementDrawsConfiguredCount()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);

        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(3, opened.State.GetPlayer(PlayerSide.Runner).Clicks);
        Assert.Single(opened.State.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds);
        Assert.Equal(DecisionKind.InterruptChoice, opened.State.PendingDecision!.Kind);
        Assert.Single(opened.State.PendingDecision.LegalActions);
        Assert.Equal(ActionKind.UseReplacement, opened.State.PendingDecision.LegalActions[0].Kind);
        Assert.Empty(opened.State.GetZone(ZoneKind.RunnerGrip).CardIds);

        ApplyResult used = Apply(opened.State, opened.State.PendingDecision.LegalActions[0], catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(2, used.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(stack.Take(2), used.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(3, used.State.GetPlayer(PlayerSide.Runner).Clicks);
        Assert.Equal(1, used.State.Turn.ThisTurnFacts!.GetFirstXCount(source, "first-basic-draw-test"));
        Assert.Empty(StateInvariantValidator.Collect(used.State, catalog));
    }

    [Fact]
    public void SecondRealDrawIsNormalAndGlobalLedgerRetainsBothOccurrences()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult first = Apply(actionState, draw, catalog);
        Assert.True(first.Accepted, first.Rejection?.Message);
        ApplyResult replaced = Apply(first.State, first.State.PendingDecision!.LegalActions.Single(), catalog);
        Assert.True(replaced.Accepted, replaced.Rejection?.Message);
        GameState afterPriority = Pass(Pass(replaced.State, catalog), catalog);

        LegalAction secondDraw = Assert.Single(afterPriority.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult second = Apply(afterPriority, secondDraw, catalog);
        Assert.True(second.Accepted, second.Rejection?.Message);
        Assert.Equal(3, second.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(stack.Take(3), second.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(2, second.State.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds.Count);
        Assert.Equal(1, second.State.Turn.ThisTurnFacts.GetFirstXCount(source, "first-basic-draw-test"));
        Assert.DoesNotContain(second.State.ResolutionStack, frame => frame is EffectCheckpointFrame);
    }

    [Fact]
    public void NoSourceStillRecordsAcceptedBasicDrawAndLateSourceCannotRearm()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture(includeSource: false);
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult result = Apply(actionState, draw, catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        GameState late = AddSource(result.State, source, catalog);
        GameState nextAction = Pass(Pass(late, catalog), catalog);
        LegalAction second = Assert.Single(nextAction.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult secondResult = Apply(nextAction, second, catalog);
        Assert.True(secondResult.Accepted, secondResult.Rejection?.Message);
        Assert.Equal(2, secondResult.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(stack.Take(2), secondResult.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(2, secondResult.State.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds.Count);
        Assert.Equal(0, secondResult.State.Turn.ThisTurnFacts.GetFirstXCount(source, "first-basic-draw-test"));
    }

    [Fact]
    public void PendingReplacementRoundTripsWithStableStateHash()
    {
        (GameState state, BehaviorCatalog catalog, _, _) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void MultipleActiveSourcesArbitrateDeterministicallyWithoutStacking()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture();
        CardInstanceId secondSource = new("card:first-basic-draw-source-two");
        state = AddSource(state, secondSource, catalog);

        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Single(opened.State.PendingDecision!.LegalActions);
        Assert.Equal(ActionKind.UseReplacement, opened.State.PendingDecision.LegalActions[0].Kind);

        ApplyResult used = Apply(opened.State, opened.State.PendingDecision.LegalActions[0], catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(stack.Take(2), used.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(1, used.State.Turn.ThisTurnFacts!.GetFirstXCount(source, "first-basic-draw-test"));
        Assert.Equal(0, used.State.Turn.ThisTurnFacts.GetFirstXCount(secondSource, "first-basic-draw-test"));
        Assert.Empty(StateInvariantValidator.Collect(used.State, catalog));
    }

    [Fact]
    public void SourceMovementAfterMaterializationDoesNotInvalidateReplacement()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);

        GameState moved = MoveCard(opened.State, source, ZoneKind.RunnerHeap, catalog);
        Assert.Empty(StateInvariantValidator.Collect(moved, catalog));
        ApplyResult used = Apply(moved, moved.PendingDecision!.LegalActions.Single(), catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(stack.Take(2), used.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(1, used.State.Turn.ThisTurnFacts!.GetFirstXCount(source, "first-basic-draw-test"));
    }

    [Fact]
    public void FormalTurnBoundaryResetsGlobalDrawLedgerAndSourceUse()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        ApplyResult used = Apply(opened.State, opened.State.PendingDecision!.LegalActions.Single(), catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);

        GameState nextTurn = StartRunnerFormalTurn(used.State, catalog, turnNumber: 2);
        GameState nextAction = BeginAction(nextTurn, catalog);
        LegalAction nextDraw = Assert.Single(nextAction.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult nextOpened = Apply(nextAction, nextDraw, catalog);
        Assert.True(nextOpened.Accepted, nextOpened.Rejection?.Message);
        Assert.Equal(DecisionKind.InterruptChoice, nextOpened.State.PendingDecision!.Kind);
        ApplyResult nextUsed = Apply(nextOpened.State, nextOpened.State.PendingDecision.LegalActions.Single(), catalog);
        Assert.True(nextUsed.Accepted, nextUsed.Rejection?.Message);
        Assert.Equal(4, nextUsed.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(stack.Take(4), nextUsed.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Single(nextUsed.State.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds);
        Assert.Equal(1, nextUsed.State.Turn.ThisTurnFacts.GetFirstXCount(source, "first-basic-draw-test"));
    }

    [Fact]
    public void RejectedOrStaleReplacementCommandIsAtomic()
    {
        (GameState state, BehaviorCatalog catalog, _, _) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        string beforeHash = DeterministicHash.ForState(opened.State);
        PendingDecision pending = opened.State.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            opened.State,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, new ActionId("action:stale-basic-draw")),
            catalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.UnknownAction, rejected.Rejection!.Code);
        Assert.Equal(beforeHash, DeterministicHash.ForState(rejected.State));
        Assert.Empty(rejected.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(0, rejected.State.Turn.ThisTurnFacts!.GetFirstXCount(new CardInstanceId("card:first-basic-draw-source"), "first-basic-draw-test"));
    }

    [Fact]
    public void ReplacementDrawStopsCleanlyWhenRunnerStackRunsShort()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture(stackCount: 1);
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        ApplyResult used = Apply(opened.State, opened.State.PendingDecision!.LegalActions.Single(), catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(stack, used.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Empty(used.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Equal(3, used.State.GetPlayer(PlayerSide.Runner).Clicks);
        Assert.Equal(1, used.State.Turn.ThisTurnFacts!.GetFirstXCount(source, "first-basic-draw-test"));
        Assert.DoesNotContain(used.State.ResolutionStack, frame => frame is EffectCheckpointFrame);
    }

    [Fact]
    public void GenericBehaviorDrawDoesNotEnterBasicDrawReplacementPipeline()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, CardInstanceId[] stack) = CreateFixture(
            includeReplacement: false,
            sourceInstructions: [new BehaviorInstruction(BehaviorInstructionKind.Draw, 1)]);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(source, runner),
            catalog);

        Assert.Equal(stack[0], Assert.Single(result.GetZone(ZoneKind.RunnerGrip).CardIds));
        Assert.Empty(result.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds);
        Assert.Equal(0, result.Turn.ThisTurnFacts.GetFirstXCount(source, "first-basic-draw-test"));
        Assert.DoesNotContain(result.ResolutionStack, frame => frame is EffectCheckpointFrame);
        Assert.Empty(StateInvariantValidator.Collect(result, catalog));
    }

    [Fact]
    public void CorpMandatoryDrawRemainsDirectAndDoesNotEnterBasicDrawPipeline()
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:corp-basic-draw-catalog").Build();
        GameState baseline = GameState.CreateNew(
            seed: 418,
            behaviorRegistryVersion: catalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState deck = baseline.GetZone(ZoneKind.CorpDeck);
        CardInstanceId cardId = new("card:corp-mandatory-draw");
        CardInstanceState card = new(
            cardId,
            new CardDefinitionId("test:corp-mandatory-card"),
            deck.Id,
            PlayerSide.Corp);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            [card],
            baseline.Zones.Select(zone => zone.Id == deck.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [cardId])
                : zone).ToArray(),
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            behaviorRegistryVersion: catalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        StateInvariantValidator.Validate(state, catalog);

        GameState opened = GameEngine.BeginTurn(state, PlayerSide.Corp, catalog);
        GameState formal = Pass(Pass(opened, catalog), catalog);
        Assert.Single(formal.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Empty(formal.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.Empty(formal.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds);
        Assert.DoesNotContain(formal.ResolutionStack, frame => frame is EffectCheckpointFrame);
        Assert.Empty(StateInvariantValidator.Collect(formal, catalog));
    }

    [Fact]
    public void ForgedBasicDrawFactsAndInvocationAreRejectedByInvariantMatrix()
    {
        (GameState state, BehaviorCatalog catalog, CardInstanceId source, _) = CreateFixture();
        GameState actionState = BeginAction(state, catalog);
        LegalAction draw = Assert.Single(actionState.PendingDecision!.LegalActions, action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(actionState, draw, catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);

        TurnFactsState facts = opened.State.Turn.ThisTurnFacts!;
        GameState missingLedger = WithRunnerFacts(opened.State, new TurnFactsState(
            1,
            PlayerSide.Runner,
            facts.TurnNumber,
            facts.ClicksAllotted,
            facts.ClicksLost,
            facts.TurnBegan,
            facts.MandatoryDrawPerformed,
            facts.ActionPhaseEnded,
            facts.DiscardPhaseEnded,
            facts.InstalledCardIds,
            facts.SuccessfulRun,
            facts.UsedAbilityKeys,
            facts.AttemptedRunServerIds,
            facts.SuccessfulRunServerIds,
            facts.AccessedCardOccurrenceIds,
            facts.TrashedCardOccurrenceIds,
            facts.AgendaTurnChanges,
            facts.TagTurnChanges,
            facts.FirstXUseKeys,
            facts.ProgramInstallOccurrenceIds,
            facts.HqBreachOccurrenceServerIds,
            []),
            catalog);
        Assert.Contains(
            StateInvariantValidator.Collect(missingLedger, catalog),
            violation => violation.Code == "turn.basic-draw-first-ledger-mismatch");

        FirstXUseKey consumed = new(source, "first-basic-draw-test", 1);
        GameState consumedPending = WithRunnerFacts(opened.State, new TurnFactsState(
            1,
            PlayerSide.Runner,
            facts.TurnNumber,
            facts.ClicksAllotted,
            facts.ClicksLost,
            facts.TurnBegan,
            facts.MandatoryDrawPerformed,
            facts.ActionPhaseEnded,
            facts.DiscardPhaseEnded,
            facts.InstalledCardIds,
            facts.SuccessfulRun,
            facts.UsedAbilityKeys,
            facts.AttemptedRunServerIds,
            facts.SuccessfulRunServerIds,
            facts.AccessedCardOccurrenceIds,
            facts.TrashedCardOccurrenceIds,
            facts.AgendaTurnChanges,
            facts.TagTurnChanges,
            [consumed],
            facts.ProgramInstallOccurrenceIds,
            facts.HqBreachOccurrenceServerIds,
            facts.BasicDrawOccurrenceIds),
            catalog);
        Assert.Contains(
            StateInvariantValidator.Collect(consumedPending, catalog),
            violation => violation.Code == "turn.basic-draw-first-x-already-consumed");

        GameState duplicateLedger = WithRunnerFacts(opened.State, new TurnFactsState(
            1,
            PlayerSide.Runner,
            facts.TurnNumber,
            facts.ClicksAllotted,
            facts.ClicksLost,
            facts.TurnBegan,
            facts.MandatoryDrawPerformed,
            facts.ActionPhaseEnded,
            facts.DiscardPhaseEnded,
            facts.InstalledCardIds,
            facts.SuccessfulRun,
            facts.UsedAbilityKeys,
            facts.AttemptedRunServerIds,
            facts.SuccessfulRunServerIds,
            facts.AccessedCardOccurrenceIds,
            facts.TrashedCardOccurrenceIds,
            facts.AgendaTurnChanges,
            facts.TagTurnChanges,
            facts.FirstXUseKeys,
            facts.ProgramInstallOccurrenceIds,
            facts.HqBreachOccurrenceServerIds,
            [facts.BasicDrawOccurrenceIds[0], facts.BasicDrawOccurrenceIds[0]]),
            catalog);
        Assert.Contains(
            StateInvariantValidator.Collect(duplicateLedger, catalog),
            violation => violation.Code == "turn.duplicate-basic-draw-occurrence");

        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(opened.State.ResolutionStack[^1]);
        InterruptInvocation invocation = Assert.Single(checkpoint.InterruptQueue);
        InterruptInvocation forgedInvocation = new(
            invocation.Version,
            invocation.Id,
            invocation.Controller,
            invocation.Kind,
            invocation.Mandatory,
            invocation.BehaviorId,
            invocation.SourceCardId,
            invocation.SourceDefinitionId,
            invocation.SourceZoneId,
            invocation.Occurrence,
            invocation.Costs,
            invocation.MaximumPreventionAmount,
            invocation.ReplacementEffects,
            invocation.InterruptKey,
            new string('a', 64),
            invocation.DefinitionFingerprint,
            invocation.SourceOwnerSide,
            invocation.SourceZoneKind,
            invocation.BasicDrawCount,
            invocation.ReplacementReasonKey);
        EffectCheckpointFrame forgedCheckpoint = new(
            checkpoint.Version,
            checkpoint.BehaviorId,
            checkpoint.Controller,
            checkpoint.Occurrence,
            checkpoint.OriginalEffect,
            checkpoint.CurrentEffect,
            checkpoint.Stage,
            checkpoint.TriggerQueue,
            checkpoint.TriggerCursor,
            [forgedInvocation],
            checkpoint.InterruptCursor,
            checkpoint.AppliedTriggers,
            checkpoint.AppliedInterrupts,
            checkpoint.ReplacementDepth,
            checkpoint.ChildKind,
            checkpoint.ChildTriggerId,
            checkpoint.ChildInterruptId,
            checkpoint.ResolvedOccurrence);
        GameState forgedProvenance = WithResolutionTop(opened.State, forgedCheckpoint, catalog);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedProvenance, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-catalog-mismatch");

        PlayerId corpPlayer = opened.State.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ZoneState grip = opened.State.GetZone(ZoneKind.RunnerGrip);

        InterruptInvocation ForgeInvocation(
            PlayerId? controller = null,
            ZoneId? sourceZoneId = null,
            ZoneKind? sourceZoneKind = null,
            OccurrenceSnapshot? occurrence = null) => new(
            invocation.Version,
            invocation.Id,
            controller ?? invocation.Controller,
            invocation.Kind,
            invocation.Mandatory,
            invocation.BehaviorId,
            invocation.SourceCardId,
            invocation.SourceDefinitionId,
            sourceZoneId ?? invocation.SourceZoneId,
            occurrence ?? invocation.Occurrence,
            invocation.Costs,
            invocation.MaximumPreventionAmount,
            invocation.ReplacementEffects,
            invocation.InterruptKey,
            invocation.CatalogFingerprint,
            invocation.DefinitionFingerprint,
            invocation.SourceOwnerSide,
            sourceZoneKind ?? invocation.SourceZoneKind,
            invocation.BasicDrawCount,
            invocation.ReplacementReasonKey);

        GameState ForgeCheckpoint(
            InterruptInvocation candidate,
            OccurrenceSnapshot? occurrence = null,
            PlayerId? controller = null) => WithResolutionTop(
            opened.State,
            new EffectCheckpointFrame(
                checkpoint.Version,
                checkpoint.BehaviorId,
                controller ?? checkpoint.Controller,
                occurrence ?? checkpoint.Occurrence,
                checkpoint.OriginalEffect,
                checkpoint.CurrentEffect,
                checkpoint.Stage,
                checkpoint.TriggerQueue,
                checkpoint.TriggerCursor,
                [candidate],
                checkpoint.InterruptCursor,
                checkpoint.AppliedTriggers,
                checkpoint.AppliedInterrupts,
                checkpoint.ReplacementDepth,
                checkpoint.ChildKind,
                checkpoint.ChildTriggerId,
                checkpoint.ChildInterruptId,
                checkpoint.ResolvedOccurrence),
            catalog);

        GameState invalidZone = ForgeCheckpoint(ForgeInvocation(
            sourceZoneId: grip.Id,
            sourceZoneKind: ZoneKind.RunnerGrip));
        Assert.Contains(
            StateInvariantValidator.Collect(invalidZone, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-source-zone-inactive");

        GameState invalidController = ForgeCheckpoint(ForgeInvocation(controller: corpPlayer));
        Assert.Contains(
            StateInvariantValidator.Collect(invalidController, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-controller-mismatch");

        OccurrenceSnapshot wrongInvocationController = new(
            invocation.Occurrence.Version,
            invocation.Occurrence.Id,
            invocation.Occurrence.Kind,
            invocation.Occurrence.Phase,
            corpPlayer,
            invocation.Occurrence.TargetPlayer,
            invocation.Occurrence.SourceCardId,
            invocation.Occurrence.SourceDefinitionId,
            invocation.Occurrence.SourceZoneId,
            invocation.Occurrence.Effect);
        GameState invalidInvocationOccurrenceController = ForgeCheckpoint(ForgeInvocation(
            occurrence: wrongInvocationController));
        Assert.Contains(
            StateInvariantValidator.Collect(invalidInvocationOccurrenceController, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-occurrence-controller-mismatch");

        OccurrenceSnapshot wrongInvocationTarget = new(
            invocation.Occurrence.Version,
            invocation.Occurrence.Id,
            invocation.Occurrence.Kind,
            invocation.Occurrence.Phase,
            invocation.Occurrence.Controller,
            corpPlayer,
            invocation.Occurrence.SourceCardId,
            invocation.Occurrence.SourceDefinitionId,
            invocation.Occurrence.SourceZoneId,
            invocation.Occurrence.Effect);
        GameState invalidInvocationTarget = ForgeCheckpoint(ForgeInvocation(
            occurrence: wrongInvocationTarget));
        Assert.Contains(
            StateInvariantValidator.Collect(invalidInvocationTarget, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-target-mismatch");

        OccurrenceSnapshot wrongCheckpointController = new(
            checkpoint.Occurrence.Version,
            checkpoint.Occurrence.Id,
            checkpoint.Occurrence.Kind,
            checkpoint.Occurrence.Phase,
            corpPlayer,
            checkpoint.Occurrence.TargetPlayer,
            checkpoint.Occurrence.SourceCardId,
            checkpoint.Occurrence.SourceDefinitionId,
            checkpoint.Occurrence.SourceZoneId,
            checkpoint.Occurrence.Effect);
        GameState invalidCheckpointOccurrenceController = ForgeCheckpoint(
            invocation,
            occurrence: wrongCheckpointController);
        Assert.Contains(
            StateInvariantValidator.Collect(invalidCheckpointOccurrenceController, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-occurrence-controller-mismatch");

        OccurrenceSnapshot wrongCheckpointTarget = new(
            checkpoint.Occurrence.Version,
            checkpoint.Occurrence.Id,
            checkpoint.Occurrence.Kind,
            checkpoint.Occurrence.Phase,
            checkpoint.Occurrence.Controller,
            corpPlayer,
            checkpoint.Occurrence.SourceCardId,
            checkpoint.Occurrence.SourceDefinitionId,
            checkpoint.Occurrence.SourceZoneId,
            checkpoint.Occurrence.Effect);
        GameState invalidCheckpointTarget = ForgeCheckpoint(
            invocation,
            occurrence: wrongCheckpointTarget);
        Assert.Contains(
            StateInvariantValidator.Collect(invalidCheckpointTarget, catalog),
            violation => violation.Code == "resolution.basic-draw-replacement-target-mismatch");
    }

    private static ApplyResult Apply(GameState state, LegalAction action, BehaviorCatalog catalog) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(state.PendingDecision!.Owner, state.PendingDecision.Id, state.PendingDecision.Revision, action.Id),
            catalog);

    private static GameState BeginAction(GameState state, BehaviorCatalog catalog) => GameEngine.BeginResolution(
        state,
        new TurnFrame(PlayerSide.Runner, state.Turn.TurnNumber, TurnFrameStage.AwaitingAction),
        catalog);

    private static GameState Pass(GameState state, BehaviorCatalog catalog)
    {
        LegalAction pass = Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = Apply(state, pass, catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static (GameState State, BehaviorCatalog Catalog, CardInstanceId Source, CardInstanceId[] Stack) CreateFixture(
        bool includeSource = true,
        int stackCount = 5,
        bool includeReplacement = true,
        IReadOnlyList<BehaviorInstruction>? sourceInstructions = null)
    {
        CardDefinitionId stackDefinition = new("test:first-basic-draw-stack");
        BehaviorFirstBasicDrawReplacement[]? replacements = includeReplacement
            ? [new BehaviorFirstBasicDrawReplacement(2, "first-basic-draw-test")]
            : null;
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:first-basic-draw-catalog")
            .Add(new CardBehaviorDefinition(
                SourceDefinition,
                PlayerSide.Runner,
                instructions: sourceInstructions,
                firstBasicDrawReplacements: replacements))
            .Build();
        GameState baseline = GameState.CreateNew(
            seed: 417,
            behaviorRegistryVersion: catalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState stackZone = baseline.GetZone(ZoneKind.RunnerStack);
        CardInstanceId source = new("card:first-basic-draw-source");
        CardInstanceId[] allStack =
        [
            new CardInstanceId("card:first-basic-draw-one"),
            new CardInstanceId("card:first-basic-draw-two"),
            new CardInstanceId("card:first-basic-draw-three"),
            new CardInstanceId("card:first-basic-draw-four"),
            new CardInstanceId("card:first-basic-draw-five"),
        ];
        CardInstanceId[] stack = allStack.Take(stackCount).ToArray();
        CardInstanceState[] cards = stack
            .Select(card => new CardInstanceState(card, stackDefinition, stackZone.Id, PlayerSide.Runner))
            .Concat(includeSource
                ? [new CardInstanceState(source, SourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true)]
                : [])
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, includeSource ? [source] : [])
                : zone.Id == stackZone.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, stack)
                    : zone)
            .ToArray();
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(runner.Id, runner.Side, runner.Credits, 4, runner.AgendaPoints)
                : player)
            .ToArray();
        TurnFactsState facts = new(1, PlayerSide.Runner, 1, 4, 0, true, false, false, false);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                1,
                TurnStep.RunnerAction,
                new TurnHistoryState(1, null, null, facts, null)),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            behaviorRegistryVersion: catalog.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        StateInvariantValidator.Validate(state, catalog);
        return (state, catalog, source, stack);
    }

    private static GameState StartRunnerFormalTurn(GameState source, BehaviorCatalog catalog, int turnNumber)
    {
        PlayerState runner = source.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players = source.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(runner.Id, runner.Side, runner.Credits, 4, runner.AgendaPoints)
                : player)
            .ToArray();
        TurnFactsState facts = new(1, PlayerSide.Runner, turnNumber, 4, 0, true, false, false, false);
        TurnHistoryState history = new(
            1,
            source.Turn.History?.CorpThisTurn,
            source.Turn.History?.CorpLastTurn,
            facts,
            source.Turn.History?.RunnerThisTurn);
        GameState next = new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            players,
            source.Cards,
            source.Zones,
            source.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, turnNumber, TurnStep.RunnerAction, history),
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            [],
            null,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            catalog.BehaviorRegistryVersion,
            catalog.Fingerprint);
        StateInvariantValidator.Validate(next, catalog);
        return next;
    }

    private static GameState WithRunnerFacts(GameState source, TurnFactsState facts, BehaviorCatalog catalog)
    {
        TurnHistoryState existing = source.Turn.History ?? new TurnHistoryState();
        TurnHistoryState history = new(
            existing.Version,
            existing.CorpThisTurn,
            existing.CorpLastTurn,
            facts,
            existing.RunnerLastTurn);
        return new GameState(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards,
            source.Zones,
            source.Servers,
            new TurnState(source.Turn.ActiveSide, source.Turn.Phase, source.Turn.TurnNumber, source.Turn.Step, history),
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            catalog.BehaviorRegistryVersion,
            catalog.Fingerprint);
    }

    private static GameState MoveCard(GameState source, CardInstanceId cardId, ZoneKind destinationKind, BehaviorCatalog catalog)
    {
        CardInstanceState card = source.Cards.Single(value => value.Id == cardId);
        ZoneState origin = source.Zones.Single(zone => zone.Id == card.ZoneId);
        ZoneState destination = source.GetZone(destinationKind);
        ZoneState[] zones = source.Zones
            .Select(zone => zone.Id == origin.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destination.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        bool faceUp = destination.Kind is ZoneKind.RunnerRig
            or ZoneKind.RunnerPlayArea
            or ZoneKind.RunnerHeap
            or ZoneKind.CorpDiscard;
        CardInstanceState movedCard = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            faceUp || card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters);
        return new GameState(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards.Select(value => value.Id == cardId ? movedCard : value).ToArray(),
            zones,
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            catalog.BehaviorRegistryVersion,
            catalog.Fingerprint);
    }

    private static GameState WithResolutionTop(GameState source, ResolutionFrame top, BehaviorCatalog catalog)
    {
        ResolutionFrame[] stack = source.ResolutionStack
            .Take(source.ResolutionStack.Count - 1)
            .Append(top)
            .ToArray();
        return new GameState(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards,
            source.Zones,
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            stack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            catalog.BehaviorRegistryVersion,
            catalog.Fingerprint);
    }

    private static GameState AddSource(GameState source, CardInstanceId sourceCardId, BehaviorCatalog catalog)
    {
        ZoneState rig = source.GetZone(ZoneKind.RunnerRig);
        CardInstanceState card = new(sourceCardId, SourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true);
        GameState next = new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards.Append(card).ToArray(),
            source.Zones.Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(sourceCardId).ToArray())
                : zone).ToArray(),
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            catalog.BehaviorRegistryVersion,
            catalog.Fingerprint);
        StateInvariantValidator.Validate(next, catalog);
        return next;
    }
}
