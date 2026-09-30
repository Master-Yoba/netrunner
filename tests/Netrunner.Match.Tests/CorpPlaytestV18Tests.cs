using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Runner, Stolen Goods, against Discretion Advised): on its first turn Corp
/// v17 put Palisade on R&amp;D while Send a Message and Luminal Transubstantiation waited in an open HQ,
/// and on its second it put Seamless Launch's two counters on Palisade, which ignores them, over an
/// Urtica Cipher they would have armed. Corp v18 is asked at both positions of the recorded game.
/// </summary>
public sealed class CorpPlaytestV18Tests
{
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 16353449718733399204;
    private const string Headquarters = "server:4";
    private const string UrticaCipher = "30045";

    [Theory]
    [InlineData(CorpHeuristicPolicy.V17, false)]
    [InlineData(CorpHeuristicPolicy.V18, true)]
    public async Task IceGoesOnAnHqHoldingAgendas(string version, bool onHq)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, _) = await ChooseAtAsync(version, 8);
        bool hq = chosen.Parameters is InstallCardParameters { DestinationKind: InstallDestinationKind.ServerIce } install
            && install.ServerId?.Value == Headquarters;
        Assert.True(onHq == hq, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V18)]
    public async Task SeamlessLaunchArmsTheTrap(string version)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(version, 54);
        var choice = (BehaviorChoiceParameters)chosen.Parameters;
        Assert.Equal(UrticaCipher, state.GetCard(choice.CardIds.Single()).DefinitionId.Value);
    }

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string version, long sequence)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-central-ice-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in replay.Actions)
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
        throw new InvalidOperationException($"The replay has no action {sequence}.");
    }
}
