using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Corp, Discretion Advised against the Stolen Goods bot): Runner v12 ran
/// the same unprotected remote holding Urtica Cipher seven times, taking its net damage each time,
/// because the card it had accessed was forgotten once it turned face down again; and it left a
/// Nico Campaign it could afford to trash. Runner v13 is asked at those positions. The replay was
/// re-recorded up to action 340 after the upgrade-trash fix, which offers Manegarm's trash at 128.
/// </summary>
public sealed class RunnerPlaytestV13Tests
{
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 3748720965229884459;
    private const string UrticaRemote = "server:547";

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V12, true)]
    [InlineData(RunnerHeuristicPolicy.V13, false)]
    public async Task AKnownUrticaCipherIsNotRunAgain(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Turn 5: the Runner accessed Urtica Cipher there on turn 4 and ran it again.
        LegalAction chosen = await ChooseAtAsync(version, 243);
        bool ranUrtica = chosen.Parameters is RunServerParameters run && run.ServerId.Value == UrticaRemote;
        Assert.True(runs == ranUrtica, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V12)]
    [InlineData(RunnerHeuristicPolicy.V13)]
    public async Task UrticaCipherIsNotWorthTrashing(string version)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The playtester: trashing an installed Urtica is rarely worth it without a reward for the trash.
        LegalAction chosen = await ChooseAtAsync(version, 171);
        Assert.NotEqual(ActionKind.TrashAccessedCard, chosen.Kind);
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V12, false)]
    [InlineData(RunnerHeuristicPolicy.V13, true)]
    public async Task ANicoCampaignIsTrashedWhenAffordable(string version, bool trashes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Turn 1: Nico Campaign accessed in HQ with 5 credits; it would load 9 when rezzed.
        LegalAction chosen = await ChooseAtAsync(version, 32);
        Assert.True(trashes == (chosen.Kind == ActionKind.TrashAccessedCard), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    private static async Task<LegalAction> ChooseAtAsync(string version, long sequence)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-runner-bot-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime));
                return pending.LegalActions.Single(action => action.Id == id);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The replay has no action {sequence}.");
    }
}
