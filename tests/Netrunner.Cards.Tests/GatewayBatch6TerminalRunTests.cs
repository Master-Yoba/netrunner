using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed partial class GatewayBatch4ReactionRuntimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("GatewayCard", "30051")]
    public void TerminalDrawOrDamageCleansActiveRunEventWithoutRunEndRewards(bool deckOut)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardDefinitionId ice = deckOut ? GatewayBatch5CardCodes.Ping : GatewayBatch5CardCodes.Diviner;
        CardDefinitionId corpIdentity = new(deckOut ? "30051" : "30077");
        RuntimeCardCatalog runtime = new("fixture:terminal-run-event",
            new CardRuntimeSpec(new("30076"), PlayerSide.Runner, RuntimeCardClass.Identity),
            new CardRuntimeSpec(corpIdentity, PlayerSide.Corp, RuntimeCardClass.Identity),
            new CardRuntimeSpec(ice, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2),
            new CardRuntimeSpec(GatewayCardCodes.Overclock, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 1),
            new CardRuntimeSpec(HqCostCardA, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 0));
        GameState initial = AddRunIdentity(BuildState(catalog, runtime, ice, sourceInIce: true, sourceRezzed: !deckOut),
            new("30076"), corpIdentity);
        StableIdAllocator allocator = new(initial.Allocator);
        CardInstanceState overclock = new(allocator.NextCardInstanceId(), GatewayCardCodes.Overclock,
            initial.GetZone(ZoneKind.RunnerGrip).Id, PlayerSide.Runner);
        CardInstanceState deckCard = new(allocator.NextCardInstanceId(), HqCostCardA,
            initial.GetZone(ZoneKind.CorpDeck).Id, PlayerSide.Corp);
        CardInstanceState[] cards = [.. initial.Cards, overclock, deckCard];
        initial = CopyRunFixture(initial, cards, initial.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind,
            zone.OwnerSide, zone.ServerId, cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToArray(), allocator.State);
        ServerId server = initial.Servers.Single(value => value.Kind == ServerKind.Remote).Id;
        GameState state = GameEngine.BeginTurn(initial, PlayerSide.Runner, catalog, runtime);
        bool played = false;
        List<EngineEvent> events = [];
        for (int step = 0; step < 100 && state.PendingDecision is { } decision && state.Outcome.Kind != GameOutcomeKind.Won; step++)
        {
            LegalAction action = decision.LegalActions.FirstOrDefault(action => action.Parameters is PlayCardParameters play
                    && play.CardId == overclock.Id && play.TargetServerId == server)
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.RezCard)
                ?? decision.LegalActions.FirstOrDefault(action => action.Parameters is BehaviorChoiceParameters choice && choice.OptionKeys.Contains("draw-two"))
                ?? RunContextPolicy(decision);
            played |= action.Kind == ActionKind.PlayCard;
            ApplyResult result = Apply(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            state = result.State;
        }
        Assert.True(played);
        Assert.Equal(GameOutcomeKind.Won, state.Outcome.Kind);
        Assert.Equal(deckOut ? GameWinReason.DeckOut : GameWinReason.Flatline, state.Outcome.Reason);
        Assert.Null(state.RunState);
        Assert.Empty(state.ResolutionStack);
        Assert.Contains(overclock.Id, state.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.All(state.Zones.Where(zone => zone.Kind is ZoneKind.CorpPlayArea or ZoneKind.RunnerPlayArea), zone => Assert.Empty(zone.CardIds));
        Assert.DoesNotContain(events, value => value.Kind == EngineEventKinds.RunEnded);
        Assert.Empty(StateInvariantValidator.Collect(state, catalog, runtime));
        Assert.Equal(EngineJson.Serialize(state), EngineJson.Serialize(EngineJson.DeserializeState(EngineJson.Serialize(state), catalog, runtime)));
    }
}
