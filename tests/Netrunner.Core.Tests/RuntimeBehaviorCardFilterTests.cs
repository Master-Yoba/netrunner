using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Regression coverage for the runtime-catalog authority boundary used by
/// generic hidden-zone filters. These tests deliberately open the behavior
/// through an ordinary Runner Event so the working reducer carries both
/// injected catalogs all the way to the pending Search choice.
/// </summary>
public sealed class RuntimeBehaviorCardFilterTests
{
    [Fact]
    public void RuntimeIcebreakerFilterUsesMetadataOnlyAndPreservesStackOrder()
    {
        const string suffix = "runtime-filter-authority";
        CardDefinitionId sourceDefinition = new($"test:{suffix}-event");
        CardInstanceId sourceCard = new($"card:{suffix}-event");
        CardDefinitionId firstDefinition = new($"test:{suffix}-first");
        CardInstanceId firstCard = new($"card:{suffix}-first");
        CardDefinitionId secondDefinition = new($"test:{suffix}-second");
        CardInstanceId secondCard = new($"card:{suffix}-second");
        CardDefinitionId noSubtypeDefinition = new($"test:{suffix}-no-subtype");
        CardInstanceId noSubtypeCard = new($"card:{suffix}-no-subtype");
        CardDefinitionId wrongClassDefinition = new($"test:{suffix}-wrong-class");
        CardInstanceId wrongClassCard = new($"card:{suffix}-wrong-class");
        CardDefinitionId wrongZoneDefinition = new($"test:{suffix}-wrong-zone");
        CardInstanceId wrongZoneCard = new($"card:{suffix}-wrong-zone");
        CardDefinitionId unknownDefinition = new($"test:{suffix}-unknown");
        CardInstanceId unknownCard = new($"card:{suffix}-unknown");
        CardDefinitionId wrongOwnerDefinition = new($"test:{suffix}-wrong-owner");
        CardInstanceId wrongOwnerCard = new($"card:{suffix}-wrong-owner");

        BehaviorCardFilter filter = new(
            BehaviorCardType.Icebreaker,
            ownerSide: PlayerSide.Runner,
            subtype: "icebreaker");
        BehaviorTarget searchTarget = BehaviorTarget.CardInZone(BehaviorZone.Stack, filter);
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder($"test:{suffix}")
            .Add(new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                costs: [new BehaviorCost(BehaviorCostKind.Credits)],
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Search,
                        argument: "runtime-filter-search",
                        sourceZone: BehaviorZone.Stack,
                        filter: filter,
                        target: searchTarget),
                ]))
            // This intentionally stale behavior classification must not make
            // the Resource card below look like an executable icebreaker.
            .AddIcebreaker(new IcebreakerBehaviorSpec(
                wrongClassDefinition,
                baseStrength: 1,
                interfaceType: IceType.Barrier,
                breakCost: 1,
                breakLimit: 1,
                pumpCost: 1,
                pumpStrength: 1))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder($"runtime:{suffix}")
            .Add(new CardRuntimeSpec(
                sourceDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Event,
                playCost: 1))
            .Add(new CardRuntimeSpec(
                firstDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                subtypes: ["icebreaker"]))
            .Add(new CardRuntimeSpec(
                secondDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                subtypes: ["decoder", "icebreaker"]))
            .Add(new CardRuntimeSpec(
                noSubtypeDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                subtypes: ["decoder"]))
            .Add(new CardRuntimeSpec(
                wrongClassDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 1,
                subtypes: ["icebreaker"]))
            .Add(new CardRuntimeSpec(
                wrongZoneDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                subtypes: ["icebreaker"]))
            .Add(new CardRuntimeSpec(
                wrongOwnerDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Operation,
                playCost: 0,
                subtypes: ["icebreaker"]))
            .Build();

        GameState state = CreateState(
            behaviorCatalog,
            runtimeCatalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (firstCard, firstDefinition, ZoneKind.RunnerStack, PlayerSide.Runner),
            (noSubtypeCard, noSubtypeDefinition, ZoneKind.RunnerStack, PlayerSide.Runner),
            (wrongClassCard, wrongClassDefinition, ZoneKind.RunnerStack, PlayerSide.Runner),
            (secondCard, secondDefinition, ZoneKind.RunnerStack, PlayerSide.Runner),
            (unknownCard, unknownDefinition, ZoneKind.RunnerStack, PlayerSide.Runner),
            (wrongZoneCard, wrongZoneDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (wrongOwnerCard, wrongOwnerDefinition, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState turnAction = OpenRunnerTurnAction(state, behaviorCatalog, runtimeCatalog);
        LegalAction play = Assert.Single(
            turnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult played = Apply(turnAction, behaviorCatalog, runtimeCatalog, play);

        Assert.True(played.Accepted, played.Rejection?.Message);
        PendingDecision pending = Assert.IsType<PendingDecision>(played.State.PendingDecision);
        Assert.Equal(DecisionKind.BehaviorChoice, pending.Kind);
        Assert.Equal(runner, pending.Owner);

        BehaviorResolutionFrame frame = Assert.IsType<BehaviorResolutionFrame>(played.State.ResolutionStack[^1]);
        BehaviorChoiceMaterialization choice = Assert.IsType<BehaviorChoiceMaterialization>(frame.Choice);
        Assert.Equal([firstCard, secondCard], choice.CandidateCardIds);
        Assert.Contains(
            pending.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.CardIds.Count == 0);
        Assert.Equal(
            [firstCard, secondCard],
            pending.LegalActions
                .Select(action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters))
                .Where(parameters => parameters.CardIds.Count == 1)
                .Select(parameters => parameters.CardIds.Single())
                .ToArray());
        Assert.DoesNotContain(noSubtypeCard, choice.CandidateCardIds);
        Assert.DoesNotContain(wrongClassCard, choice.CandidateCardIds);
        Assert.DoesNotContain(wrongZoneCard, choice.CandidateCardIds);
        Assert.DoesNotContain(unknownCard, choice.CandidateCardIds);
        Assert.DoesNotContain(wrongOwnerCard, choice.CandidateCardIds);

        Assert.Empty(StateInvariantValidator.Collect(
            played.State,
            behaviorCatalog,
            runtimeCatalog));

        string json = EngineJson.Serialize(played.State);
        GameState restored = EngineJson.DeserializeState(json, behaviorCatalog, runtimeCatalog);
        Assert.Equal(DeterministicHash.ForState(played.State), DeterministicHash.ForState(restored));
        Assert.Empty(StateInvariantValidator.Collect(restored, behaviorCatalog, runtimeCatalog));
        BehaviorResolutionFrame restoredFrame = Assert.IsType<BehaviorResolutionFrame>(restored.ResolutionStack[^1]);
        Assert.Equal([firstCard, secondCard], restoredFrame.Choice!.CandidateCardIds);
    }

    [Fact]
    public void RuntimeOptionalSubtypeFilterIsOrdinalAndCaseSensitive()
    {
        RuntimeFilterFixture positive = CreateRuntimeFilterFixture("icebreaker");
        GameState positiveTurnAction = OpenRunnerTurnAction(
            positive.State,
            positive.BehaviorCatalog,
            positive.RuntimeCatalog);
        LegalAction positivePlay = Assert.Single(
            positiveTurnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult positivePlayed = Apply(
            positiveTurnAction,
            positive.BehaviorCatalog,
            positive.RuntimeCatalog,
            positivePlay);
        Assert.True(positivePlayed.Accepted, positivePlayed.Rejection?.Message);
        BehaviorResolutionFrame positiveFrame = Assert.IsType<BehaviorResolutionFrame>(positivePlayed.State.ResolutionStack[^1]);
        Assert.Equal([positive.CandidateCard], positiveFrame.Choice!.CandidateCardIds);

        RuntimeFilterFixture negative = CreateRuntimeFilterFixture("Icebreaker");
        GameState negativeTurnAction = OpenRunnerTurnAction(
            negative.State,
            negative.BehaviorCatalog,
            negative.RuntimeCatalog);
        LegalAction negativePlay = Assert.Single(
            negativeTurnAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PlayCard);
        ApplyResult negativePlayed = Apply(
            negativeTurnAction,
            negative.BehaviorCatalog,
            negative.RuntimeCatalog,
            negativePlay);
        Assert.True(negativePlayed.Accepted, negativePlayed.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, negativePlayed.State.PendingDecision!.Kind);
        Assert.DoesNotContain(
            negativePlayed.Events,
            @event => @event.Kind == EngineEventKinds.BehaviorCardRevealed);
        Assert.Empty(StateInvariantValidator.Collect(
            negativePlayed.State,
            negative.BehaviorCatalog,
            negative.RuntimeCatalog));
    }

    [Fact]
    public void CatalogFreeIcebreakerFilterFallsBackToBehaviorClassification()
    {
        CardDefinitionId sourceDefinition = new("test:catalog-free-filter-source");
        CardInstanceId sourceCard = new("card:catalog-free-filter-source");
        CardDefinitionId candidateDefinition = new("test:catalog-free-filter-candidate");
        CardInstanceId candidateCard = new("card:catalog-free-filter-candidate");
        BehaviorCardFilter filter = new(
            BehaviorCardType.Icebreaker,
            ownerSide: PlayerSide.Runner);
        BehaviorTarget target = BehaviorTarget.CardInZone(BehaviorZone.Stack, filter);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:catalog-free-filter")
            .Add(new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Search,
                        sourceZone: BehaviorZone.Stack,
                        filter: filter,
                        target: target),
                ]))
            .AddIcebreaker(new IcebreakerBehaviorSpec(
                candidateDefinition,
                baseStrength: 1,
                interfaceType: IceType.Barrier,
                breakCost: 1,
                breakLimit: 1,
                pumpCost: 1,
                pumpStrength: 1))
            .Build();
        GameState state = CreateStateWithoutRuntimeCatalog(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceCard, runner),
            catalog);

        BehaviorResolutionFrame frame = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        Assert.Equal([candidateCard], frame.Choice!.CandidateCardIds);
        Assert.Equal(DecisionKind.BehaviorChoice, prompt.PendingDecision!.Kind);
        Assert.Equal(runner, prompt.PendingDecision.Owner);
        Assert.Empty(StateInvariantValidator.Collect(prompt, catalog, null));
    }

    private static RuntimeFilterFixture CreateRuntimeFilterFixture(string subtype)
    {
        CardDefinitionId sourceDefinition = new($"test:runtime-subtype-{subtype.ToLowerInvariant()}-source");
        CardInstanceId sourceCard = new($"card:runtime-subtype-{subtype.ToLowerInvariant()}-source");
        CardDefinitionId candidateDefinition = new($"test:runtime-subtype-{subtype.ToLowerInvariant()}-candidate");
        CardInstanceId candidateCard = new($"card:runtime-subtype-{subtype.ToLowerInvariant()}-candidate");
        BehaviorCardFilter filter = new(
            BehaviorCardType.Icebreaker,
            ownerSide: PlayerSide.Runner,
            subtype: subtype);
        BehaviorTarget target = BehaviorTarget.CardInZone(BehaviorZone.Stack, filter);
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(
                $"test:runtime-subtype-{subtype.ToLowerInvariant()}")
            .Add(new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                costs: [new BehaviorCost(BehaviorCostKind.Credits)],
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Search,
                        sourceZone: BehaviorZone.Stack,
                        filter: filter,
                        target: target),
                ]))
            .Build();
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder(
                $"runtime:runtime-subtype-{subtype.ToLowerInvariant()}")
            .Add(new CardRuntimeSpec(
                sourceDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Event,
                playCost: 1))
            .Add(new CardRuntimeSpec(
                candidateDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                subtypes: ["icebreaker"]))
            .Build();
        GameState state = CreateState(
            behaviorCatalog,
            runtimeCatalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        return new RuntimeFilterFixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            candidateCard);
    }

    private static GameState OpenRunnerTurnAction(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        GameState current = GameEngine.BeginTurn(
            state,
            PlayerSide.Runner,
            behaviorCatalog,
            runtimeCatalog);
        while (current.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(current.PendingDecision);
            LegalAction pass = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(current, behaviorCatalog, runtimeCatalog, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            current = result.State;
        }

        return current;
    }

    private static ApplyResult Apply(
        GameState state,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog,
        LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            behaviorCatalog,
            runtimeCatalog);

    private static GameState CreateState(
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog,
        params (CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)[] placements)
    {
        GameState baseline = GameState.CreateNew(
            seed: 912,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        return BuildState(baseline, behaviorCatalog, runtimeCatalog, placements);
    }

    private static GameState CreateStateWithoutRuntimeCatalog(
        BehaviorCatalog behaviorCatalog,
        params (CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)[] placements)
    {
        GameState baseline = GameState.CreateNew(
            seed: 913,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
        return BuildState(baseline, behaviorCatalog, runtimeCatalog: null, placements);
    }

    private static GameState BuildState(
        GameState baseline,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog? runtimeCatalog,
        IReadOnlyList<(CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)> placements)
    {
        CardInstanceState[] cards = placements
            .Select(placement => new CardInstanceState(
                placement.Id,
                placement.DefinitionId,
                baseline.GetZone(placement.Zone).Id,
                placement.Owner,
                faceUp: placement.Zone is ZoneKind.RunnerRig
                    or ZoneKind.RunnerPlayArea
                    or ZoneKind.CorpPlayArea
                    or ZoneKind.ServerIce
                    or ZoneKind.ServerRoot))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone =>
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
            })
            .ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, turnNumber: 1),
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
            runtimeCatalog?.Version,
            runtimeCatalog?.Fingerprint);
    }

    private sealed record RuntimeFilterFixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        CardInstanceId CandidateCard);
}
