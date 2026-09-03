using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused M2 2A tests for the typed cost/effect transaction boundary. These
/// tests deliberately use synthetic cards; official card behavior is outside
/// this slice.
/// </summary>
public sealed class RulesEffectsTests
{
    [Fact]
    public void AggregateUnaffordableCostsRollBackExactly()
    {
        Fixture fixture = CreateFixture(71, withRules: true, runnerBaseMemory: 4, corpCredits: 5, corpClicks: 3);
        string before = EngineJson.Serialize(fixture.State);

        Assert.Throws<ResolutionFailureException>(() => GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(new CreditCost(fixture.Corp, 4), new CreditCost(fixture.Corp, 2)),
            new EffectBundle(new CreditAdjustmentEffect(fixture.Corp, 1))));

        Assert.Equal(before, EngineJson.Serialize(fixture.State));
        Assert.Equal(5, fixture.State.GetPlayer(fixture.Corp).Credits);
        Assert.Equal(3, fixture.State.GetPlayer(fixture.Corp).Clicks);
    }

    [Fact]
    public void CostsAndEffectsAreAppliedInTypedDeterministicOrder()
    {
        Fixture fixture = CreateFixture(72, withRules: true, runnerBaseMemory: 4, corpCredits: 5, corpClicks: 3);

        GameState result = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(new CreditCost(fixture.Corp, 2), new ClickCost(fixture.Corp, 1)),
            new EffectBundle(
                new CreditAdjustmentEffect(fixture.Corp, 3),
                new ClickAdjustmentEffect(fixture.Corp, 1),
                new CreditAdjustmentEffect(fixture.Corp, -1)));

        PlayerState corp = result.GetPlayer(fixture.Corp);
        Assert.Equal(5, corp.Credits);
        Assert.Equal(3, corp.Clicks);
        Assert.Equal(2, result.RulesState!.Players.Count);
    }

    [Fact]
    public void DamageUsesRestorableRandomStateAndDeepSaveResume()
    {
        Fixture fixture = CreateFixture(
            73,
            withRules: true,
            new Placement("grip-a", PlayerSide.Runner, ZoneKind.RunnerGrip),
            new Placement("grip-b", PlayerSide.Runner, ZoneKind.RunnerGrip),
            new Placement("grip-c", PlayerSide.Runner, ZoneKind.RunnerGrip),
            new Placement("grip-d", PlayerSide.Runner, ZoneKind.RunnerGrip));
        EffectBundle damage = new(new DamageEffect(fixture.Runner, DamageKind.Net, 2));

        GameState first = GameEngine.ResolveEffects(fixture.State, fixture.Corp, new CostBundle(), damage);
        string saved = EngineJson.Serialize(fixture.State);
        GameState restoredInput = EngineJson.DeserializeState(saved);
        GameState second = GameEngine.ResolveEffects(restoredInput, fixture.Corp, new CostBundle(), damage);

        Assert.Equal(2, first.GetZone(ZoneKind.RunnerHeap).CardIds.Count);
        Assert.Equal(2, first.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.All(first.GetZone(ZoneKind.RunnerHeap).CardIds, id => Assert.True(first.GetCard(id).FaceUp));
        foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
        {
            string observation = EngineJson.Serialize(ObservationBuilder.Build(first, viewer));
            Assert.Contains("test:grip-", observation, StringComparison.Ordinal);
        }
        Assert.Equal(EngineJson.Serialize(first), EngineJson.Serialize(second));
        Assert.Equal(DeterministicHash.ForState(first), DeterministicHash.ForState(second));
    }

    [Fact]
    public void DamageGreaterThanGripFlatlinesRunnerAndClearsResolution()
    {
        Fixture fixture = CreateFixture(
            74,
            withRules: true,
            new Placement("only-card", PlayerSide.Runner, ZoneKind.RunnerGrip));

        GameState result = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(fixture.Runner, DamageKind.Meat, 2)));

        Assert.Equal(GameOutcomeKind.Won, result.Outcome.Kind);
        Assert.Equal(PlayerSide.Corp, result.Outcome.Winner);
        Assert.Equal(GameWinReason.Flatline, result.Outcome.Reason);
        Assert.Equal(TurnPhase.GameOver, result.Turn.Phase);
        Assert.Empty(result.ResolutionStack);
        Assert.Null(result.PendingDecision);
    }

    [Fact]
    public void CoreDamageChangesMaximumHandSizeAndTrashesCards()
    {
        Fixture fixture = CreateFixture(
            75,
            withRules: true,
            new Placement("grip-a", PlayerSide.Runner, ZoneKind.RunnerGrip));

        GameState result = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(fixture.Runner, DamageKind.Core, 1)));

        PlayerRulesState runner = result.RulesState!.Players.Single(value => value.PlayerId == fixture.Runner);
        Assert.Equal(1, runner.CoreDamage);
        Assert.Equal(runner.BaseHandSize - runner.CoreDamage, runner.MaximumHandSize);
        Assert.Single(result.GetZone(ZoneKind.RunnerHeap).CardIds);
    }

    [Fact]
    public void TagUnderflowRollsBackEarlierEffects()
    {
        Fixture fixture = CreateFixture(76, withRules: true);
        string before = EngineJson.Serialize(fixture.State);

        Assert.Throws<ResolutionFailureException>(() => GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(
                new TagAdjustmentEffect(fixture.Runner, 2),
                new TagAdjustmentEffect(fixture.Runner, -3))));

        Assert.Equal(before, EngineJson.Serialize(fixture.State));
    }

    [Fact]
    public void MemoryOverageYieldsTypedCleanupAndActuallyMovesPrograms()
    {
        Fixture fixture = CreateFixture(
            77,
            withRules: true,
            runnerBaseMemory: 6,
            corpCredits: 5,
            corpClicks: 3,
            new Placement("program-a", PlayerSide.Runner, ZoneKind.RunnerRig, MemoryCost: 3),
            new Placement("program-b", PlayerSide.Runner, ZoneKind.RunnerRig, MemoryCost: 3));

        GameState prompt = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new MemoryAdjustmentEffect(fixture.Runner, -5)));
        PendingDecision firstDecision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.MemoryCleanup, firstDecision.Kind);
        Assert.Equal(2, firstDecision.LegalActions.Count);
        Assert.Equal(2, prompt.ResolutionStack.Count);

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json);
        Assert.Equal(json, EngineJson.Serialize(restored));

        LegalAction firstAction = firstDecision.LegalActions[0];
        ApplyResult first = GameEngine.Apply(
            prompt,
            new ActionSubmission(firstDecision.Owner, firstDecision.Id, firstDecision.Revision, firstAction.Id));
        Assert.True(first.Accepted, first.Rejection?.Message);
        Assert.NotNull(first.State.PendingDecision);
        Assert.Equal(DecisionKind.MemoryCleanup, first.State.PendingDecision!.Kind);
        Assert.Single(first.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Single(first.State.RulesState!.Programs);

        PendingDecision restoredDecision = restored.PendingDecision!;
        ApplyResult restoredFirst = GameEngine.Apply(
            restored,
            new ActionSubmission(restoredDecision.Owner, restoredDecision.Id, restoredDecision.Revision, restoredDecision.LegalActions[0].Id));
        Assert.True(restoredFirst.Accepted, restoredFirst.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(first.State), EngineJson.Serialize(restoredFirst.State));

        PendingDecision secondDecision = first.State.PendingDecision!;
        LegalAction secondAction = secondDecision.LegalActions[0];
        ApplyResult completed = GameEngine.Apply(
            first.State,
            new ActionSubmission(secondDecision.Owner, secondDecision.Id, secondDecision.Revision, secondAction.Id));
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Null(completed.State.PendingDecision);
        Assert.Empty(completed.State.ResolutionStack);
        Assert.Equal(2, completed.State.GetZone(ZoneKind.RunnerHeap).CardIds.Count);
        Assert.Empty(completed.State.RulesState!.Programs);
        Assert.Equal(0, completed.State.RulesState.Players.Single(value => value.PlayerId == fixture.Runner).UsedMemory);
    }

    [Fact]
    public void MoveCardEffectUpdatesCardAndBothOrderedZonesAtomically()
    {
        Fixture fixture = CreateFixture(
            78,
            withRules: true,
            new Placement("movable", PlayerSide.Runner, ZoneKind.RunnerGrip));
        CardInstanceId cardId = fixture.Cards["movable"];

        GameState result = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new MoveCardEffect(cardId, fixture.State.GetZone(ZoneKind.RunnerHeap).Id)));

        Assert.Equal(result.GetZone(ZoneKind.RunnerHeap).Id, result.GetCard(cardId).ZoneId);
        Assert.DoesNotContain(cardId, result.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Contains(cardId, result.GetZone(ZoneKind.RunnerHeap).CardIds);

        string before = EngineJson.Serialize(fixture.State);
        Assert.Throws<ResolutionFailureException>(() => GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new MoveCardEffect(cardId, new ZoneId("zone:missing")))));
        Assert.Equal(before, EngineJson.Serialize(fixture.State));
    }

    [Fact]
    public void CountersSupportAddRemoveUnderflowAndGlobalVirusPurge()
    {
        Fixture fixture = CreateFixture(
            79,
            withRules: true,
            new Placement("counter-card", PlayerSide.Runner, ZoneKind.RunnerHeap),
            new Placement("second-counter-card", PlayerSide.Runner, ZoneKind.RunnerHeap));
        CardInstanceId cardId = fixture.Cards["counter-card"];
        CardInstanceId secondCardId = fixture.Cards["second-counter-card"];

        GameState withCounters = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(
                new CardCounterAdjustmentEffect(cardId, "virus", 2),
                new CardCounterAdjustmentEffect(secondCardId, "virus", 3),
                new CardCounterAdjustmentEffect(cardId, "power", 1)));
        Assert.Equal(2, withCounters.RulesState!.Counters.Single(value => value.CardId == cardId && value.CounterName == "virus").Count);
        Assert.Equal(3, withCounters.RulesState.Counters.Single(value => value.CardId == secondCardId && value.CounterName == "virus").Count);
        Assert.Single(withCounters.RulesState.Counters, value => value.CounterName == "power");

        RulesState counterRules = withCounters.RulesState;
        GameState withHostedCredit = Rebuild(
            withCounters,
            new RulesState(
                counterRules.Version,
                counterRules.Players,
                counterRules.Programs,
                counterRules.Counters
                    .Append(CardCounterState.CreateHostedCredit(
                        cardId,
                        4,
                        HostedCreditSpendRestriction.AnyCost))
                    .ToArray(),
                counterRules.LingeringEffects,
                counterRules.Triggers,
                counterRules.Interrupts));
        StateInvariantValidator.Validate(withHostedCredit);
        GameState purged = GameEngine.ResolveEffects(
            withHostedCredit,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new VirusPurgeEffect()));
        Assert.DoesNotContain(purged.RulesState!.Counters, value => value.CounterName == "virus");
        Assert.Contains(purged.RulesState.Counters, value => value.CounterName == "power");
        CardCounterState hostedCredit = Assert.Single(
            purged.RulesState.Counters,
            value => value.Purpose == CardCounterPurpose.HostedCredit);
        Assert.Equal(4, hostedCredit.Count);

        string before = EngineJson.Serialize(withCounters);
        Assert.Throws<ResolutionFailureException>(() => GameEngine.ResolveEffects(
            withCounters,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new CardCounterAdjustmentEffect(cardId, "power", -2))));
        Assert.Equal(before, EngineJson.Serialize(withCounters));
    }

    [Fact]
    public void LingeringEffectsHaveExplicitExpiryAndCleanup()
    {
        Fixture fixture = CreateFixture(80, withRules: true);
        var encounter = new GameLingeringEffect(
            new RunEffectId("effect:encounter"),
            "fixture.encounter-effect",
            null,
            LingeringEffectExpiry.Encounter);
        var turn = new GameLingeringEffect(
            new RunEffectId("effect:turn"),
            "fixture.turn-effect",
            null,
            LingeringEffectExpiry.Turn);

        GameState withEffects = GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(),
            new EffectBundle(new AddLingeringEffect(encounter), new AddLingeringEffect(turn)));
        Assert.Equal(2, withEffects.RulesState!.LingeringEffects.Count);

        GameState afterEncounter = GameEngine.CleanupLingeringEffects(
            withEffects,
            fixture.Corp,
            LingeringEffectExpiry.Encounter);
        Assert.Single(afterEncounter.RulesState!.LingeringEffects);
        Assert.Equal(LingeringEffectExpiry.Turn, afterEncounter.RulesState.LingeringEffects[0].Expiry);
        GameState afterTurn = GameEngine.CleanupLingeringEffects(afterEncounter, fixture.Corp, LingeringEffectExpiry.Turn);
        Assert.Empty(afterTurn.RulesState!.LingeringEffects);
    }

    [Fact]
    public void RulesStateAndEffectsAreStrictAndDeeplyDetached()
    {
        Fixture fixture = CreateFixture(
            81,
            withRules: true,
            new Placement("program", PlayerSide.Runner, ZoneKind.RunnerRig, MemoryCost: 2));
        RulesState sourceRules = fixture.State.RulesState!;
        string rulesJson = EngineJson.Serialize(sourceRules);
        RulesState loadedRules = EngineJson.Deserialize<RulesState>(rulesJson);
        Assert.Equal(rulesJson, EngineJson.Serialize(loadedRules));
        Assert.NotSame(sourceRules, loadedRules);
        Assert.NotSame(sourceRules.Players[0], loadedRules.Players[0]);
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<PlayerRulesState>>(loadedRules.Players).Clear());

        JsonObject rulesObject = JsonNode.Parse(rulesJson)!.AsObject();
        rulesObject["programs"]!.AsArray()[0]!.AsObject().Remove("memoryCost");
        string missingMemoryCost = rulesObject.ToJsonString(EngineJson.Options);
        Assert.NotEqual(rulesJson, missingMemoryCost);
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<RulesState>(missingMemoryCost));

        string unknownEffect = EngineJson.Serialize(new EffectBundle(new MoveCardEffect(
            fixture.Cards["program"],
            fixture.State.GetZone(ZoneKind.RunnerHeap).Id)));
        Assert.Throws<JsonException>(() => EngineJson.Deserialize<EffectBundle>(
            unknownEffect.Replace("\"$type\":\"move-card\"", "\"$type\":\"future-effect\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void InvalidRulesStateIsActionableAndM1StateRemainsUnchanged()
    {
        Fixture fixture = CreateFixture(82, withRules: true);
        PlayerRulesState runner = fixture.State.RulesState!.Players.Single(value => value.PlayerId == fixture.Runner);
        RulesState invalidRules = new(
            1,
            fixture.State.RulesState.Players.Select(value => value.PlayerId == fixture.Runner
                ? new PlayerRulesState(value.PlayerId, value.BaseHandSize, value.CoreDamage, value.Tags, value.BaseMemory, value.UsedMemory + 1)
                : value).ToArray(),
            fixture.State.RulesState.Programs,
            fixture.State.RulesState.Counters,
            fixture.State.RulesState.LingeringEffects);
        GameState invalid = Rebuild(fixture.State, rulesState: invalidRules);
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(invalid);
        Assert.Contains(violations, value => value.Code == "rules.used-memory-mismatch");

        string before = EngineJson.Serialize(fixture.State);
        Assert.Throws<ResolutionFailureException>(() => GameEngine.ResolveEffects(
            fixture.State,
            fixture.Corp,
            new CostBundle(new CreditCost(fixture.Corp, 100)),
            new EffectBundle()));
        Assert.Equal(before, EngineJson.Serialize(fixture.State));
        Assert.Equal(0, runner.UsedMemory);
    }

    [Fact]
    public void OverMemoryRequiresCleanupFrameAndFlatlineRequiresCorpWinner()
    {
        Fixture fixture = CreateFixture(
            83,
            withRules: true,
            runnerBaseMemory: 6,
            corpCredits: 5,
            corpClicks: 3,
            new Placement("program", PlayerSide.Runner, ZoneKind.RunnerRig, MemoryCost: 3));
        RulesState rules = fixture.State.RulesState!;
        RulesState overMemory = new(
            rules.Version,
            rules.Players.Select(player => player.PlayerId == fixture.Runner
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    baseMemory: 2,
                    player.UsedMemory)
                : player).ToArray(),
            rules.Programs,
            rules.Counters,
            rules.LingeringEffects);
        GameState illegalMemory = Rebuild(fixture.State, rulesState: overMemory);
        Assert.Contains(
            StateInvariantValidator.Collect(illegalMemory),
            violation => violation.Code == "rules.memory-over-limit-without-cleanup");

        GameState illegalFlatline = Rebuild(
            fixture.State,
            outcome: new GameOutcome(GameOutcomeKind.Won, PlayerSide.Runner, GameWinReason.Flatline),
            turn: new TurnState(PlayerSide.Runner, TurnPhase.GameOver, fixture.State.Turn.TurnNumber));
        Assert.Contains(
            StateInvariantValidator.Collect(illegalFlatline),
            violation => violation.Code == "outcome.flatline-winner");
    }

    private static Fixture CreateFixture(
        ulong seed,
        bool withRules,
        params Placement[] placements) =>
        CreateFixture(seed, withRules, runnerBaseMemory: 4, corpCredits: 5, corpClicks: 3, placements);

    private static Fixture CreateFixture(
        ulong seed,
        bool withRules,
        int runnerBaseMemory,
        int corpCredits,
        int corpClicks,
        params Placement[] placements)
    {
        GameState baseline = GameState.CreateNew(seed);
        PlayerId corpId = baseline.Players.Single(value => value.Side == PlayerSide.Corp).Id;
        PlayerId runnerId = baseline.Players.Single(value => value.Side == PlayerSide.Runner).Id;
        var players = new[]
        {
            new PlayerState(corpId, PlayerSide.Corp, corpCredits, corpClicks, 0),
            new PlayerState(runnerId, PlayerSide.Runner, 5, 4, 0),
        };
        var allocator = new StableIdAllocator(baseline.Allocator);
        var cards = new List<CardInstanceState>();
        var zoneCards = baseline.Zones.ToDictionary(zone => zone.Id, zone => zone.CardIds.ToList());
        var cardIds = new Dictionary<string, CardInstanceId>(StringComparer.Ordinal);
        foreach (Placement placement in placements)
        {
            ZoneState zone = baseline.Zones.Single(value => value.Kind == placement.Zone && value.ServerId is null);
            CardInstanceId cardId = allocator.NextCardInstanceId();
            if (!cardIds.TryAdd(placement.Name, cardId))
            {
                throw new ArgumentException($"Duplicate fixture card name '{placement.Name}'.", nameof(placements));
            }

            zoneCards[zone.Id].Add(cardId);
            cards.Add(new CardInstanceState(
                cardId,
                new CardDefinitionId($"test:{placement.Name}"),
                zone.Id,
                placement.Side,
                faceUp: placement.Zone == ZoneKind.RunnerHeap));
        }

        ZoneState[] zones = baseline.Zones.Select(zone => new ZoneState(
            zone.Id,
            zone.Kind,
            zone.OwnerSide,
            zone.ServerId,
            zoneCards[zone.Id])).ToArray();
        RulesState? rules = null;
        if (withRules)
        {
            RulesState defaults = RulesState.CreateDefault(players);
            PlayerRulesState[] rulesPlayers = defaults.Players.Select(player =>
                player.PlayerId == runnerId
                    ? new PlayerRulesState(player.PlayerId, player.BaseHandSize, player.CoreDamage, player.Tags, runnerBaseMemory, 0)
                    : player).ToArray();
            ProgramMemoryState[] programs = placements
                .Where(placement => placement.Side == PlayerSide.Runner && placement.Zone == ZoneKind.RunnerRig)
                .Select(placement => new ProgramMemoryState(cardIds[placement.Name], runnerId, placement.MemoryCost))
                .ToArray();
            int usedMemory = programs.Where(program => program.Owner == runnerId).Sum(program => program.MemoryCost);
            rulesPlayers = rulesPlayers.Select(player => player.PlayerId == runnerId
                ? new PlayerRulesState(player.PlayerId, player.BaseHandSize, player.CoreDamage, player.Tags, player.BaseMemory, usedMemory)
                : player).ToArray();
            rules = new RulesState(defaults.Version, rulesPlayers, programs, [], []);
        }

        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            rules);
        StateInvariantValidator.Validate(state);
        return new Fixture(state, corpId, runnerId, cardIds);
    }

    private static GameState Rebuild(
        GameState state,
        RulesState? rulesState = null,
        GameOutcome? outcome = null,
        TurnState? turn = null) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        turn ?? state.Turn,
        outcome ?? state.Outcome,
        state.RandomState,
        state.Allocator,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        rulesState ?? state.RulesState);

    private sealed record Fixture(
        GameState State,
        PlayerId Corp,
        PlayerId Runner,
        IReadOnlyDictionary<string, CardInstanceId> Cards);

    private sealed record Placement(
        string Name,
        PlayerSide Side,
        ZoneKind Zone,
        int MemoryCost = 0);
}
