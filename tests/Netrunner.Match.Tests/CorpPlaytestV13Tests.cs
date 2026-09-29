using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 29 September (Runner, Stolen Goods, against Quick and Dirty and Hyper Velocity):
/// Corp v12 rezzed Malapert Data Vault during a run, played Public Trail with its last click,
/// left Archives open to a money farm until turn 8, and never trashed a tagged Runner's
/// resources. Corp v13 is asked at those positions of the recorded games.
/// </summary>
public sealed class CorpPlaytestV13Tests
{
    private const string Weyland = "playtest-weyland-archives";
    private const string WeylandContent = "gateway-quick-and-dirty~gateway-stolen-goods";
    private const ulong WeylandSeed = 5474919460604003952;

    [Fact]
    public async Task MalapertIsNotRezzedDuringARun()
    {
        if (!Official) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(Weyland, WeylandContent, WeylandSeed, 171);
        Assert.False(chosen.Parameters is RezCardParameters rez && state.GetCard(rez.CardId).DefinitionId.Value == "30066", Describe(chosen));
    }

    [Fact]
    public async Task PublicTrailIsNotPlayedWithTheLastClick()
    {
        if (!Official) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(Weyland, WeylandContent, WeylandSeed, 432);
        Assert.False(chosen.Parameters is PlayCardParameters play && Title(state, play.CardId) == "Public Trail", Describe(chosen));
    }

    [Fact]
    public async Task ArchivesIsWalledAgainstTheMoneyFarmBeforeTurnEight()
    {
        if (!Official) return;
        var choices = new List<string>();
        foreach (long sequence in new long[] { 242, 245, 249, 326, 330, 333 })
        {
            (LegalAction chosen, GameState state) = await ChooseAtAsync(Weyland, WeylandContent, WeylandSeed, sequence);
            if (chosen.Parameters is InstallCardParameters install && !install.CreateNewRemote
                && state.Servers.Single(server => server.Id == install.ServerId).Kind == ServerKind.Archives) return;
            choices.Add(Describe(chosen));
        }
        Assert.Fail("No ICE went on Archives in turns 6 and 7: " + string.Join("; ", choices));
    }

    [Fact]
    public async Task ATaggedRunnersResourceIsTrashed()
    {
        if (!Official) return;
        var choices = new List<string>();
        foreach (long sequence in new long[] { 166, 169, 172 })
        {
            (LegalAction chosen, _) = await ChooseAtAsync("playtest-nbn-tags", "gateway-hyper-velocity~gateway-stolen-goods", 14798909876295211671, sequence);
            if (chosen.Kind == ActionKind.TrashTaggedResource) return;
            choices.Add(Describe(chosen));
        }
        Assert.Fail("No tagged resource was trashed in turn 4: " + string.Join("; ", choices));
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static string Describe(LegalAction action) => $"{action.Kind} {action.Description}";

    private static string Title(GameState state, CardInstanceId id) =>
        new CardCatalogMetadataResolver(CardDataResource.LoadEmbedded()).TryResolve(state.GetCard(id).DefinitionId.Value, out CardPresentationMetadata card)
            ? card.Title : "";

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string fixture, string content, ulong seed, long sequence)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(content, seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", $"{fixture}-replay.json")), definition.Behavior, definition.Runtime);
        GameState state = replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner, "standard", CorpHeuristicPolicy.V13);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                Assert.Equal(PlayerSide.Corp, state.GetPlayer(pending.Owner).Side);
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime));
                return (pending.LegalActions.Single(action => action.Id == id), state);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The replay has no action {sequence}.");
    }
}
