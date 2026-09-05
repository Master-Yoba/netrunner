using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused ENG-404/406 coverage for the serializable generic behavior
/// interpreter. These fixtures intentionally use opaque synthetic IDs: the
/// runtime proves the contract vocabulary, not card-title dispatch.
/// </summary>
public sealed class BehaviorRuntimeTests
{
    [Fact]
    public void ChoiceOptionPaysTypedCostsAndTrashesTheAuthenticatedPrivateCard()
    {
        CardDefinitionId definitionId = new("test:runtime-choice-cost");
        CardDefinitionId discardDefinitionId = new("test:runtime-choice-cost-card");
        CardInstanceId sourceId = new("card:runtime-choice-cost-source");
        CardInstanceId discardId = new("card:runtime-choice-cost-discard");
        BehaviorTarget additionalTarget = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            new BehaviorCardFilter(
                definitionId: discardDefinitionId,
                ownerSide: PlayerSide.Corp));
        BehaviorCost[] costs =
        [
            new BehaviorCost(BehaviorCostKind.Credits, 2),
            new BehaviorCost(
                BehaviorCostKind.Additional,
                additionalKey: "trash-card",
                additionalTarget: additionalTarget,
                additionalKind: BehaviorAdditionalCostKind.TrashCards),
        ];
        BehaviorChoiceSpec choice = new(
            "runtime-choice-cost-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption(
                    "pay",
                    [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)],
                    costs: costs,
                    semantic: BehaviorChoiceOptionSemantic.Normal),
                new BehaviorChoiceOption(
                    "fallback",
                    [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                    semantic: BehaviorChoiceOptionSemantic.Fallback),
            ]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (discardId, discardDefinitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        string promptJson = EngineJson.Serialize(prompt);
        prompt = EngineJson.DeserializeState(promptJson, catalog);
        Assert.Equal(promptJson, EngineJson.Serialize(prompt));
        LegalAction pay = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["pay"]));
        BehaviorChoiceParameters payload = Assert.IsType<BehaviorChoiceParameters>(pay.Parameters);
        Assert.Equal([discardId], payload.CardIds);
        Assert.Contains("Normal", pay.Description, StringComparison.Ordinal);

        ApplyResult applied = GameEngine.Apply(
            prompt,
            new ActionSubmission(corp, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, pay.Id),
            catalog);

        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.Equal(6, applied.State.GetPlayer(corp).Credits);
        Assert.Equal(ZoneKind.CorpDiscard, applied.State.GetZone(applied.State.GetCard(discardId).ZoneId).Kind);
        Assert.Empty(applied.State.ResolutionStack);
    }

    [Fact]
    public void InvocationUsesInjectedDefinitionAndRejectsWrongProvenance()
    {
        CardDefinitionId definitionId = new("test:runtime-gain");
        CardInstanceId sourceId = new("card:runtime-source");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            costs: [new BehaviorCost(BehaviorCostKind.Credits, 2)],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 3)]));
        GameState state = CreateState(catalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        Assert.DoesNotContain(
            typeof(GameEngine).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            method => method.Name == "BeginBehavior");
        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);

        Assert.Null(result.PendingDecision);
        Assert.Equal(6, result.GetPlayer(corp).Credits);
        Assert.Empty(result.ResolutionStack);

        BehaviorCatalog changed = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 4)]));
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            changed));
    }

    [Fact]
    public void BehaviorTrashSelfDefersCounterCleanupUntilItsEntireCostLoopFinishes()
    {
        CardDefinitionId definitionId = new("test:runtime-trash-first-behavior-cost");
        CardInstanceId sourceId = new("card:runtime-trash-first-behavior-cost");
        BehaviorChoiceSpec continuation = new(
            "trash-self-resume",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption("continue"),
                new BehaviorChoiceOption("skip"),
            ]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            costs:
            [
                new BehaviorCost(BehaviorCostKind.TrashSelf),
                new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "charge"),
            ],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: continuation)]));
        GameState state = WithCounters(
            CreateState(catalog, (sourceId, definitionId, ZoneKind.RunnerRig, PlayerSide.Runner)),
            new CardCounterState(sourceId, "charge", 1));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        state = ReplaceTurn(state, new TurnState(
            PlayerSide.Runner,
            TurnPhase.Runner,
            1,
            TurnStep.RunnerAction,
            new TurnHistoryState(
                1,
                null,
                null,
                new TurnFactsState(1, PlayerSide.Runner, 1, 4, 0, true, false, false, false),
                null)));

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);

        Assert.Equal(ZoneKind.RunnerHeap, prompt.GetZone(prompt.GetCard(sourceId).ZoneId).Kind);
        Assert.DoesNotContain(prompt.RulesState!.Counters, counter => counter.CardId == sourceId);
        Assert.Equal([sourceId], prompt.Turn.ThisTurnFacts!.TrashedCardOccurrenceIds);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        LegalAction continueAction = Assert.Single(
            decision.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual(["continue"]));
        ApplyResult resumed = GameEngine.Apply(
            restored,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, continueAction.Id),
            catalog);
        Assert.True(resumed.Accepted, resumed.Rejection?.Message);
        Assert.Equal([sourceId], resumed.State.Turn.ThisTurnFacts!.TrashedCardOccurrenceIds);
        Assert.Empty(resumed.State.ResolutionStack);
    }

    [Theory]
    [InlineData(PlayerSide.Corp, ZoneKind.CorpHand, ZoneKind.CorpRemovedFromGame)]
    [InlineData(PlayerSide.Runner, ZoneKind.RunnerRig, ZoneKind.RunnerRemovedFromGame)]
    public void BehaviorRemoveSelfFromGameMovesFaceupAndDefersCounterCleanup(
        PlayerSide owner,
        ZoneKind sourceZone,
        ZoneKind removedZone)
    {
        CardDefinitionId definitionId = new($"test:runtime-remove-self-{owner.ToString().ToLowerInvariant()}");
        CardInstanceId sourceId = new($"card:runtime-remove-self-{owner.ToString().ToLowerInvariant()}");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            owner,
            costs:
            [
                new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame),
                new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "charge"),
            ],
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]));
        GameState state = WithCounters(
            CreateState(catalog, (sourceId, definitionId, sourceZone, owner)),
            new CardCounterState(sourceId, "charge", 1));
        PlayerId controller = state.Players.Single(player => player.Side == owner).Id;

        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, controller),
            catalog);

        CardInstanceState source = result.GetCard(sourceId);
        Assert.Equal(removedZone, result.GetZone(source.ZoneId).Kind);
        Assert.True(source.FaceUp);
        Assert.DoesNotContain(result.RulesState!.Counters, counter => counter.CardId == sourceId);
        Assert.Equal(2, result.EventSequence);

        string json = EngineJson.Serialize(result);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(result), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void HostedCreditInstructionsLoadPlaceTakeAndTakeAll()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-credit");
        CardInstanceId sourceId = new("card:runtime-hosted-credit-source");
        PlayerSide owner = PlayerSide.Corp;

        BehaviorCatalog loadCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            owner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.LoadHostedCredits,
                    amount: 3,
                    spendRestriction: HostedCreditSpendRestriction.AbilityCost),
            ]));
        GameState loadState = CreateState(
            loadCatalog,
            (sourceId, definitionId, ZoneKind.CorpHand, owner));
        PlayerId corp = loadState.Players.Single(player => player.Side == owner).Id;
        GameState loaded = GameEngine.BeginBehaviorForTesting(
            loadState,
            new BehaviorInvocationContext(sourceId, corp),
            loadCatalog);
        CardCounterState loadedPool = Assert.Single(loaded.RulesState!.Counters);
        Assert.Equal(3, loadedPool.Count);
        Assert.Equal(HostedCreditSpendRestriction.AbilityCost, loadedPool.SpendRestriction);

        BehaviorCatalog placeCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            owner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.PlaceHostedCredits,
                    amount: 2,
                    spendRestriction: HostedCreditSpendRestriction.AbilityCost),
            ]));
        GameState placed = GameEngine.BeginBehaviorForTesting(
            WithCounters(
                CreateState(placeCatalog, (sourceId, definitionId, ZoneKind.CorpHand, owner)),
                CardCounterState.CreateHostedCredit(sourceId, 3, HostedCreditSpendRestriction.AbilityCost)),
            new BehaviorInvocationContext(sourceId, corp),
            placeCatalog);
        Assert.Equal(5, Assert.Single(placed.RulesState!.Counters).Count);

        BehaviorCatalog takeCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            owner,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.TakeHostedCredits, amount: 2)]));
        GameState taken = GameEngine.BeginBehaviorForTesting(
            WithCounters(
                CreateState(takeCatalog, (sourceId, definitionId, ZoneKind.CorpHand, owner)),
                CardCounterState.CreateHostedCredit(sourceId, 5, HostedCreditSpendRestriction.AbilityCost)),
            new BehaviorInvocationContext(sourceId, corp),
            takeCatalog);
        Assert.Equal(7, taken.GetPlayer(corp).Credits);
        Assert.Equal(3, Assert.Single(taken.RulesState!.Counters).Count);

        BehaviorCatalog takeAllCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            owner,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.TakeAllHostedCredits)]));
        GameState takenAll = GameEngine.BeginBehaviorForTesting(
            WithCounters(
                CreateState(takeAllCatalog, (sourceId, definitionId, ZoneKind.CorpHand, owner)),
                CardCounterState.CreateHostedCredit(sourceId, 3, HostedCreditSpendRestriction.AbilityCost)),
            new BehaviorInvocationContext(sourceId, corp),
            takeAllCatalog);
        Assert.Equal(8, takenAll.GetPlayer(corp).Credits);
        Assert.Empty(takenAll.RulesState!.Counters);
    }

    [Fact]
    public void HostedCreditInstructionsRejectRestrictionMismatchEmptyUnderflowAndOverflowAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-credit-failures");
        CardInstanceId sourceId = new("card:runtime-hosted-credit-failures-source");
        PlayerId corp;

        BehaviorCatalog mismatchCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [new BehaviorInstruction(
                BehaviorInstructionKind.LoadHostedCredits,
                amount: 1,
                spendRestriction: HostedCreditSpendRestriction.RunCost)]));
        GameState mismatchState = WithCounters(
            CreateState(mismatchCatalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)),
            CardCounterState.CreateHostedCredit(sourceId, 1, HostedCreditSpendRestriction.AbilityCost));
        corp = mismatchState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        string mismatchJson = EngineJson.Serialize(mismatchState);
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            mismatchState,
            new BehaviorInvocationContext(sourceId, corp),
            mismatchCatalog));
        Assert.Equal(mismatchJson, EngineJson.Serialize(mismatchState));

        BehaviorCatalog takeCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.TakeHostedCredits, amount: 2)]));
        GameState underflowState = WithCounters(
            CreateState(takeCatalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)),
            CardCounterState.CreateHostedCredit(sourceId, 1, HostedCreditSpendRestriction.AnyCost));
        corp = underflowState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            underflowState,
            new BehaviorInvocationContext(sourceId, corp),
            takeCatalog));
        Assert.Equal(5, underflowState.GetPlayer(corp).Credits);
        Assert.Equal(1, Assert.Single(underflowState.RulesState!.Counters).Count);

        BehaviorCatalog emptyCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.TakeAllHostedCredits)]));
        GameState emptyState = CreateState(
            emptyCatalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        corp = emptyState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            emptyState,
            new BehaviorInvocationContext(sourceId, corp),
            emptyCatalog));

        BehaviorCatalog overflowCatalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [new BehaviorInstruction(
                BehaviorInstructionKind.PlaceHostedCredits,
                amount: 1,
                spendRestriction: HostedCreditSpendRestriction.AnyCost)]));
        GameState overflowState = WithCounters(
            CreateState(overflowCatalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)),
            CardCounterState.CreateHostedCredit(sourceId, int.MaxValue, HostedCreditSpendRestriction.AnyCost));
        corp = overflowState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            overflowState,
            new BehaviorInvocationContext(sourceId, corp),
            overflowCatalog));
        Assert.Equal(int.MaxValue, Assert.Single(overflowState.RulesState!.Counters).Count);
    }

    [Fact]
    public void HostedCreditTakeEmitsTypedPoolEventBeforeWalletTransferAndRoundTrips()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-credit-event");
        CardInstanceId sourceId = new("card:runtime-hosted-credit-event-source");
        BehaviorChoiceSpec gate = new(
            "hosted-credit-event-gate",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption("take", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: gate),
                new BehaviorInstruction(BehaviorInstructionKind.TakeHostedCredits, amount: 1),
            ]));
        GameState state = WithCounters(
            CreateState(catalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)),
            CardCounterState.CreateHostedCredit(sourceId, 1, HostedCreditSpendRestriction.RunCost));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = decision.LegalActions.Single(candidate =>
            candidate.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual(["take"]));
        ApplyResult applied = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.Equal(
            [EngineEventKinds.HostedCreditPoolChanged, EngineEventKinds.CreditsAdjusted],
            applied.Events.Select(@event => @event.Kind));
        HostedCreditPoolChangedEventPayload pool = Assert.IsType<HostedCreditPoolChangedEventPayload>(applied.Events[0].Payload);
        Assert.Equal(HostedCreditPoolOperation.Take, pool.Operation);
        Assert.Equal(sourceId, pool.CardId);
        Assert.Equal(-1, pool.Delta);
        Assert.Equal(0, pool.CountAfter);
        Assert.Equal(HostedCreditSpendRestriction.RunCost, pool.Restriction);
        Assert.Equal(corp, pool.Recipient);
        Assert.True(pool.Emptied);
        Assert.Equal(6, applied.State.GetPlayer(corp).Credits);

        string eventJson = EngineJson.Serialize(applied.Events[0]);
        EngineEvent restoredEvent = EngineJson.Deserialize<EngineEvent>(eventJson);
        Assert.Equal(eventJson, EngineJson.Serialize(restoredEvent));
        Assert.Equal(applied.Events[0], restoredEvent);
        string stateJson = EngineJson.Serialize(applied.State);
        GameState restoredState = EngineJson.DeserializeState(stateJson, catalog);
        Assert.Equal(stateJson, EngineJson.Serialize(restoredState));
        Assert.Equal(DeterministicHash.ForState(applied.State), DeterministicHash.ForState(restoredState));
    }

    [Fact]
    public void GenericAddCounterCannotCreateOrAdjustHostedCreditPool()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-credit-generic-isolation");
        CardInstanceId sourceId = new("card:runtime-hosted-credit-generic-isolation-source");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.AddCounter, amount: 1, argument: "credit")]));
        GameState state = WithCounters(
            CreateState(catalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)),
            CardCounterState.CreateHostedCredit(sourceId, 1, HostedCreditSpendRestriction.AnyCost));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog));
        Assert.Equal(1, Assert.Single(state.RulesState!.Counters).Count);
    }

    [Fact]
    public void NestedModeChoiceRoundTripsAndPreservesOwnerAndHash()
    {
        CardDefinitionId definitionId = new("test:runtime-mode");
        CardInstanceId sourceId = new("card:runtime-mode-source");
        BehaviorChoiceSpec nested = new(
            "runtime-mode",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption("gain", [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 2)]),
                new BehaviorChoiceOption("draw", [new BehaviorInstruction(BehaviorInstructionKind.Draw, 1)]),
            ]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: nested)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (new CardInstanceId("card:runtime-draw"), new CardDefinitionId("test:draw"), ZoneKind.CorpDeck, PlayerSide.Corp));
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, state.Players.Single(player => player.Side == PlayerSide.Corp).Id),
            catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.BehaviorChoice, decision.Kind);
        Assert.Equal(PlayerSide.Corp, prompt.GetPlayer(decision.Owner).Side);
        Assert.All(prompt.ResolutionStack, frame => Assert.IsType<BehaviorResolutionFrame>(frame));

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        LegalAction gain = decision.LegalActions.Single(action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual(["gain"]));
        ApplyResult applied = GameEngine.Apply(
            restored,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, gain.Id),
            catalog);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.Equal(7, applied.State.GetPlayer(decision.Owner).Credits);
        Assert.Null(applied.State.PendingDecision);
    }

    [Fact]
    public void ExactTargetSelectionFreezesCandidatesAndTrashesChosenCard()
    {
        CardDefinitionId definitionId = new("test:runtime-target");
        CardInstanceId sourceId = new("card:runtime-target-source");
        CardInstanceId first = new("card:runtime-target-a");
        CardInstanceId second = new("card:runtime-target-b");
        BehaviorTarget target = BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, BehaviorCardFilter.Any);
        BehaviorChoiceSpec choice = new(
            "runtime-target-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: target);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (first, new CardDefinitionId("test:hq-a"), ZoneKind.CorpHand, PlayerSide.Corp),
            (second, new CardDefinitionId("test:hq-b"), ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(sourceId, corp), catalog);
        BehaviorResolutionFrame frame = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        Assert.Equal([first, second], frame.Choice!.CandidateCardIds);

        Assert.Equal(2, prompt.PendingDecision!.LegalActions.Count);
        LegalAction selected = prompt.PendingDecision.LegalActions[0];
        BehaviorChoiceParameters parameters = Assert.IsType<BehaviorChoiceParameters>(selected.Parameters);
        Assert.Single(parameters.CardIds);
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(prompt.PendingDecision.Owner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, selected.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Contains(parameters.CardIds[0], result.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        Assert.DoesNotContain(parameters.CardIds[0], result.State.GetZone(ZoneKind.CorpHand).CardIds);
    }

    [Fact]
    public void ExactMultiCardSelectionPreservesEveryCardThroughSetAsideAndTransfer()
    {
        CardDefinitionId definitionId = new("test:runtime-multi-target");
        CardInstanceId sourceId = new("card:runtime-multi-target-source");
        CardInstanceId first = new("card:runtime-multi-target-a");
        CardInstanceId second = new("card:runtime-multi-target-b");
        BehaviorTarget headquarters = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            BehaviorCardFilter.Any);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: new BehaviorChoiceSpec(
                        "runtime-multi-target-choice",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(2),
                        target: headquarters)),
                new BehaviorInstruction(
                    BehaviorInstructionKind.TrashCard,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (first, new CardDefinitionId("test:runtime-multi-target-a-definition"), ZoneKind.CorpHand, PlayerSide.Corp),
            (second, new CardDefinitionId("test:runtime-multi-target-b-definition"), ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        LegalAction selection = prompt.PendingDecision!.LegalActions.Single(action =>
            action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.SequenceEqual([first, second]));

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                selection.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Equal(
            [first, second],
            result.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        Assert.All(
            new[] { first, second },
            cardId => Assert.True(result.State.GetCard(cardId).FaceUp));
        Assert.Contains(sourceId, result.State.GetZone(ZoneKind.CorpHand).CardIds);
        CardInstanceId[] setAsideCards = result.State.Zones
            .Where(zone => zone.Kind == ZoneKind.BehaviorSetAside)
            .SelectMany(zone => zone.CardIds)
            .ToArray();
        Assert.DoesNotContain(first, setAsideCards);
        Assert.DoesNotContain(second, setAsideCards);
    }

    [Fact]
    public void CardinalityActionsExposeUpToAndAnyNumberIncludingExplicitSkip()
    {
        CardDefinitionId definitionId = new("test:runtime-cardinality");
        CardInstanceId sourceId = new("card:runtime-cardinality-source");
        BehaviorTarget target = BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, BehaviorCardFilter.Any);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: new BehaviorChoiceSpec(
                        "runtime-up-to",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Optional,
                        BehaviorChoiceCardinality.UpTo(1),
                        target: target)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (new CardInstanceId("card:runtime-cardinality-a"), new CardDefinitionId("test:a"), ZoneKind.CorpHand, PlayerSide.Corp),
            (new CardInstanceId("card:runtime-cardinality-b"), new CardDefinitionId("test:b"), ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(sourceId, corp), catalog);
        Assert.Equal(3, prompt.PendingDecision!.LegalActions.Count);
        Assert.Contains(prompt.PendingDecision.LegalActions, action =>
            action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Count == 0);
    }

    [Fact]
    public void SearchRevealAndSameZoneShufflePreserveFrozenSelection()
    {
        CardDefinitionId definitionId = new("test:runtime-search");
        CardInstanceId sourceId = new("card:runtime-search-source");
        CardInstanceId hidden = new("card:runtime-search-hidden");
        BehaviorTarget stack = BehaviorTarget.CardInZone(BehaviorZone.Stack, BehaviorCardFilter.Any);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Search, sourceZone: BehaviorZone.Stack, filter: BehaviorCardFilter.Any, target: stack),
                new BehaviorInstruction(BehaviorInstructionKind.ShuffleIntoDeck, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard), sourceZone: BehaviorZone.Stack, destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (hidden, new CardDefinitionId("test:hidden"), ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(sourceId, runner), catalog);
        Assert.Single(prompt.PendingDecision!.LegalActions);
        GameState promptRoundTrip = EngineJson.DeserializeState(EngineJson.Serialize(prompt), catalog);
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(promptRoundTrip));

        LegalAction action = prompt.PendingDecision!.LegalActions[0];
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(runner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, action.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.False(result.State.GetCard(hidden).FaceUp);
        Assert.Contains(hidden, result.State.GetZone(ZoneKind.RunnerStack).CardIds);
        int shuffleIndex = result.Events.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled);
        int revealIndex = result.Events.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.BehaviorCardRevealed);
        Assert.True(shuffleIndex >= 0);
        Assert.True(revealIndex > shuffleIndex);
        Assert.Contains(result.Events, @event =>
            @event.Kind == EngineEventKinds.BehaviorCardRevealed
            && @event.Payload is BehaviorCardRevealedEventPayload payload
            && payload.CardId == hidden);
    }

    [Fact]
    public void ArchivesSearchCanAddToHandAndObservationDoesNotLeakOpponentCandidates()
    {
        CardDefinitionId definitionId = new("test:runtime-archives");
        CardInstanceId sourceId = new("card:runtime-archives-source");
        CardInstanceId hidden = new("card:runtime-archives-hidden");
        BehaviorTarget archives = BehaviorTarget.CardInZone(BehaviorZone.Archives, BehaviorCardFilter.Any, reveal: true);
        BehaviorChoiceSpec choice = new(
            "runtime-archives-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: archives);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard), sourceZone: BehaviorZone.Archives, destinationZone: BehaviorZone.Headquarters),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (hidden, new CardDefinitionId("test:archives-card"), ZoneKind.CorpDiscard, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(sourceId, corp), catalog);

        Observation runnerObservation = ObservationBuilder.Build(prompt, PlayerSide.Runner);
        Assert.Null(runnerObservation.Decision);
        Assert.DoesNotContain(hidden.Value, EngineJson.Serialize(runnerObservation), StringComparison.Ordinal);

        LegalAction action = Assert.Single(prompt.PendingDecision!.LegalActions);
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(corp, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, action.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Contains(hidden, result.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.DoesNotContain(hidden, result.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        _ = runner;
    }

    [Fact]
    public void HeadquartersChoiceShufflesSelectedHiddenCardBackToDeck()
    {
        CardDefinitionId definitionId = new("test:runtime-hq-shuffle");
        CardInstanceId sourceId = new("card:runtime-hq-shuffle-source");
        CardInstanceId hidden = new("card:runtime-hq-shuffle-hidden");
        BehaviorChoiceSpec choice = new(
            "runtime-hq-shuffle-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: BehaviorTarget.CardInZone(BehaviorZone.Headquarters, BehaviorCardFilter.Any));
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Headquarters,
                    destinationZone: BehaviorZone.Deck),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (hidden, new CardDefinitionId("test:runtime-hq-shuffle-hidden-definition"), ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        Assert.Single(prompt.PendingDecision!.LegalActions);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                corp,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                prompt.PendingDecision.LegalActions[0].Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Contains(hidden, result.State.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.DoesNotContain(hidden, result.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.False(result.State.GetCard(hidden).FaceUp);
        Assert.Contains(result.Events, @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled);
        Assert.DoesNotContain(hidden.Value, EngineJson.Serialize(ObservationBuilder.Build(result.State, PlayerSide.Runner)), StringComparison.Ordinal);
        _ = runner;
    }

    [Fact]
    public void CorpDeckOutAndRunnerEmptyStackUseDistinctCriticalRules()
    {
        CardDefinitionId corpDefinition = new("test:runtime-corp-draw");
        CardInstanceId corpSource = new("card:runtime-corp-draw-source");
        BehaviorCatalog corpCatalog = Catalog(new CardBehaviorDefinition(
            corpDefinition,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Draw, 1)]));
        GameState corpState = CreateState(corpCatalog, (corpSource, corpDefinition, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = corpState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState deckOut = GameEngine.BeginBehaviorForTesting(corpState, new BehaviorInvocationContext(corpSource, corp), corpCatalog);
        Assert.Equal(GameOutcomeKind.Won, deckOut.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, deckOut.Outcome.Winner);
        Assert.Equal(GameWinReason.DeckOut, deckOut.Outcome.Reason);

        CardDefinitionId runnerDefinition = new("test:runtime-runner-draw");
        CardInstanceId runnerSource = new("card:runtime-runner-draw-source");
        BehaviorCatalog runnerCatalog = Catalog(new CardBehaviorDefinition(
            runnerDefinition,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Draw, 1),
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 3),
            ]));
        GameState runnerState = CreateState(runnerCatalog, (runnerSource, runnerDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner));
        PlayerId runner = runnerState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState runnerResult = GameEngine.BeginBehaviorForTesting(
            runnerState,
            new BehaviorInvocationContext(runnerSource, runner),
            runnerCatalog);
        Assert.Equal(runnerState.GetPlayer(runner).Credits + 3, runnerResult.GetPlayer(runner).Credits);
    }

    [Fact]
    public void SuccessfulRunMarksCurrentFactsAndActualNextRunnerTurnResetsThem()
    {
        GameState baseState = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        CardInstanceId corpDeckCard = new("card:runtime-success-corp-draw");
        baseState = AddCardFixture(
            baseState,
            corpDeckCard,
            new CardDefinitionId("test:runtime-success-corp-draw-definition"),
            ZoneKind.CorpDeck,
            PlayerSide.Corp);
        int turnNumber = baseState.Turn.TurnNumber;
        var facts = new TurnFactsState(
            1,
            PlayerSide.Runner,
            turnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        GameState state = ReplaceTurn(
            baseState,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                turnNumber,
                TurnStep.RunnerAction,
                new TurnHistoryState(1, null, null, facts, null)));

        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            state,
            RunTimingTests.RemoteId(state));
        ApplyResult success = RunTimingTests.PassTwice(started.State);
        Assert.True(success.Accepted, success.Rejection?.Message);
        Assert.Contains(success.Events, @event => @event.Kind == EngineEventKinds.RunSuccessful);
        Assert.True(success.State.Turn.History!.RunnerThisTurn!.SuccessfulRun);

        // Close the active Runner's formal turn through the real turn frame;
        // the forced boundary stands in for a rule effect ending the action
        // phase while clicks remain.
        GameState corpPriority = GameEngine.BeginResolution(
            success.State,
            new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Runner,
                turnNumber,
                TurnFrameStage.ActionPhaseEnd,
                forcedActionEnd: true));
        GameState stateAtCorpPriority = RunTimingTests.PassTwice(corpPriority).State;
        Assert.Equal(PlayerSide.Corp, stateAtCorpPriority.Turn.ActiveSide);

        // Run the Corp's real turn through mandatory draw, three click
        // actions, discard priority, and formal turn end. This reaches the
        // next Runner allotment, where TurnHistory.Start resets its current
        // facts while retaining the just-finished turn as last-turn history.
        GameState corpAction = RunTimingTests.PassTwice(stateAtCorpPriority).State;
        for (int click = 0; click < 3; click++)
        {
            corpAction = RunTimingTests.PassTwice(corpAction).State;
            ApplyResult gain = RunTimingTests.ApplyKind(corpAction, ActionKind.GainCredit);
            Assert.True(gain.Accepted, gain.Rejection?.Message);
            corpAction = gain.State;
        }

        GameState runnerPriority = RunTimingTests.PassTwice(corpAction).State;
        runnerPriority = RunTimingTests.PassTwice(runnerPriority).State;
        Assert.Equal(PlayerSide.Runner, runnerPriority.Turn.ActiveSide);
        Assert.False(runnerPriority.Turn.History!.RunnerThisTurn!.SuccessfulRun);
        Assert.True(runnerPriority.Turn.History.RunnerLastTurn!.SuccessfulRun);
    }

    [Fact]
    public void ForgedBehaviorChoiceAndStaleCandidateAreRejectedBeforeMutation()
    {
        CardDefinitionId definitionId = new("test:runtime-forge");
        CardInstanceId sourceId = new("card:runtime-forge-source");
        CardInstanceId candidate = new("card:runtime-forge-candidate");
        BehaviorChoiceSpec choice = new(
            "runtime-forge-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: BehaviorTarget.CardInZone(BehaviorZone.Headquarters));
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (candidate, new CardDefinitionId("test:candidate"), ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(sourceId, corp), catalog);
        PendingDecision pending = prompt.PendingDecision!;
        LegalAction legal = Assert.Single(pending.LegalActions);

        // The candidate was valid when the decision was issued, but a stale
        // save/load boundary or another reducer may move it before submission.
        // Revalidate the frozen candidate against the current physical zone
        // before any behavior mutation is allowed.
        GameState stale = MoveCardFixture(prompt, candidate, ZoneKind.CorpDiscard);
        string staleHash = DeterministicHash.ForState(stale);
        ApplyResult staleResult = GameEngine.Apply(
            stale,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, legal.Id),
            catalog);
        Assert.False(staleResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, staleResult.Rejection?.Code);
        Assert.Equal(staleHash, DeterministicHash.ForState(staleResult.State));

        var forged = new LegalAction(
            legal.Id,
            ActionKind.ChooseBehavior,
            legal.Description,
            new BehaviorChoiceParameters(pending.LegalActions[0].Parameters is BehaviorChoiceParameters p
                ? p.ChoiceKey
                : "runtime-forge-choice",
                new[] { new CardInstanceId("card:forged") }));
        GameState tampered = ReplacePending(prompt, new PendingDecision(
            pending.Id,
            pending.Revision,
            pending.Owner,
            pending.Kind,
            pending.Prompt,
            [forged]));
        ApplyResult result = GameEngine.Apply(
            tampered,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, forged.Id),
            catalog);
        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, result.Rejection?.Code);
    }

    [Fact]
    public void BehaviorEntryPointIsInternalAndCannotBeCalledByPublicCallers()
    {
        CardDefinitionId definitionId = new("test:runtime-installed-source");
        CardInstanceId sourceId = new("card:runtime-installed-source");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.RunnerRig, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        Assert.DoesNotContain(
            typeof(GameEngineSession).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
            method => method.Name == "BeginBehavior");
        Assert.DoesNotContain(
            typeof(EngineSession).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance),
            method => method.Name == "BeginBehavior");
    }

    [Fact]
    public void SerializedBehaviorFrameContainsCatalogCursorNotExecutableInstructionLists()
    {
        CardDefinitionId definitionId = new("test:runtime-serialized-cursor");
        CardInstanceId sourceId = new("card:runtime-serialized-source");
        BehaviorChoiceSpec choice = new(
            "runtime-serialized-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption("left", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("right", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]));
        GameState state = CreateState(catalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);

        using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(EngineJson.Serialize(prompt));
        System.Text.Json.JsonElement frame = json.RootElement
            .GetProperty("resolutionStack")[0];
        Assert.False(frame.TryGetProperty("rootInstructions", out _));
        Assert.False(frame.TryGetProperty("instructions", out _));
        Assert.True(frame.TryGetProperty("continuation", out _));
        Assert.True(frame.TryGetProperty("catalogFingerprint", out _));
        Assert.True(frame.TryGetProperty("frameIdentity", out _));
    }

    [Fact]
    public void ForgedCatalogContinuationIsRejectedBeforeMutation()
    {
        CardDefinitionId definitionId = new("test:runtime-forged-continuation");
        CardInstanceId sourceId = new("card:runtime-forged-continuation-source");
        BehaviorChoiceSpec choice = new(
            "runtime-continuation-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption("left", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("right", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]));
        GameState state = CreateState(catalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        BehaviorResolutionFrame original = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        BehaviorResolutionFrame forged = new(
            original.Version,
            original.BehaviorId,
            original.DefinitionId,
            original.SourceCardId,
            original.Controller,
            original.ActingPlayer,
            original.CatalogFingerprint,
            original.DefinitionFingerprint,
            original.Stage,
            original.InstructionCursor,
            original.Bindings,
            original.SetAside,
            original.Choice,
            [new BehaviorContinuationStep(
                original.InstructionCursor,
                "forged-continuation",
                BehaviorContinuationKind.ChoiceOption,
                "left")],
            original.SetAsideZoneId,
            original.ParentBehaviorId,
            original.FrameIdentity,
            original.SourceZoneId,
            original.SourceZoneKind,
            original.ParentFrameIdentity);
        GameState tampered = ReplaceResolutionStack(prompt, [forged]);

        ApplyResult result = GameEngine.Apply(
            tampered,
            new ActionSubmission(
                corp,
                tampered.PendingDecision!.Id,
                tampered.PendingDecision.Revision,
                tampered.PendingDecision.LegalActions[0].Id),
            catalog);
        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, result.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(tampered), DeterministicHash.ForState(result.State));
    }

    [Fact]
    public void NestedBehaviorChildBindsExactParentIdentityAndResumesParent()
    {
        CardDefinitionId parentDefinitionId = new("test:runtime-parent");
        CardDefinitionId childDefinitionId = new("test:runtime-child");
        CardInstanceId parentSourceId = new("card:runtime-parent-source");
        CardInstanceId childSourceId = new("card:runtime-child-source");
        BehaviorChoiceSpec childChoice = new(
            "runtime-child-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption("left", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("right", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        CardBehaviorDefinition parentDefinition = new(
            parentDefinitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        CardBehaviorDefinition childDefinition = new(
            childDefinitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: childChoice)]);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:behavior-nesting")
            .Add(parentDefinition)
            .Add(childDefinition)
            .Build();
        GameState state = CreateState(
            catalog,
            (parentSourceId, parentDefinitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (childSourceId, childDefinitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ZoneState hand = state.GetZone(ZoneKind.CorpHand);
        var parent = new BehaviorResolutionFrame(
            parentDefinition,
            new BehaviorInvocationContext(parentSourceId, corp, corp),
            catalog.Fingerprint,
            catalog.GetDefinitionFingerprint(parentDefinitionId),
            "behavior-frame:1",
            hand.Id,
            hand.Kind,
            parentBehaviorId: null,
            parentFrameIdentity: null);
        state = ReplaceResolutionStack(state, [parent]);

        GameState nested = GameEngine.BeginBehaviorChildForTesting(
            state,
            new BehaviorInvocationContext(childSourceId, corp),
            catalog);
        BehaviorResolutionFrame actualParent = Assert.IsType<BehaviorResolutionFrame>(nested.ResolutionStack[0]);
        BehaviorResolutionFrame child = Assert.IsType<BehaviorResolutionFrame>(nested.ResolutionStack[1]);
        Assert.Equal(actualParent.BehaviorId, child.ParentBehaviorId);
        Assert.Equal(BehaviorFrameIdentity.Compute(actualParent), child.ParentFrameIdentity);
        Assert.Equal(child.ParentFrameIdentity, BehaviorFrameIdentity.Compute(actualParent));

        string json = EngineJson.Serialize(nested);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(nested), DeterministicHash.ForState(restored));

        BehaviorResolutionFrame forged = new(
            child.Version,
            child.BehaviorId,
            child.DefinitionId,
            child.SourceCardId,
            child.Controller,
            child.ActingPlayer,
            child.CatalogFingerprint,
            child.DefinitionFingerprint,
            child.Stage,
            child.InstructionCursor,
            child.Bindings,
            child.SetAside,
            child.Choice,
            child.Continuation,
            child.SetAsideZoneId,
            child.ParentBehaviorId,
            child.FrameIdentity,
            child.SourceZoneId,
            child.SourceZoneKind,
            "parent:forged");
        GameState tampered = ReplaceResolutionStack(nested, [actualParent, forged]);
        PendingDecision pending = Assert.IsType<PendingDecision>(tampered.PendingDecision);
        ApplyResult rejected = GameEngine.Apply(
            tampered,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[0].Id),
            catalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(tampered), DeterministicHash.ForState(rejected.State));

        PendingDecision validPending = Assert.IsType<PendingDecision>(restored.PendingDecision);
        ApplyResult completed = GameEngine.Apply(
            restored,
            new ActionSubmission(validPending.Owner, validPending.Id, validPending.Revision, validPending.LegalActions[0].Id),
            catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.PendingDecision);
        Assert.Empty(completed.State.ResolutionStack);
    }

    [Fact]
    public void LargeAnyNumberSelectionUsesBoundedIncrementalProtocol()
    {
        CardDefinitionId definitionId = new("test:runtime-large-any");
        CardInstanceId sourceId = new("card:runtime-large-any-source");
        var placements = new List<(CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)>
        {
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
        };
        for (int index = 0; index < 10; index++)
        {
            placements.Add((
                new CardInstanceId($"card:runtime-large-any-{index}"),
                new CardDefinitionId($"test:runtime-large-any-{index}"),
                ZoneKind.CorpHand,
                PlayerSide.Corp));
        }

        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.Choose,
                choice: new BehaviorChoiceSpec(
                    "runtime-large-any-choice",
                    BehaviorChooser.Corp,
                    BehaviorChoiceMode.TargetSelection,
                    BehaviorChoiceCardinality.AnyNumber,
                    target: BehaviorTarget.CardsInZone(BehaviorZone.Headquarters))) ]));
        GameState state = CreateState(catalog, placements.ToArray());
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);

        Assert.Equal(11, prompt.PendingDecision!.LegalActions.Count);
        Assert.All(prompt.PendingDecision.LegalActions, action =>
            Assert.IsType<BehaviorChoiceParameters>(action.Parameters));
        Assert.Contains(prompt.PendingDecision.LegalActions, action =>
            action.Parameters is BehaviorChoiceParameters parameters
            && parameters.FinalizeSelection
            && parameters.CardIds.Count == 0);

        LegalAction add = Assert.IsAssignableFrom<LegalAction>(
            prompt.PendingDecision.LegalActions.First(
                action => action.Parameters is BehaviorChoiceParameters parameters
                    && !parameters.FinalizeSelection
                    && parameters.CardIds.Count == 1));
        BehaviorChoiceParameters addParameters = Assert.IsType<BehaviorChoiceParameters>(add.Parameters);
        ApplyResult partial = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                corp,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                add.Id),
            catalog);
        Assert.True(partial.Accepted, partial.Rejection?.Message);
        BehaviorResolutionFrame partialFrame = Assert.IsType<BehaviorResolutionFrame>(
            partial.State.ResolutionStack.Single());
        Assert.Equal(addParameters.CardIds, partialFrame.Choice!.SelectedCardIds);

        string partialJson = EngineJson.Serialize(partial.State);
        GameState restored = EngineJson.DeserializeState(partialJson, catalog);
        Assert.Equal(partialJson, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(partial.State), DeterministicHash.ForState(restored));

        LegalAction finalize = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.FinalizeSelection
                && parameters.CardIds.SequenceEqual(addParameters.CardIds));
        ApplyResult completed = GameEngine.Apply(
            restored,
            new ActionSubmission(
                corp,
                restored.PendingDecision.Id,
                restored.PendingDecision.Revision,
                finalize.Id),
            catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.PendingDecision);
        Assert.Empty(completed.State.ResolutionStack);
    }

    [Fact]
    public void MandatoryTargetChoiceWithNoCandidatesCompletesWithoutPrompt()
    {
        CardDefinitionId definitionId = new("test:runtime-empty-mandatory");
        CardInstanceId sourceId = new("card:runtime-empty-mandatory-source");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.Choose,
                choice: new BehaviorChoiceSpec(
                    "runtime-empty-mandatory-choice",
                    BehaviorChooser.Corp,
                    BehaviorChoiceMode.Mandatory,
                    BehaviorChoiceCardinality.Exactly(1),
                    target: BehaviorTarget.CardInZone(BehaviorZone.Headquarters))) ]));
        GameState state = CreateState(catalog, (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
    }

    [Fact]
    public void EmptyMandatoryTargetPersistsNoOpForChosenTransferAndContinues()
    {
        CardDefinitionId definitionId = new("test:runtime-empty-transfer");
        CardInstanceId sourceId = new("card:runtime-empty-transfer-source");
        BehaviorTarget emptyArchives = BehaviorTarget.CardInZone(
            BehaviorZone.Archives,
            BehaviorCardFilter.Any);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: new BehaviorChoiceSpec(
                        "runtime-empty-transfer-choice",
                        BehaviorChooser.Corp,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: emptyArchives)),
                new BehaviorInstruction(
                    BehaviorInstructionKind.TrashCard,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);

        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
        Assert.Equal(state.GetPlayer(corp).Credits + 2, result.GetPlayer(corp).Credits);
        Assert.Contains(sourceId, result.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Empty(result.GetZone(ZoneKind.CorpDiscard).CardIds);
    }

    [Fact]
    public void ChosenCardWithoutEmptySelectionContinuationRejectsAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-missing-chosen");
        CardInstanceId sourceId = new("card:runtime-missing-chosen-source");
        BehaviorChoiceSpec optionChoice = new(
            "runtime-missing-chosen-option",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption(
                "trash",
                [new BehaviorInstruction(
                    BehaviorInstructionKind.TrashCard,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard))]),
             new BehaviorChoiceOption("leave", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: optionChoice)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        LegalAction trash = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["trash"]));
        string hash = DeterministicHash.ForState(prompt);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                corp,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                trash.Id),
            catalog);

        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, result.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(result.State));
        Assert.Contains(sourceId, result.State.GetZone(ZoneKind.CorpHand).CardIds);
    }

    [Fact]
    public void ForgedChosenBindingWithoutEmptySelectionContinuationRejectsAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-forged-chosen");
        CardInstanceId sourceId = new("card:runtime-forged-chosen-source");
        BehaviorChoiceSpec choice = new(
            "runtime-forged-chosen-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
             new BehaviorChoiceOption("leave", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        BehaviorResolutionFrame original = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        BehaviorResolutionFrame forged = new(
            original.Version,
            original.BehaviorId,
            original.DefinitionId,
            original.SourceCardId,
            original.Controller,
            original.ActingPlayer,
            original.CatalogFingerprint,
            original.DefinitionFingerprint,
            original.Stage,
            original.InstructionCursor,
            [new BehaviorBinding("chosen", [sourceId])],
            original.SetAside,
            original.Choice,
            original.Continuation,
            original.SetAsideZoneId,
            original.ParentBehaviorId,
            original.FrameIdentity,
            original.SourceZoneId,
            original.SourceZoneKind,
            original.ParentFrameIdentity);
        GameState tampered = ReplaceResolutionStack(prompt, [forged]);
        string hash = DeterministicHash.ForState(tampered);
        LegalAction skip = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["skip"]));

        ApplyResult result = GameEngine.Apply(
            tampered,
            new ActionSubmission(
                corp,
                tampered.PendingDecision!.Id,
                tampered.PendingDecision.Revision,
                skip.Id),
            catalog);

        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, result.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(result.State));
    }

    [Fact]
    public void SetAsideRevealAndLaterGripChoicePreserveHiddenStateAndBinding()
    {
        CardDefinitionId definitionId = new("test:runtime-setaside");
        CardInstanceId sourceId = new("card:runtime-setaside-source");
        CardInstanceId hidden = new("card:runtime-setaside-hidden");
        BehaviorChoiceSpec disposition = new(
            "runtime-setaside-disposition",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption(
                "grip",
                [new BehaviorInstruction(
                    BehaviorInstructionKind.AddToHand,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Grip)],
                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
             new BehaviorChoiceOption("leave", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Search,
                    sourceZone: BehaviorZone.Stack,
                    filter: BehaviorCardFilter.Any,
                    target: BehaviorTarget.CardInZone(BehaviorZone.Stack, BehaviorCardFilter.Any)),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: disposition),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (hidden, new CardDefinitionId("test:runtime-setaside-hidden-definition"), ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction search = Assert.Single(prompt.PendingDecision!.LegalActions);
        ApplyResult selected = GameEngine.Apply(
            prompt,
            new ActionSubmission(runner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, search.Id),
            catalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        BehaviorResolutionFrame waiting = Assert.IsType<BehaviorResolutionFrame>(selected.State.ResolutionStack.Single());
        Assert.DoesNotContain(
            hidden,
            selected.State.Zones
                .Where(zone => zone.Kind == ZoneKind.BehaviorSetAside)
                .SelectMany(zone => zone.CardIds));
        Assert.Contains(hidden, selected.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.False(selected.State.GetCard(hidden).FaceUp);
        Assert.Contains(waiting.Bindings, binding => binding.Key == "chosen" && binding.CardIds.SequenceEqual([hidden]));
        Assert.Empty(waiting.SetAside);
        Assert.Null(waiting.SetAsideZoneId);
        Assert.Contains(selected.Events, @event =>
            @event.Kind == EngineEventKinds.BehaviorCardRevealed
            && @event.Payload is BehaviorCardRevealedEventPayload payload
            && payload.CardId == hidden);
        Assert.Contains(selected.Events, @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled);
        Assert.DoesNotContain(hidden.Value, EngineJson.Serialize(ObservationBuilder.Build(selected.State, PlayerSide.Corp)), StringComparison.Ordinal);

        LegalAction grip = Assert.Single(
            selected.State.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["grip"]));
        ApplyResult completed = GameEngine.Apply(
            selected.State,
            new ActionSubmission(runner, selected.State.PendingDecision.Id, selected.State.PendingDecision.Revision, grip.Id),
            catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Contains(hidden, completed.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(hidden, completed.State.Zones.Where(zone => zone.Kind == ZoneKind.BehaviorSetAside).SelectMany(zone => zone.CardIds));
        Assert.False(completed.State.GetCard(hidden).FaceUp);
    }

    [Fact]
    public void InstalledNotThisTurnTracksActualInstallAndResetsAtNextTurn()
    {
        CardDefinitionId queryDefinition = new("test:runtime-fact-query");
        CardInstanceId querySource = new("card:runtime-fact-query-source");
        CardInstanceId installed = new("card:runtime-fact-installed");
        BehaviorChoiceSpec queryChoice = new(
            "runtime-fact-query-choice",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: BehaviorTarget.InstalledCardNotThisTurn());
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:behavior-facts")
            .Add(new CardBehaviorDefinition(
                queryDefinition,
                PlayerSide.Runner,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: queryChoice)]))
            .Build();
        GameState state = CreateState(
            catalog,
            (querySource, queryDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (installed, new CardDefinitionId("test:runtime-fact-program"), ZoneKind.RunnerRig, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        state = ReplaceTurn(state, new TurnState(
            PlayerSide.Runner,
            TurnPhase.Runner,
            1,
            TurnStep.RunnerAction,
            new TurnHistoryState(
                1,
                null,
                null,
                new TurnFactsState(
                    1,
                    PlayerSide.Runner,
                    1,
                    4,
                    0,
                    true,
                    false,
                    false,
                    false,
                    installedCardIds: [installed]),
                null)));

        GameState sameTurnQuery = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(querySource, runner),
            catalog);
        Assert.Null(sameTurnQuery.PendingDecision);
        string runnerObservation = EngineJson.Serialize(
            ObservationBuilder.Build(sameTurnQuery, PlayerSide.Runner));
        string corpObservation = EngineJson.Serialize(
            ObservationBuilder.Build(sameTurnQuery, PlayerSide.Corp));
        Assert.Contains(installed.Value, runnerObservation, StringComparison.Ordinal);
        Assert.Contains(installed.Value, corpObservation, StringComparison.Ordinal);

        GameState nextTurn = ReplaceTurn(sameTurnQuery, new TurnState(
            PlayerSide.Runner,
            TurnPhase.Runner,
            2,
            TurnStep.RunnerAction,
            new TurnHistoryState(
                1,
                null,
                null,
                new TurnFactsState(1, PlayerSide.Runner, 2, 4, 0, true, false, false, false),
                sameTurnQuery.Turn.History!.RunnerThisTurn)));
        GameState nextTurnQuery = GameEngine.BeginBehaviorForTesting(
            nextTurn,
            new BehaviorInvocationContext(querySource, runner),
            catalog);
        Assert.NotNull(nextTurnQuery.PendingDecision);
        Assert.Contains(nextTurnQuery.PendingDecision!.LegalActions, action =>
            action.Parameters is BehaviorChoiceParameters parameters
            && parameters.CardIds.Contains(installed));
    }

    [Fact]
    public void StaticModifiersProjectFromActiveCardWithoutAccumulating()
    {
        CardDefinitionId definitionId = new("test:runtime-static");
        CardInstanceId sourceId = new("card:runtime-static-source");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            staticModifiers:
            [
                new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, 1),
                new BehaviorStaticModifier(BehaviorStaticModifierKind.MaximumHandSize, 2),
            ]));
        GameState state = CreateState(catalog, (sourceId, definitionId, ZoneKind.RunnerRig, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        Assert.Equal(1, BehaviorStaticModifierProjection.Sum(
            catalog,
            state.Cards,
            state.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits));
        Assert.Equal(2, BehaviorStaticModifierProjection.Sum(
            catalog,
            state.Cards,
            state.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MaximumHandSize));
        GameState first = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        GameState second = GameEngine.BeginBehaviorForTesting(
            first,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        Assert.Equal(4, second.RulesState?.Players.Single(player => player.PlayerId == runner).BaseMemory ?? 4);
        Assert.Equal(1, BehaviorStaticModifierProjection.Sum(
            catalog,
            second.Cards,
            second.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits));
        Assert.Equal(2, BehaviorStaticModifierProjection.Sum(
            catalog,
            second.Cards,
            second.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MaximumHandSize));

        GameState trashed = MoveCardFixture(second, sourceId, ZoneKind.RunnerHeap);
        Assert.Equal(0, BehaviorStaticModifierProjection.Sum(
            catalog,
            trashed.Cards,
            trashed.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MemoryUnits));
        Assert.Equal(0, BehaviorStaticModifierProjection.Sum(
            catalog,
            trashed.Cards,
            trashed.Zones,
            PlayerSide.Runner,
            BehaviorStaticModifierKind.MaximumHandSize));
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            trashed,
            new BehaviorInvocationContext(sourceId, runner),
            catalog));
    }

    [Fact]
    public void GenericAdvanceUsesHonestBehaviorPayloadWithoutAgendaRequirement()
    {
        CardDefinitionId definitionId = new("test:runtime-advance");
        CardInstanceId sourceId = new("card:runtime-advance-source");
        CardInstanceId targetId = new("card:runtime-advance-target");
        BehaviorChoiceSpec choice = new(
            "runtime-advance-confirm",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption(
                "advance",
                [new BehaviorInstruction(
                    BehaviorInstructionKind.AdvanceCard,
                    amount: 2,
                    target: new BehaviorTarget(BehaviorTargetKind.InstalledCard))]),
             new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (targetId, new CardDefinitionId("test:runtime-advance-target-definition"), ZoneKind.RunnerRig, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction advance = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["advance"]));
        ApplyResult applied = GameEngine.Apply(
            prompt,
            new ActionSubmission(runner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, advance.Id),
            catalog);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        GameState result = applied.State;

        Assert.Equal(2, result.GetCard(targetId).AdvancementCounters);
        Assert.DoesNotContain(result.ResolutionStack, frame => frame is BehaviorResolutionFrame);
        Assert.Contains(applied.Events, @event =>
            @event.Kind == EngineEventKinds.BehaviorCardAdvanced
            && @event.Payload is BehaviorCardAdvancedEventPayload payload
            && payload.CardId == targetId
            && payload.Amount == 2
            && payload.AdvancementCounters == 2);
        Assert.DoesNotContain(applied.Events, @event => @event.Kind == EngineEventKinds.AgendaAdvanced);
    }

    [Fact]
    public void InstalledChoiceBindsWithoutMovingAndChosenAdvanceUsesOriginSelector()
    {
        CardDefinitionId definitionId = new("test:runtime-installed-choice");
        CardInstanceId sourceId = new("card:runtime-installed-choice-source");
        CardInstanceId targetId = new("card:runtime-installed-choice-target");
        BehaviorTarget target = new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Runner));
        BehaviorChoiceSpec choice = new(
            "runtime-installed-choice",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: target);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(
                    BehaviorInstructionKind.AdvanceCard,
                    amount: 2,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (targetId, new CardDefinitionId("test:runtime-installed-target"), ZoneKind.RunnerRig, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction selection = Assert.Single(prompt.PendingDecision!.LegalActions);
        BehaviorResolutionFrame promptedFrame = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        Assert.Contains(targetId, promptedFrame.Choice!.CandidateCardIds);
        Assert.Contains(targetId, prompt.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.DoesNotContain(targetId, prompt.Zones.Where(zone => zone.Kind == ZoneKind.BehaviorSetAside).SelectMany(zone => zone.CardIds));

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(runner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, selection.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal([targetId], result.State.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Equal(2, result.State.GetCard(targetId).AdvancementCounters);
        Assert.Contains(result.Events, @event =>
            @event.Kind == EngineEventKinds.BehaviorCardAdvanced
            && @event.Payload is BehaviorCardAdvancedEventPayload payload
            && payload.CardId == targetId
            && payload.Amount == 2);
    }

    [Fact]
    public void ChosenAdvanceRejectsMovedTargetAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-stale-installed-choice");
        CardInstanceId sourceId = new("card:runtime-stale-installed-source");
        CardInstanceId targetId = new("card:runtime-stale-installed-target");
        BehaviorTarget target = new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Runner));
        BehaviorChoiceSpec choice = new(
            "runtime-stale-installed-choice",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: target);
        BehaviorChoiceSpec pause = new(
            "runtime-stale-installed-pause",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("continue"), new BehaviorChoiceOption("decline")]);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: pause),
                new BehaviorInstruction(
                    BehaviorInstructionKind.AdvanceCard,
                    amount: 2,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (targetId, new CardDefinitionId("test:runtime-stale-installed-target"), ZoneKind.RunnerRig, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction select = Assert.Single(prompt.PendingDecision!.LegalActions);
        ApplyResult selected = GameEngine.Apply(
            prompt,
            new ActionSubmission(runner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, select.Id),
            catalog);
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.NotNull(selected.State.PendingDecision);

        GameState stale = MoveCardFixture(selected.State, targetId, ZoneKind.RunnerHeap);
        PendingDecision pending = stale.PendingDecision!;
        LegalAction continueAction = Assert.Single(
            pending.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["continue"]));
        ApplyResult rejected = GameEngine.Apply(
            stale,
            new ActionSubmission(runner, pending.Id, pending.Revision, continueAction.Id),
            catalog);

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, rejected.Rejection?.Code);
        Assert.Same(stale, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(0, stale.GetCard(targetId).AdvancementCounters);
    }

    [Fact]
    public void ChosenAdvanceRejectsReachableNonInstalledOriginAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-noninstalled-origin");
        CardInstanceId sourceId = new("card:runtime-noninstalled-origin-source");
        CardInstanceId targetId = new("card:runtime-noninstalled-origin-target");
        BehaviorChoiceSpec choice = new(
            "runtime-noninstalled-origin-choice",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: BehaviorTarget.CardInZone(BehaviorZone.Headquarters));
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(
                    BehaviorInstructionKind.AdvanceCard,
                    amount: 2,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            ]));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (targetId, new CardDefinitionId("test:runtime-noninstalled-target"), ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog);
        LegalAction target = Assert.Single(prompt.PendingDecision!.LegalActions);
        string hash = DeterministicHash.ForState(prompt);

        ApplyResult rejected = GameEngine.Apply(
            prompt,
            new ActionSubmission(corp, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, target.Id),
            catalog);

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, rejected.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        Assert.Same(prompt, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Contains(targetId, rejected.State.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.DoesNotContain(
            targetId,
            rejected.State.Zones
                .Where(zone => zone.Kind == ZoneKind.BehaviorSetAside)
                .SelectMany(zone => zone.CardIds));
    }

    private static BehaviorCatalog Catalog(CardBehaviorDefinition definition) =>
        new BehaviorCatalogBuilder("test:behavior-runtime").Add(definition).Build();

    private static GameState WithCounters(GameState state, params CardCounterState[] counters)
    {
        RulesState defaults = state.RulesState ?? RulesState.CreateDefault(state.Players);
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        return new GameState(
            gameId: state.GameId,
            rulesVersion: state.RulesVersion,
            cardDataVersion: state.CardDataVersion,
            players: state.Players,
            cards: state.Cards,
            zones: state.Zones,
            servers: state.Servers,
            turn: state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: state.Allocator,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState,
            rulesState: rules,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);
    }

    private static GameState ReplaceResolutionStack(
        GameState state,
        IReadOnlyList<ResolutionFrame> frames) => new(
        state.SchemaVersion,
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
        state.DecisionRevision,
        state.EventSequence,
        frames,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint);

    private static GameState ReplaceTurn(GameState state, TurnState turn) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        turn,
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

    private static GameState CreateState(
        BehaviorCatalog catalog,
        params (CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)[] placements)
    {
        GameState baseline = GameState.CreateNew(
            seed: 987,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        var cards = placements.Select(placement => new CardInstanceState(
            placement.Id,
            placement.DefinitionId,
            baseline.GetZone(placement.Zone).Id,
            placement.Owner,
            faceUp: placement.Zone is ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea)).ToArray();
        var zones = baseline.Zones.Select(zone =>
        {
            CardInstanceId[] additions = placements
                .Where(placement => placement.Zone == zone.Kind)
                .Select(placement => placement.Id)
                .ToArray();
            return additions.Length == 0
                ? zone
                : new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Concat(additions).ToArray());
        }).ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers,
            baseline.Turn,
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
            catalog.Version,
            catalog.Fingerprint);
    }

    private static GameState ReplacePending(GameState state, PendingDecision pending) => new(
        state.SchemaVersion,
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
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        pending,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint);

    private static GameState MoveCardFixture(GameState state, CardInstanceId cardId, ZoneKind destinationKind)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.Zones.Single(zone => zone.Id == card.ZoneId);
        ZoneState destination = state.GetZone(destinationKind);
        if (source.Id == destination.Id)
        {
            return state;
        }

        var sourceIds = source.CardIds.Where(id => id != cardId).ToArray();
        var destinationIds = destination.CardIds.Append(cardId).ToArray();
        var zones = state.Zones.Select(zone => zone.Id == source.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, sourceIds)
            : zone.Id == destination.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, destinationIds)
                : zone).ToArray();
        bool faceUp = destination.Kind is ZoneKind.RunnerRig
            or ZoneKind.RunnerPlayArea
            or ZoneKind.RunnerHeap
            or ZoneKind.CorpDiscard;
        var cards = state.Cards.Select(value => value.Id == cardId
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

    private static GameState AddCardFixture(
        GameState state,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        ZoneKind destinationKind,
        PlayerSide ownerSide)
    {
        ZoneState destination = state.GetZone(destinationKind);
        CardInstanceState card = new(
            cardId,
            definitionId,
            destination.Id,
            ownerSide,
            faceUp: destinationKind is ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea);
        var zones = state.Zones.Select(zone => zone.Id == destination.Id
            ? new ZoneState(
                zone.Id,
                zone.Kind,
                zone.OwnerSide,
                zone.ServerId,
                zone.CardIds.Append(cardId).ToArray())
            : zone).ToArray();
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards.Append(card).ToArray(),
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
}
