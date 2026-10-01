using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Codex reviews of Corp v19, 1 October, in saved ladder games of the current bots:
/// - it kept advancing an Urtica Cipher whose damage already exceeded the Runner's grip;
/// - Sprint shuffled the agendas that would have won (at 6 points) back into R&D;
/// - its reserve for agenda ICE declined Ansel 1.0 on R&D and Diviner on HQ (with an agenda in hand), and
///   the Runner stole from both.
/// </summary>
public sealed class CorpCodexV20Tests
{
    [Theory]
    [InlineData(CorpHeuristicPolicy.V19, true)]
    [InlineData(CorpHeuristicPolicy.V20, false)]
    public async Task ALethalTrapIsNotAdvancedFurther(string version, bool advances)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-urtica", version, 461);
        bool urtica = chosen.Parameters is AdvanceCardParameters advance && Title(state, advance.CardId) == "Urtica Cipher";
        Assert.True(advances == urtica, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V19, true)]
    [InlineData(CorpHeuristicPolicy.V20, false)]
    public async Task SprintKeepsTheAgendaThatWouldWin(string version, bool shufflesAnAgenda)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("codex-corp-sprint-ansel", version, 785);
        CardInstanceId[] cards = chosen.Parameters is BehaviorChoiceParameters choice ? [.. choice.CardIds] : [];
        bool agenda = cards.Any(id => Title(state, id) is "Luminal Transubstantiation" or "Offworld Office");
        Assert.True(shufflesAnAgenda == agenda, $"{version} chose {string.Join(", ", cards.Select(id => Title(state, id)))}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V19, "codex-corp-sprint-ansel", 246, false)]
    [InlineData(CorpHeuristicPolicy.V20, "codex-corp-sprint-ansel", 246, true)]
    [InlineData(CorpHeuristicPolicy.V19, "codex-corp-diviner", 121, false)]
    [InlineData(CorpHeuristicPolicy.V20, "codex-corp-diviner", 121, true)]
    public async Task CentralIceIsRezzedWhenItCanStopTheRun(string version, string fixture, long sequence, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, _) = await ChooseAtAsync(fixture, version, sequence);
        Assert.True(rezzes == (chosen.Kind == ActionKind.RezCard), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    private static string Title(GameState state, CardInstanceId id) =>
        CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(id).DefinitionId.Value).Title;

    /// <summary>The Corp version's choice at the recorded decision <paramref name="sequence"/>, having watched the game to it.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, string version, long sequence)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", fixture)));
        GameState state = saved.Replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime));
                return (pending.LegalActions.Single(action => action.Id == id), state);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }
}
