using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Bot loop 1's diagnosis (docs/BOT_IMPROVEMENT_PLAN.md): on the Core Battle Box, 41% of Standard Runner v22's games were
/// lost to its own choice, because a choice's damage cost nothing. Each fixture is a Battle Box ladder game at the fatal
/// decision.
/// </summary>
public sealed class BotLoop1Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, true)]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    public async Task TopanDoesNotInstallItsLastCardIntoAFlatline(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // One card in the grip: the install empties it, and Topan's meat damage then flatlines the Runner.
        LegalAction chosen = await ChooseAtAsync("loop1-topan-last-card", version, 140);
        bool topan = chosen.Parameters is AbilityActionParameters { AbilityKey: "topan-install" };
        Assert.True(installs == topan, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, "suffer-damage")]
    [InlineData(RunnerHeuristicPolicy.V23, "end-run")]
    public async Task SemakSamunsDamageIsNotTakenWithTooFewCards(string version, string option)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Two cards in the grip against "end the run unless the Runner suffers 3 net damage".
        LegalAction chosen = await ChooseAtAsync("loop1-semak-samun-damage", version, 120);
        Assert.Equal(option, Assert.Single(Assert.IsType<BehaviorChoiceParameters>(chosen.Parameters).OptionKeys));
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, "take-damage")]
    [InlineData(RunnerHeuristicPolicy.V23, "pay-eight")]
    public async Task MeasuredResponseIsPaidRatherThanFlatlining(string version, string option)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // 18 credits and 3 cards in the grip against "do 4 meat damage unless the Runner pays 8".
        LegalAction chosen = await ChooseAtAsync("loop1-measured-response", version, 285);
        Assert.Equal(option, Assert.Single(Assert.IsType<BehaviorChoiceParameters>(chosen.Parameters).OptionKeys));
    }

    // Loop 1's games, rebound to the current catalog: the findings Codex's reviews made and the replays confirmed.

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, true)]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    public async Task TopanIsNotUsedWithOnlyEventsInTheGrip(string version, bool uses)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("loop1-005", version, 120);
        Assert.True(uses == chosen.Parameters is AbilityActionParameters { AbilityKey: "topan-install" }, $"{version} chose {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V22, true)]
    [InlineData(RunnerHeuristicPolicy.V23, false)]
    public async Task MadaniDoesNotHostWithNoProgramInTheGrip(string version, bool hosts)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("loop1-007", version, 128);
        Assert.True(hosts == chosen.Parameters is AbilityActionParameters { AbilityKey: "madani-host" }, $"{version} chose {chosen.Description}.");
    }

    [Fact]
    public async Task TheCorpSpringsByteWhenTheRunnerAccessesItFromRAndD()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Its own trigger's source was hidden from the Corp in R&D, so it valued the trigger blind and declined a lethal Byte!.
        LegalAction chosen = await ChooseAtAsync("loop1-002", CorpHeuristicPolicy.PolicyVersion, 131, PlayerSide.Corp);
        Assert.Equal(ActionKind.UseTrigger, chosen.Kind);
    }

    [Fact]
    public async Task PoetrisScoringInstallFromRAndDIsTaken()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The cards offered from R&D were hidden from the Corp, so it declined every free install as "unknown metadata".
        LegalAction chosen = await ChooseAtAsync("loop1-101", CorpHeuristicPolicy.PolicyVersion, 401, PlayerSide.Corp);
        Assert.True(chosen.Parameters is BehaviorInstallChoiceParameters { Choice: not null }, $"chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V26, false)]
    [InlineData(CorpHeuristicPolicy.V27, true)]
    public async Task PhatGioanBaotixitaIsRezzedToGatherCounters(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-002", version, 52, PlayerSide.Corp);
        bool phat = chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == "35051";
        Assert.True(rezzes == phat, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V26, false)]
    [InlineData(CorpHeuristicPolicy.V27, true)]
    public async Task HumanoidResourcesIsRezzedWithTheClicksToUseIt(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop1-003", version, 167, PlayerSide.Corp);
        bool humanoid = chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == "35039";
        Assert.True(rezzes == humanoid, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>The version's choice for a side at the recorded decision, having watched the game to it.</summary>
    private static async Task<LegalAction> ChooseAtAsync(string fixture, string version, long sequence, PlayerSide side = PlayerSide.Runner) =>
        (await ChooseWithStateAtAsync(fixture, version, sequence, side)).Chosen;

    private static async Task<(LegalAction Chosen, GameState State)> ChooseWithStateAtAsync(string fixture, string version, long sequence,
        PlayerSide side)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", fixture)));
        GameState state = saved.Replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
            "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime));
                return (pending.LegalActions.Single(action => action.Id == id), state);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }
}
