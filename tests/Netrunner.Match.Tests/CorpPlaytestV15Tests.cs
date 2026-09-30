using System.Text.Json.Nodes;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Playtest, 30 September (Runner, Stolen Goods, against Hyper Velocity): Corp v14 removed Spin
/// Doctor from the game to shuffle Archives into R&amp;D while Archives was empty. Corp v15 is asked
/// at that position of the recorded game, and at edited copies of it for the uses Spin Doctor has
/// (from the playtester): recovering an agenda hidden in Archives, economy cards when short of money,
/// and an upgrade trashed early.
/// </summary>
public sealed class CorpPlaytestV15Tests
{
    private const string Fixture = "playtest-spin-doctor";
    private const string Content = "gateway-hyper-velocity~gateway-stolen-goods";
    private const ulong Seed = 14438435225079198195;
    private const long SpinDoctorUse = 16;
    private const string SpinDoctor = "30053";
    private const string SendAMessage = "30069";
    private const string HedgeFund = "30075";
    private const string AmazeAmusements = "30058";
    private const string Palisade = "30072";
    private const string Tithe = "30073";

    [Theory]
    [InlineData(CorpHeuristicPolicy.V14, true)]
    [InlineData(CorpHeuristicPolicy.V15, false)]
    public async Task SpinDoctorIsKeptWhileArchivesIsEmpty(string version, bool uses)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(version, SpinDoctorUse);
        Assert.Empty(state.GetZone(ZoneKind.CorpDiscard).CardIds);
        bool usedSpinDoctor = chosen.Parameters is AbilityActionParameters ability && state.GetCard(ability.SourceCardId).DefinitionId.Value == SpinDoctor;
        Assert.True(uses == usedSpinDoctor, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(new[] { SendAMessage }, 5, true)]
    [InlineData(new[] { HedgeFund, HedgeFund }, 1, true)]
    [InlineData(new[] { AmazeAmusements }, 5, true)]
    [InlineData(new[] { HedgeFund, HedgeFund }, 12, false)]
    [InlineData(new[] { Palisade, Tithe }, 5, false)]
    public async Task SpinDoctorIsSpentOnCardsWorthGettingBack(string[] archives, int credits, bool uses)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LegalAction chosen, GameState state) = await ChooseAtAsync(CorpHeuristicPolicy.V15, SpinDoctorUse,
            snapshot => Edit(snapshot, archives, credits));
        Assert.Equal(archives.Length, state.GetZone(ZoneKind.CorpDiscard).CardIds.Count);
        bool usedSpinDoctor = chosen.Parameters is AbilityActionParameters ability && state.GetCard(ability.SourceCardId).DefinitionId.Value == SpinDoctor;
        Assert.True(uses == usedSpinDoctor, $"With {string.Join(", ", archives)} in Archives and {credits} credits v15 chose {chosen.Kind} {chosen.Description}.");
    }

    /// <summary>Moves the named cards from R&amp;D into Archives, face up, and sets the Corp's credits.</summary>
    private static void Edit(JsonNode snapshot, string[] archives, int credits)
    {
        JsonArray zones = snapshot["zones"]!.AsArray();
        JsonNode deck = zones.Single(zone => (string?)zone!["kind"] == "corpDeck")!;
        JsonNode discard = zones.Single(zone => (string?)zone!["kind"] == "corpDiscard")!;
        JsonArray cards = snapshot["cards"]!.AsArray();
        foreach (string code in archives)
        {
            JsonNode card = cards.First(candidate => (string?)candidate!["definitionId"] == code
                && (string?)candidate["zoneId"] == (string?)deck["id"])!;
            string id = (string)card["id"]!;
            JsonArray deckIds = deck["cardIds"]!.AsArray();
            deckIds.Remove(deckIds.First(value => (string?)value == id));
            discard["cardIds"]!.AsArray().Add(id);
            card["zoneId"] = (string)discard["id"]!;
            card["faceUp"] = true;
        }
        snapshot["players"]!.AsArray().Single(player => (string?)player!["side"] == "corp")!["credits"] = credits;
    }

    private static async Task<(LegalAction Chosen, GameState State)> ChooseAtAsync(string version, long sequence,
        Action<JsonNode>? edit = null)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(Content, Seed);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", $"{Fixture}-replay.json")), definition.Behavior, definition.Runtime);
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
