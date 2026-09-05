using Xunit;

namespace Netrunner.Core.Tests;

public sealed class BehaviorDamageRuntimeTests
{
    private static readonly CardDefinitionId DefinitionId = new("test:damage-behavior");
    private static readonly CardInstanceId SourceId = new("card:damage-source");

    [Theory]
    [InlineData(DamageKind.Net, 0, 4, false)]
    [InlineData(DamageKind.Net, 2, 2, false)]
    [InlineData(DamageKind.Meat, 5, 0, true)]
    public void DamageUsesDeterministicGripTrashAndFlatline(
        DamageKind kind, int amount, int remaining, bool flatlined)
    {
        var catalog = new BehaviorCatalogBuilder("test:damage")
            .Add(new CardBehaviorDefinition(DefinitionId, PlayerSide.Runner,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount, damageKind: kind)]))
            .Build();
        GameState state = CreateState(catalog);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState result = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(SourceId, runner), catalog);
        GameState replayed = GameEngine.BeginBehaviorForTesting(state, new BehaviorInvocationContext(SourceId, runner), catalog);
        Assert.Equal(remaining, result.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(flatlined, result.Outcome.Kind == GameOutcomeKind.Won);
        Assert.Equal(EngineJson.Serialize(result), EngineJson.Serialize(replayed));
        Assert.Empty(StateInvariantValidator.Collect(result, catalog));
    }

    [Fact]
    public void SelfTrashSnapshotsAdvancementBeforeDamagePreventionAndRoundTrip()
    {
        var catalog = new BehaviorCatalogBuilder("test:damage-snapshot")
            .Add(new CardBehaviorDefinition(DefinitionId, PlayerSide.Runner,
                triggers:
                [
                    new BehaviorTrigger("damage", OccurrenceKind.CardInstalled, TriggerMode.Optional,
                        BehaviorTriggerScope.Self, null,
                        [new BehaviorInstruction(BehaviorInstructionKind.DealDamage,
                            damageKind: DamageKind.Meat,
                            damageAmountOperand: new BehaviorDamageAmountOperand(
                                BehaviorDamageAmountKind.SourceAdvancementCounters, trashSourceAsCost: true))]),
                ]))
            .Build();
        GameState state = CreateState(catalog, advancement: 3, prevention: true);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginGameplayOccurrenceForTesting(state,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, SourceId), catalog);
        ApplyResult used = Choose(prompt, catalog, ActionKind.UseTrigger);
        Assert.True(used.Accepted, used.Rejection?.Message);
        GameState damagePrompt = used.State;
        Assert.Equal(DecisionKind.InterruptChoice, damagePrompt.PendingDecision?.Kind);
        Assert.Contains(SourceId, damagePrompt.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Equal(0, damagePrompt.GetCard(SourceId).AdvancementCounters);
        Assert.DoesNotContain(damagePrompt.RulesState!.Counters, counter => counter.CardId == SourceId);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(damagePrompt.ResolutionStack[^1]);
        Assert.Equal(3, Assert.IsType<DamageEffect>(checkpoint.CurrentEffect).Amount);
        string json = EngineJson.Serialize(damagePrompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        ApplyResult prevented = Choose(restored, catalog, ActionKind.UsePrevention);
        Assert.True(prevented.Accepted, prevented.Rejection?.Message);
        GameState final = prevented.State;
        while (final.PendingDecision is not null)
        {
            ApplyResult declined = Choose(final, catalog, ActionKind.DeclinePrevention);
            Assert.True(declined.Accepted, declined.Rejection?.Message);
            final = declined.State;
        }
        Assert.Equal(2, final.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Empty(StateInvariantValidator.Collect(final, catalog));
    }

    private static ApplyResult Choose(GameState state, BehaviorCatalog catalog, ActionKind kind)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction action = Assert.Single(decision.LegalActions, action => action.Kind == kind);
        return GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id), catalog);
    }

    private static GameState CreateState(BehaviorCatalog catalog, int advancement = 0, bool prevention = false)
    {
        GameState baseline = GameState.CreateNew(987,
            behaviorRegistryVersion: catalog.Version, behaviorCatalogFingerprint: catalog.Fingerprint);
        PlayerId runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState[] cards =
        [
            new(SourceId, DefinitionId, rig.Id, PlayerSide.Runner, faceUp: true, advancementCounters: advancement),
            .. Enumerable.Range(0, 4).Select(index => new CardInstanceState(new CardInstanceId($"card:grip-{index}"),
                new CardDefinitionId("test:grip"), grip.Id, PlayerSide.Runner)),
        ];
        var zones = baseline.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToArray();
        RulesState defaults = RulesState.CreateDefault(baseline.Players);
        RulesState rules = new(defaults.Version, defaults.Players, defaults.Programs,
            advancement == 0 ? [] : [new CardCounterState(SourceId, "advancement", advancement)],
            defaults.LingeringEffects, defaults.Triggers,
            prevention ? [new InterruptRegistration(new InterruptId("interrupt:prevent-one"), runner,
                InterruptKind.Prevention, false, new TriggerCondition(OccurrenceKind.Damage), "fixture.prevent-one", null,
                new CostBundle(), 1, new EffectBundle())] : []);
        return new GameState(gameId: baseline.GameId, rulesVersion: baseline.RulesVersion,
            cardDataVersion: baseline.CardDataVersion, players: baseline.Players, cards: cards, zones: zones,
            servers: baseline.Servers, turn: baseline.Turn, outcome: baseline.Outcome, randomState: baseline.RandomState,
            allocator: baseline.Allocator, schemaVersion: baseline.SchemaVersion, initialSeed: baseline.InitialSeed,
            rulesState: rules, behaviorRegistryVersion: catalog.Version, behaviorCatalogFingerprint: catalog.Fingerprint);
    }
}
