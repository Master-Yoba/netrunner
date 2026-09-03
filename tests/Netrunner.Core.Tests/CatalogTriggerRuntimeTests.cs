using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused C2a-1 coverage for catalog trigger materialization.</summary>
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

    private static BehaviorCatalog Catalog(params CardBehaviorDefinition[] definitions)
    {
        var builder = new BehaviorCatalogBuilder("test:c2a-catalog");
        foreach (CardBehaviorDefinition definition in definitions)
        {
            builder.Add(definition);
        }

        return builder.Build();
    }

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
