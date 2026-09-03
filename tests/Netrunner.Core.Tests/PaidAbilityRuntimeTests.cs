using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Small end-to-end checks for the A2b paid-ability cursor.  The fixture is
/// deliberately a real Runner turn shell rather than a direct reducer seam:
/// the action is materialized into the pending decision, consumed, paid, and
/// executed through the normal behavior child.
/// </summary>
public sealed class PaidAbilityRuntimeTests
{
    [Fact]
    public void ClickAbilityPaysAndExecutesItsDeclarationInstructions()
    {
        CardDefinitionId definitionId = new("test:runtime-click");
        CardInstanceId sourceId = new("card:runtime-click");
        BehaviorPaidAbility ability = Ability(
            "ability:click",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks)],
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 3, clicks: 4);
        TurnFrame frame = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.AwaitingAction);

        GameState prompt = GameEngine.BeginResolution(state, frame, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = Assert.Single(
            decision.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(5, result.State.GetPlayer(sourceOwner: PlayerSide.Runner).Credits);
        Assert.Equal(3, result.State.GetPlayer(sourceOwner: PlayerSide.Runner).Clicks);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack.Single()).Stage);
        Assert.NotNull(result.State.PendingDecision);
        Assert.DoesNotContain(result.State.PendingDecision!.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);
    }

    [Fact]
    public void PaidAbilityMarksOncePerTurnAndExecutesWithoutDefinitionCosts()
    {
        CardDefinitionId definitionId = new("test:runtime-paid");
        CardInstanceId sourceId = new("card:runtime-paid");
        BehaviorPaidAbility ability = Ability(
            "ability:paid",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits, 2)],
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)],
            usageLimit: BehaviorAbilityUsageLimit.OncePerTurn);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        TurnFrame frame = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);

        GameState prompt = GameEngine.BeginResolution(state, frame, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = Assert.Single(
            decision.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(6, result.State.GetPlayer(sourceOwner: PlayerSide.Runner).Credits);
        TurnFactsState facts = result.State.Turn.History!.ThisTurn(PlayerSide.Runner)!;
        Assert.Contains(facts.UsedAbilityKeys, key => key.SourceCardId == sourceId && key.AbilityKey == ability.Key);
        Assert.DoesNotContain(result.State.PendingDecision!.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);
        Assert.Equal(TurnFrameStage.BeforeTurnPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack.Single()).Stage);
    }

    [Fact]
    public void HostedCreditAbilityRequiresAnAbilityEligibleSourcePool()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-cost-legality");
        CardInstanceId sourceId = new("card:runtime-hosted-cost-legality");
        BehaviorPaidAbility ability = Ability(
            "ability:hosted-cost-legality",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.HostedCredits)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);

        foreach (HostedCreditSpendRestriction restriction in Enum.GetValues<HostedCreditSpendRestriction>())
        {
            GameState state = BuildRunnerState(
                catalog,
                definitionId,
                sourceId,
                credits: 5,
                clicks: 4,
                counters: [CardCounterState.CreateHostedCredit(sourceId, 1, restriction)]);
            GameState prompt = BeginPaidPrompt(state, catalog);
            bool legal = restriction is HostedCreditSpendRestriction.AbilityCost
                or HostedCreditSpendRestriction.AnyCost;
            Assert.Equal(
                legal,
                prompt.PendingDecision!.LegalActions.Any(candidate => candidate.Kind == ActionKind.UseAbility));
        }

        foreach (CardCounterState counter in new[]
                 { new CardCounterState(sourceId, "charge", 1), new CardCounterState(sourceId, "virus", 1) })
        {
            GameState state = BuildRunnerState(
                catalog,
                definitionId,
                sourceId,
                credits: 5,
                clicks: 4,
                counters: [counter]);
            GameState prompt = BeginPaidPrompt(state, catalog);
            Assert.DoesNotContain(prompt.PendingDecision!.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);
        }
    }

    [Fact]
    public void RepeatedHostedCreditCostsAggregateAndPayInDeclaredOrder()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-cost-repeat");
        CardInstanceId sourceId = new("card:runtime-hosted-cost-repeat");
        BehaviorPaidAbility ability = Ability(
            "ability:hosted-cost-repeat",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [
                new BehaviorCost(BehaviorCostKind.HostedCredits, 2),
                new BehaviorCost(BehaviorCostKind.HostedCredits, 1),
            ],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);

        GameState insufficient = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 5,
            clicks: 4,
            counters: [CardCounterState.CreateHostedCredit(sourceId, 2, HostedCreditSpendRestriction.AnyCost)]);
        GameState insufficientPrompt = BeginPaidPrompt(insufficient, catalog);
        Assert.DoesNotContain(insufficientPrompt.PendingDecision!.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);

        GameState state = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 5,
            clicks: 4,
            counters: [CardCounterState.CreateHostedCredit(sourceId, 3, HostedCreditSpendRestriction.AnyCost)]);
        GameState prompt = BeginPaidPrompt(state, catalog);
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision!.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                Assert.Single(prompt.PendingDecision.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility).Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        HostedCreditPoolChangedEventPayload[] events = result.Events
            .Select(@event => @event.Payload)
            .OfType<HostedCreditPoolChangedEventPayload>()
            .ToArray();
        Assert.Equal(2, events.Length);
        Assert.All(events, value =>
        {
            Assert.Equal(HostedCreditPoolOperation.Spend, value.Operation);
            Assert.Equal(sourceId, value.CardId);
            Assert.Equal(HostedCreditSpendRestriction.AnyCost, value.Restriction);
            Assert.Null(value.Recipient);
        });
        Assert.Equal([-2, -1], events.Select(value => value.Delta));
        Assert.Equal([1, 0], events.Select(value => value.CountAfter));
        Assert.False(events[0].Emptied);
        Assert.True(events[1].Emptied);
        Assert.Empty(result.State.RulesState!.Counters);
    }

    [Fact]
    public void HostedCreditAbilityRejectsStaleInsufficientPoolAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-cost-stale");
        CardInstanceId sourceId = new("card:runtime-hosted-cost-stale");
        BehaviorPaidAbility ability = Ability(
            "ability:hosted-cost-stale",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.HostedCredits, 2)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 5,
            clicks: 4,
            counters: [CardCounterState.CreateHostedCredit(sourceId, 2, HostedCreditSpendRestriction.AbilityCost)]);
        GameState prompt = BeginPaidPrompt(state, catalog);
        LegalAction action = Assert.Single(prompt.PendingDecision!.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);
        GameState stale = ReplaceCounters(
            prompt,
            [CardCounterState.CreateHostedCredit(sourceId, 1, HostedCreditSpendRestriction.AbilityCost)]);
        string before = EngineJson.Serialize(stale);

        ApplyResult result = GameEngine.Apply(
            stale,
            new ActionSubmission(stale.PendingDecision!.Owner, stale.PendingDecision.Id, stale.PendingDecision.Revision, action.Id),
            catalog);

        Assert.False(result.Accepted);
        Assert.Equal(before, EngineJson.Serialize(result.State));
        Assert.Equal(1, Assert.Single(result.State.RulesState!.Counters).Count);
        Assert.DoesNotContain(result.State.ResolutionStack, frame => frame is AbilityFrame);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void HostedCreditCostCheckpointResumesWithoutDoubleSpend()
    {
        CardDefinitionId definitionId = new("test:runtime-hosted-cost-checkpoint");
        CardInstanceId sourceId = new("card:runtime-hosted-cost-checkpoint");
        BehaviorPaidAbility ability = Ability(
            "ability:hosted-cost-checkpoint",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.HostedCredits)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 5,
            clicks: 4,
            counters: [CardCounterState.CreateHostedCredit(sourceId, 1, HostedCreditSpendRestriction.AbilityCost)]);
        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);
        AbilityActionParameters parameters = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(ReplaceStack(state, [parent]), catalog));
        PlayerId runnerId = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        AbilityFrame abilityFrame = new(
            1,
            ResolutionBehaviorIds.Ability,
            parameters.DefinitionId,
            parameters.SourceCardId,
            parameters.AbilityKey,
            runnerId,
            parameters.Timing,
            parameters.Activation,
            parameters.Costs,
            parameters.TargetCardIds,
            parameters.Usage,
            parameters.CatalogFingerprint,
            parameters.DefinitionFingerprint,
            parameters.SourceZoneId,
            parameters.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            BehaviorFrameIdentity.Compute(parent));

        GameState checkpoint = GameEngine.BeginResolution(
            ReplaceStack(state, [parent, abilityFrame]),
            abilityFrame,
            catalog,
            maxResolutionSteps: 1);
        Assert.Equal(AbilityLifecycleStage.CostCheckpoint, Assert.IsType<AbilityFrame>(checkpoint.ResolutionStack[^1]).Stage);
        Assert.Empty(checkpoint.RulesState!.Counters);
        long checkpointSequence = checkpoint.EventSequence;
        string json = EngineJson.Serialize(checkpoint);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));

        AbilityFrame restoredFrame = Assert.IsType<AbilityFrame>(restored.ResolutionStack[^1]);
        GameState resumed = GameEngine.BeginResolution(restored, restoredFrame, catalog);
        Assert.Empty(resumed.RulesState!.Counters);
        // Resuming completes the child and reopens the parent paid window,
        // which emits exactly one fresh timing-window event; the hosted-cost
        // payment itself must not be repeated.
        Assert.Equal(checkpointSequence + 1, resumed.EventSequence);
        Assert.Equal(5, resumed.GetPlayer(sourceOwner: PlayerSide.Runner).Credits);
    }

    [Fact]
    public void CostCheckpointRoundTripsAndResumesTheSameAbilityFrame()
    {
        CardDefinitionId definitionId = new("test:runtime-checkpoint");
        CardInstanceId sourceId = new("card:runtime-checkpoint");
        BehaviorPaidAbility ability = Ability(
            "ability:checkpoint",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);
        AbilityActionParameters parameters = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(
                ReplaceStack(state, [parent]),
                catalog));
        AbilityFrame abilityFrame = new(
            1,
            ResolutionBehaviorIds.Ability,
            parameters.DefinitionId,
            parameters.SourceCardId,
            parameters.AbilityKey,
            state.Players.Single(player => player.Side == PlayerSide.Runner).Id,
            parameters.Timing,
            parameters.Activation,
            parameters.Costs,
            parameters.TargetCardIds,
            parameters.Usage,
            parameters.CatalogFingerprint,
            parameters.DefinitionFingerprint,
            parameters.SourceZoneId,
            parameters.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            BehaviorFrameIdentity.Compute(parent));
        GameState beforeCheckpoint = ReplaceStack(state, [parent, abilityFrame]);

        GameState checkpoint = GameEngine.BeginResolution(
            beforeCheckpoint,
            abilityFrame,
            catalog,
            maxResolutionSteps: 1);
        Assert.Equal(AbilityLifecycleStage.CostCheckpoint, Assert.IsType<AbilityFrame>(checkpoint.ResolutionStack[^1]).Stage);

        string json = EngineJson.Serialize(checkpoint);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        AbilityFrame restoredFrame = Assert.IsType<AbilityFrame>(restored.ResolutionStack[^1]);

        GameState resumed = GameEngine.BeginResolution(
            restored,
            restoredFrame,
            catalog);
        Assert.Equal(4, resumed.GetPlayer(sourceOwner: PlayerSide.Runner).Credits);
        Assert.Single(resumed.ResolutionStack);
        Assert.Equal(TurnFrameStage.BeforeTurnPriority, Assert.IsType<TurnFrame>(resumed.ResolutionStack[^1]).Stage);
        Assert.NotNull(resumed.PendingDecision);
    }

    [Fact]
    public void CounterAndTrashSelfCostsApplyInDeclaredOrder()
    {
        CardDefinitionId definitionId = new("test:runtime-counter-trash");
        CardInstanceId sourceId = new("card:runtime-counter-trash");
        BehaviorPaidAbility ability = Ability(
            "ability:counter-trash",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [
                new BehaviorCost(BehaviorCostKind.Counter, 2, counterName: "charge"),
                new BehaviorCost(BehaviorCostKind.TrashSelf),
            ],
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 3,
            clicks: 4,
            counters: [new CardCounterState(sourceId, "charge", 2)]);
        GameState prompt = BeginPaidPrompt(state, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = Assert.Single(decision.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        CardInstanceState source = result.State.GetCard(sourceId);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(source.ZoneId).Kind);
        Assert.DoesNotContain(result.State.RulesState!.Counters, counter => counter.CardId == sourceId);
        Assert.Equal(4, result.State.GetPlayer(sourceOwner: PlayerSide.Runner).Credits);
    }

    [Fact]
    public void TrashSelfMayPrecedeCounterAndHostedCreditCostsUntilPaymentCompletes()
    {
        CardDefinitionId definitionId = new("test:runtime-trash-first-costs");
        CardInstanceId sourceId = new("card:runtime-trash-first-costs");
        BehaviorPaidAbility ability = Ability(
            "ability:trash-first-costs",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [
                new BehaviorCost(BehaviorCostKind.TrashSelf),
                new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "charge"),
                new BehaviorCost(BehaviorCostKind.HostedCredits, 1),
            ],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 5,
            clicks: 4,
            counters:
            [
                new CardCounterState(sourceId, "charge", 2),
                CardCounterState.CreateHostedCredit(sourceId, 2, HostedCreditSpendRestriction.AbilityCost),
            ]);
        GameState prompt = BeginPaidPrompt(state, catalog);
        LegalAction action = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                action.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(result.State.GetCard(sourceId).ZoneId).Kind);
        Assert.DoesNotContain(result.State.RulesState!.Counters, counter => counter.CardId == sourceId);
        Assert.Equal(
            [EngineEventKinds.CardMoved, EngineEventKinds.CounterAdjusted, EngineEventKinds.HostedCreditPoolChanged],
            result.Events
                .Where(@event => @event.Kind is EngineEventKinds.CardMoved
                    or EngineEventKinds.CounterAdjusted
                    or EngineEventKinds.HostedCreditPoolChanged)
                .Select(@event => @event.Kind));
        HostedCreditPoolChangedEventPayload hosted = Assert.IsType<HostedCreditPoolChangedEventPayload>(
            result.Events.Single(@event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged).Payload);
        Assert.Equal(-1, hosted.Delta);
        Assert.Equal(1, hosted.CountAfter);
        Assert.False(hosted.Emptied);
    }

    [Fact]
    public void RemoveSelfFromGameMayPrecedeCounterAndHostedCreditCostsUntilPaymentCompletes()
    {
        CardDefinitionId definitionId = new("test:runtime-remove-self-paid");
        CardInstanceId sourceId = new("card:runtime-remove-self-paid");
        BehaviorPaidAbility ability = Ability(
            "ability:remove-self-paid",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [
                new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame),
                new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "charge"),
                new BehaviorCost(BehaviorCostKind.HostedCredits, 1),
            ],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(
            catalog,
            definitionId,
            sourceId,
            credits: 5,
            clicks: 4,
            counters:
            [
                new CardCounterState(sourceId, "charge", 2),
                CardCounterState.CreateHostedCredit(sourceId, 2, HostedCreditSpendRestriction.AbilityCost),
            ]);
        GameState prompt = BeginPaidPrompt(state, catalog);
        LegalAction action = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                action.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        CardInstanceState source = result.State.GetCard(sourceId);
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, result.State.GetZone(source.ZoneId).Kind);
        Assert.True(source.FaceUp);
        Assert.Empty(result.State.RulesState!.Counters);
        Assert.Equal(
            [EngineEventKinds.CardMoved, EngineEventKinds.CounterAdjusted, EngineEventKinds.HostedCreditPoolChanged],
            result.Events
                .Where(@event => @event.Kind is EngineEventKinds.CardMoved
                    or EngineEventKinds.CounterAdjusted
                    or EngineEventKinds.HostedCreditPoolChanged)
                .Select(@event => @event.Kind));
        HostedCreditPoolChangedEventPayload hosted = Assert.IsType<HostedCreditPoolChangedEventPayload>(
            result.Events.Single(@event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged).Payload);
        Assert.Equal(-1, hosted.Delta);
        Assert.Equal(1, hosted.CountAfter);
        Assert.Null(hosted.Recipient);
        Assert.False(hosted.Emptied);
    }

    [Fact]
    public void RemoveSelfCostCheckpointRoundTripsWithoutRepeatingTheMove()
    {
        CardDefinitionId definitionId = new("test:runtime-remove-self-checkpoint");
        CardInstanceId sourceId = new("card:runtime-remove-self-checkpoint");
        BehaviorPaidAbility ability = Ability(
            "ability:remove-self-checkpoint",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);
        AbilityActionParameters parameters = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(
                ReplaceStack(state, [parent]),
                catalog));
        AbilityFrame frame = new(
            1,
            ResolutionBehaviorIds.Ability,
            parameters.DefinitionId,
            parameters.SourceCardId,
            parameters.AbilityKey,
            runner,
            parameters.Timing,
            parameters.Activation,
            parameters.Costs,
            parameters.TargetCardIds,
            parameters.Usage,
            parameters.CatalogFingerprint,
            parameters.DefinitionFingerprint,
            parameters.SourceZoneId,
            parameters.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            BehaviorFrameIdentity.Compute(parent));

        GameState checkpoint = GameEngine.BeginResolution(
            ReplaceStack(state, [parent, frame]),
            frame,
            catalog,
            maxResolutionSteps: 1);
        Assert.Equal(
            AbilityLifecycleStage.CostCheckpoint,
            Assert.IsType<AbilityFrame>(checkpoint.ResolutionStack[^1]).Stage);
        Assert.Equal(
            ZoneKind.RunnerRemovedFromGame,
            checkpoint.GetZone(checkpoint.GetCard(sourceId).ZoneId).Kind);
        long checkpointSequence = checkpoint.EventSequence;

        string json = EngineJson.Serialize(checkpoint);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        AbilityFrame restoredFrame = Assert.IsType<AbilityFrame>(restored.ResolutionStack[^1]);
        GameState resumed = GameEngine.BeginResolution(restored, restoredFrame, catalog);

        Assert.Equal(
            ZoneKind.RunnerRemovedFromGame,
            resumed.GetZone(resumed.GetCard(sourceId).ZoneId).Kind);
        Assert.Equal(checkpointSequence + 1, resumed.EventSequence);
    }

    [Fact]
    public void ForgedRemoveSelfFromGameDestinationIsRejectedAfterPayment()
    {
        CardDefinitionId definitionId = new("test:runtime-remove-self-forged-destination");
        CardInstanceId sourceId = new("card:runtime-remove-self-forged-destination");
        BehaviorPaidAbility ability = Ability(
            "ability:remove-self-forged-destination",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);
        AbilityActionParameters parameters = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(
                ReplaceStack(state, [parent]),
                catalog));
        AbilityFrame frame = new(
            1,
            ResolutionBehaviorIds.Ability,
            parameters.DefinitionId,
            parameters.SourceCardId,
            parameters.AbilityKey,
            runner,
            parameters.Timing,
            parameters.Activation,
            parameters.Costs,
            parameters.TargetCardIds,
            parameters.Usage,
            parameters.CatalogFingerprint,
            parameters.DefinitionFingerprint,
            parameters.SourceZoneId,
            parameters.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            BehaviorFrameIdentity.Compute(parent));

        GameState checkpoint = GameEngine.BeginResolution(
            ReplaceStack(state, [parent, frame]),
            frame,
            catalog,
            maxResolutionSteps: 1);
        AbilityFrame paidFrame = Assert.IsType<AbilityFrame>(checkpoint.ResolutionStack[^1]);
        GameState forged = MoveCardFixture(checkpoint, sourceId, ZoneKind.RunnerHeap);

        Assert.Contains(
            StateInvariantValidator.Collect(forged, catalog),
            violation => violation.Code == "resolution.ability-source-location-mismatch");
        Assert.Throws<StateInvariantException>(() => GameEngine.BeginResolution(
            forged,
            paidFrame,
            catalog));
    }

    [Fact]
    public void StaleInsufficientCreditsAreRejectedAtomically()
    {
        CardDefinitionId definitionId = new("test:runtime-stale");
        CardInstanceId sourceId = new("card:runtime-stale");
        BehaviorPaidAbility ability = Ability(
            "ability:stale",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits, 3)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        GameState prompt = BeginPaidPrompt(state, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = Assert.Single(decision.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);
        PlayerId runnerId = prompt.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerState[] insufficientPlayers = prompt.Players
            .Select(player => player.Id == runnerId
                ? new PlayerState(player.Id, player.Side, credits: 2, player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();
        GameState stale = ReplacePlayers(prompt, insufficientPlayers);
        string before = EngineJson.Serialize(stale);

        ApplyResult result = GameEngine.Apply(
            stale,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);

        Assert.False(result.Accepted);
        Assert.Equal(before, EngineJson.Serialize(result.State));
        Assert.Equal(2, result.State.GetPlayer(sourceOwner: PlayerSide.Runner).Credits);
        Assert.DoesNotContain(result.State.ResolutionStack, frame => frame is AbilityFrame);
    }

    [Fact]
    public void LoseClickPaidAbilityCanBeUsedAgainWhileClicksRemain()
    {
        CardDefinitionId definitionId = new("test:runtime-lose-click");
        CardInstanceId sourceId = new("card:runtime-lose-click");
        BehaviorPaidAbility ability = Ability(
            "ability:lose-click",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.LoseClick)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        GameState firstPrompt = BeginPaidPrompt(state, catalog);
        GameState afterFirst = ApplyAbility(firstPrompt, catalog);
        Assert.Equal(3, afterFirst.GetPlayer(sourceOwner: PlayerSide.Runner).Clicks);
        Assert.Contains(afterFirst.PendingDecision!.LegalActions, candidate => candidate.Kind == ActionKind.UseAbility);

        GameState afterSecond = ApplyAbility(afterFirst, catalog);
        Assert.True(afterSecond.PendingDecision is not null);
        Assert.Equal(2, afterSecond.GetPlayer(sourceOwner: PlayerSide.Runner).Clicks);
    }

    [Fact]
    public void RunPaidAbilityResumesSamePriorityHolderAndResetsPasses()
    {
        CardDefinitionId definitionId = new("test:runtime-run");
        CardInstanceId sourceId = new("card:runtime-run");
        BehaviorPaidAbility ability = Ability(
            "ability:run",
            BehaviorAbilityTiming.MovementPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerRunState(catalog, definitionId, sourceId, consecutivePasses: 1);
        AbilityActionParameters expected = Assert.Single(GameEngine.MaterializeAbilityActionParameters(state, catalog));
        GameState prompt = WithPendingRunAction(state, expected);
        GameState result = ApplyAbility(prompt, catalog);

        Assert.True(result.PendingDecision is not null);
        Assert.IsType<RunTimingFrame>(result.ResolutionStack.Single());
        Assert.Equal(PlayerSide.Runner, result.RunState!.Priority!.PriorityHolder);
        Assert.Equal(0, result.RunState!.Priority!.ConsecutivePasses);
        Assert.Equal(RunTimingStep.MovementPaidWindow, result.RunState.Step);
    }

    [Fact]
    public void AbilityChoiceChildRoundTripsWithExactParentTopology()
    {
        CardDefinitionId definitionId = new("test:runtime-choice");
        CardInstanceId sourceId = new("card:runtime-choice");
        BehaviorChoiceSpec choice = new(
            "choice:runtime",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption("option:no-op", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("option:gain", [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]),
            ]);
        BehaviorPaidAbility ability = Ability(
            "ability:choice",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        GameState pending = BeginPaidPrompt(state, catalog);
        GameState childPrompt = ApplyAbility(pending, catalog);
        Assert.IsType<AbilityFrame>(childPrompt.ResolutionStack[^2]);
        BehaviorResolutionFrame child = Assert.IsType<BehaviorResolutionFrame>(childPrompt.ResolutionStack[^1]);
        Assert.Equal(ability.Key, child.AbilityKey);
        Assert.Equal(AbilityLifecycleStage.AwaitingChild, ((AbilityFrame)childPrompt.ResolutionStack[^2]).Stage);
        Assert.NotNull(childPrompt.PendingDecision);

        string json = EngineJson.Serialize(childPrompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        PendingDecision decision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction selected = Assert.Single(
            decision.LegalActions,
            candidate => candidate.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["option:no-op"]));
        ApplyResult result = GameEngine.Apply(
            restored,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, selected.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(TurnFrameStage.BeforeTurnPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack.Single()).Stage);
    }

    [Fact]
    public void ForgedAbilityCatalogOrParentIsRejectedByStateInvariant()
    {
        CardDefinitionId definitionId = new("test:runtime-forged");
        CardInstanceId sourceId = new("card:runtime-forged");
        BehaviorPaidAbility ability = Ability(
            "ability:forged",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        BehaviorCatalog catalog = Catalog(definitionId, PlayerSide.Runner, [ability]);
        GameState state = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);
        AbilityActionParameters parameters = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(ReplaceStack(state, [parent]), catalog));
        PlayerId runnerId = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        AbilityFrame forged = new(
            1,
            ResolutionBehaviorIds.Ability,
            parameters.DefinitionId,
            parameters.SourceCardId,
            parameters.AbilityKey,
            runnerId,
            parameters.Timing,
            parameters.Activation,
            parameters.Costs,
            parameters.TargetCardIds,
            parameters.Usage,
            parameters.CatalogFingerprint,
            parameters.DefinitionFingerprint,
            parameters.SourceZoneId,
            parameters.SourceZoneKind,
            AbilityLifecycleStage.PayCosts,
            "parent:forged");
        GameState invalid = ReplaceStack(state, [parent, forged]);

        Assert.Contains(
            StateInvariantValidator.Collect(invalid, catalog),
            violation => violation.Code == "resolution.ability-parent-context-mismatch");
        BehaviorCatalog wrongCatalog = new BehaviorCatalogBuilder("test:runtime-wrong-catalog").Build();
        Assert.Contains(
            StateInvariantValidator.Collect(invalid, wrongCatalog),
            violation => violation.Code == "resolution.ability-catalog-mismatch");
    }

    private static BehaviorPaidAbility Ability(
        string key,
        BehaviorAbilityTiming timing,
        BehaviorAbilityActivation activation,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorAbilityUsageLimit usageLimit = BehaviorAbilityUsageLimit.Unlimited) =>
        new(
            key,
            timing,
            activation,
            costs,
            conditions: [],
            targets: [],
            instructions,
            usageLimit);

    private static GameState BeginPaidPrompt(GameState state, BehaviorCatalog catalog)
    {
        TurnFrame frame = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Runner,
            state.Turn.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnPriorityWindowKind.PaidAndRez,
            PlayerSide.Runner);
        return GameEngine.BeginResolution(state, frame, catalog);
    }

    private static GameState ApplyAbility(GameState prompt, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction action = Assert.Single(
            decision.LegalActions,
            candidate => candidate.Kind == ActionKind.UseAbility);
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static BehaviorCatalog Catalog(
        CardDefinitionId definitionId,
        PlayerSide side,
        IReadOnlyList<BehaviorPaidAbility> abilities) =>
        new BehaviorCatalogBuilder("test:runtime-paid-ability")
            .Add(new CardBehaviorDefinition(
                definitionId,
                side,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                paidAbilities: abilities))
            .Build();

    private static GameState BuildRunnerState(
        BehaviorCatalog catalog,
        CardDefinitionId definitionId,
        CardInstanceId sourceId,
        int credits,
        int clicks,
        IReadOnlyList<CardCounterState>? counters = null)
    {
        GameState baseline = GameState.CreateNew(
            90210,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players =
        [
            new PlayerState(corp.Id, corp.Side, corp.Credits, corp.Clicks, corp.AgendaPoints),
            new PlayerState(runner.Id, runner.Side, credits, clicks, runner.AgendaPoints),
        ];
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState source = new(sourceId, definitionId, rig.Id, PlayerSide.Runner, faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [sourceId])
                : zone)
            .ToArray();
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnHistoryState history = new(1, null, null, facts, null);
        TurnState turn = new(PlayerSide.Runner, TurnPhase.Runner, 1, TurnStep.RunnerAction, history);
        RulesState defaults = RulesState.CreateDefault(players);
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counters ?? [],
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [source],
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            resolutionStack: [],
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
    }

    private static GameState BuildRunnerRunState(
        BehaviorCatalog catalog,
        CardDefinitionId definitionId,
        CardInstanceId sourceId,
        int consecutivePasses)
    {
        GameState baseline = BuildRunnerState(catalog, definitionId, sourceId, credits: 5, clicks: 4);
        PlayerId runnerId = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        ServerState server = baseline.Servers.Single(value => value.Kind == ServerKind.Headquarters);
        RunId runId = new("run:runtime-paid");
        RunTimingStep step = RunTimingStep.MovementPaidWindow;
        RunPriorityState priority = new(
            1,
            RunPriorityWindowKind.MovementPaid,
            PlayerSide.Runner,
            consecutivePasses,
            corpMayRezApproachedIce: false,
            corpMayRezNonIce: false);
        RunState run = new(
            1,
            runId,
            server.Id,
            PlayerSide.Runner,
            runnerId,
            initiatingSource: null,
            RunCrPhase.Movement,
            step,
            currentIceIndex: -1,
            currentIceId: null,
            successful: false,
            unsuccessful: false,
            endReason: null,
            priority,
            encounter: null,
            breachCandidates: [],
            randomAccessLimit: 0,
            chosenAccessCards: [],
            accessedCards: [],
            accessState: RunAccessState.NotStarted,
            runCredits: 0,
            activeRunEventId: null,
            lingeringEffects: []);
        return ReplaceState(
            baseline,
            resolutionStack: [new RunTimingFrame(runId, step)],
            runState: run);
    }

    private static GameState WithPendingRunAction(
        GameState state,
        AbilityActionParameters expected)
    {
        PlayerId runnerId = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PendingDecision pending = new(
            new DecisionId("decision:runtime-run"),
            1,
            runnerId,
            DecisionKind.RunTiming,
            "Run paid window",
            [
                new LegalAction(
                    new ActionId("action:runtime-run-pass"),
                    ActionKind.PassPriority,
                    "Pass",
                    new NoActionParameters()),
                new LegalAction(
                    new ActionId("action:runtime-run-ability"),
                    ActionKind.UseAbility,
                    "Use ability",
                    expected),
            ]);
        return ReplaceState(state, pending: pending, decisionRevision: 1);
    }

    private static GameState ReplacePlayers(
        GameState state,
        IReadOnlyList<PlayerState> players) =>
        ReplaceState(state, players: players);

    private static GameState ReplaceCounters(
        GameState state,
        IReadOnlyList<CardCounterState> counters)
    {
        RulesState defaults = state.RulesState
            ?? throw new InvalidOperationException("The test state must have rules state.");
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        return ReplaceState(state, rulesState: rules);
    }

    private static GameState ReplaceStack(GameState state, IReadOnlyList<ResolutionFrame> stack) =>
        ReplaceState(state, resolutionStack: stack);

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
        return ReplaceState(state, cards: cards, zones: zones);
    }

    private static GameState ReplaceState(
        GameState state,
        IReadOnlyList<PlayerState>? players = null,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pending = null,
        long? decisionRevision = null,
        RunState? runState = null,
        RulesState? rulesState = null) =>
        new(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players ?? state.Players,
            cards ?? state.Cards,
            zones ?? state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            decisionRevision ?? state.DecisionRevision,
            state.EventSequence,
            resolutionStack ?? state.ResolutionStack,
            pending ?? state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            runState ?? state.RunState,
            rulesState ?? state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);
}

internal static class PaidAbilityRuntimeTestExtensions
{
    internal static PlayerState GetPlayer(this GameState state, PlayerSide sourceOwner) =>
        state.Players.Single(player => player.Side == sourceOwner);
}
