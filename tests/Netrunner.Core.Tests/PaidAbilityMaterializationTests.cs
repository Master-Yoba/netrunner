using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Compact evidence for the Slice 4.3 A2 materialization boundary. These
/// tests exercise the pure projection and the invariant bijection without
/// entering the not-yet-implemented consumption lifecycle.
/// </summary>
public sealed class PaidAbilityMaterializationTests
{
    [Fact]
    public void RunnerRigClickAndCorpServerPaidAbilitiesUseTheActiveWindow()
    {
        BehaviorPaidAbility click = Ability(
            "ability:click",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks)]);
        BehaviorPaidAbility paid = Ability(
            "ability:paid",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)]);
        CardDefinitionId runnerDefinition = new("test:materialize-runner");
        CardDefinitionId corpDefinition = new("test:materialize-corp");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-positive")
            .Add(new CardBehaviorDefinition(runnerDefinition, PlayerSide.Runner, paidAbilities: [click, paid]))
            .Add(new CardBehaviorDefinition(corpDefinition, PlayerSide.Corp, paidAbilities: [paid]))
            .Build();

        GameState runnerAction = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.AwaitingAction,
            [Source("card:materialize-runner", runnerDefinition, PlayerSide.Runner, ZoneKind.RunnerRig)]);
        IReadOnlyList<AbilityActionParameters> clickActions =
            GameEngine.MaterializeAbilityActionParameters(runnerAction, catalog);
        Assert.Single(clickActions);
        Assert.Equal("ability:click", clickActions[0].AbilityKey);
        Assert.Equal(BehaviorAbilityActivation.ClickAction, clickActions[0].Activation);

        GameState runnerPaid = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-runner", runnerDefinition, PlayerSide.Runner, ZoneKind.RunnerRig)]);
        IReadOnlyList<AbilityActionParameters> paidActions =
            GameEngine.MaterializeAbilityActionParameters(runnerPaid, catalog);
        Assert.Single(paidActions);
        Assert.Equal("ability:paid", paidActions[0].AbilityKey);

        ServerState headquarters = runnerPaid.Servers.Single(value => value.Kind == ServerKind.Headquarters);
        GameState corpPaid = BuildState(
            catalog,
            PlayerSide.Corp,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-corp", corpDefinition, PlayerSide.Corp, ZoneKind.ServerRoot, headquarters.Id, faceUp: true, rezzed: true)]);
        IReadOnlyList<AbilityActionParameters> corpActions =
            GameEngine.MaterializeAbilityActionParameters(corpPaid, catalog);
        Assert.Single(corpActions);
        Assert.Equal("ability:paid", corpActions[0].AbilityKey);
        Assert.Equal(ZoneKind.ServerRoot, corpActions[0].SourceZoneKind);
    }

    [Fact]
    public void InactiveAndUnrezzedSourcesAndUnsupportedDeclarationsFailClosed()
    {
        BehaviorPaidAbility paid = Ability(
            "ability:paid",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)]);
        BehaviorPaidAbility targeted = Ability(
            "ability:targeted",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            targets: [new BehaviorTarget(BehaviorTargetKind.Self)]);
        BehaviorPaidAbility additional = Ability(
            "ability:additional",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Additional, additionalKey: "cost:later")]);
        CardDefinitionId runnerDefinition = new("test:materialize-inactive-runner");
        CardDefinitionId corpDefinition = new("test:materialize-inactive-corp");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-inactive")
            .Add(new CardBehaviorDefinition(runnerDefinition, PlayerSide.Runner, paidAbilities: [paid, targeted, additional]))
            .Add(new CardBehaviorDefinition(corpDefinition, PlayerSide.Corp, paidAbilities: [paid]))
            .Build();

        GameState facedown = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-facedown", runnerDefinition, PlayerSide.Runner, ZoneKind.RunnerRig, faceUp: false)]);
        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(facedown, catalog));

        ServerState remote = facedown.Servers.First(value => value.Kind is ServerKind.Remote or ServerKind.Headquarters);
        GameState unrezzed = BuildState(
            catalog,
            PlayerSide.Corp,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-unrezzed", corpDefinition, PlayerSide.Corp, ZoneKind.ServerRoot, remote.Id, faceUp: true, rezzed: false)]);
        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(unrezzed, catalog));

        GameState unaffordable = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-unsupported", runnerDefinition, PlayerSide.Runner, ZoneKind.RunnerRig)],
            runnerCredits: 0);
        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(unaffordable, catalog));
    }

    [Fact]
    public void AffordabilityAggregatesCreditsClicksTagsAndCounters()
    {
        BehaviorPaidAbility paid = Ability(
            "ability:aggregate",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [
                new BehaviorCost(BehaviorCostKind.Credits, 2),
                new BehaviorCost(BehaviorCostKind.Credits, 2),
                new BehaviorCost(BehaviorCostKind.LoseClick, 1),
                new BehaviorCost(BehaviorCostKind.LoseClick, 1),
                new BehaviorCost(BehaviorCostKind.Tags, 1),
                new BehaviorCost(BehaviorCostKind.Tags, 2),
                new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "charge"),
                new BehaviorCost(BehaviorCostKind.Counter, 2, counterName: "charge"),
            ]);
        CardDefinitionId definition = new("test:materialize-aggregate");
        CardInstanceId sourceId = new("card:materialize-aggregate");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-aggregate")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [paid]))
            .Build();

        GameState affordable = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig)],
            counters: [new CardCounterState(sourceId, "charge", 3)],
            runnerCredits: 4,
            runnerClicks: 2,
            runnerTags: 3);
        Assert.Single(GameEngine.MaterializeAbilityActionParameters(affordable, catalog));

        GameState insufficientCounter = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig)],
            counters: [new CardCounterState(sourceId, "charge", 2)],
            runnerCredits: 4,
            runnerClicks: 2,
            runnerTags: 3);
        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(insufficientCounter, catalog));
    }

    [Fact]
    public void GenericCounterAbilityCannotSpendHostedCreditByName()
    {
        BehaviorPaidAbility paid = Ability(
            "ability:generic-credit-counter",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Counter, 1, counterName: "credit")]);
        CardDefinitionId definition = new("test:materialize-generic-credit-counter");
        CardInstanceId sourceId = new("card:materialize-generic-credit-counter");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-generic-credit-counter")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [paid]))
            .Build();

        GameState state = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig)],
            counters: [CardCounterState.CreateHostedCredit(
                sourceId,
                1,
                HostedCreditSpendRestriction.AbilityCost)]);

        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(state, catalog));
    }

    [Fact]
    public void OncePerTurnUsageDisappearsAndNewTurnFactsResetIt()
    {
        BehaviorPaidAbility once = Ability(
            "ability:once",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            usageLimit: BehaviorAbilityUsageLimit.OncePerTurn);
        CardDefinitionId definition = new("test:materialize-once");
        CardInstanceId sourceId = new("card:materialize-once");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-once")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [once]))
            .Build();
        SourceSpec source = Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig);

        GameState available = BuildState(catalog, PlayerSide.Runner, TurnFrameStage.BeforeTurnPriority, [source]);
        Assert.Single(GameEngine.MaterializeAbilityActionParameters(available, catalog));

        GameState used = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [source],
            usedAbilityKeys: [new AbilityUseKey(sourceId, once.Key)]);
        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(used, catalog));

        GameState nextTurn = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [source],
            turnNumber: 2);
        Assert.Single(GameEngine.MaterializeAbilityActionParameters(nextTurn, catalog));
    }

    [Fact]
    public void StaleRunnerTurnFactsDoNotOfferRunnerAbilitiesDuringCorpTurn()
    {
        CardDefinitionId definition = new("test:materialize-stale-runner-facts");
        CardInstanceId sourceId = new("card:materialize-stale-runner-facts");
        BehaviorPaidAbility successfulRun = Ability(
            "ability:successful-run",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            conditions: [BehaviorCondition.SuccessfulRunThisTurn()]);
        BehaviorPaidAbility installed = Ability(
            "ability:installed",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            conditions: [BehaviorCondition.InstalledThisTurn()]);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-stale-runner-facts")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [successfulRun, installed]))
            .Build();

        GameState state = BuildState(
            catalog,
            PlayerSide.Corp,
            TurnFrameStage.ActionPriority,
            [Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig)],
            turnNumber: 2);
        TurnFactsState corpFacts = state.Turn.History!.CorpThisTurn!;
        TurnFactsState staleRunnerFacts = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false,
            installedCardIds: [sourceId],
            successfulRun: true);
        state = ReplaceTurn(
            state,
            new TurnState(
                PlayerSide.Corp,
                TurnPhase.Corp,
                2,
                TurnStep.CorpAction,
                new TurnHistoryState(1, corpFacts, null, staleRunnerFacts, null)));
        state = ReplaceStack(
            state,
            [new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Corp,
                2,
                TurnFrameStage.ActionPriority,
                TurnPriorityWindowKind.PaidRezAndScore,
                PlayerSide.Runner)]);

        Assert.Empty(GameEngine.MaterializeAbilityActionParameters(state, catalog));
    }

    [Fact]
    public void OpponentTurnOncePerTurnUsageRecordsActiveTurnFacts()
    {
        CardDefinitionId definition = new("test:materialize-opponent-usage");
        CardInstanceId sourceId = new("card:materialize-opponent-usage");
        BehaviorPaidAbility once = Ability(
            "ability:opponent-once",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)],
            usageLimit: BehaviorAbilityUsageLimit.OncePerTurn);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-opponent-usage")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [once]))
            .Build();

        GameState state = BuildState(
            catalog,
            PlayerSide.Corp,
            TurnFrameStage.ActionPriority,
            [Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig)],
            turnNumber: 2);
        TurnFactsState corpFacts = state.Turn.History!.CorpThisTurn!;
        TurnFactsState staleRunnerFacts = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false,
            usedAbilityKeys: [new AbilityUseKey(sourceId, "ability:previous")]);
        state = ReplaceTurn(
            state,
            new TurnState(
                PlayerSide.Corp,
                TurnPhase.Corp,
                2,
                TurnStep.CorpAction,
                new TurnHistoryState(1, corpFacts, null, staleRunnerFacts, null)));
        TurnFrame frame = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Corp,
            2,
            TurnFrameStage.ActionPriority,
            TurnPriorityWindowKind.PaidRezAndScore,
            PlayerSide.Runner);
        state = ReplaceStack(state, []);

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
        Assert.Contains(
            result.State.Turn.History!.CorpThisTurn!.UsedAbilityKeys,
            key => key.SourceCardId == sourceId && key.AbilityKey == once.Key);
        Assert.DoesNotContain(
            result.State.Turn.History.RunnerThisTurn!.UsedAbilityKeys,
            key => key.SourceCardId == sourceId && key.AbilityKey == once.Key);
    }

    [Fact]
    public void AdditionalCostMaterializationDoesNotCreateAbsentLazyZone()
    {
        CardDefinitionId definition = new("test:materialize-absent-lazy-zone");
        CardInstanceId sourceId = new("card:materialize-absent-lazy-zone");
        BehaviorTarget target = BehaviorTarget.CardsInZone(
            BehaviorZone.PlayArea,
            new BehaviorCardFilter(ownerSide: PlayerSide.Runner));
        BehaviorPaidAbility ability = Ability(
            "ability:absent-lazy-zone",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(
                BehaviorCostKind.Additional,
                additionalKey: "cost:absent-lazy-zone",
                additionalTarget: target,
                additionalKind: BehaviorAdditionalCostKind.TrashCards)]);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-absent-lazy-zone")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [ability]))
            .Build();

        GameState state = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.ActionPriority,
            [Source(sourceId.Value, definition, PlayerSide.Runner, ZoneKind.RunnerRig)]);
        ulong allocatorBefore = state.Allocator.NextSequence;
        Assert.DoesNotContain(state.Zones, zone => zone.Kind == ZoneKind.RunnerPlayArea);
        state = ReplaceStack(state, []);

        GameState prompt = GameEngine.BeginResolution(
            state,
            new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                PlayerSide.Runner,
                1,
                TurnFrameStage.ActionPriority,
                TurnPriorityWindowKind.PaidAndRez,
                PlayerSide.Runner),
            catalog);

        Assert.DoesNotContain(prompt.Zones, zone => zone.Kind == ZoneKind.RunnerPlayArea);
        Assert.Equal(checked(allocatorBefore + 2), prompt.Allocator.NextSequence);
        Assert.DoesNotContain(prompt.PendingDecision!.LegalActions, action => action.Kind == ActionKind.UseAbility);
    }

    [Fact]
    public void DuplicateUsageEntriesSurviveConstructionAndFailInvariantValidation()
    {
        CardInstanceId sourceId = new("card:materialize-duplicate-usage");
        AbilityUseKey key = new(sourceId, "ability:once");
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            1,
            4,
            0,
            true,
            false,
            false,
            false,
            usedAbilityKeys: [key, key]);
        Assert.Equal(2, facts.UsedAbilityKeys.Count);
        Assert.Equal(key, facts.UsedAbilityKeys[0]);
        Assert.Equal(key, facts.UsedAbilityKeys[1]);

        GameState baseline = GameState.CreateNew(734);
        GameState forged = ReplaceTurn(
            baseline,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                1,
                TurnStep.RunnerAction,
                new TurnHistoryState(1, null, null, facts, null)));
        Assert.Contains(
            StateInvariantValidator.Collect(forged),
            violation => violation.Code == "turn.duplicate-ability-use");
    }

    [Fact]
    public void AbilityActionBijectionRejectsMissingExtraAndMutatedPayloads()
    {
        BehaviorPaidAbility paid = Ability(
            "ability:bijective",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)]);
        CardDefinitionId definition = new("test:materialize-bijection");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-bijection")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [paid]))
            .Build();
        GameState state = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-bijection", definition, PlayerSide.Runner, ZoneKind.RunnerRig)]);
        AbilityActionParameters expected = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(state, catalog));
        LegalAction use = new(new ActionId("action:materialize-bijection"), ActionKind.UseAbility, "Use ability", expected);
        LegalAction pass = new(new ActionId("action:materialize-pass"), ActionKind.PassPriority, "Pass priority", new NoActionParameters());
        GameState valid = ReplacePending(state, [pass, use]);
        Assert.Empty(StateInvariantValidator.Collect(valid, catalog));

        GameState missing = ReplacePending(state, [pass]);
        Assert.Contains(
            StateInvariantValidator.Collect(missing, catalog),
            violation => violation.Code == "decision.missing-ability-action");

        LegalAction duplicate = new(new ActionId("action:materialize-extra"), ActionKind.UseAbility, "Use ability again", expected);
        GameState extra = ReplacePending(state, [pass, use, duplicate]);
        Assert.Contains(
            StateInvariantValidator.Collect(extra, catalog),
            violation => violation.Code == "decision.extra-ability-action");

        AbilityActionParameters mutated = new(
            expected.SourceCardId,
            expected.DefinitionId,
            expected.AbilityKey,
            expected.Timing,
            expected.Activation,
            [new BehaviorCost(BehaviorCostKind.Credits, 2)],
            expected.TargetCardIds,
            expected.Usage,
            expected.CatalogFingerprint,
            expected.DefinitionFingerprint,
            expected.SourceZoneId,
            expected.SourceZoneKind);
        GameState forged = ReplacePending(
            state,
            [pass, new LegalAction(use.Id, ActionKind.UseAbility, "Mutated ability", mutated)]);
        Assert.Contains(
            StateInvariantValidator.Collect(forged, catalog),
            violation => violation.Code == "decision.ability-action-payload-mismatch");

        BehaviorCatalog wrongCatalog = new BehaviorCatalogBuilder("test:materialize-wrong-catalog").Build();
        Assert.Contains(
            StateInvariantValidator.Collect(valid, wrongCatalog),
            violation => violation.Code == "decision.unknown-ability-definition");
    }

    [Fact]
    public void AbilityDecisionRoundTripsAtCurrentStateSchemaWithExactPayload()
    {
        BehaviorPaidAbility paid = Ability(
            "ability:roundtrip",
            BehaviorAbilityTiming.AnyPaidWindow,
            BehaviorAbilityActivation.PaidAbility,
            [new BehaviorCost(BehaviorCostKind.Credits)]);
        CardDefinitionId definition = new("test:materialize-roundtrip");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:materialize-roundtrip")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner, paidAbilities: [paid]))
            .Build();
        GameState state = BuildState(
            catalog,
            PlayerSide.Runner,
            TurnFrameStage.BeforeTurnPriority,
            [Source("card:materialize-roundtrip", definition, PlayerSide.Runner, ZoneKind.RunnerRig)]);
        AbilityActionParameters expected = Assert.Single(
            GameEngine.MaterializeAbilityActionParameters(state, catalog));
        GameState withDecision = ReplacePending(
            state,
            [
                new LegalAction(new ActionId("action:roundtrip-pass"), ActionKind.PassPriority, "Pass priority", new NoActionParameters()),
                new LegalAction(new ActionId("action:roundtrip-use"), ActionKind.UseAbility, "Use ability", expected),
            ]);

        string json = EngineJson.Serialize(withDecision);
        Assert.Contains($"\"schemaVersion\":{GameState.CurrentSchemaVersion}", json, StringComparison.Ordinal);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(GameState.CurrentSchemaVersion, restored.SchemaVersion);
        Assert.Equal(json, EngineJson.Serialize(restored));
        AbilityActionParameters actual = Assert.IsType<AbilityActionParameters>(
            restored.PendingDecision!.LegalActions.Single(action => action.Kind == ActionKind.UseAbility).Parameters);
        Assert.Equal(expected.SourceCardId, actual.SourceCardId);
        Assert.Equal(expected.DefinitionId, actual.DefinitionId);
        Assert.Equal(expected.AbilityKey, actual.AbilityKey);
        Assert.Equal(expected.Timing, actual.Timing);
        Assert.Equal(expected.Activation, actual.Activation);
        Assert.True(expected.Costs.SequenceEqual(actual.Costs));
        Assert.True(expected.TargetCardIds.SequenceEqual(actual.TargetCardIds));
        Assert.Equal(expected.Usage.Limit, actual.Usage.Limit);
        Assert.Equal(expected.Usage.UsesThisTurn, actual.Usage.UsesThisTurn);
        Assert.Equal(expected.CatalogFingerprint, actual.CatalogFingerprint);
        Assert.Equal(expected.DefinitionFingerprint, actual.DefinitionFingerprint);
        Assert.Equal(expected.SourceZoneId, actual.SourceZoneId);
        Assert.Equal(expected.SourceZoneKind, actual.SourceZoneKind);
    }

    private static BehaviorPaidAbility Ability(
        string key,
        BehaviorAbilityTiming timing,
        BehaviorAbilityActivation activation,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<BehaviorCondition>? conditions = null,
        IReadOnlyList<BehaviorTarget>? targets = null,
        BehaviorAbilityUsageLimit usageLimit = BehaviorAbilityUsageLimit.Unlimited) =>
        new(
            key,
            timing,
            activation,
            costs,
            conditions ?? [],
            targets ?? [],
            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
            usageLimit);

    private static SourceSpec Source(
        string cardId,
        CardDefinitionId definitionId,
        PlayerSide owner,
        ZoneKind zoneKind,
        ServerId? serverId = null,
        bool faceUp = true,
        bool rezzed = false) =>
        new(new CardInstanceId(cardId), definitionId, owner, zoneKind, serverId, faceUp, rezzed);

    private static GameState BuildState(
        BehaviorCatalog catalog,
        PlayerSide activeSide,
        TurnFrameStage stage,
        IReadOnlyList<SourceSpec> sources,
        IReadOnlyList<CardCounterState>? counters = null,
        IReadOnlyList<AbilityUseKey>? usedAbilityKeys = null,
        int turnNumber = 1,
        int runnerCredits = 5,
        int runnerClicks = 4,
        int runnerTags = 0,
        int corpCredits = 5,
        int corpClicks = 3)
    {
        GameState baseline = GameState.CreateNew(
            seed: 743,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        PlayerState corp = baseline.Players.Single(value => value.Side == PlayerSide.Corp);
        PlayerState runner = baseline.Players.Single(value => value.Side == PlayerSide.Runner);
        var players = new[]
        {
            new PlayerState(corp.Id, corp.Side, corpCredits, corpClicks, corp.AgendaPoints),
            new PlayerState(runner.Id, runner.Side, runnerCredits, runnerClicks, runner.AgendaPoints),
        };

        var resolved = sources.Select(source =>
        {
            ZoneState zone;
            if (source.ZoneKind == ZoneKind.ServerRoot)
            {
                ServerState server = source.ServerId is { } serverId
                    ? baseline.Servers.Single(value => value.Id == serverId)
                    : baseline.Servers.First(value => value.Kind is ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives);
                zone = baseline.GetZone(server.RootZoneId);
            }
            else if (source.ZoneKind == ZoneKind.ServerIce)
            {
                ServerState server = source.ServerId is { } serverId
                    ? baseline.Servers.Single(value => value.Id == serverId)
                    : baseline.Servers.First(value => value.Kind is ServerKind.Headquarters or ServerKind.RAndD or ServerKind.Archives);
                zone = baseline.GetZone(server.IceZoneId);
            }
            else
            {
                zone = baseline.GetZone(source.ZoneKind);
            }
            return (source, zone);
        }).ToArray();
        CardInstanceState[] cards = resolved.Select(item => new CardInstanceState(
            item.source.CardId,
            item.source.DefinitionId,
            item.zone.Id,
            item.source.Owner,
            item.source.FaceUp,
            item.source.Rezzed)).ToArray();
        ZoneState[] zones = baseline.Zones.Select(zone =>
        {
            (SourceSpec source, ZoneState zone)[] matching = resolved.Where(item => item.zone.Id == zone.Id).ToArray();
            return matching.Length == 0
                ? zone
                : new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Concat(matching.Select(item => item.source.CardId)).ToArray());
        }).ToArray();

        TurnFactsState facts = new(
            1,
            activeSide,
            turnNumber,
            activeSide == PlayerSide.Runner ? 4 : 3,
            0,
            true,
            activeSide == PlayerSide.Corp,
            false,
            false,
            usedAbilityKeys: usedAbilityKeys);
        TurnHistoryState history = activeSide == PlayerSide.Corp
            ? new TurnHistoryState(1, facts, null, null, null)
            : new TurnHistoryState(1, null, null, facts, null);
        TurnFrame frame = new(
            1,
            ResolutionBehaviorIds.Turn,
            activeSide,
            turnNumber,
            stage,
            stage == TurnFrameStage.AwaitingAction
                ? null
                : activeSide == PlayerSide.Runner
                    ? TurnPriorityWindowKind.PaidAndRez
                    : TurnPriorityWindowKind.PaidRezAndScore,
            stage == TurnFrameStage.AwaitingAction ? null : activeSide);
        TurnState turn = new(
            activeSide,
            activeSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
            turnNumber,
            frame.Step,
            history);
        RulesState defaults = RulesState.CreateDefault(players);
        PlayerRulesState[] rulePlayers = defaults.Players.Select(value => value.PlayerId == runner.Id
            ? new PlayerRulesState(value.PlayerId, value.BaseHandSize, value.CoreDamage, runnerTags, value.BaseMemory, value.UsedMemory)
            : value).ToArray();
        RulesState rules = new(
            defaults.Version,
            rulePlayers,
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
            cards,
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            decisionRevision: 0,
            eventSequence: baseline.EventSequence,
            resolutionStack: [frame],
            pendingDecision: null,
            actionSequence: baseline.ActionSequence,
            initialSeed: baseline.InitialSeed,
            runState: null,
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
    }

    private static GameState ReplacePending(GameState state, IReadOnlyList<LegalAction> actions)
    {
        PlayerId owner = state.Players.Single(value => value.Side == state.Turn.ActiveSide).Id;
        DecisionKind kind = state.ResolutionStack[^1] is TurnFrame { Stage: TurnFrameStage.AwaitingAction }
            ? DecisionKind.TurnAction
            : DecisionKind.TurnTiming;
        PendingDecision pending = new(
            new DecisionId("decision:materialize-pending"),
            1,
            owner,
            kind,
            "Choose an ability",
            actions);
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
            decisionRevision: 1,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: pending,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState,
            rulesState: state.RulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion,
            runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);
    }

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
        state.BehaviorCatalogFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private static GameState ReplaceStack(GameState state, IReadOnlyList<ResolutionFrame> resolutionStack) => new(
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
        resolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private sealed record SourceSpec(
        CardInstanceId CardId,
        CardDefinitionId DefinitionId,
        PlayerSide Owner,
        ZoneKind ZoneKind,
        ServerId? ServerId,
        bool FaceUp,
        bool Rezzed);
}
