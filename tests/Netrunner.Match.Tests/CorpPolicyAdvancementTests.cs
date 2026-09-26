using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Playtest regressions: Corp v4 advancement valuation, and how its history reads.</summary>
public sealed class CorpPolicyAdvancementTests
{
    [OfficialGatewayTheory]
    [InlineData(CorpHeuristicPolicy.V3, true)]
    [InlineData(CorpHeuristicPolicy.V4, false)]
    public async Task SeamlessLaunchWaitsWhenOnlyIceCanTakeTheCounters(string version, bool plays)
    {
        // The agenda was installed this turn, so Seamless Launch can only target the ICE.
        TacticalBoard board = Board()
            .Add("agenda", "30067", ZoneKind.ServerRoot, "remote")
            .Add("ice", GatewayBatch5CardCodes.Diviner.Value, ZoneKind.ServerIce, "remote", rezzed: true)
            .Add("launch", GatewayBatch1CardCodes.SeamlessLaunch.Value, ZoneKind.CorpHand)
            .Add("filler", "30067", ZoneKind.CorpHand);
        board.InstalledThisTurn.Add("agenda");

        Assert.Equal(plays, PlayScore(await Evaluate(board, version)) > 0);
    }

    [OfficialGatewayFact]
    public async Task SeamlessLaunchIsPlayedWhenItLetsAnAgendaScoreThisTurn()
    {
        TacticalBoard board = Board();
        int requirement = board.Runtime.GetSpec(new("30067")).AdvancementRequirement;
        board.Add("agenda", "30067", ZoneKind.ServerRoot, "remote", advancement: requirement - 2)
            .Add("ice", GatewayBatch5CardCodes.Diviner.Value, ZoneKind.ServerIce, "remote", rezzed: true)
            .Add("launch", GatewayBatch1CardCodes.SeamlessLaunch.Value, ZoneKind.CorpHand);

        Assert.Equal(ActionKind.PlayCard, Best(await Evaluate(board, CorpHeuristicPolicy.V4)));
    }

    [OfficialGatewayFact]
    public async Task V4AdvancesAProtectedAgendaItCanFinishNextTurn()
    {
        // Four counters can't be placed in one turn, so v3 never starts; v4 does behind ICE.
        TacticalBoard board = Board();
        board.CorpCredits = 8;
        string code = GatewayCodeWithRequirement(board, 4);
        board.Add("agenda", code, ZoneKind.ServerRoot, "remote")
            .Add("ice", GatewayBatch5CardCodes.Diviner.Value, ZoneKind.ServerIce, "remote", rezzed: true)
            .Add("hand", "30067", ZoneKind.CorpHand).Add("hand-2", "30067", ZoneKind.CorpHand)
            .Add("hand-3", "30067", ZoneKind.CorpHand);

        Assert.Equal(ActionKind.AdvanceCard, Best(await Evaluate(board, CorpHeuristicPolicy.V4)));
        Assert.NotEqual(ActionKind.AdvanceCard, Best(await Evaluate(board, CorpHeuristicPolicy.V3)));
    }

    [OfficialGatewayFact]
    public void HistoryNamesThePlayedOperationAndWhereItsCountersWent()
    {
        TacticalBoard board = Board()
            .Add("ice", GatewayBatch5CardCodes.Diviner.Value, ZoneKind.ServerIce, "remote", rezzed: true)
            .Add("launch", GatewayBatch1CardCodes.SeamlessLaunch.Value, ZoneKind.CorpHand);
        var metadata = new CardCatalogMetadataResolver(CardDataResource.LoadEmbedded());
        MatchViewModel View(GameState value) => ObservationViewMapper.Build(ObservationBuilder.Build(value, PlayerSide.Runner, board.Behavior),
            metadata, board.Behavior, handSizeProjection: null,
            presentationContext: MatchPresentationProjector.Build(value, PlayerSide.Runner, board.Behavior, board.Runtime));
        var projector = new MatchHistoryProjector();
        var entries = new List<MatchHistoryEntry>();
        GameState state = board.Action(PlayerSide.Corp);
        LegalAction next = state.PendingDecision!.LegalActions.Single(action => action.Parameters is PlayCardParameters);
        for (int step = 0; step < 4; step++)
        {
            MatchViewModel before = View(state);
            ApplyResult applied = board.Apply(state, next);
            entries.AddRange(projector.Project(before, View(applied.State), applied.State, applied.Events, PlayerSide.Corp, next));
            state = applied.State;
            if (state.PendingDecision?.Kind != DecisionKind.BehaviorChoice) break;
            next = state.PendingDecision.LegalActions.FirstOrDefault(action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.Contains(board.Card("ice"))) ?? state.PendingDecision.LegalActions.First(action =>
                action.Parameters is BehaviorChoiceParameters { FinalizeSelection: true });
        }

        string text = string.Join("\n", entries.SelectMany(entry => entry.Details.Prepend(entry.Summary)));
        Assert.Contains("Corp plays Seamless Launch", text, StringComparison.Ordinal);
        Assert.Contains("Corp places 2 advancement counters on Diviner", text, StringComparison.Ordinal);
        Assert.Contains("Seamless Launch → Archives", text, StringComparison.Ordinal);
        Assert.DoesNotContain("face-down card", text, StringComparison.Ordinal);
    }

    private static TacticalBoard Board()
    {
        CardDataSet data = CardDataResource.LoadEmbedded();
        return new TacticalBoard(GatewayBehaviorCatalog.Create(), GatewayRuntimeCardCatalog.Create(data), data.Manifest.CommitSha);
    }

    private static string GatewayCodeWithRequirement(TacticalBoard board, int requirement) =>
        CardDataResource.LoadEmbedded().Cards.Select(card => card.Code).First(code =>
        {
            CardRuntimeSpec spec = board.Runtime.GetSpec(new(code));
            return spec.CardClass == RuntimeCardClass.Agenda && spec.AdvancementRequirement == requirement;
        });

    private static async Task<ImmutableArray<(ActionKind Kind, int Score)>> Evaluate(TacticalBoard board, string version)
    {
        GameState state = board.Action(PlayerSide.Corp);
        var agent = new OfflineAgent(CorpPolicyProfiles.Create("standard", 5301, version), new CorpHeuristicPolicy(version),
            new GatewayEvaluationCatalog(board.Runtime, board.Behavior), captureDiagnostics: true);
        AgentLifecycleHost.Observe(agent, state, board.Behavior);
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Corp, board.Behavior);
        await agent.ChooseActionAsync(observation);
        return [.. agent.LastEvaluation!.Candidates.Select(candidate => (
            state.PendingDecision!.LegalActions.Single(action => action.Id == candidate.ActionId).Kind, candidate.Utility.Score))];
    }

    private static int PlayScore(ImmutableArray<(ActionKind Kind, int Score)> candidates) =>
        candidates.Single(candidate => candidate.Kind == ActionKind.PlayCard).Score;

    private static ActionKind Best(ImmutableArray<(ActionKind Kind, int Score)> candidates) =>
        candidates.MaxBy(candidate => candidate.Score).Kind;
}

public sealed class OfficialGatewayFactAttribute : FactAttribute
{
    public OfficialGatewayFactAttribute()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
            Skip = "Requires the provisioned official Gateway content lane.";
    }
}
