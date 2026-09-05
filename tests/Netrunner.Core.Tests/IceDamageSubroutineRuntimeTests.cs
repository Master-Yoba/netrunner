using Xunit;

namespace Netrunner.Core.Tests;

public sealed partial class IceSubroutineRuntimeTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [Trait("GatewayCard", "30046")]
    public void DivinerUsesPrintedCostOfTheActualDamageVictim(int cost, bool endsRun)
    {
        BehaviorCatalog behavior = Catalog(TrashIce,
            new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.EndRunIfTrashedCostOdd));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(RunnerProgram, PlayerSide.Runner, RuntimeCardClass.Program, installCost: cost, memoryUnits: 1));
        GameState initial = BuildState(behavior, runtime, TrashIce, includeRulesState: true,
            placements: [(RunnerProgram, PlayerSide.Runner, ZoneKind.RunnerGrip)]);
        GameState result = PassTwice(ReachEncounterPaid(initial, behavior, runtime), behavior, runtime);
        Assert.Single(result.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Equal(endsRun, result.RunState is null);
        Assert.Empty(StateInvariantValidator.Collect(result, behavior, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30046")]
    public void DivinerBranchesOnRandomlySelectedVictimRatherThanGripOrder()
    {
        BehaviorCatalog behavior = Catalog(TrashIce,
            new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.EndRunIfTrashedCostOdd));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(RunnerProgram, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 1),
            new CardRuntimeSpec(RunnerHardware, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 2));
        GameState initial = BuildState(behavior, runtime, TrashIce, includeRulesState: true,
            placements: [(RunnerProgram, PlayerSide.Runner, ZoneKind.RunnerGrip), (RunnerHardware, PlayerSide.Runner, ZoneKind.RunnerGrip)]);
        GameState result = PassTwice(ReachEncounterPaid(initial, behavior, runtime), behavior, runtime);
        CardInstanceId victim = Assert.Single(result.GetZone(ZoneKind.RunnerHeap).CardIds);
        bool oddVictim = result.Cards.Single(card => card.Id == victim).DefinitionId == RunnerProgram;
        Assert.Equal(oddVictim, result.RunState is null);
        Assert.NotEqual(initial.RandomState, result.RandomState);
    }

    [Theory]
    [InlineData(IceSubroutineChoiceKind.JackOut)]
    [InlineData(IceSubroutineChoiceKind.ContinueRun)]
    [Trait("GatewayCard", "30047")]
    public void KarunaDamagesBeforeOfferingItsRunnerOwnedJackOut(IceSubroutineChoiceKind selected)
    {
        BehaviorCatalog behavior = Catalog(TrashIce,
            new DamageSubroutineSpec(DamageKind.Net, 2, DamageSubroutineFollowUp.OfferJackOut));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(RunnerProgram, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 1));
        GameState initial = BuildState(behavior, runtime, TrashIce, includeRulesState: true,
            placements: [(RunnerProgram, PlayerSide.Runner, ZoneKind.RunnerGrip), (RunnerProgram, PlayerSide.Runner, ZoneKind.RunnerGrip)]);
        GameState choice = ReachIceChoice(initial, behavior, runtime);
        Assert.Equal(2, choice.GetZone(ZoneKind.RunnerHeap).CardIds.Count);
        Assert.Equal(choice.Players.Single(player => player.Side == PlayerSide.Runner).Id, choice.PendingDecision!.Owner);
        Assert.Empty(StateInvariantValidator.Collect(choice, behavior, runtime));
        choice = EngineJson.DeserializeState(EngineJson.Serialize(choice), behavior, runtime);
        LegalAction action = Assert.Single(choice.PendingDecision!.LegalActions,
            value => value.Parameters is IceSubroutineChoiceParameters parameters && parameters.Choice == selected);
        ApplyResult result = Apply(choice, action, behavior, runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(selected == IceSubroutineChoiceKind.JackOut, result.State.RunState is null);
    }

    [Fact]
    [Trait("GatewayCard", "30046")]
    public void PreventedDivinerDamageRoundTripsItsCheckpointAndDoesNotEndTheRun()
    {
        BehaviorCatalog behavior = Catalog(TrashIce,
            new DamageSubroutineSpec(DamageKind.Net, 1, DamageSubroutineFollowUp.EndRunIfTrashedCostOdd));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(RunnerProgram, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 1, memoryUnits: 1));
        GameState initial = BuildState(behavior, runtime, TrashIce, includeRulesState: true,
            placements: [(RunnerProgram, PlayerSide.Runner, ZoneKind.RunnerGrip)]);
        RulesState rules = initial.RulesState!;
        PlayerId runner = initial.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        var interrupt = new InterruptRegistration(new InterruptId("fixture:prevent-ice-damage"), runner,
            InterruptKind.Prevention, false, new TriggerCondition(OccurrenceKind.Damage),
            "fixture.prevent-one", null, new CostBundle(), 1, new EffectBundle());
        var updatedRules = new RulesState(rules.Version, rules.Players, rules.Programs,
            rules.Counters, rules.LingeringEffects, rules.Triggers, [interrupt]);
        var json = System.Text.Json.Nodes.JsonNode.Parse(EngineJson.Serialize(initial))!;
        json["rulesState"] = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(updatedRules, EngineJson.Options));
        initial = EngineJson.DeserializeState(json.ToJsonString(), behavior, runtime);
        GameState prompt = PassTwice(ReachEncounterPaid(initial, behavior, runtime), behavior, runtime);
        Assert.Equal(DecisionKind.InterruptChoice, prompt.PendingDecision!.Kind);
        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(prompt), behavior, runtime);
        LegalAction prevent = Assert.Single(restored.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UsePrevention);
        ApplyResult result = Apply(restored, prevent, behavior, runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(RunTimingStep.MovementPaidWindow, result.State.RunState!.Step);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
    }

    [Fact]
    [Trait("GatewayCard", "30047")]
    public void FlatlineStopsBeforeTheJackOutInstruction()
    {
        BehaviorCatalog behavior = Catalog(TrashIce,
            new DamageSubroutineSpec(DamageKind.Net, 2, DamageSubroutineFollowUp.OfferJackOut));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0));
        GameState initial = BuildState(behavior, runtime, TrashIce, includeRulesState: true);
        GameState result = PassTwice(ReachEncounterPaid(initial, behavior, runtime), behavior, runtime);
        Assert.Equal(GameOutcomeKind.Won, result.Outcome.Kind);
        Assert.Null(result.PendingDecision);
    }

    [Fact]
    [Trait("GatewayCard", "30062")]
    public void BallistaAlwaysOffersEndRunAndFiltersTrashTargetsToPrograms()
    {
        BehaviorCatalog behavior = Catalog(TrashIce,
            new TrashInstalledRunnerCardSubroutineSpec(RuntimeCardClass.Program, endRunAlternative: true));
        RuntimeCardCatalog runtime = RuntimeCatalog(
            new CardRuntimeSpec(TrashIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 0),
            new CardRuntimeSpec(RunnerHardware, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 0));
        GameState initial = BuildState(behavior, runtime, TrashIce,
            placements: [(RunnerHardware, PlayerSide.Runner, ZoneKind.RunnerRig)]);
        GameState choice = ReachIceChoice(initial, behavior, runtime);
        LegalAction action = Assert.Single(choice.PendingDecision!.LegalActions);
        Assert.Equal(IceSubroutineChoiceKind.EndRun, Assert.IsType<IceSubroutineChoiceParameters>(action.Parameters).Choice);
        ApplyResult result = Apply(choice, action, behavior, runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.RunState);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
    }
}
