using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationHarnessTests
{
    [Fact]
    public void TheTestRuntimeCompilesBothPacksAndTheBoardOpensAnActionPhase()
    {
        // 159 cards, 66 Vantage Point cards, 8 fillers and VP.2's 24 probe cards.
        Assert.Equal(159 + 66 + 8 + 24 + 17 + 11 + 7, Runtime.DefinitionIds.Count);
        GameState state = new ElevationBoard(PlayerSide.Corp).Add("30075", Place.Hq).Begin();

        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision!.Kind);
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, "30075")).State;
        state = ToAction(state);
        Assert.Equal(5 - 5 + 9, Player(state, PlayerSide.Corp).Credits);
        RoundTrip(state);
    }
}
