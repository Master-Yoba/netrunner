using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// ENG-405 ordinary Event/Operation play coverage. These tests intentionally
/// use synthetic metadata and generic behavior catalogs so the turn reducer,
/// runtime catalog, and behavior catalog are exercised independently of card
/// registrations.
/// </summary>
public sealed class OrdinaryCardPlayTests
{
    [Theory]
    [InlineData(PlayerSide.Runner, 2, 4)]
    [InlineData(PlayerSide.Corp, 1, 3)]
    public void TurnActionExposesOrdinaryPlayActionsInPhysicalHandOrder(
        PlayerSide side,
        int firstCost,
        int secondCost)
    {
        CardDefinitionId firstDefinition = Definition(side, "first");
        CardDefinitionId secondDefinition = Definition(side, "second");
        CardInstanceId firstCard = Instance(side, "first");
        CardInstanceId secondCard = Instance(side, "second");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-play-actions")
            .Add(Behavior(firstDefinition, side, firstCost))
            .Add(Behavior(secondDefinition, side, secondCost))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-play-actions")
            .Add(Runtime(firstDefinition, side, firstCost))
            .Add(Runtime(secondDefinition, side, secondCost))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [
                new FixtureCard(firstCard, firstDefinition),
                new FixtureCard(secondCard, secondDefinition),
            ],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);

        GameState state = OpenTurnAction(fixture);
        ZoneKind sourceKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        Assert.Equal([firstCard, secondCard], state.GetZone(sourceKind).CardIds.Take(2));

