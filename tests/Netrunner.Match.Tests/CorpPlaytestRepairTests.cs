using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Corp v6 repairs, checked at the positions of the 27 September 2026 playtest
/// where Corp v4 went wrong (official content only). The replay is the human
/// Runner's game against Standard; v4's recorded choices are the "before".
/// </summary>
public sealed class CorpPlaytestRepairTests
{
    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    [Fact]
    public void V6DoesNotOverwriteItsOwnAgendaWithAnotherInstall()
    {
        if (!Official) return;
        // Action 52: v4 installed its second Superconducting Hub into the root holding the first.
        (FullMatchDefinition definition, GameState state) = At(52);
        bool Replaces(ActionId id) => state.PendingDecision!.LegalActions.Single(action => action.Id == id).Parameters
            is InstallCardParameters { RequiredReplacementCardIds.Count: > 0 };

        // The recorded v4 choice replaced the agenda; v6 never does.
        Assert.True(Replaces(Recorded(52)));
        Assert.False(Replaces(Choose(definition, state, CorpHeuristicPolicy.V6)));
    }

    [Fact]
    public void V6RezzesTheUpgradeInTheServerBeingRun()
    {
        if (!Official) return;
        // Actions 215–226: the Corp had 1 credit during a run on the remote holding
        // AMAZE Amusements (rez cost 1) and passed every window; it should rez it.
        bool rezzed = false;
        foreach (long sequence in new long[] { 215, 217, 221, 223, 226 })
        {
            (FullMatchDefinition definition, GameState state) = At(sequence);
            if (!state.PendingDecision!.LegalActions.Any(action => action.Kind == ActionKind.RezCard)) continue;
            Assert.Equal(ActionKind.PassPriority, Kind(state, Choose(definition, state, CorpHeuristicPolicy.V4)));
            rezzed = Kind(state, Choose(definition, state, CorpHeuristicPolicy.V6)) == ActionKind.RezCard;
            break;
        }
        Assert.True(rezzed, "V6 passed the window where it could rez the upgrade in the attacked server.");
    }

    [Fact]
    public void V6KeepsTheCreditThatRezzesTheAgendasIce()
    {
        if (!Official) return;
        // Action 180: v4 advanced an agenda it couldn't finish with 2 credits while its
        // Palisade (rez cost 3) was unrezzed.
        (FullMatchDefinition definition, GameState state) = At(180);
        Assert.Equal(ActionKind.AdvanceCard, Kind(state, Choose(definition, state, CorpHeuristicPolicy.V4)));
        Assert.NotEqual(ActionKind.AdvanceCard, Kind(state, Choose(definition, state, CorpHeuristicPolicy.V6)));
    }

    private static ReplayArtifact? _replay;
    private static FullMatchDefinition? _definition;

    private static ReplayArtifact Replay(out FullMatchDefinition definition)
    {
        definition = _definition ??= RulesFuzzRunner.CreateDefinition("expanded-expanded", 6153418917552992760);
        return _replay ??= EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-corp-repairs-replay.json")), definition.Behavior, definition.Runtime);
    }

    private static ActionId Recorded(long sequence) => Replay(out _).Actions.Single(action => action.Sequence == sequence).Submission.ActionId;

    /// <summary>The state just before the recorded action <paramref name="sequence"/>.</summary>
    private static (FullMatchDefinition, GameState) At(long sequence)
    {
        ReplayArtifact replay = Replay(out FullMatchDefinition definition);
        GameState state = replay.InitialSnapshot.ToState();
        foreach (ReplayAction action in replay.Actions.TakeWhile(action => action.Sequence < sequence))
        {
            ApplyResult applied = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }
        return (definition, state);
    }

    private static ActionKind Kind(GameState state, ActionId id) => state.PendingDecision!.LegalActions.Single(action => action.Id == id).Kind;

    /// <summary>A fresh Standard Corp of <paramref name="version"/> that has observed <paramref name="state"/>.</summary>
    private static ActionId Choose(FullMatchDefinition definition, GameState state, string version)
    {
        OfflineAgent fresh = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", version);
        OfflineAgentCheckpoint checkpoint = fresh.Checkpoint with { ObservationCursor = state.ActionSequence - 1, ObservationDigest = "position" };
        var agent = new OfflineAgent(fresh.Configuration, OfflinePolicies.Create(fresh.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), checkpoint);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        return agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior)).AsTask().GetAwaiter().GetResult();
    }
}
