using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The playtester's Spin Doctor trick (30 September): an agenda the Runner can reach before it can be
/// scored is trashed by installing an asset over it, and Spin Doctor then shuffles it back from Archives
/// into R&amp;D. Recorded from a Corp v16 self-play game (Hyper Velocity against Planning Ahead).
/// </summary>
public sealed class CorpAgendaHidingV16Tests
{
    private const string Fixture = "selfplay-agenda-hiding";
    private const string Content = "gateway-hyper-velocity~gateway-planning-ahead";
    private const ulong Seed = 2029;
    private const long Hiding = 176;

    [Theory]
    [InlineData(CorpHeuristicPolicy.V15, false)]
    [InlineData(CorpHeuristicPolicy.V16, true)]
    public async Task AThreatenedAgendaIsHiddenForSpinDoctor(string version, bool hides)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state, FullMatchDefinition definition) = await ChooseAtAsync(version, Hiding);
        bool hid = chosen.Parameters is InstallCardParameters install && install.RequiredReplacementCardIds
            .Any(id => definition.Runtime.TryGet(state.GetCard(id).DefinitionId, out CardRuntimeSpec spec) && spec.CardClass == RuntimeCardClass.Agenda);
        Assert.True(hides == hid, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public void TheHiddenAgendaIsBackInResearchAndDevelopmentThatTurn()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (FullMatchDefinition definition, ReplayArtifact replay) = Load();
        GameState state = replay.InitialSnapshot.ToState();
        CardInstanceId? agenda = null;
        int turn = 0;
        foreach (ReplayAction recorded in replay.Actions)
        {
            if (recorded.Sequence == Hiding)
            {
                var install = (InstallCardParameters)state.PendingDecision!.LegalActions.Single(action => action.Id == recorded.Submission.ActionId).Parameters;
                agenda = install.RequiredReplacementCardIds.Single();
                turn = state.Turn.TurnNumber;
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            if (agenda is { } id && state.GetZone(ZoneKind.CorpDeck).CardIds.Contains(id))
            {
                Assert.Equal(turn, state.Turn.TurnNumber);
                return;
            }
        }
        Assert.Fail("The hidden agenda never went back into R&D.");
    }

    private static (FullMatchDefinition Definition, ReplayArtifact Replay) Load()
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", $"{Fixture}-replay.json")), definition.Behavior, definition.Runtime);
        return (definition, replay);
    }

    private static async Task<(LegalAction Chosen, GameState State, FullMatchDefinition Definition)> ChooseAtAsync(string version, long sequence)
    {
        (FullMatchDefinition definition, ReplayArtifact replay) = Load();
        SavedChoice choice = await SavedDecision.ChooseAsync(replay, definition, sequence, PlayerSide.Corp, version);
        return (choice.Chosen, choice.State, definition);
    }
}