        PlayCardParameters[] plays = state.PendingDecision!
            .LegalActions
            .Where(action => action.Kind == ActionKind.PlayCard)
            .Select(action => Assert.IsType<PlayCardParameters>(action.Parameters))
            .ToArray();
        Assert.Equal(
            [
                new PlayCardParameters(firstCard, firstCost),
                new PlayCardParameters(secondCard, secondCost),
            ],
            plays);
    }

    [Theory]
    [InlineData(OrdinaryPlayExclusion.NoClicks)]
    [InlineData(OrdinaryPlayExclusion.Unaffordable)]
    [InlineData(OrdinaryPlayExclusion.WrongClass)]
    [InlineData(OrdinaryPlayExclusion.MissingGenericBehavior)]
    [InlineData(OrdinaryPlayExclusion.RuntimeBehaviorCostMismatch)]
    [InlineData(OrdinaryPlayExclusion.NonCreditsUpfrontCost)]
    [InlineData(OrdinaryPlayExclusion.LegacyRunEventOnly)]
    public void TurnActionExcludesCardsWithoutEveryOrdinaryPlayFact(OrdinaryPlayExclusion exclusion)
    {
        const PlayerSide side = PlayerSide.Runner;
        CardDefinitionId definitionId = new($"test:ordinary-exclusion-{exclusion.ToString().ToLowerInvariant()}");
        CardInstanceId cardId = new($"card:ordinary-exclusion-{exclusion.ToString().ToLowerInvariant()}");
        CardRuntimeSpec runtime = exclusion == OrdinaryPlayExclusion.WrongClass
            ? new CardRuntimeSpec(
                definitionId,
                side,
                RuntimeCardClass.Program,
                installCost: 0)
            : Runtime(definitionId, side, exclusion == OrdinaryPlayExclusion.RuntimeBehaviorCostMismatch ? 2 : 1);
        BehaviorCatalogBuilder behaviorBuilder = new("test:ordinary-play-exclusions");
        if (exclusion == OrdinaryPlayExclusion.LegacyRunEventOnly)
        {
            behaviorBuilder.AddRunEvent(new RunEventBehaviorSpec(definitionId, cost: 1, hostedCredits: 0));
        }
        else if (exclusion != OrdinaryPlayExclusion.MissingGenericBehavior)
        {
            IReadOnlyList<BehaviorCost> costs = exclusion == OrdinaryPlayExclusion.NonCreditsUpfrontCost
                ? [new BehaviorCost(BehaviorCostKind.Clicks, 1)]
                : [new BehaviorCost(BehaviorCostKind.Credits, 1)];
            behaviorBuilder.Add(new CardBehaviorDefinition(
                definitionId,
                side,
                costs,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]));
        }

        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-play-exclusions")
            .Add(runtime)
            .Build();
        BehaviorCatalog behaviorCatalog = behaviorBuilder.Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: exclusion == OrdinaryPlayExclusion.Unaffordable ? 0 : 5);

        GameState state = OpenTurnAction(fixture);
        if (exclusion == OrdinaryPlayExclusion.NoClicks)
        {
            state = SpendAllClicksWithGainCredit(state, fixture);
            Assert.Equal(0, state.GetPlayer(fixture.PlayerId).Clicks);
            Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision!.Kind);
            Assert.DoesNotContain(state.PendingDecision.LegalActions, action => action.Kind == ActionKind.PlayCard);
        }

        Assert.Empty(GameEngine.MaterializePlayCardParameters(state, behaviorCatalog, runtimeCatalog));
    }

    [Fact]
    public void TopLevelInstalledTargetPreconditionExcludesPlayWithoutACandidate()
    {
        const PlayerSide side = PlayerSide.Corp;
        CardDefinitionId definitionId = Definition(side, "target-precondition");
        CardInstanceId cardId = Instance(side, "target-precondition");
        BehaviorTarget target = new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Corp));
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-target-precondition")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                targets: [target],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-target-precondition")
            .Add(Runtime(definitionId, side, 1))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);

        GameState state = OpenTurnAction(fixture);

        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PlayCard);
        Assert.Empty(GameEngine.MaterializePlayCardParameters(state, behaviorCatalog, runtimeCatalog));
    }

    [Fact]
    public void TopLevelInstalledTargetPreconditionIsMaterializedAndRevalidated()
    {
        const PlayerSide side = PlayerSide.Corp;
        CardDefinitionId definitionId = Definition(side, "target-precondition-valid");
        CardInstanceId cardId = Instance(side, "target-precondition-valid");
        CardInstanceId installedId = new("card:ordinary-target-precondition-installed");
        CardDefinitionId installedDefinition = new("test:ordinary-target-precondition-installed");
        BehaviorTarget target = new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Corp));
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-target-precondition-valid")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 1)],
                targets: [target],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-target-precondition-valid")
            .Add(Runtime(definitionId, side, 1))
            .Add(new CardRuntimeSpec(
                installedDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Asset,
                rezCost: 0))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        fixture = fixture with
        {
            State = AddInstalledCorpCard(
                fixture.State,
                installedId,
                installedDefinition),
        };

        GameState state = OpenTurnAction(fixture);
        LegalAction play = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        Assert.Equal(cardId, Assert.IsType<PlayCardParameters>(play.Parameters).CardId);

        GameState stale = MoveCardOutOfCorpServerRoot(state, installedId);
        ApplyResult rejected = Apply(stale, fixture, play);

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(stale, rejected.State);
        Assert.Empty(rejected.Events);
    }

    [Fact]
    public void RunnerEventPlayExecutesGenericBehaviorAndCompletesInOneBatch()
    {
        const PlayerSide side = PlayerSide.Runner;
        CardDefinitionId definitionId = new("test:ordinary-runner-gain");
        CardInstanceId cardId = new("card:ordinary-runner-gain");
        const int printedCost = 5;
        const int gain = 9;
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-runner-gain")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                costs: [new BehaviorCost(BehaviorCostKind.Credits, printedCost)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, gain)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-runner-gain")
            .Add(Runtime(definitionId, side, printedCost))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: printedCost);
        GameState state = OpenTurnAction(fixture);
        PlayerState runnerBefore = state.GetPlayer(fixture.PlayerId);
        LegalAction play = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);

        ApplyResult result = Apply(state, fixture, play);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardPlayStarted,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardPlayCompleted,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(value => value.Kind));
        Assert.Equal(runnerBefore.Clicks - 1, result.State.GetPlayer(fixture.PlayerId).Clicks);
        Assert.Equal(9, result.State.GetPlayer(fixture.PlayerId).Credits);
        CreditsAdjustedEventPayload[] creditEvents = result.Events
            .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
            .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload))
            .ToArray();
        Assert.Equal([-printedCost, gain], creditEvents.Select(value => value.Delta));
        Assert.All(creditEvents, value => Assert.Equal(fixture.PlayerId, value.Target));

        ZoneState heap = result.State.GetZone(ZoneKind.RunnerHeap);
        Assert.Single(heap.CardIds, cardId);
        Assert.True(result.State.GetCard(cardId).FaceUp);
        ZoneState playArea = Assert.Single(
            result.State.Zones,
            zone => zone.Kind == ZoneKind.RunnerPlayArea && zone.OwnerSide == PlayerSide.Runner);
        Assert.Empty(playArea.CardIds);
        Assert.Single(result.State.ResolutionStack);
        TurnFrame turn = Assert.IsType<TurnFrame>(result.State.ResolutionStack[0]);
        Assert.Equal(TurnFrameStage.ActionPriority, turn.Stage);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(fixture.PlayerId, result.State.PendingDecision.Owner);
    }

    [Fact]
    public void CorpOperationPlayExecutesZeroCostBehaviorWithoutZeroCreditEvent()
    {
        const PlayerSide side = PlayerSide.Corp;
        CardDefinitionId definitionId = new("test:ordinary-corp-gain");
        CardInstanceId cardId = new("card:ordinary-corp-gain");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-corp-gain")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 3)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-corp-gain")
            .Add(Runtime(definitionId, side, playCost: 0))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        GameState state = OpenTurnAction(fixture);
        LegalAction play = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);

        ApplyResult result = Apply(state, fixture, play);

        Assert.True(result.Accepted, result.Rejection?.Message);
        CreditsAdjustedEventPayload[] creditEvents = result.Events
            .Where(value => value.Kind == EngineEventKinds.CreditsAdjusted)
            .Select(value => Assert.IsType<CreditsAdjustedEventPayload>(value.Payload))
            .ToArray();
        Assert.Single(creditEvents);
        Assert.Equal(3, creditEvents[0].Delta);
        Assert.DoesNotContain(creditEvents, value => value.Delta == 0);
        Assert.Equal(8, result.State.GetPlayer(fixture.PlayerId).Credits);
        Assert.Equal(2, result.State.GetPlayer(fixture.PlayerId).Clicks);
        Assert.True(result.State.GetCard(cardId).FaceUp);
        Assert.Single(result.State.GetZone(ZoneKind.CorpDiscard).CardIds, cardId);
        ZoneState playArea = Assert.Single(
            result.State.Zones,
            zone => zone.Kind == ZoneKind.CorpPlayArea && zone.OwnerSide == PlayerSide.Corp);
        Assert.Empty(playArea.CardIds);
        Assert.Single(result.State.ResolutionStack);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[0]).Stage);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(fixture.PlayerId, result.State.PendingDecision.Owner);
    }

    [Fact]
    public void CorpOperationDrawDeckOutCompletesPlayBeforeRealTurnTerminalCleanup()
    {
        const PlayerSide side = PlayerSide.Corp;
        CardDefinitionId definitionId = new("test:ordinary-corp-draw-deck-out");
        CardInstanceId cardId = new("card:ordinary-corp-draw-deck-out");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-corp-draw-deck-out")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 2)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-corp-draw-deck-out")
            .Add(Runtime(definitionId, side, playCost: 0))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        CardInstanceId drawnCardId = new("card:ordinary-corp-draw-deck-out-second");
        fixture = fixture with
        {
            State = AddCorpDeckCard(
                fixture.State,
                drawnCardId,
                new CardDefinitionId("test:ordinary-corp-draw-deck-out-filler")),
        };
        Assert.Equal(
            [new CardInstanceId("card:ordinary-corp-draw"), drawnCardId],
            fixture.State.GetZone(ZoneKind.CorpDeck).CardIds);

        GameState turnAction = OpenTurnAction(fixture);
        Assert.Equal([drawnCardId], turnAction.GetZone(ZoneKind.CorpDeck).CardIds);
        ApplyResult result = Apply(
            turnAction,
            fixture,
            Assert.Single(
                turnAction.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.PlayCard));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(GameOutcomeKind.Won, result.State.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, result.State.Outcome.Winner);
        Assert.Equal(GameWinReason.DeckOut, result.State.Outcome.Reason);
        Assert.Equal(
            [cardId],
            result.State.Turn.History!.CorpThisTurn!.TrashedCardOccurrenceIds);
        Assert.All(result.State.Players, player => Assert.Equal(0, player.Clicks));
        Assert.Single(result.State.GetZone(ZoneKind.CorpDiscard).CardIds, cardId);
        Assert.True(result.State.GetCard(cardId).FaceUp);
        Assert.Contains(drawnCardId, result.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Empty(result.State.GetZone(ZoneKind.CorpPlayArea).CardIds);
        Assert.Empty(result.State.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);

        int completedIndex = result.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.CardPlayCompleted);
        int wonIndex = result.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.GameWon);
        int drawIndex = result.Events.ToList().FindIndex(value => value.Kind == EngineEventKinds.CardDrawn);
        Assert.True(drawIndex >= 0);
        CardDrawnEventPayload drawn = Assert.IsType<CardDrawnEventPayload>(result.Events[drawIndex].Payload);
        Assert.Equal(drawnCardId, drawn.CardId);
        Assert.True(drawIndex < completedIndex);
        Assert.True(completedIndex >= 0);
        Assert.True(completedIndex < wonIndex);
        Assert.Equal(result.Events.Count - 1, wonIndex);
        Assert.Empty(StateInvariantValidator.Collect(result.State, behaviorCatalog, runtimeCatalog));

        string json = EngineJson.Serialize(result.State);
        GameState restored = EngineJson.DeserializeState(json, behaviorCatalog, runtimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(result.State), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void BehaviorInstallOptionIsOmittedWhenPhysicalRigMetadataIsUnknown()
    {
        const PlayerSide side = PlayerSide.Runner;
        CardDefinitionId sourceDefinition = new("test:ordinary-install-option-source");
        CardDefinitionId incomingDefinition = new("test:ordinary-install-option-incoming");
        CardDefinitionId unknownRigDefinition = new("test:ordinary-install-option-unknown-rig");
        CardInstanceId sourceCard = new("card:ordinary-install-option-source");
        CardInstanceId incomingCard = new("card:ordinary-install-option-incoming");
        CardInstanceId unknownRigCard = new("card:ordinary-install-option-unknown-rig");
        BehaviorChoiceSpec select = InstallOptionSelection(
            "ordinary-install-option-select",
            BehaviorZone.Stack);
        BehaviorChoiceSpec disposition = InstallOptionDisposition();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-install-option-unknown-rig")
            .Add(new CardBehaviorDefinition(
                sourceDefinition,
                side,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: select),
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: disposition),
                ]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-install-option-unknown-rig")
            .Add(new CardRuntimeSpec(
                sourceDefinition,
                side,
                RuntimeCardClass.Event,
                playCost: 0))
            .Add(new CardRuntimeSpec(
                incomingDefinition,
                side,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(sourceCard, sourceDefinition)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        GameState withStack = AddCardToZone(
            fixture.State,
            incomingCard,
            incomingDefinition,
            ZoneKind.RunnerStack,
            faceUp: false);
        withStack = AddCardToZone(
            withStack,
            unknownRigCard,
            unknownRigDefinition,
            ZoneKind.RunnerRig,
            faceUp: true);
        fixture = fixture with { State = withStack };

        GameState turnAction = OpenTurnAction(fixture);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult played = Apply(turnAction, fixture, play);
        Assert.True(played.Accepted, played.Rejection?.Message);
        GameState selectionPrompt = played.State;
        LegalAction selection = Assert.Single(
            selectionPrompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.CardIds.SequenceEqual([incomingCard]));
        ApplyResult selected = Apply(selectionPrompt, fixture, selection);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        GameState dispositionPrompt = selected.State;

        BehaviorChoiceParameters[] options = dispositionPrompt.PendingDecision!.LegalActions
            .Select(action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters))
            .ToArray();
        Assert.Single(options);
        Assert.Equal(["pass"], options[0].OptionKeys);
        Assert.DoesNotContain(
            dispositionPrompt.PendingDecision.LegalActions,
            action => action.Description.Contains("install", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(StateInvariantValidator.Collect(
            dispositionPrompt,
            behaviorCatalog,
            runtimeCatalog));
    }

    [Fact]
    public void BehaviorInstallOptionIsOmittedWhenChosenBindingOriginIsNotStack()
    {
        const PlayerSide side = PlayerSide.Runner;
        CardDefinitionId sourceDefinition = new("test:ordinary-install-option-origin-source");
        CardDefinitionId incomingDefinition = new("test:ordinary-install-option-origin-incoming");
        CardInstanceId sourceCard = new("card:ordinary-install-option-origin-source");
        CardInstanceId incomingCard = new("card:ordinary-install-option-origin-incoming");
        BehaviorChoiceSpec select = InstallOptionSelection(
            "ordinary-install-option-origin-select",
            BehaviorZone.Grip);
        BehaviorChoiceSpec disposition = InstallOptionDisposition();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-install-option-origin")
            .Add(new CardBehaviorDefinition(
                sourceDefinition,
                side,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: select),
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: disposition),
                ]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-install-option-origin")
            .Add(new CardRuntimeSpec(
                sourceDefinition,
                side,
                RuntimeCardClass.Event,
                playCost: 0))
            .Add(new CardRuntimeSpec(
                incomingDefinition,
                side,
                RuntimeCardClass.Program,
                installCost: 2,
                memoryUnits: 1))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [
                new FixtureCard(sourceCard, sourceDefinition),
                new FixtureCard(incomingCard, incomingDefinition),
            ],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);

        GameState turnAction = OpenTurnAction(fixture);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult played = Apply(turnAction, fixture, play);
        Assert.True(played.Accepted, played.Rejection?.Message);
        GameState selectionPrompt = played.State;
        LegalAction selection = Assert.Single(
            selectionPrompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.CardIds.SequenceEqual([incomingCard]));
        ApplyResult selected = Apply(selectionPrompt, fixture, selection);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        GameState dispositionPrompt = MoveSelectedBehaviorCardToStack(
            selected.State,
            incomingCard);

        Assert.Equal(ZoneKind.RunnerStack, dispositionPrompt.GetZone(
            dispositionPrompt.GetCard(incomingCard).ZoneId).Kind);
        BehaviorChoiceParameters[] options = dispositionPrompt.PendingDecision!.LegalActions
            .Select(action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters))
            .ToArray();
        Assert.Single(options);
        Assert.Equal(["pass"], options[0].OptionKeys);
        Assert.Empty(StateInvariantValidator.Collect(
            dispositionPrompt,
            behaviorCatalog,
            runtimeCatalog));
    }

    [Fact]
    public void RunnerEventCorpModeChoicePersistsPublicPlayAreaAndRoundTrips()
    {
        ChoiceState choiceState = OpenChoiceState();
        const PlayerSide side = PlayerSide.Runner;
        Fixture fixture = choiceState.Fixture;
        CardDefinitionId definitionId = choiceState.DefinitionId;
        CardInstanceId cardId = choiceState.CardId;
        BehaviorCatalog behaviorCatalog = fixture.BehaviorCatalog;
        RuntimeCardCatalog runtimeCatalog = fixture.RuntimeCardCatalog;
        GameState firstState = choiceState.State;
        CardPlayFrame cardPlay = choiceState.CardPlay;
        BehaviorResolutionFrame behavior = choiceState.Behavior;

        Assert.Equal(DecisionKind.BehaviorChoice, firstState.PendingDecision!.Kind);
        Assert.Equal(fixture.CorpId, firstState.PendingDecision.Owner);
        Assert.Equal(
            [typeof(TurnFrame), typeof(CardPlayFrame), typeof(BehaviorResolutionFrame)],
            firstState.ResolutionStack.Select(frame => frame.GetType()));
        TurnFrame parent = Assert.IsType<TurnFrame>(firstState.ResolutionStack[0]);
        Assert.Equal(TurnFrameStage.AwaitingAction, parent.Stage);
        Assert.Equal(cardId, cardPlay.CardId);
        Assert.Equal(side, cardPlay.Side);
        Assert.Equal(cardPlay.BehaviorId, behavior.ParentBehaviorId);
        Assert.Equal(BehaviorFrameIdentity.Compute(cardPlay), behavior.ParentFrameIdentity);
        Assert.Equal(cardPlay.PlayAreaZoneId, behavior.SourceZoneId);
        Assert.Equal(ZoneKind.RunnerPlayArea, behavior.SourceZoneKind);

        ZoneState playArea = Assert.Single(
            firstState.Zones,
            zone => zone.Kind == ZoneKind.RunnerPlayArea && zone.OwnerSide == PlayerSide.Runner);
        Assert.Single(playArea.CardIds, cardId);
        Assert.True(firstState.GetCard(cardId).FaceUp);
        Assert.False(firstState.GetCard(cardId).Rezzed);
        Observation runnerObservation = ObservationBuilder.Build(firstState, PlayerSide.Runner, behaviorCatalog);
        Observation corpObservation = ObservationBuilder.Build(firstState, PlayerSide.Corp, behaviorCatalog);
        Assert.Empty(runnerObservation.LegalActions);
        Assert.Equal(firstState.PendingDecision.LegalActions, corpObservation.LegalActions);
        Assert.Contains(cardId.Value, EngineJson.Serialize(runnerObservation), StringComparison.Ordinal);
        Assert.Contains(cardId.Value, EngineJson.Serialize(corpObservation), StringComparison.Ordinal);

        string json = EngineJson.Serialize(firstState);
        GameState restored = EngineJson.DeserializeState(json, behaviorCatalog, runtimeCatalog);
        Assert.Equal(DeterministicHash.ForState(firstState), DeterministicHash.ForState(restored));
        Assert.Equal(json, EngineJson.Serialize(restored));
        CardPlayFrame restoredCardPlay = Assert.IsType<CardPlayFrame>(restored.ResolutionStack[1]);
        BehaviorResolutionFrame restoredBehavior = Assert.IsType<BehaviorResolutionFrame>(restored.ResolutionStack[2]);
        Assert.Equal(cardPlay.PlayAreaZoneId, restoredCardPlay.PlayAreaZoneId);
        Assert.Equal(cardPlay.CreditCost, restoredCardPlay.CreditCost);
        Assert.Equal(behavior.DefinitionId, restoredBehavior.DefinitionId);
        Assert.Equal(behavior.ParentFrameIdentity, restoredBehavior.ParentFrameIdentity);

        LegalAction gain = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["gain"]));
        ApplyResult second = Apply(restored, fixture, gain);

        Assert.True(second.Accepted, second.Rejection?.Message);
        Assert.Equal(5, second.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(7, second.State.GetPlayer(fixture.PlayerId).Credits);
        Assert.Equal(3, second.State.GetPlayer(fixture.PlayerId).Clicks);
        Assert.Single(second.State.GetZone(ZoneKind.RunnerHeap).CardIds, cardId);
        Assert.True(second.State.GetCard(cardId).FaceUp);
        Assert.Empty(second.State.GetZone(ZoneKind.RunnerPlayArea).CardIds);
        Assert.Single(second.State.ResolutionStack);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(second.State.ResolutionStack[0]).Stage);
        Assert.Equal(DecisionKind.TurnTiming, second.State.PendingDecision!.Kind);
        Assert.Equal(fixture.PlayerId, second.State.PendingDecision.Owner);
        Assert.Contains(second.Events, value => value.Kind == EngineEventKinds.CardMoved);
        Assert.Contains(second.Events, value => value.Kind == EngineEventKinds.CardPlayCompleted);
        Assert.Contains(second.Events, value => value.Kind == EngineEventKinds.TurnTimingWindowOpened);
        Assert.DoesNotContain(second.Events, value => value.Kind == EngineEventKinds.CardPlayStarted);
    }

    [Fact]
    public void ForgedTurnActionPlayDecisionsReportPlayDiagnosticsAndRejectAtomically()
    {
        CardDefinitionId definitionId = Definition(PlayerSide.Runner, "forged-turn-action");
        CardInstanceId cardId = Instance(PlayerSide.Runner, "forged-turn-action");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-forged-turn-action")
            .Add(Behavior(definitionId, PlayerSide.Runner, cost: 2))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-forged-turn-action")
            .Add(Runtime(definitionId, PlayerSide.Runner, playCost: 2))
            .Build();
        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        GameState state = OpenTurnAction(fixture);
        PendingDecision pending = state.PendingDecision!;
        LegalAction issued = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        PlayCardParameters issuedParameters = Assert.IsType<PlayCardParameters>(issued.Parameters);

        LegalAction alteredCost = new(
            issued.Id,
            issued.Kind,
            issued.Description,
            new PlayCardParameters(issuedParameters.CardId, checked(issuedParameters.CreditCost + 1)));
        AssertForgedTurnDecisionRejected(
            state,
            fixture,
            pending.LegalActions.Select(action => action.Id == issued.Id ? alteredCost : action).ToArray(),
            issued.Id,
            "decision.missing-play-action",
            "decision.extra-play-action");

        LegalAction duplicate = new(
            new ActionId("action:ordinary-forged-turn-duplicate"),
            issued.Kind,
            issued.Description,
            issued.Parameters);
        AssertForgedTurnDecisionRejected(
            state,
            fixture,
            pending.LegalActions.Append(duplicate).ToArray(),
            issued.Id,
            "decision.duplicate-play-action");

        AssertForgedTurnDecisionRejected(
            state,
            fixture,
            pending.LegalActions.Where(action => action.Id != issued.Id).ToArray(),
            pending.LegalActions.First(action => action.Kind == ActionKind.GainCredit).Id,
            "decision.missing-play-action");

        LegalAction extra = new(
            new ActionId("action:ordinary-forged-turn-extra"),
            ActionKind.PlayCard,
            "Play card",
            new PlayCardParameters(new CardInstanceId("card:ordinary-forged-turn-extra"), 0));
        AssertForgedTurnDecisionRejected(
            state,
            fixture,
            pending.LegalActions.Append(extra).ToArray(),
            extra.Id,
            "decision.extra-play-action");
    }

    [Fact]
    public void IssuedPlayIsRejectedAtomicallyWhenWalletBecomesUnaffordable()
    {
        CardDefinitionId definitionId = Definition(PlayerSide.Runner, "stale-wallet");
        CardInstanceId cardId = Instance(PlayerSide.Runner, "stale-wallet");
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-stale-wallet")
            .Add(Behavior(definitionId, PlayerSide.Runner, cost: 2))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-stale-wallet")
            .Add(Runtime(definitionId, PlayerSide.Runner, playCost: 2))
            .Build();
        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        GameState issuedState = OpenTurnAction(fixture);
        LegalAction issued = Assert.Single(
            issuedState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        PlayerState runner = issuedState.GetPlayer(fixture.PlayerId);
        GameState stale = RebuildState(
            issuedState,
            players: issuedState.Players
                .Select(player => player.Id == runner.Id
                    ? new PlayerState(player.Id, player.Side, 1, player.Clicks, player.AgendaPoints)
                    : player)
                .ToArray());

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            stale,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Contains(violations, violation => violation.Code == "decision.extra-play-action");

        ApplyResult rejected = Apply(stale, fixture, issued);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(stale, rejected.State);
        Assert.Empty(rejected.Events);
    }

    [Fact]
    public void CardPlayFrameForgeryVariantsReportSpecificViolationsWithoutThrowing()
    {
        ChoiceState valid = OpenChoiceState();
        CardPlayFrame frame = valid.CardPlay;
        BehaviorResolutionFrame child = valid.Behavior;
        ZoneState playArea = valid.State.GetZone(frame.PlayAreaZoneId);

        var variants = new (string Code, GameState State)[]
        {
            (
                "resolution.card-play-cost-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, creditCost: checked(frame.CreditCost + 1)))),
            (
                "resolution.card-play-catalog-fingerprint-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, catalogFingerprint: AlternateFingerprint(frame.CatalogFingerprint)))),
            (
                "resolution.card-play-definition-fingerprint-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, definitionFingerprint: AlternateFingerprint(frame.DefinitionFingerprint)))),
            (
                "resolution.card-play-definition-missing",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, definitionId: new CardDefinitionId("test:ordinary-forged-definition")))),
            (
                "resolution.card-play-runtime-class-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, cardClass: RuntimeCardClass.Operation))),
            (
                "resolution.card-play-source-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, sourceZoneId: new ZoneId("zone:ordinary-forged-source")))),
            (
                "resolution.card-play-source-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, sourceZoneKind: ZoneKind.CorpHand))),
            (
                "resolution.card-play-area-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, playAreaZoneId: new ZoneId("zone:ordinary-forged-play-area")))),
            (
                "resolution.card-play-discard-mismatch",
                ReplaceCardPlayFrame(
                    valid.State,
                    CloneCardPlayFrame(frame, discardZoneId: new ZoneId("zone:ordinary-forged-discard")))),
            (
                "resolution.card-play-definition-mismatch",
                ReplaceCard(
                    valid.State,
                    valid.CardId,
                    new CardInstanceState(
                        valid.CardId,
                        new CardDefinitionId("test:ordinary-forged-card-definition"),
                        playArea.Id,
                        PlayerSide.Runner,
                        faceUp: true))),
            (
                "resolution.card-play-card-owner-mismatch",
                ReplaceCard(
                    valid.State,
                    valid.CardId,
                    new CardInstanceState(
                        valid.CardId,
                        frame.DefinitionId,
                        playArea.Id,
                        PlayerSide.Corp,
                        faceUp: true))),
            (
                "resolution.card-play-card-facedown",
                ReplaceCard(
                    valid.State,
                    valid.CardId,
                    new CardInstanceState(
                        valid.CardId,
                        frame.DefinitionId,
                        playArea.Id,
                        PlayerSide.Runner,
                        faceUp: false))),
            (
                "resolution.card-play-card-rezzed",
                ReplaceCard(
                    valid.State,
                    valid.CardId,
                    new CardInstanceState(
                        valid.CardId,
                        frame.DefinitionId,
                        playArea.Id,
                        PlayerSide.Runner,
                        faceUp: true,
                        rezzed: true))),
            (
                "resolution.card-play-child-parent-identity-mismatch",
                ReplaceCardPlayChild(
                    valid.State,
                    CloneBehaviorFrame(child, parentFrameIdentity: "parent-frame:ordinary-forged"))),
            (
                "resolution.card-play-area-count",
                AddSecondPlayAreaMember(valid.State, frame.PlayAreaZoneId)),
            (
                "resolution.card-play-area-count",
                MoveCardOutOfPlayArea(valid.State, valid.CardId)),
        };

        foreach ((string code, GameState forged) in variants)
        {
            IReadOnlyList<StateInvariantViolation>? violations = null;
            Exception? exception = Record.Exception(() =>
                violations = StateInvariantValidator.Collect(
                    forged,
                    valid.Fixture.BehaviorCatalog,
                    valid.Fixture.RuntimeCardCatalog));

            Assert.Null(exception);
            Assert.NotNull(violations);
            Assert.Contains(violations!, violation => violation.Code == code);
        }
    }

    [Fact]
    public void CardPlayChildMustRemainTopOfResolutionStack()
    {
        ChoiceState valid = OpenChoiceState();
        ResolutionFrame extra = new TurnFrame(
            PlayerSide.Runner,
            valid.State.Turn.TurnNumber,
            TurnFrameStage.ActionPriority);
        GameState forged = ReplaceResolutionStack(
            valid.State,
            valid.State.ResolutionStack.Append(extra).ToArray());

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            valid.Fixture.BehaviorCatalog,
            valid.Fixture.RuntimeCardCatalog);
        Assert.Contains(violations, violation => violation.Code == "resolution.card-play-child-not-top");

        PendingDecision pending = forged.PendingDecision!;
        ApplyResult rejected = Apply(
            forged,
            valid.Fixture,
            pending.LegalActions[0]);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(forged, rejected.State);
        Assert.Empty(rejected.Events);
    }

    [Theory]
    [InlineData(PlayerSide.Corp, ZoneKind.CorpPlayArea, "resolution.card-play-area-without-frame")]
    [InlineData(PlayerSide.Runner, ZoneKind.RunnerPlayArea, "run.active-event-without-state")]
    public void PopulatedOrphanPlayAreaWithoutLifecycleFrameIsRejected(
        PlayerSide side,
        ZoneKind playAreaKind,
        string expectedCode)
    {
        CardDefinitionId definitionId = Definition(side, "orphan-play-area");
        CardInstanceId cardId = Instance(side, "orphan-play-area");
        int cost = side == PlayerSide.Corp ? 1 : 2;
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder($"test:ordinary-orphan-{side}")
            .Add(Behavior(definitionId, side, cost))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder($"runtime:ordinary-orphan-{side}")
            .Add(Runtime(definitionId, side, cost))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        GameState orphan = PopulateOrphanPlayArea(fixture, playAreaKind, cardId);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            orphan,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.Contains(violations, violation => violation.Code == expectedCode);

        ApplyResult rejected = GameEngine.Apply(
            orphan,
            new ActionSubmission(
                fixture.PlayerId,
                new DecisionId("decision:ordinary-orphan"),
                revision: 1,
                new ActionId("action:ordinary-orphan")),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(orphan, rejected.State);
        Assert.Empty(rejected.Events);
    }

    [Fact]
    public void RunnerCardPlayFrameConflictsWithLiveRunState()
    {
        ChoiceState valid = OpenChoiceState();
        ServerState headquarters = valid.State.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        RunId runId = new("run:ordinary-play-conflict");
        RunState run = new(
            runId,
            headquarters.Id,
            PlayerSide.Runner,
            valid.Fixture.PlayerId,
            currentIceIndex: -1,
            currentIceId: null,
            RunTimingStep.InitiationPaidWindow,
            new RunPriorityState(
                RunPriorityWindowKind.InitiationPaid,
                PlayerSide.Runner,
                corpMayRezNonIce: true));
        GameState forged = ReplaceRunState(
            valid.State,
            run,
            [new RunTimingFrame(runId, run.Step), .. valid.State.ResolutionStack]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            valid.Fixture.BehaviorCatalog,
            valid.Fixture.RuntimeCardCatalog);
        Assert.Contains(violations, violation => violation.Code == "run.card-play-frame-conflict");
    }

    private static ChoiceState OpenChoiceState()
    {
        const PlayerSide side = PlayerSide.Runner;
        CardDefinitionId definitionId = new("test:ordinary-runner-corp-choice");
        CardInstanceId cardId = new("card:ordinary-runner-corp-choice");
        BehaviorChoiceSpec choice = new(
            "ordinary-corp-mode",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption(
                    "gain",
                    [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 2)]),
                new BehaviorChoiceOption("pass", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder("test:ordinary-runner-corp-choice")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:ordinary-runner-corp-choice")
            .Add(Runtime(definitionId, side, playCost: 0))
            .Build();
        Fixture fixture = CreateFixture(
            side,
            [new FixtureCard(cardId, definitionId)],
            behaviorCatalog,
            runtimeCatalog,
            credits: 5);
        GameState turnAction = OpenTurnAction(fixture);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult result = Apply(turnAction, fixture, play);
        Assert.True(result.Accepted, result.Rejection?.Message);

        return new ChoiceState(
            result.State,
            fixture,
            cardId,
            definitionId,
            Assert.IsType<CardPlayFrame>(result.State.ResolutionStack[1]),
            Assert.IsType<BehaviorResolutionFrame>(result.State.ResolutionStack[2]));
    }

    private static void AssertForgedTurnDecisionRejected(
        GameState source,
        Fixture fixture,
        IReadOnlyList<LegalAction> actions,
        ActionId submittedAction,
        params string[] expectedCodes)
    {
        PendingDecision pending = source.PendingDecision!;
        GameState forged = ReplacePendingDecision(
            source,
            ReissueDecision(pending, actions));
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            forged,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        foreach (string expectedCode in expectedCodes)
        {
            Assert.Contains(violations, violation => violation.Code == expectedCode);
        }

        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(
                pending.Owner,
                pending.Id,
                pending.Revision,
                submittedAction),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(forged, rejected.State);
        Assert.Empty(rejected.Events);
    }

    private static PendingDecision ReissueDecision(
        PendingDecision source,
        IReadOnlyList<LegalAction> actions) => new(
        source.Id,
        source.Revision,
        source.Owner,
        source.Kind,
        source.Prompt,
        actions);

    private static GameState ReplacePendingDecision(GameState state, PendingDecision pending) =>
        RebuildState(state, pendingDecision: pending);

    private static GameState ReplaceResolutionStack(
        GameState state,
        IReadOnlyList<ResolutionFrame> resolutionStack) =>
        RebuildState(state, resolutionStack: resolutionStack);

    private static GameState ReplaceRunState(
        GameState state,
        RunState runState,
        IReadOnlyList<ResolutionFrame> resolutionStack) =>
        RebuildState(state, resolutionStack: resolutionStack, runState: runState);

    private static GameState ReplaceCardPlayFrame(GameState state, CardPlayFrame frame)
    {
        ResolutionFrame[] stack = state.ResolutionStack.ToArray();
        stack[1] = frame;
        return ReplaceResolutionStack(state, stack);
    }

    private static GameState ReplaceCardPlayChild(GameState state, BehaviorResolutionFrame child)
    {
        ResolutionFrame[] stack = state.ResolutionStack.ToArray();
        stack[2] = child;
        return ReplaceResolutionStack(state, stack);
    }

    private static CardPlayFrame CloneCardPlayFrame(
        CardPlayFrame source,
        CardDefinitionId? definitionId = null,
        RuntimeCardClass? cardClass = null,
        ZoneId? sourceZoneId = null,
        ZoneKind? sourceZoneKind = null,
        ZoneId? playAreaZoneId = null,
        ZoneId? discardZoneId = null,
        int? creditCost = null,
        string? catalogFingerprint = null,
        string? definitionFingerprint = null,
        PlayerId? owner = null) => new(
        source.Version,
        source.BehaviorId,
        owner ?? source.Owner,
        source.Side,
        source.CardId,
        definitionId ?? source.DefinitionId,
        cardClass ?? source.CardClass,
        sourceZoneId ?? source.SourceZoneId,
        sourceZoneKind ?? source.SourceZoneKind,
        playAreaZoneId ?? source.PlayAreaZoneId,
        discardZoneId ?? source.DiscardZoneId,
        creditCost ?? source.CreditCost,
        catalogFingerprint ?? source.CatalogFingerprint,
        definitionFingerprint ?? source.DefinitionFingerprint);

    private static BehaviorResolutionFrame CloneBehaviorFrame(
        BehaviorResolutionFrame source,
        string? parentFrameIdentity = null) => new(
        source.Version,
        source.BehaviorId,
        source.DefinitionId,
        source.SourceCardId,
        source.Controller,
        source.ActingPlayer,
        source.CatalogFingerprint,
        source.DefinitionFingerprint,
        source.Stage,
        source.InstructionCursor,
        source.Bindings,
        source.SetAside,
        source.Choice,
        source.Continuation,
        source.SetAsideZoneId,
        source.ParentBehaviorId,
        source.FrameIdentity,
        source.SourceZoneId,
        source.SourceZoneKind,
        parentFrameIdentity ?? source.ParentFrameIdentity);

    private static string AlternateFingerprint(string fingerprint)
    {
        char replacement = fingerprint[0] == '0' ? '1' : '0';
        string alternate = new(replacement, fingerprint.Length);
        return alternate == fingerprint ? new string('2', fingerprint.Length) : alternate;
    }

    private static GameState ReplaceCard(
        GameState state,
        CardInstanceId cardId,
        CardInstanceState replacement) =>
        RebuildState(
            state,
            cards: state.Cards
                .Select(card => card.Id == cardId ? replacement : card)
                .ToArray());

    private static GameState MoveCardOutOfPlayArea(GameState state, CardInstanceId cardId)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.GetZone(card.ZoneId);
        ZoneState destination = state.GetZone(
            state.Turn.ActiveSide == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == source.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destination.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters);
        return ReplaceCard(
            RebuildState(state, zones: zones),
            cardId,
            moved);
    }

    private static GameState AddSecondPlayAreaMember(GameState state, ZoneId playAreaZoneId)
    {
        CardInstanceId extraCardId = new("card:ordinary-extra-play-area");
        CardDefinitionId extraDefinitionId = new("test:ordinary-extra-play-area");
        ZoneState playArea = state.GetZone(playAreaZoneId);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == playArea.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(extraCardId).ToArray())
                : zone)
            .ToArray();
        CardInstanceState extraCard = new(
            extraCardId,
            extraDefinitionId,
            playArea.Id,
            PlayerSide.Runner,
            faceUp: true);
        return RebuildState(
            state,
            cards: state.Cards.Append(extraCard).ToArray(),
            zones: zones);
    }

    private static GameState PopulateOrphanPlayArea(
        Fixture fixture,
        ZoneKind playAreaKind,
        CardInstanceId cardId)
    {
        GameState state = fixture.State;
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.GetZone(card.ZoneId);
        ZoneId orphanZoneId = new($"zone:ordinary-orphan-{fixture.Side.ToString().ToLowerInvariant()}");
        ZoneState orphan = new(orphanZoneId, playAreaKind, fixture.Side, cardIds: [cardId]);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == source.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone)
            .Append(orphan)
            .ToArray();
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            orphan.Id,
            card.OwnerSide,
            faceUp: true,
            rezzed: false,
            card.AdvancementCounters);
        return ReplaceCard(
            RebuildState(state, zones: zones),
            cardId,
            moved);
    }

    private static GameState AddInstalledCorpCard(
        GameState state,
        CardInstanceId cardId,
        CardDefinitionId definitionId)
    {
        ServerState server = state.Servers.Single(server => server.Kind == ServerKind.RAndD);
        ZoneState root = state.GetZone(server.RootZoneId);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == root.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        CardInstanceState card = new(cardId, definitionId, root.Id, PlayerSide.Corp);
        return RebuildState(state, cards: state.Cards.Append(card).ToArray(), zones: zones);
    }

    private static GameState MoveCardOutOfCorpServerRoot(GameState state, CardInstanceId cardId)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.GetZone(card.ZoneId);
        ZoneState destination = state.GetZone(ZoneKind.CorpDiscard);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == source.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destination.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            faceUp: true,
            rezzed: false,
            card.AdvancementCounters);
        return RebuildState(
            state,
            cards: state.Cards.Select(value => value.Id == cardId ? moved : value).ToArray(),
            zones: zones);
    }

    private static GameState RebuildState(
        GameState source,
        IReadOnlyList<PlayerState>? players = null,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pendingDecision = null,
        RunState? runState = null) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        players ?? source.Players,
        cards ?? source.Cards,
        zones ?? source.Zones,
        source.Servers,
        source.Turn,
        source.Outcome,
        source.RandomState,
        source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        resolutionStack ?? source.ResolutionStack,
        pendingDecision ?? source.PendingDecision,
        source.ActionSequence,
        source.InitialSeed,
        runState ?? source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static ApplyResult Apply(GameState state, Fixture fixture, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);

    private static GameState OpenTurnAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            fixture.Side,
            fixture.BehaviorCatalog,
            fixture.RuntimeCardCatalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, fixture, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState SpendAllClicksWithGainCredit(GameState state, Fixture fixture)
    {
        int clicks = state.GetPlayer(fixture.PlayerId).Clicks;
        for (int index = 0; index < clicks; index++)
        {
            LegalAction gain = Assert.Single(
                state.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.GainCredit);
            ApplyResult result = Apply(state, fixture, gain);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
            if (index + 1 < clicks)
            {
                for (int passIndex = 0; passIndex < 2; passIndex++)
                {
                    PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
                    LegalAction pass = Assert.Single(
                        decision.LegalActions,
                        action => action.Kind == ActionKind.PassPriority);
                    ApplyResult passed = Apply(state, fixture, pass);
                    Assert.True(passed.Accepted, passed.Rejection?.Message);
                    state = passed.State;
                }
            }
        }

        return state;
    }

    private static BehaviorChoiceSpec InstallOptionSelection(
        string key,
        BehaviorZone zone) =>
        new(
            key,
            BehaviorChooser.Controller,
            BehaviorChoiceMode.TargetSelection,
            BehaviorChoiceCardinality.Exactly(1),
            target: BehaviorTarget.CardInZone(
                zone,
                new BehaviorCardFilter(ownerSide: PlayerSide.Runner)));

    private static BehaviorChoiceSpec InstallOptionDisposition() =>
        new(
            "ordinary-install-option-disposition",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption(
                    "install",
                    [
                        new BehaviorInstruction(
                            BehaviorInstructionKind.InstallCard,
                            target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                            sourceZone: BehaviorZone.Stack,
                            destinationZone: BehaviorZone.Rig),
                    ]),
                new BehaviorChoiceOption(
                    "pass",
                    [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);

    private static GameState AddCardToZone(
        GameState source,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        ZoneKind zoneKind,
        bool faceUp)
    {
        ZoneState destination = source.GetZone(zoneKind);
        CardInstanceState card = new(
            cardId,
            definitionId,
            destination.Id,
            PlayerSide.Runner,
            faceUp);
        ZoneState[] zones = source.Zones
            .Select(zone => zone.Id == destination.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        return RebuildState(
            source,
            cards: source.Cards.Append(card).ToArray(),
            zones: zones);
    }

    private static GameState AddCorpDeckCard(
        GameState source,
        CardInstanceId cardId,
        CardDefinitionId definitionId)
    {
        ZoneState deck = source.GetZone(ZoneKind.CorpDeck);
        CardInstanceState card = new(cardId, definitionId, deck.Id, PlayerSide.Corp, faceUp: false);
        ZoneState[] zones = source.Zones
            .Select(zone => zone.Id == deck.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        return RebuildState(
            source,
            cards: source.Cards.Append(card).ToArray(),
            zones: zones);
    }

    private static GameState MoveSelectedBehaviorCardToStack(
        GameState source,
        CardInstanceId cardId)
    {
        BehaviorResolutionFrame frame = Assert.IsType<BehaviorResolutionFrame>(source.ResolutionStack[^1]);
        ZoneId setAsideZoneId = Assert.IsType<ZoneId>(frame.SetAsideZoneId);
        ZoneState setAside = source.Zones.Single(zone => zone.Id == setAsideZoneId);
        ZoneState stack = source.GetZone(ZoneKind.RunnerStack);
        CardInstanceState card = source.GetCard(cardId);
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            stack.Id,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters);
        ZoneState[] zones = source.Zones
            .Where(zone => zone.Id != setAside.Id)
            .Select(zone => zone.Id == stack.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(cardId).ToArray())
                : zone)
            .ToArray();
        ResolutionFrame[] frames = source.ResolutionStack.ToArray();
        frames[^1] = frame.With(
            setAside: [],
            clearSetAsideZone: true);
        return RebuildState(
            source,
            cards: source.Cards
                .Select(value => value.Id == cardId ? moved : value)
                .ToArray(),
            zones: zones,
            resolutionStack: frames);
    }

    private static Fixture CreateFixture(
        PlayerSide side,
        IReadOnlyList<FixtureCard> handCards,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog,
        int credits)
    {
        GameState baseline = GameState.CreateNew(
            seed: 42,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        PlayerId playerId = baseline.Players.Single(player => player.Side == side).Id;
        PlayerState[] players = baseline.Players
            .Select(player => player.Id == playerId
                ? new PlayerState(player.Id, player.Side, credits, player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();
        ZoneKind handKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState hand = baseline.GetZone(handKind);
        List<FixtureCard> cards = handCards.ToList();
        CardInstanceId? corpDeckCardId = null;
        if (side == PlayerSide.Corp)
        {
            corpDeckCardId = new CardInstanceId("card:ordinary-corp-draw");
            cards.Add(new FixtureCard(corpDeckCardId.Value, new CardDefinitionId("test:ordinary-corp-draw")));
        }

        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == hand.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, handCards.Select(card => card.Id).ToArray())
                : zone)
            .ToArray();
        if (corpDeckCardId is { } deckCardId)
        {
            ZoneState deck = baseline.GetZone(ZoneKind.CorpDeck);
            zones = zones
                .Select(zone => zone.Id == deck.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [deckCardId])
                    : zone)
                .ToArray();
        }

        CardInstanceState[] cardStates = cards
            .Select(card => new CardInstanceState(
                card.Id,
                card.DefinitionId,
                card.Id == corpDeckCardId
                    ? baseline.GetZone(ZoneKind.CorpDeck).Id
                    : hand.Id,
                side))
            .ToArray();
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cardStates,
            zones,
            baseline.Servers,
            new TurnState(side, side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner, turnNumber: 1),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            baseline.RulesState,
            behaviorCatalog.Version,
            behaviorCatalog.Fingerprint,
            runtimeCatalog.Version,
            runtimeCatalog.Fingerprint);
        return new Fixture(state, behaviorCatalog, runtimeCatalog, side, playerId, baseline.Players.Single(player => player.Side == PlayerSide.Corp).Id);
    }

    private static CardRuntimeSpec Runtime(CardDefinitionId definitionId, PlayerSide side, int playCost) =>
        new(definitionId, side, side == PlayerSide.Corp ? RuntimeCardClass.Operation : RuntimeCardClass.Event, playCost: playCost);

    private static CardBehaviorDefinition Behavior(CardDefinitionId definitionId, PlayerSide side, int cost) =>
        new(
            definitionId,
            side,
            costs: [new BehaviorCost(BehaviorCostKind.Credits, cost)],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);

    private static CardDefinitionId Definition(PlayerSide side, string suffix) =>
        new($"test:ordinary-{side.ToString().ToLowerInvariant()}-{suffix}");

    private static CardInstanceId Instance(PlayerSide side, string suffix) =>
        new($"card:ordinary-{side.ToString().ToLowerInvariant()}-{suffix}");

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCardCatalog,
        PlayerSide Side,
        PlayerId PlayerId,
        PlayerId CorpId);

    private sealed record ChoiceState(
        GameState State,
        Fixture Fixture,
        CardInstanceId CardId,
        CardDefinitionId DefinitionId,
        CardPlayFrame CardPlay,
        BehaviorResolutionFrame Behavior);

    private sealed record FixtureCard(CardInstanceId Id, CardDefinitionId DefinitionId);

    public enum OrdinaryPlayExclusion
    {
        NoClicks,
        Unaffordable,
        WrongClass,
        MissingGenericBehavior,
        RuntimeBehaviorCostMismatch,
        NonCreditsUpfrontCost,
        LegacyRunEventOnly,
    }
}
