using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused C2a-2 coverage for catalog trigger materialization.</summary>
public sealed class CatalogTriggerRuntimeTests
{
    [Fact]
    public void MandatoryCatalogTriggerRunsThroughTheExistingCheckpoint()
    {
        CardDefinitionId definitionId = new("test:c2a-mandatory");
        CardInstanceId sourceId = new("card:c2a-mandatory");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("mandatory", TriggerMode.Mandatory, BehaviorTriggerScope.Self, null, 2),
                ]));
        GameState state = CreateState(catalog, (sourceId, definitionId, PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState result = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            catalog);

        Assert.Equal(7, result.GetPlayer(runner).Credits);
        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
    }

    [Fact]
    public void OptionalCatalogTriggerCanBeDeclinedOrUsed()
    {
        CardDefinitionId definitionId = new("test:c2a-optional");
        CardInstanceId sourceId = new("card:c2a-optional");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("optional", TriggerMode.Optional, BehaviorTriggerScope.Self, null, 2),
                ]));
        PlayerId runner;
        GameState state = CreateState(catalog, (sourceId, definitionId, PlayerSide.Runner, true));
        runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameplayOccurrenceEffect occurrence = new(OccurrenceKind.CardInstalled, runner, sourceId);

        GameState prompt = GameEngine.BeginGameplayOccurrenceForTesting(state, occurrence, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction decline = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        ApplyResult declined = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decline.Id),
            catalog);
        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Equal(5, declined.State.GetPlayer(runner).Credits);

        prompt = GameEngine.BeginGameplayOccurrenceForTesting(state, occurrence, catalog);
        decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction use = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        ApplyResult used = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, use.Id),
            catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(7, used.State.GetPlayer(runner).Credits);
    }

    [Fact]
    public void ScopeAndSubjectFilterSelectOnlyMatchingActiveSources()
    {
        CardDefinitionId sourceDefinition = new("test:c2a-source");
        CardDefinitionId subjectDefinition = new("test:c2a-subject");
        CardInstanceId sourceId = new("card:c2a-source");
        BehaviorCardFilter filter = new(ownerSide: PlayerSide.Runner);
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("self", TriggerMode.Mandatory, BehaviorTriggerScope.Self, filter, 1),
                    Trigger("controller", TriggerMode.Mandatory, BehaviorTriggerScope.Controller, filter, 1),
                    Trigger("any", TriggerMode.Mandatory, BehaviorTriggerScope.Any, filter, 1),
                ]),
            new CardBehaviorDefinition(new CardDefinitionId("test:c2a-subject"), PlayerSide.Runner));
        GameState state = CreateState(
            catalog,
            (sourceId, sourceDefinition, PlayerSide.Runner, true),
            (new CardInstanceId("card:c2a-subject"), subjectDefinition, PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        CardInstanceState sourceCard = state.Cards.Single(card => card.Id == sourceId);
        Assert.True(RuntimeCardActivity.IsActive(sourceCard, state.Zones));
        Assert.True(catalog.TryGet(sourceDefinition, out CardBehaviorDefinition? sourceContract));
        Assert.Equal(3, sourceContract.Triggers.Count);
        Assert.All(sourceContract.Triggers, trigger => Assert.NotNull(trigger.SubjectCardFilter));

        GameState checkpoint = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            catalog,
            maxResolutionSteps: 4);
        Assert.NotNull(checkpoint.PendingDecision);
        Assert.Equal(DecisionKind.TriggerOrdering, checkpoint.PendingDecision!.Kind);
        Assert.Equal(3, ((EffectCheckpointFrame)checkpoint.ResolutionStack[^1]).TriggerQueue.Count);

        GameState result = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            catalog);
        result = ResolveAllCheckpointTriggers(result, catalog);

        Assert.Equal(8, result.GetPlayer(runner).Credits);

        BehaviorCatalog missCatalog = Catalog(
            new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger(
                        "miss",
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self,
                        new BehaviorCardFilter(definitionId: new CardDefinitionId("test:not-subject")),
                        3),
                ]));
        GameState missState = CreateState(missCatalog, (sourceId, sourceDefinition, PlayerSide.Runner, true));
        runner = missState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState miss = GameEngine.BeginGameplayOccurrenceForTesting(
            missState,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            missCatalog);
        Assert.Equal(5, miss.GetPlayer(runner).Credits);
    }

    [Fact]
    public void YieldedCatalogTriggerSubjectBindingRoundTripsAndRejectsForgedProvenance()
    {
        CardDefinitionId definitionId = new("test:c2a-yielded");
        CardInstanceId sourceId = new("card:c2a-yielded-source");
        CardInstanceId subjectId = new("card:c2a-yielded-subject");
        BehaviorChoiceSpec choice = new(
            "trigger-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption("gain", [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 2)]),
                new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "yielded",
                        OccurrenceKind.CardInstalled,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any,
                        new BehaviorCardFilter(ownerSide: PlayerSide.Runner),
                        [
                            new BehaviorInstruction(
                                BehaviorInstructionKind.TrashCard,
                                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard, selector: "trigger-subject")),
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                        ]),
                ]),
            new CardBehaviorDefinition(new CardDefinitionId("test:c2a-yielded-subject"), PlayerSide.Runner));
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, PlayerSide.Runner, true),
            (subjectId, new CardDefinitionId("test:c2a-yielded-subject"), PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, subjectId),
            catalog);
        BehaviorResolutionFrame child = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack[^1]);
        Assert.Equal("yielded", child.TriggerKey);
        Assert.Equal([subjectId], child.Bindings.Single(binding => binding.Key == "trigger-subject").CardIds);
        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        PendingDecision decision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction gain = Assert.Single(
            decision.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["gain"]));
        ApplyResult completed = GameEngine.Apply(
            restored,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, gain.Id),
            catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(7, completed.State.GetPlayer(runner).Credits);
        Assert.Contains(subjectId, completed.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(completed.State.ResolutionStack);

        BehaviorResolutionFrame forgedChild = child.With(
            bindings: [new BehaviorBinding("trigger-subject", [new CardInstanceId("card:c2a-forged-subject")])]);
        GameState forged = ReplaceResolutionStack(prompt, [prompt.ResolutionStack[^2], forgedChild]);
        PendingDecision forgedDecision = Assert.IsType<PendingDecision>(forged.PendingDecision);
        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(
                forgedDecision.Owner,
                forgedDecision.Id,
                forgedDecision.Revision,
                forgedDecision.LegalActions[0].Id),
            catalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(forged), DeterministicHash.ForState(rejected.State));

        BehaviorResolutionFrame forgedParent = CloneCatalogChild(
            child,
            parentFrameIdentity: "parent:forged");
        GameState forgedParentState = ReplaceResolutionStack(
            prompt,
            [prompt.ResolutionStack[^2], forgedParent]);
        PendingDecision forgedParentDecision = Assert.IsType<PendingDecision>(forgedParentState.PendingDecision);
        ApplyResult parentRejected = GameEngine.Apply(
            forgedParentState,
            new ActionSubmission(
                forgedParentDecision.Owner,
                forgedParentDecision.Id,
                forgedParentDecision.Revision,
                forgedParentDecision.LegalActions[0].Id),
            catalog);
        Assert.False(parentRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, parentRejected.Rejection?.Code);

        BehaviorResolutionFrame forgedCatalog = CloneCatalogChild(
            child,
            catalogFingerprint: new string('1', 64));
        GameState forgedCatalogState = ReplaceResolutionStack(
            prompt,
            [prompt.ResolutionStack[^2], forgedCatalog]);
        PendingDecision forgedCatalogDecision = Assert.IsType<PendingDecision>(forgedCatalogState.PendingDecision);
        ApplyResult catalogRejected = GameEngine.Apply(
            forgedCatalogState,
            new ActionSubmission(
                forgedCatalogDecision.Owner,
                forgedCatalogDecision.Id,
                forgedCatalogDecision.Revision,
                forgedCatalogDecision.LegalActions[0].Id),
            catalog);
        Assert.False(catalogRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, catalogRejected.Rejection?.Code);
    }

    [Fact]
    public void InactiveSourceIsIgnoredAndMaterializedQueueIsDeterministicAcrossSaveLoad()
    {
        CardDefinitionId definitionId = new("test:c2a-order");
        BehaviorTrigger trigger = Trigger("optional", TriggerMode.Optional, BehaviorTriggerScope.Any, null, 1);
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(definitionId, PlayerSide.Runner, triggers: [trigger]));
        CardInstanceId inactiveId = new("card:c2a-inactive");
        GameState inactiveState = CreateState(catalog, (inactiveId, definitionId, PlayerSide.Runner, false));
        PlayerId runner = inactiveState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState inactive = GameEngine.BeginGameplayOccurrenceForTesting(
            inactiveState,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, inactiveId),
            catalog);
        Assert.Equal(5, inactive.GetPlayer(runner).Credits);
        Assert.Null(inactive.PendingDecision);

        GameState orderedState = CreateState(
            catalog,
            (new CardInstanceId("card:c2a-z"), definitionId, PlayerSide.Runner, true),
            (new CardInstanceId("card:c2a-a"), definitionId, PlayerSide.Runner, true));
        runner = orderedState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState ordered = GameEngine.BeginGameplayOccurrenceForTesting(
            orderedState,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, new CardInstanceId("card:c2a-a")),
            catalog);
        Assert.Equal(DecisionKind.TriggerOrdering, Assert.IsType<PendingDecision>(ordered.PendingDecision).Kind);
        string json = EngineJson.Serialize(ordered);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(ordered), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void MaterializedCatalogTriggerUsesDetachedSourceSnapshotAfterMoveAndJsonRoundTrip()
    {
        CardDefinitionId definitionId = new("test:c2a-detached-source");
        CardInstanceId sourceId = new("card:c2a-detached-source");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("optional", TriggerMode.Optional, BehaviorTriggerScope.Self, null, 2),
                ]));
        GameState state = CreateState(catalog, (sourceId, definitionId, PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameplayOccurrenceEffect occurrence = new(OccurrenceKind.CardInstalled, runner, sourceId);

        GameState materialized = GameEngine.BeginGameplayOccurrenceForTesting(state, occurrence, catalog);
        Assert.Equal(DecisionKind.TriggerChoice, Assert.IsType<PendingDecision>(materialized.PendingDecision).Kind);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(materialized.ResolutionStack[^1]);
        TriggerInvocation invocation = Assert.Single(checkpoint.TriggerQueue);
        Assert.Equal(ZoneKind.RunnerRig, invocation.SourceZoneKind);
        Assert.Equal(PlayerSide.Runner, invocation.SourceOwnerSide);

        GameState moved = MoveCard(materialized, sourceId, ZoneKind.RunnerHeap);
        ApplyResult direct = ApplyCurrentTriggerChoice(moved, catalog);
        Assert.True(direct.Accepted, direct.Rejection?.Message);
        Assert.Equal(7, direct.State.GetPlayer(runner).Credits);
        Assert.Contains(sourceId, direct.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(direct.State.ResolutionStack);

        GameState movedForLoad = MoveCard(materialized, sourceId, ZoneKind.RunnerHeap);
        string json = EngineJson.Serialize(movedForLoad);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        ApplyResult afterLoad = ApplyCurrentTriggerChoice(restored, catalog);
        Assert.True(afterLoad.Accepted, afterLoad.Rejection?.Message);
        Assert.Equal(7, afterLoad.State.GetPlayer(runner).Credits);
        Assert.Contains(sourceId, afterLoad.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(afterLoad.State.ResolutionStack);
    }

    [Fact]
    public void DetachedCatalogTriggerAuthorizationSurvivesCursorProgressAndJsonResume()
    {
        CardDefinitionId definitionId = new("test:c2a-detached-yield");
        CardInstanceId sourceId = new("card:c2a-detached-yield");
        BehaviorChoiceSpec choice = new(
            "resume-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption("continue", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "yield",
                        OccurrenceKind.CardInstalled,
                        TriggerMode.Optional,
                        BehaviorTriggerScope.Self,
                        null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 1),
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                        ]),
                ]));
        GameState state = CreateState(catalog, (sourceId, definitionId, PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState materialized = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            catalog);
        GameState moved = MoveCard(materialized, sourceId, ZoneKind.RunnerHeap);
        ApplyResult started = ApplyCurrentTriggerChoice(moved, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        BehaviorResolutionFrame child = Assert.IsType<BehaviorResolutionFrame>(started.State.ResolutionStack[^1]);
        Assert.True(child.CatalogTriggerSourceMovedBeforeStart);
        Assert.Equal(1, child.InstructionCursor);
        Assert.Contains(sourceId, started.State.GetZone(ZoneKind.RunnerHeap).CardIds);

        string json = EngineJson.Serialize(started.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        BehaviorResolutionFrame restoredChild = Assert.IsType<BehaviorResolutionFrame>(restored.ResolutionStack[^1]);
        Assert.True(restoredChild.CatalogTriggerSourceMovedBeforeStart);
        Assert.Equal(json, EngineJson.Serialize(restored));

        ApplyResult completed = ApplyCurrentBehaviorChoice(restored, catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(6, completed.State.GetPlayer(runner).Credits);
        Assert.Contains(sourceId, completed.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(completed.State.ResolutionStack);
    }

    [Fact]
    public void MaterializedCatalogTriggerRejectsChangedDefinitionAndOwner()
    {
        CardDefinitionId definitionId = new("test:c2a-detached-provenance");
        CardDefinitionId changedDefinitionId = new("test:c2a-detached-changed");
        CardInstanceId sourceId = new("card:c2a-detached-provenance");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("optional", TriggerMode.Optional, BehaviorTriggerScope.Self, null, 2),
                ]),
            new CardBehaviorDefinition(changedDefinitionId, PlayerSide.Runner));
        GameState state = CreateState(catalog, (sourceId, definitionId, PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameplayOccurrenceEffect occurrence = new(OccurrenceKind.CardInstalled, runner, sourceId);
        GameState materialized = GameEngine.BeginGameplayOccurrenceForTesting(state, occurrence, catalog);

        GameState changedDefinition = ReplaceCard(
            materialized,
            sourceId,
            changedDefinitionId,
            PlayerSide.Runner);
        ApplyResult definitionRejected = ApplyCurrentTriggerChoice(changedDefinition, catalog);
        Assert.False(definitionRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, definitionRejected.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(changedDefinition), DeterministicHash.ForState(definitionRejected.State));

        GameState changedOwner = ReplaceCard(
            materialized,
            sourceId,
            definitionId,
            PlayerSide.Corp);
        ApplyResult ownerRejected = ApplyCurrentTriggerChoice(changedOwner, catalog);
        Assert.False(ownerRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, ownerRejected.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(changedOwner), DeterministicHash.ForState(ownerRejected.State));
    }

    [Fact]
    public void SameControllerCatalogTriggerOrderingIsStableAcrossJsonLoad()
    {
        CardDefinitionId definitionId = new("test:c2a-same-controller-order");
        CardInstanceId zuluId = new("card:c2a-order-zulu");
        CardInstanceId alphaId = new("card:c2a-order-alpha");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("optional", TriggerMode.Optional, BehaviorTriggerScope.Any, null, 1),
                ]));
        GameState state = CreateState(
            catalog,
            (zuluId, definitionId, PlayerSide.Runner, true),
            (alphaId, definitionId, PlayerSide.Runner, true));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, alphaId),
            catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerOrdering, decision.Kind);
        TriggerId[] expectedActions = decision.LegalActions
            .Where(action => action.Kind == ActionKind.OrderTriggers)
            .Select(action => Assert.IsType<TriggerOrderParameters>(action.Parameters).TriggerId)
            .ToArray();
        Assert.Equal(2, expectedActions.Length);

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        PendingDecision restoredDecision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        TriggerId[] restoredActions = restoredDecision.LegalActions
            .Where(action => action.Kind == ActionKind.OrderTriggers)
            .Select(action => Assert.IsType<TriggerOrderParameters>(action.Parameters).TriggerId)
            .ToArray();
        Assert.Equal(expectedActions, restoredActions);

        LegalAction originalChoice = decision.LegalActions
            .First(action => action.Kind == ActionKind.OrderTriggers);
        LegalAction restoredChoice = restoredDecision.LegalActions
            .Single(action => action.Kind == ActionKind.OrderTriggers
                && Assert.IsType<TriggerOrderParameters>(action.Parameters).TriggerId
                    == Assert.IsType<TriggerOrderParameters>(originalChoice.Parameters).TriggerId);
        ApplyResult originalOrdered = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, originalChoice.Id),
            catalog);
        ApplyResult restoredOrdered = GameEngine.Apply(
            restored,
            new ActionSubmission(restoredDecision.Owner, restoredDecision.Id, restoredDecision.Revision, restoredChoice.Id),
            catalog);
        Assert.True(originalOrdered.Accepted, originalOrdered.Rejection?.Message);
        Assert.True(restoredOrdered.Accepted, restoredOrdered.Rejection?.Message);
        Assert.Equal(
            DeterministicHash.ForState(originalOrdered.State),
            DeterministicHash.ForState(restoredOrdered.State));
    }

    [Fact]
    public void ActivePlayerControllerPrecedesInactiveControllerForCatalogTriggers()
    {
        CardDefinitionId corpDefinitionId = new("test:c2a-active-corp");
        CardDefinitionId runnerDefinitionId = new("test:c2a-active-runner");
        CardInstanceId corpSourceId = new("card:c2a-active-corp");
        CardInstanceId runnerSourceId = new("card:c2a-active-runner");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                corpDefinitionId,
                PlayerSide.Corp,
                triggers:
                [
                    Trigger("corp", TriggerMode.Optional, BehaviorTriggerScope.Any, null, 1),
                ]),
            new CardBehaviorDefinition(
                runnerDefinitionId,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger("runner", TriggerMode.Optional, BehaviorTriggerScope.Any, null, 1),
                ]));
        GameState state = CreateControllerState(
            catalog,
            corpSourceId,
            corpDefinitionId,
            runnerSourceId,
            runnerDefinitionId);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, runnerSourceId),
            catalog,
            maxResolutionSteps: 32);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(prompt.ResolutionStack[^1]);
        Assert.Equal(2, checkpoint.TriggerQueue.Count);
        Assert.Equal(PlayerSide.Corp, state.GetPlayer(checkpoint.TriggerQueue[0].Controller).Side);
        Assert.Equal(PlayerSide.Runner, state.GetPlayer(checkpoint.TriggerQueue[1].Controller).Side);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, decision.Kind);
        Assert.Equal(PlayerSide.Corp, state.GetPlayer(decision.Owner).Side);
        TriggerChoiceParameters choice = Assert.IsType<TriggerChoiceParameters>(
            Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseTrigger).Parameters);
        Assert.Equal(checkpoint.TriggerQueue[0].Id, choice.TriggerId);
    }

    [Fact]
    public void TurnBeginControllerTriggerRunsOnlyForTheActiveControllerSource()
    {
        CardDefinitionId corpDefinitionId = new("test:c2b-turn-controller-corp");
        CardDefinitionId runnerDefinitionId = new("test:c2b-turn-controller-runner");
        CardInstanceId corpSourceId = new("card:c2b-turn-controller-corp");
        CardInstanceId runnerSourceId = new("card:c2b-turn-controller-runner");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                corpDefinitionId,
                PlayerSide.Corp,
                triggers:
                [
                    TurnTrigger("corp", TriggerMode.Mandatory, BehaviorTriggerScope.Controller, 4),
                ]),
            new CardBehaviorDefinition(
                runnerDefinitionId,
                PlayerSide.Runner,
                triggers:
                [
                    TurnTrigger("runner", TriggerMode.Mandatory, BehaviorTriggerScope.Controller, 2),
                ]));
        GameState state = SetActiveSide(
            CreateControllerState(
                catalog,
                corpSourceId,
                corpDefinitionId,
                runnerSourceId,
                runnerDefinitionId),
            PlayerSide.Runner);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        state = BeginFormalTurn(state, catalog);

        Assert.Equal(5, state.GetPlayer(corp).Credits);
        Assert.Equal(7, state.GetPlayer(runner).Credits);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision?.Kind);
        Assert.True(state.Turn.ThisTurnFacts!.TurnBegan);
    }

    [Fact]
    public void TurnBeginAnyTriggersUseActivePlayerOrderingAcrossJsonLoad()
    {
        CardDefinitionId corpDefinitionId = new("test:c2b-turn-any-corp");
        CardDefinitionId runnerDefinitionId = new("test:c2b-turn-any-runner");
        CardInstanceId corpSourceId = new("card:c2b-turn-any-corp");
        CardInstanceId runnerSourceId = new("card:c2b-turn-any-runner");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                corpDefinitionId,
                PlayerSide.Corp,
                triggers:
                [
                    TurnTrigger("corp", TriggerMode.Optional, BehaviorTriggerScope.Any, 1),
                ]),
            new CardBehaviorDefinition(
                runnerDefinitionId,
                PlayerSide.Runner,
                triggers:
                [
                    TurnTrigger("runner", TriggerMode.Optional, BehaviorTriggerScope.Any, 1),
                ]));
        GameState state = SetActiveSide(
            CreateControllerState(
                catalog,
                corpSourceId,
                corpDefinitionId,
                runnerSourceId,
                runnerDefinitionId),
            PlayerSide.Runner);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = BeginFormalTurn(state, catalog, stopAtCheckpoint: true);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, decision.Kind);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(prompt.ResolutionStack[^1]);
        Assert.Equal([runnerSourceId, corpSourceId], checkpoint.TriggerQueue.Select(invocation => invocation.SourceCardId));
        Assert.Equal(runner, decision.Owner);

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        ApplyResult used = ApplyCurrentTriggerChoice(restored, catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        PendingDecision inactiveControllerChoice = Assert.IsType<PendingDecision>(used.State.PendingDecision);
        Assert.Equal(corp, inactiveControllerChoice.Owner);
        ApplyResult declined = ApplyCurrentTriggerDecline(used.State, catalog);
        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Equal(6, declined.State.GetPlayer(runner).Credits);
        Assert.Equal(5, declined.State.GetPlayer(corp).Credits);
        Assert.Equal(DecisionKind.TurnTiming, declined.State.PendingDecision?.Kind);
    }

    [Fact]
    public void TurnBeginIgnoresHiddenSourcesAndDoesNotFireTwiceAfterJsonResume()
    {
        CardDefinitionId definitionId = new("test:c2b-turn-hidden");
        CardInstanceId activeSourceId = new("card:c2b-turn-hidden-active");
        CardInstanceId hiddenSourceId = new("card:c2b-turn-hidden-hidden");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    TurnTrigger("optional", TriggerMode.Optional, BehaviorTriggerScope.Any, 3),
                ]));
        GameState state = SetActiveSide(
            CreateState(
                catalog,
                (activeSourceId, definitionId, PlayerSide.Runner, true),
                (hiddenSourceId, definitionId, PlayerSide.Runner, false)),
            PlayerSide.Runner);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = BeginFormalTurn(state, catalog, stopAtCheckpoint: true);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(prompt.ResolutionStack[^1]);
        Assert.Single(checkpoint.TriggerQueue);
        Assert.Equal(activeSourceId, checkpoint.TriggerQueue[0].SourceCardId);
        Assert.Equal(DecisionKind.TriggerChoice, prompt.PendingDecision?.Kind);

        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(prompt), catalog);
        ApplyResult used = ApplyCurrentTriggerChoice(restored, catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(8, used.State.GetPlayer(runner).Credits);
        Assert.Equal(DecisionKind.TurnTiming, used.State.PendingDecision?.Kind);
        Assert.DoesNotContain(
            used.State.ResolutionStack.OfType<EffectCheckpointFrame>(),
            frame => frame.TriggerQueue.Any(invocation => invocation.SourceCardId == activeSourceId));
    }

    [Fact]
    public void CatalogTriggerTrashSelfThenGainRunsOnceAndClearsTypedCounters()
    {
        CardDefinitionId definitionId = new("test:c2a-trash-self");
        CardInstanceId sourceId = new("card:c2a-trash-self");
        BehaviorCatalog catalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "trash-self",
                        OccurrenceKind.CardInstalled,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self,
                        null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.TrashSelf),
                            new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 2),
                        ]),
                ]));
        GameState state = AddCounter(
            CreateState(catalog, (sourceId, definitionId, PlayerSide.Runner, true)),
            sourceId,
            new CardCounterState(sourceId, "virus", 2));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState result = GameEngine.BeginGameplayOccurrenceForTesting(
            state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            catalog);

        Assert.Equal(7, result.GetPlayer(runner).Credits);
        Assert.Contains(sourceId, result.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.DoesNotContain(result.RulesState!.Counters, counter => counter.CardId == sourceId);
        Assert.Empty(result.ResolutionStack);
        Assert.Null(result.PendingDecision);
    }

    [Fact]
    public void CatalogTriggerRejectsForgedMovementBeforeAndAfterTrashSelf()
    {
        CardDefinitionId definitionId = new("test:c2a-trash-self-forged");
        CardInstanceId sourceId = new("card:c2a-trash-self-forged");
        BehaviorChoiceSpec pause = new(
            "pause",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options:
            [
                new BehaviorChoiceOption("continue", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
                new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog beforeTrashCatalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "before-trash",
                        OccurrenceKind.CardInstalled,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self,
                        null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.NoOp),
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: pause),
                            new BehaviorInstruction(BehaviorInstructionKind.TrashSelf),
                            new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 1),
                        ]),
                ]));
        GameState beforeTrashState = CreateState(
            beforeTrashCatalog,
            (sourceId, definitionId, PlayerSide.Runner, true));
        PlayerId runner = beforeTrashState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState beforeTrashPrompt = GameEngine.BeginGameplayOccurrenceForTesting(
            beforeTrashState,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            beforeTrashCatalog,
            maxResolutionSteps: 32);
        BehaviorResolutionFrame beforeTrashChild = Assert.IsType<BehaviorResolutionFrame>(beforeTrashPrompt.ResolutionStack[^1]);
        Assert.False(beforeTrashChild.CatalogTriggerSourceMovedBeforeStart);
        Assert.Equal(1, beforeTrashChild.InstructionCursor);
        GameState movedBeforeTrash = MoveCard(beforeTrashPrompt, sourceId, ZoneKind.RunnerHeap);
        ApplyResult beforeTrashRejected = ApplyCurrentBehaviorChoice(movedBeforeTrash, beforeTrashCatalog);
        Assert.False(beforeTrashRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, beforeTrashRejected.Rejection?.Code);
        Assert.Equal(DeterministicHash.ForState(movedBeforeTrash), DeterministicHash.ForState(beforeTrashRejected.State));

        BehaviorCatalog afterTrashCatalog = Catalog(
            new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger(
                        "after-trash",
                        OccurrenceKind.CardInstalled,
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Self,
                        null,
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.TrashSelf),
                            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: pause),
                            new BehaviorInstruction(BehaviorInstructionKind.GainCredits, 1),
                        ]),
                ]));
        GameState afterTrashState = CreateState(
            afterTrashCatalog,
            (sourceId, definitionId, PlayerSide.Runner, true));
        runner = afterTrashState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState afterTrashPrompt = GameEngine.BeginGameplayOccurrenceForTesting(
            afterTrashState,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, sourceId),
            afterTrashCatalog,
            maxResolutionSteps: 32);
        BehaviorResolutionFrame afterTrashChild = Assert.IsType<BehaviorResolutionFrame>(afterTrashPrompt.ResolutionStack[^1]);
        Assert.False(afterTrashChild.CatalogTriggerSourceMovedBeforeStart);
        Assert.Equal(1, afterTrashChild.InstructionCursor);
        Assert.Contains(sourceId, afterTrashPrompt.GetZone(ZoneKind.RunnerHeap).CardIds);
        GameState movedToWrongDestination = MoveCard(afterTrashPrompt, sourceId, ZoneKind.RunnerGrip);
        ApplyResult wrongDestinationRejected = ApplyCurrentBehaviorChoice(movedToWrongDestination, afterTrashCatalog);
        Assert.False(wrongDestinationRejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, wrongDestinationRejected.Rejection?.Code);
        Assert.Equal(
            DeterministicHash.ForState(movedToWrongDestination),
            DeterministicHash.ForState(wrongDestinationRejected.State));
    }

    [Fact]
    public void MarkerAndCatalogInvocationDiscriminatorsRoundTripAndRejectMixedFrameKeys()
    {
        PlayerId runner = new("player:runner");
        GameplayOccurrenceEffect marker = new(
            OccurrenceKind.CardAccessed,
            runner,
            new CardInstanceId("card:c2a-marker"),
            new ServerId("server:c2a"));
        EffectInstruction markerInstruction = marker;
        string markerJson = EngineJson.Serialize(markerInstruction);
        GameplayOccurrenceEffect restoredMarker = Assert.IsType<GameplayOccurrenceEffect>(
            EngineJson.Deserialize<EffectInstruction>(markerJson));
        Assert.Equal(marker, restoredMarker);

        Assert.Throws<ArgumentException>(() => new TriggerInvocation(
            1,
            new TriggerId("trigger:c2a-malformed"),
            runner,
            TriggerMode.Optional,
            ResolutionBehaviorIds.CardBehavior,
            null,
            null,
            null,
            new OccurrenceSnapshot(
                new OccurrenceId("occurrence:c2a-malformed"),
                OccurrenceKind.CardInstalled,
                runner,
                null,
                null,
                null,
                null,
                marker),
            new CostBundle(),
            new EffectBundle(),
            triggerKey: "trigger:c2a-key"));
    }

    private static BehaviorTrigger Trigger(
        string key,
        TriggerMode mode,
        BehaviorTriggerScope scope,
        BehaviorCardFilter? filter,
        int amount) => new(
        key,
        OccurrenceKind.CardInstalled,
        mode,
        scope,
        filter,
        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount)]);

    private static BehaviorTrigger TurnTrigger(
        string key,
        TriggerMode mode,
        BehaviorTriggerScope scope,
        int amount) => new(
        key,
        OccurrenceKind.TurnBegin,
        mode,
        scope,
        null,
        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount)]);

    private static BehaviorCatalog Catalog(params CardBehaviorDefinition[] definitions)
    {
        var builder = new BehaviorCatalogBuilder("test:c2a-catalog");
        foreach (CardBehaviorDefinition definition in definitions)
        {
            builder.Add(definition);
        }

        return builder.Build();
    }

    private static GameState CreateControllerState(
        BehaviorCatalog catalog,
        CardInstanceId corpSourceId,
        CardDefinitionId corpDefinitionId,
        CardInstanceId runnerSourceId,
        CardDefinitionId runnerDefinitionId)
    {
        GameState baseline = GameState.CreateNew(
            4302,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState corpRoot = baseline.Zones.Single(zone => zone.Id == baseline.GetServer(
            baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id).RootZoneId);
        ZoneState runnerRig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState corpSource = new(
            corpSourceId,
            corpDefinitionId,
            corpRoot.Id,
            PlayerSide.Corp,
            faceUp: true,
            rezzed: true);
        CardInstanceState runnerSource = new(
            runnerSourceId,
            runnerDefinitionId,
            runnerRig.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == corpRoot.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(corpSourceId).ToArray())
                : zone.Id == runnerRig.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(runnerSourceId).ToArray())
                    : zone)
            .ToArray();
        return CopyState(
            baseline,
            cards: [corpSource, runnerSource],
            zones: zones);
    }

    private static ApplyResult ApplyCurrentTriggerChoice(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction use = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, use.Id),
            catalog);
    }

    private static ApplyResult ApplyCurrentTriggerDecline(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction decline = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decline.Id),
            catalog);
    }

    private static GameState BeginFormalTurn(
        GameState state,
        BehaviorCatalog catalog,
        bool stopAtCheckpoint = false)
    {
        state = GameEngine.BeginTurn(
            state,
            state.Turn.ActiveSide,
            catalog,
            maxResolutionSteps: stopAtCheckpoint ? 128 : GameEngine.DefaultResolutionStepBudget);
        state = Pass(state, catalog);
        if (stopAtCheckpoint)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            if (pending.Kind == DecisionKind.TriggerChoice)
            {
                return state;
            }
        }

        return Pass(state, catalog);
    }

    private static GameState Pass(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static ApplyResult ApplyCurrentBehaviorChoice(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction choice = decision.LegalActions.First(action => action.Kind == ActionKind.ChooseBehavior);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, choice.Id),
            catalog);
    }

    private static GameState AddCounter(
        GameState state,
        CardInstanceId cardId,
        CardCounterState counter)
    {
        RulesState rules = state.RulesState ?? RulesState.CreateDefault(state.Players);
        RulesState withCounter = new(
            rules.Version,
            rules.Players,
            rules.Programs,
            rules.Counters.Append(counter).ToArray(),
            rules.LingeringEffects,
            rules.Triggers,
            rules.Interrupts);
        return CopyState(state, rulesState: withCounter);
    }

    private static GameState MoveCard(
        GameState state,
        CardInstanceId cardId,
        ZoneKind destinationKind)
    {
        CardInstanceState card = state.Cards.Single(candidate => candidate.Id == cardId);
        ZoneState source = state.Zones.Single(zone => zone.Id == card.ZoneId);
        ZoneState destination = state.GetZone(destinationKind);
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            faceUp: destination.Kind is ZoneKind.CorpDiscard
                or ZoneKind.RunnerHeap
                or ZoneKind.CorpRemovedFromGame
                or ZoneKind.RunnerRemovedFromGame
                || card.FaceUp,
            rezzed: destination.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce && card.Rezzed,
            card.AdvancementCounters);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == source.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destination.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        return CopyState(
            state,
            cards: state.Cards.Select(candidate => candidate.Id == cardId ? moved : candidate).ToArray(),
            zones: zones);
    }

    private static GameState ReplaceCard(
        GameState state,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        PlayerSide ownerSide)
    {
        CardInstanceState card = state.Cards.Single(candidate => candidate.Id == cardId);
        CardInstanceState replacement = new(
            card.Id,
            definitionId,
            card.ZoneId,
            ownerSide,
            card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters);
        return CopyState(
            state,
            cards: state.Cards.Select(candidate => candidate.Id == cardId ? replacement : candidate).ToArray());
    }

    private static GameState CopyState(
        GameState state,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        RulesState? rulesState = null,
        TurnState? turn = null) =>
        new(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            cards ?? state.Cards,
            zones ?? state.Zones,
            state.Servers,
            turn ?? state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            resolutionStack ?? state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            rulesState ?? state.RulesState,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);

    private static GameState SetActiveSide(GameState state, PlayerSide side) => CopyState(
        state,
        turn: new TurnState(
            side,
            side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
            state.Turn.TurnNumber,
            TurnStep.None,
            state.Turn.History));

    private static GameState CreateState(
        BehaviorCatalog catalog,
        params (CardInstanceId Id, CardDefinitionId DefinitionId, PlayerSide Owner, bool Active)[] placements)
    {
        GameState baseline = GameState.CreateNew(
            4301,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState[] cards = placements
            .Select(placement => new CardInstanceState(
                placement.Id,
                placement.DefinitionId,
                placement.Active ? rig.Id : baseline.GetZone(ZoneKind.RunnerGrip).Id,
                placement.Owner,
                faceUp: placement.Active))
            .ToArray();
        CardInstanceId[] rigIds = placements.Where(placement => placement.Active).Select(placement => placement.Id).ToArray();
        CardInstanceId[] gripIds = placements.Where(placement => !placement.Active).Select(placement => placement.Id).ToArray();
        ZoneState[] zones = baseline.Zones.Select(zone => zone.Id == rig.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Concat(rigIds).ToArray())
            : zone.Kind == ZoneKind.RunnerGrip
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Concat(gripIds).ToArray())
                : zone).ToArray();
        return new GameState(
            gameId: baseline.GameId,
            rulesVersion: baseline.RulesVersion,
            cardDataVersion: baseline.CardDataVersion,
            players: baseline.Players,
            cards: cards,
            zones: zones,
            servers: baseline.Servers,
            turn: baseline.Turn,
            outcome: baseline.Outcome,
            randomState: baseline.RandomState,
            allocator: baseline.Allocator,
            schemaVersion: baseline.SchemaVersion,
            initialSeed: baseline.InitialSeed,
            rulesState: baseline.RulesState,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
    }

    private static GameState ResolveAllCheckpointTriggers(GameState state, BehaviorCatalog catalog)
    {
        while (state.PendingDecision is { } decision)
        {
            LegalAction action = decision.Kind switch
            {
                DecisionKind.TriggerOrdering => decision.LegalActions
                    .First(candidate => candidate.Kind == ActionKind.OrderTriggers),
                DecisionKind.TriggerChoice => decision.LegalActions
                    .Single(candidate => candidate.Kind == ActionKind.UseTrigger),
                _ => throw new InvalidOperationException($"Unexpected decision kind '{decision.Kind}'."),
            };
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
                catalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static GameState ReplaceResolutionStack(GameState state, IReadOnlyList<ResolutionFrame> frames)
    {
        return new GameState(
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
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);
    }

    private static BehaviorResolutionFrame CloneCatalogChild(
        BehaviorResolutionFrame source,
        string? parentFrameIdentity = null,
        string? catalogFingerprint = null) => new(
        source.Version,
        source.BehaviorId,
        source.DefinitionId,
        source.SourceCardId,
        source.Controller,
        source.ActingPlayer,
        catalogFingerprint ?? source.CatalogFingerprint,
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
        parentFrameIdentity ?? source.ParentFrameIdentity,
        source.AbilityKey,
        source.TriggerKey);
}
