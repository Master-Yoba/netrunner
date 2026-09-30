using System.Text.Json.Nodes;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Runner, Stolen Goods, against Discretion Advised): Ansel 1.0's "install 1
/// card from HQ or Archives" let Corp v16 install Nico Campaign into the remote the Runner was running,
/// trashing its own Offworld Office. The Runner, with a click left, simply took it from Archives.
/// Corp v17 is asked at that position of the recorded game, and at an edited copy where Archives
/// holds a rezzed Palisade and the Runner has no credits: then the trash denies the steal for the
/// turn (the playtester: worth it when Archives holds and the Runner is short of money).
/// </summary>
public sealed class CorpPlaytestV17Tests
{
    private const string Content = "gateway-discretion-advised~gateway-stolen-goods";
    private const ulong Seed = 18095256415510229748;
    private const long AnselInstall = 212;
    private const string AttackedRemote = "server:180";

    [Theory]
    [InlineData(CorpHeuristicPolicy.V16, true)]
    [InlineData(CorpHeuristicPolicy.V17, false)]
    public async Task AnAgendaIsNotTrashedByInstallingOverItMidRun(string version, bool trashes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(version, AnselInstall);
        Assert.True(state.Players.Single(player => player.Side == PlayerSide.Runner).Clicks > 0);
        bool overAgenda = chosen.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.InstallCard } install
            && install.ServerId?.Value == AttackedRemote;
        Assert.True(trashes == overAgenda, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task AnAgendaIsTrashedToDenyASteal()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(CorpHeuristicPolicy.V17, AnselInstall, ProtectArchives);
        Assert.Equal(0, state.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        bool overAgenda = chosen.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.InstallCard } install
            && install.ServerId?.Value == AttackedRemote;
        Assert.True(overAgenda, $"v17 chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>A rezzed Palisade from R&amp;D on Archives, and a Runner with no credits.</summary>
    private static void ProtectArchives(JsonNode snapshot)
    {
        JsonArray zones = snapshot["zones"]!.AsArray();
        JsonNode deck = zones.Single(zone => (string?)zone!["kind"] == "corpDeck")!;
        string archivesIce = (string)snapshot["servers"]!.AsArray().Single(server => (string?)server!["kind"] == "archives")!["iceZoneId"]!;
        JsonNode ice = zones.Single(zone => (string?)zone!["id"] == archivesIce)!;
        JsonNode card = snapshot["cards"]!.AsArray().First(candidate => (string?)candidate!["definitionId"] == "30072"
            && (string?)candidate["zoneId"] == (string?)deck["id"])!;
        string id = (string)card["id"]!;
        JsonArray deckIds = deck["cardIds"]!.AsArray();
        deckIds.Remove(deckIds.First(value => (string?)value == id));
        ice["cardIds"]!.AsArray().Add(id);
        card["zoneId"] = archivesIce;
        card["faceUp"] = true;
        card["rezzed"] = true;
        snapshot["players"]!.AsArray().Single(player => (string?)player!["side"] == "runner")!["credits"] = 0;
    }

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string version, long sequence,
        Action<JsonNode>? edit = null)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "playtest-agenda-swap-replay.json")), definition.Behavior, definition.Runtime);
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
            if (edit is not null && recorded.Sequence == sequence - 1)
            {
                // Edited before the agent observes it, as if the game had reached it.
                JsonNode snapshot = JsonNode.Parse(EngineJson.Serialize(GameSnapshot.From(state, definition.Behavior, definition.Runtime)))!;
                edit(snapshot);
                state = EngineJson.Deserialize<GameSnapshot>(snapshot.ToJsonString(), definition.Behavior, definition.Runtime).ToState();
            }
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The replay has no action {sequence}.");
    }
}
