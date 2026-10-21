using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The first Gateway-deck audit (Wave 4 C1) found cards the bots never used.
/// Self-play on the decks that hold them checks that Corp v7 and Runner v6 use
/// them more than Corp v6 and Runner v5 did.
/// </summary>
public sealed class GatewayAuditRepairTests
{
    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    [Fact]
    public async Task CorpV7PlaysItsTagPunishmentAndWorksItsTraps()
    {
        if (!Official) return;
        // Hyper Velocity: Public Trail. Quick and Dirty: Public Trail, Retribution.
        // Advanced Yomi: Urtica Cipher and Clearinghouse advanced, Clearinghouse rezzed.
        string[] contents = ["gateway-hyper-velocity~gateway-stolen-goods", "gateway-quick-and-dirty~gateway-stolen-goods",
            "gateway-advanced-yomi~gateway-stolen-goods"];
        Dictionary<string, int> before = await UsageAsync(contents, CorpHeuristicPolicy.V6, RunnerHeuristicPolicy.V5);
        Dictionary<string, int> after = await UsageAsync(contents, CorpHeuristicPolicy.V7, RunnerHeuristicPolicy.V5);
        Assert.True(after.GetValueOrDefault("30057:PlayCard") > before.GetValueOrDefault("30057:PlayCard"),
            "Corp v7 played Public Trail no more often than v6.");
        Assert.Equal(0, before.GetValueOrDefault("30045:AdvanceCard") + before.GetValueOrDefault("30061:AdvanceCard"));
        Assert.True(after.GetValueOrDefault("30045:AdvanceCard") + after.GetValueOrDefault("30061:AdvanceCard") > 0,
            "Corp v7 never advanced a trap.");
    }

    [Fact]
    public async Task RunnerV6InstallsVerbalPlasticity()
    {
        if (!Official) return;
        // Stolen Goods and Planning Ahead hold it. (Tread Lightly is valued too, but
        // stays situational: it pays only against unrezzed ICE and a Corp short of credits.)
        string[] contents = ["gateway-discretion-advised~gateway-stolen-goods", "gateway-discretion-advised~gateway-planning-ahead",
            "gateway-quick-and-dirty~gateway-stolen-goods", "gateway-quick-and-dirty~gateway-planning-ahead"];
        Dictionary<string, int> before = await UsageAsync(contents, CorpHeuristicPolicy.V7, RunnerHeuristicPolicy.V5);
        Dictionary<string, int> after = await UsageAsync(contents, CorpHeuristicPolicy.V7, RunnerHeuristicPolicy.V6);
        Assert.True(after.GetValueOrDefault("30034:InstallCard") > before.GetValueOrDefault("30034:InstallCard"),
            "Runner v6 installed Verbal Plasticity no more often than v5.");
    }

    /// <summary>How often each card code was played, installed, advanced or rezzed, as <c>code:ActionKind</c>.</summary>
    private static async Task<Dictionary<string, int>> UsageAsync(string[] contents, string corpVersion, string runnerVersion)
    {
        var usage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string content in contents)
            for (int index = 0; index < 4; index++)
            {
                FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(content, RulesFuzzRunner.DeriveGameSeed(4242, index));
                FullMatchResult result = await FullMatchRunner.RunAsync(definition, 3000,
                    OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", corpVersion),
                    OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", runnerVersion));
                GameState state = result.Replay.InitialSnapshot.ToState();
                foreach (ReplayAction action in result.Replay.Actions)
                {
                    LegalAction chosen = state.PendingDecision!.LegalActions.Single(legal => legal.Id == action.Submission.ActionId);
                    CardInstanceId? card = chosen.Parameters switch
                    {
                        PlayCardParameters play => play.CardId,
                        InstallCardParameters install => install.CardId,
                        AdvanceCardParameters advance => advance.CardId,
                        RezCardParameters rez => rez.CardId,
                        _ => null,
                    };
                    if (card is { } id)
                    {
                        string key = $"{state.GetCard(id).DefinitionId.Value}:{chosen.Kind}";
                        usage[key] = usage.GetValueOrDefault(key) + 1;
                    }
                    state = GameEngine.Apply(state, action.Submission, definition.Behavior, definition.Runtime, ApplyMode.Simulation).State;
                }
            }
        return usage;
    }
}
