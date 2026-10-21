using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed partial class GatewayBatch4ReactionRuntimeTests
{
    [Fact]
    [Trait("GatewayCard", "30001")]
    public void LoupActualAccessTrashGainsCreditAndDrawsBeforeRunEnds()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardDefinitionId identity = new("30001");
        CardDefinitionId drawCard = new("fixture:loup-draw");
        RuntimeCardCatalog runtime = new("fixture:loup-runtime",
            new CardRuntimeSpec(identity, PlayerSide.Runner, RuntimeCardClass.Identity),
            new CardRuntimeSpec(new("30077"), PlayerSide.Corp, RuntimeCardClass.Identity),
            new CardRuntimeSpec(drawCard, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0),
            new CardRuntimeSpec(GatewayBatch5CardCodes.AmazeAmusements, PlayerSide.Corp, RuntimeCardClass.Upgrade, rezCost: 1, trashCost: 3));
        GameState initial = AddRunIdentity(BuildState(catalog, runtime, GatewayBatch5CardCodes.AmazeAmusements), identity);
        StableIdAllocator allocator = new(initial.Allocator);
        ZoneState stack = initial.GetZone(ZoneKind.RunnerStack);
        CardInstanceState draw = new(allocator.NextCardInstanceId(), drawCard, stack.Id, PlayerSide.Runner);
        initial = CopyRunFixture(initial, [.. initial.Cards, draw], initial.Zones.Select(zone => zone.Id == stack.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [draw.Id]) : zone).ToArray(), allocator.State);
        GameState state = BeginRun(initial, catalog, runtime, ServerKind.Remote);
        bool trashed = false;
        for (int step = 0; step < 100 && state.PendingDecision is { } decision; step++)
        {
            LegalAction action = decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.TrashAccessedCard)
                ?? RunContextPolicy(decision);
            ApplyResult result = Apply(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
            if (action.Kind == ActionKind.TrashAccessedCard)
            {
                trashed = true;
                Assert.Contains(draw.Id, state.GetZone(ZoneKind.RunnerGrip).CardIds);
                Assert.Equal(8, state.GetPlayerBySide(PlayerSide.Runner).Credits);
                Assert.Empty(StateInvariantValidator.Collect(state, catalog, runtime));
            }
        }
        Assert.True(trashed);
        Assert.Single(state.GetZone(ZoneKind.RunnerGrip).CardIds);
    }
}
