using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Runner v4 repairs, checked at positions of the 27 September 2026 Corp-seat
/// playtest (official content only), where Runner v2 spent turn after turn
/// topping up Smartware Distributor instead of building its rig.
/// </summary>
public sealed class RunnerPlaytestRepairTests
{
    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    [Fact]
    public async Task V4StopsToppingUpAStockedSmartwareDistributor()
    {
        if (!Official) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("expanded-expanded", 189389632887970245);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-runner-smartware-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        int stockedTopUps = 0, v4TopUps = 0;
        foreach (ReplayAction action in replay.Actions)
        {
            PendingDecision pending = state.PendingDecision!;
            LegalAction recorded = pending.LegalActions.Single(legal => legal.Id == action.Submission.ActionId);
            if (IsStockedTopUp(state, recorded))
            {
                stockedTopUps++;
                ActionId chosen = await ChooseAsync(definition, state);
                if (IsStockedTopUp(state, pending.LegalActions.Single(legal => legal.Id == chosen))) v4TopUps++;
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.True(stockedTopUps >= 3, $"The fixture has only {stockedTopUps} stocked top-ups.");
        Assert.Equal(0, v4TopUps);
    }

    /// <summary>A Smartware Distributor top-up while the card already hosts at least what it would place.</summary>
    private static bool IsStockedTopUp(GameState state, LegalAction action) =>
        action.Parameters is AbilityActionParameters { AbilityKey: "smartware-place" } ability
        && state.RulesState?.Counters.Where(counter => counter.CardId == ability.SourceCardId).Sum(counter => counter.Count) >= 3;

    private static async Task<ActionId> ChooseAsync(FullMatchDefinition definition, GameState state)
    {
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", RunnerHeuristicPolicy.V4);
        OfflineAgentCheckpoint checkpoint = fresh.Checkpoint with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
        var agent = new OfflineAgent(fresh.Configuration, OfflinePolicies.Create(fresh.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), checkpoint);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        return await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior));
    }
}
