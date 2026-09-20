using Xunit;

namespace Netrunner.Core.Tests;

public sealed partial class CheckpointTests
{
    [Theory]
    [InlineData(PlayerSide.Runner, true)]
    [InlineData(PlayerSide.Corp, false)]
    public void ActionSourcesRequireBothDecisionOwnershipAndSourceVisibility(PlayerSide controller, bool visible)
    {
        GameState baseline = AddRunnerGripCard(GameState.CreateNew(217), "card:action-source");
        PlayerId owner = baseline.Players.Single(player => player.Side == controller).Id;
        GameState state = WithRules(baseline, triggers:
        [
            new TriggerRegistration(new("trigger:action-source"), owner, TriggerMode.Optional,
                new TriggerCondition(OccurrenceKind.CreditAdjustment), "fixture.action-source",
                new("card:action-source"), new CostBundle(), new EffectBundle(new CreditAdjustmentEffect(owner, 2))),
        ]);
        GameState prompt = GameEngine.ResolveEffects(state, owner, new CostBundle(), new EffectBundle(new CreditAdjustmentEffect(owner, 1)));
        string hash = DeterministicHash.ForState(prompt);
        Observation observation = ObservationBuilder.Build(prompt, controller);
        Assert.Equal(visible ? 2 : 0, observation.ActionSources.Count);
        Assert.All(observation.ActionSources, source =>
        {
            Assert.Equal("card:action-source", source.CardId.Value);
            Assert.Contains(observation.LegalActions, action => action.Id == source.ActionId);
        });
        PlayerSide opponent = controller == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        Assert.Empty(ObservationBuilder.Build(prompt, opponent).ActionSources);
        Assert.Equal(hash, DeterministicHash.ForState(prompt));
        Assert.Equal(EngineJson.Serialize(observation with { ActionSources = [] }), EngineJson.Serialize(observation));
    }
}
