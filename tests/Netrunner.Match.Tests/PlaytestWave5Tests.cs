using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The Wave 5 playtest (2 October), three games against the bots:
/// - Standard Runner: Mutual Favor fetched Carmen with two barriers out and no sentry, and R&amp;D's barrier was never broken;
/// - Standard Corp: two Urtica Ciphers advanced past lethal on remotes the Runner never ran, while HQ stayed empty;
/// - Hard aggressive Runner: broke, with two Sure Gambles in the grip, it paid 2 credits a tag seven times against a Corp
///   that never punished one.
/// </summary>
public sealed class PlaytestWave5Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V18, "Carmen")]
    [InlineData(RunnerHeuristicPolicy.V20, "Marjanah")]
    public async Task ATutorFindsTheBreakerTheBoardNeeds(string version, string found)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("playtest-w5-mutual-favor", PlayerSide.Runner, version, 327);
        CardInstanceId[] cards = chosen.Parameters is BehaviorChoiceParameters choice ? [.. choice.CardIds] : [];
        Assert.Equal(found, Title(state, Assert.Single(cards)));
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V18, true)]
    [InlineData(RunnerHeuristicPolicy.V20, false)]
    public async Task ABrokeRunnerKeepsATagTheCorpCanTPunish(string version, bool removes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, _) = await ChooseAtAsync("playtest-w5-tags", PlayerSide.Runner, version, 174);
        Assert.True(removes == (chosen.Kind == ActionKind.RemoveTag), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V23, true)]
    [InlineData(CorpHeuristicPolicy.V25, false)]
    public async Task ATrapPastLethalIsNotAdvancedWhateverThePlannerSees(string version, bool advances)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync("playtest-w5-urtica", PlayerSide.Corp, version, 922);
        bool urtica = chosen.Parameters is AdvanceCardParameters advance && Title(state, advance.CardId) == "Urtica Cipher";
        Assert.True(advances == urtica, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public void TheCorpsOwnTrapCountersAreNotScoringProgressInSetFour()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Load("playtest-w5-urtica");
        GameState state = StateAt(saved, definition, 922);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        int index = EvaluationFeatures.NamesFor(4).ToList().IndexOf("board-advancement");
        AgentFeatures corp = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime));
        AgentFeatures runner = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
            AgentFeatures.VersionWithHiddenAdvancements);

        Assert.True(EvaluationFeatures.Extract(corp, catalog, 3)[index] > 0);
        Assert.Equal(0, EvaluationFeatures.Extract(corp, catalog, 4)[index]);
        Assert.Equal(EvaluationFeatures.Extract(runner, catalog, 3)[index], EvaluationFeatures.Extract(runner, catalog, 4)[index]);
    }

    private static string Title(GameState state, CardInstanceId id) =>
        CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(id).DefinitionId.Value).Title;

    private static (LadderSavedGame Saved, FullMatchDefinition Definition) Load(string fixture) =>
        Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", fixture)));

    private static GameState StateAt(LadderSavedGame saved, FullMatchDefinition definition, long sequence)
    {
        GameState state = saved.Replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == sequence) return state;
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }

    /// <summary>The version's choice for <paramref name="side"/> at the recorded decision, having watched the game to it.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, PlayerSide side, string version, long sequence)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Load(fixture);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        PlayerSide human = side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, human, "standard", version);
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
