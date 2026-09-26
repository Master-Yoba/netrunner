using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class PolicyTriggerObservationTests
{
    [OfficialGatewayTheory]
    [InlineData("corp-choices-03")]
    [InlineData("corp-choices-04")]
    public void IssuedTriggerContextIsTypedOwnerOnlyAndExpiresWithTheDecision(string id)
    {
        TacticalFixture fixture = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded()).Build(id);
        GameState state = fixture.Initial;
        Observation owned = ObservationBuilder.Build(state, PlayerSide.Corp, fixture.Board.Behavior);
        AgentFeatures features = ObservationReader.Read(owned);
        Assert.NotEmpty(features.PrivateCards.Triggers);
        Assert.All(features.PrivateCards.Triggers, trigger =>
        {
            Assert.Contains(features.Actions, action => action.Parameters is TriggerChoiceParameters choice && choice.TriggerId.Value == trigger.Id
                || action.Parameters is TriggerOrderParameters order && order.TriggerId.Value == trigger.Id);
            Assert.NotNull(ObservationBuilder.ObserveCard(state, PlayerSide.Corp, new(trigger.Source.Id)));
        });
        Observation restored = EngineJson.Deserialize<Observation>(EngineJson.Serialize(owned)) with { Decision = owned.Decision };
        Assert.Equal(ObservationReader.Digest(features), ObservationReader.Digest(ObservationReader.Read(restored)));
        Assert.DoesNotContain("triggers", ObservationBuilder.Build(state, PlayerSide.Runner, fixture.Board.Behavior).PrivateState.Keys);
        if (id == "corp-choices-03")
        {
            Assert.Equal("30070", Assert.Single(features.PrivateCards.Triggers).Source.DefinitionId);
            GameState declined = fixture.Board.Apply(state, state.PendingDecision!.LegalActions.Single(action => action.Kind == ActionKind.DeclineTrigger)).State;
            Assert.DoesNotContain("triggers", ObservationBuilder.Build(declined, PlayerSide.Corp, fixture.Board.Behavior).PrivateState.Keys);
        }
    }
}
