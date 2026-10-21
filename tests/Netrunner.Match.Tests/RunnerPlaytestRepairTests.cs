using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Runner repairs, checked at positions of the 27 September 2026 Corp-seat
/// playtests (official content only): v4, where v2 spent turn after turn
/// topping up Smartware Distributor instead of building its rig, and v5, where
/// v4 ran a remote whose rezzed Manegarm Skunkworks toll it couldn't pay.
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
                ActionId chosen = await ChooseAsync(definition, state, RunnerHeuristicPolicy.V4);
                if (IsStockedTopUp(state, pending.LegalActions.Single(legal => legal.Id == chosen))) v4TopUps++;
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.True(stockedTopUps >= 3, $"The fixture has only {stockedTopUps} stocked top-ups.");
        Assert.Equal(0, v4TopUps);
    }

    [Fact]
    public async Task V5DoesNotRunIntoATollItCannotPay()
    {
        if (!Official) return;
        // Turn 2, action 75: a second run on the remote with 1 click and 0 credits behind a
        // rezzed Manegarm (2 clicks or 5 credits). Turns 8 and 9, actions 299 and 368: runs
        // with 3 credits and 2 clicks through two ICE, reaching Manegarm with neither.
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("expanded-expanded", 16458389011475437105);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-runner-toll-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        long[] positions = [75, 299, 368];
        int checkedPositions = 0;
        foreach (ReplayAction action in replay.Actions)
        {
            if (positions.Contains(action.Sequence))
            {
                PendingDecision pending = state.PendingDecision!;
                LegalAction recorded = pending.LegalActions.Single(legal => legal.Id == action.Submission.ActionId);
                // The recorded run is v4's own choice. The replay was re-recorded after the Carmen fix
                // (playtest, 30 September: Mutual Favor installed it from the stack at full price at
                // action 31), which leaves the Runner 2 credits richer from then on, so v4 isn't asked again.
                Assert.Equal(ActionKind.StartRun, recorded.Kind);
                ActionId v5Choice = await ChooseAsync(definition, state, RunnerHeuristicPolicy.V5);
                LegalAction v5 = pending.LegalActions.Single(legal => legal.Id == v5Choice);
                Assert.False(v5.Kind == ActionKind.StartRun && v5.Parameters is RunServerParameters run
                    && run.ServerId == ((RunServerParameters)recorded.Parameters).ServerId, $"V5 still runs the tolled remote at action {action.Sequence}.");
                checkedPositions++;
            }
            state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime).State;
        }
        Assert.Equal(positions.Length, checkedPositions);
    }

    /// <summary>A Smartware Distributor top-up while the card already hosts at least what it would place.</summary>
    private static bool IsStockedTopUp(GameState state, LegalAction action) =>
        action.Parameters is AbilityActionParameters { AbilityKey: "smartware-place" } ability
        && state.RulesState?.Counters.Where(counter => counter.CardId == ability.SourceCardId).Sum(counter => counter.Count) >= 3;

    private static async Task<ActionId> ChooseAsync(FullMatchDefinition definition, GameState state, string version)
    {
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", version);
        OfflineAgentCheckpoint checkpoint = fresh.Checkpoint with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
        var agent = new OfflineAgent(fresh.Configuration, OfflinePolicies.Create(fresh.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), checkpoint);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        return await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior));
    }
}
