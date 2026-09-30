using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Oddity lints, 1 October, in ladder games of Corp v18:
/// - it played Seamless Launch with no agenda or trap installed and put its counters on ICE, because its
///   planner's fitted evaluation counted any counter in a remote as progress (v19 plans with feature
///   set 3, which counts only counters that matter);
/// - it used Ansel 1.0 to install an agenda over its own agenda in the remote being run, where the
///   Runner takes the new one instead.
/// </summary>
public sealed class CorpLintV19Tests
{
    private const string SeamlessLaunch = "30040";
    private const string LuminalTransubstantiation = "30036";

    [Theory]
    [InlineData(CorpHeuristicPolicy.V18, true)]
    [InlineData(CorpHeuristicPolicy.V19, false)]
    public async Task SeamlessLaunchWaitsForATargetThatMatters(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("seamless-launch-nothing", version,
            (_, pending) => pending.Kind == DecisionKind.TurnAction, 222);
        bool seamless = chosen.Parameters is PlayCardParameters play && state.GetCard(play.CardId).DefinitionId.Value == SeamlessLaunch;
        Assert.True(plays == seamless, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V18, true)]
    [InlineData(CorpHeuristicPolicy.V19, false)]
    public async Task AnAgendaIsNotInstalledIntoTheServerBeingRun(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Ansel 1.0's install subroutine, just before the recorded install over Offworld Office (action 213).
        (LegalAction chosen, GameState state) = await ChooseAtAsync("agenda-over-agenda-mid-run", version,
            (_, pending) => pending.Kind == DecisionKind.IceSubroutineChoice, 213);
        bool agendaIn = chosen.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.InstallCard, CardId: { } card }
            && state.GetCard(card).DefinitionId.Value == LuminalTransubstantiation;
        Assert.True(installs == agendaIn, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>The version's choice at the last decision matching <paramref name="match"/> at or before <paramref name="limit"/>.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, string version,
        Func<GameState, PendingDecision, bool> match, long limit)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", fixture)));
        long target = 0;
        GameState scan = saved.Replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in saved.Replay.Actions.TakeWhile(action => action.Sequence <= limit))
        {
            if (match(scan, scan.PendingDecision!)) target = recorded.Sequence;
            scan = GameEngine.Apply(scan, recorded.Submission, definition.Behavior, definition.Runtime).State;
        }
        GameState state = saved.Replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == target)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime));
                return (pending.LegalActions.Single(action => action.Id == id), state);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The game has no matching decision by action {limit}.");
    }
}
