using Xunit;

namespace Netrunner.Core.Tests;

public sealed partial class CheckpointTests
{
    [Fact]
    public void CompletedDamageIsNotPresentedAsPendingDuringItsOptionalReaction()
    {
        GameState initial = AddRunnerGripCard(GameState.CreateNew(460404), "fixture:damage-observation-grip");
        PlayerId corp = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = initial.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        initial = WithRules(initial, triggers:
        [
            new TriggerRegistration(new TriggerId("fixture:after-damage"), corp, TriggerMode.Optional,
                new TriggerCondition(OccurrenceKind.Damage), "fixture.damage-reaction", null,
                new CostBundle(), new EffectBundle(new CreditAdjustmentEffect(corp, 1))),
        ]);
        GameState prompt = GameEngine.ResolveEffects(initial, corp, new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 1)));
        Assert.Equal(DecisionKind.TriggerChoice, prompt.PendingDecision!.Kind);
        Assert.Empty(prompt.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Single(prompt.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.False(ObservationBuilder.Build(prompt, PlayerSide.Runner).PublicState.ContainsKey("pendingDamage"));
    }
}
