using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;
using static Netrunner.Match.Tests.BotLoop1Tests;

namespace Netrunner.Match.Tests;

/// <summary>
/// Bot loop 2's findings (docs/BOT_LOOP_2.md), at the recorded decision of the loop's games against Claude Sonnet: Runner
/// v25 and Corp v29 against the versions the loop played.
/// </summary>
public sealed class BotLoop2Tests
{
    [Theory]
    [InlineData(RunnerHeuristicPolicy.V24, false)]
    [InlineData(RunnerHeuristicPolicy.V25, true)]
    public async Task MuslihaTsFreeCardIsTakenEvenAsASecondCopy(string version, bool takes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // The top card is a second Marjanah: not worth installing, but free into a one-card grip.
        LegalAction chosen = await ChooseAtAsync("loop2-006", version, 140);
        Assert.True(takes == Assert.IsType<BehaviorChoiceParameters>(chosen.Parameters).CardIds.Count > 0,
            $"{version} chose {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V24, true)]
    [InlineData(RunnerHeuristicPolicy.V25, false)]
    public async Task ALoneScatterFieldIsSeenAtStrengthFour(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // 2 credits against R&D's lone Scatter Field: Buzzsaw can't reach strength 4 and break it.
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop2-204", version, 78, PlayerSide.Runner);
        bool rnd = chosen.Parameters is RunServerParameters run && state.Servers.Single(server => server.Id == run.ServerId).Kind == ServerKind.RAndD;
        Assert.True(runs == rnd, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V24, ActionKind.StartRun)]
    [InlineData(RunnerHeuristicPolicy.V25, ActionKind.DrawCard)]
    public async Task OneCardInTheGripDrawsBeforeRunningUnknownIce(string version, ActionKind kind)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Three clicks and one card; the remote's unrezzed Empiricist did 3 net damage.
        LegalAction chosen = await ChooseAtAsync("loop2-203", version, 704);
        Assert.True(kind == chosen.Kind, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V28, false)]
    [InlineData(CorpHeuristicPolicy.V29, true)]
    public async Task PublicAccessPlazaIsRezzedForItsTurnIncome(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseWithStateAtAsync("loop2-201", version, 137, PlayerSide.Corp);
        bool plaza = chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == "35062";
        Assert.True(rezzes == plaza, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V28, true)]
    [InlineData(CorpHeuristicPolicy.V29, false)]
    public async Task AmazeAmusementsIsNotRezzedInARemoteWithNoAgenda(string version, bool rezzes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("loop2-202", version, 394, PlayerSide.Corp);
        Assert.True(rezzes == chosen.Parameters is RezCardParameters, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }
}
