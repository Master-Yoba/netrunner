using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>A6: the style check's measures, and the personality and Hard entrants it compares.</summary>
public sealed class PlayStyleTests
{
    [Fact]
    public void ASummaryAveragesEachSidesGames()
    {
        PlayStyleSample[] samples =
        [
            new(PlayerSide.Corp, "rush", 4, 3, 2, 1, 0, 0, null, 0),
            new(PlayerSide.Corp, "rush", null, 0, 4, 1, 2, 0, null, 0),
            new(PlayerSide.Runner, "standard", 6, 1, 0, 0, 0, 5, 1, 2),
        ];

        IReadOnlyList<PlayStyleSummary> rows = PlayStyle.Summarize(samples);

        PlayStyleSummary rush = Assert.Single(rows, row => row.Side == PlayerSide.Corp);
        Assert.Equal(("rush", 2, 4.0, 0.5, 1.5, 3.0, 1.0), (rush.Entrant, rush.Games, rush.FirstPointTurn!.Value, rush.ScoredByTurn6, rush.PointsByTurn8, rush.IceAtTurn6, rush.AssetsInstalled));
        PlayStyleSummary runner = Assert.Single(rows, row => row.Side == PlayerSide.Runner);
        Assert.Equal((5.0, 1.0, 2.0, 1.0), (runner.RunsByTurn6, runner.FirstRunTurn!.Value, runner.RigAtTurn6, runner.ScoredByTurn6));
    }

    [Fact]
    public void ASavedGameMeasuresBothSides()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", "unique-beside-copy")));

        IReadOnlyList<PlayStyleSample> samples = PlayStyle.Measure(saved, definition);

        Assert.Equal([PlayerSide.Corp, PlayerSide.Runner], samples.Select(sample => sample.Side));
        Assert.Equal((saved.Game.Corp, saved.Game.Runner), (samples[0].Entrant, samples[1].Entrant));
        Assert.True(samples[0].IceAtTurn6 > 0);
        Assert.True(samples[1].RunsByTurn6 > 0 && samples[1].FirstRunTurn <= PlayStyle.SetupTurn);
        Assert.Equal(samples, PlayStyle.Measure(saved, definition));
    }

    [Fact]
    public void APersonalityEntrantPlaysItsStyleOnlyInItsOwnSeat()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-discretion-advised~gateway-stolen-goods", 11);

        var corp = (OfflineAgent)RatingLadder.CreateAgent("rush", PlayerSide.Corp, definition);
        var runner = (OfflineAgent)RatingLadder.CreateAgent("rush", PlayerSide.Runner, definition);
        var hard = (OfflineAgent)RatingLadder.CreateAgent("hard", PlayerSide.Runner, definition);

        Assert.Equal(AgentPersonality.Rush, AgentPersonalities.Of(corp.Configuration));
        Assert.Equal(AgentPersonality.Balanced, AgentPersonalities.Of(runner.Configuration));
        Assert.Equal(("hard", RunnerHeuristicPolicy.PolicyVersion), (hard.Configuration.ProfileId, hard.Configuration.PolicyVersion));
    }

    [Fact]
    public void ThePlayersOpponentTakesItsDifficultyAndPersonality()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition("gateway-discretion-advised~gateway-stolen-goods", 11);

        OfflineAgent opponent = OfflineMatchFactory.CreatePlayerOpponent(definition, PlayerSide.Runner, "hard", AgentPersonality.Glacier);

        Assert.Equal(("hard", AgentPersonality.Glacier), (opponent.Configuration.ProfileId, AgentPersonalities.Of(opponent.Configuration)));
        Assert.Throws<ArgumentException>(() => OfflineMatchFactory.CreatePlayerOpponent(definition, PlayerSide.Corp, "standard", AgentPersonality.Glacier));
        Assert.Equal(["easy", "standard", "hard"], OfflineMatchFactory.Difficulties);
    }
}
