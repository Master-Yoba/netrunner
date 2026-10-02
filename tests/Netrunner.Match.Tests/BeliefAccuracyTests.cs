using Netrunner.Cards;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>A9a's gate on the saved-game fixtures: each tier's tracker predicts the Corp's hidden cards better than its prior.</summary>
public sealed class BeliefAccuracyTests
{
    [Fact]
    public void EveryTierBeatsItsPriorAndEasyForgets()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games");
        IReadOnlyList<BeliefScore> scores = BeliefAccuracy.Score(Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal)
            .SelectMany(LadderGameArchive.Read));

        foreach (BeliefScore score in scores)
        {
            Assert.True(score.Slots > 1000 && score.HqSamples > 300, $"{score.Tier}: too few samples.");
            Assert.True(score.SlotLogLoss < score.SlotPriorLogLoss, $"{score.Tier} slot log loss {score.SlotLogLoss} against {score.SlotPriorLogLoss}.");
            Assert.True(score.SlotTop1 > score.SlotPriorTop1, $"{score.Tier} slot top-1 {score.SlotTop1} against {score.SlotPriorTop1}.");
            Assert.True(score.HqAgendaError < score.HqPriorAgendaError, $"{score.Tier} HQ agenda error {score.HqAgendaError} against {score.HqPriorAgendaError}.");
        }
        BeliefScore easy = scores.Single(score => score.Tier == "easy"), standard = scores.Single(score => score.Tier == "standard");
        Assert.True(easy.SlotLogLoss > standard.SlotLogLoss && easy.HqAgendaError > standard.HqAgendaError, "Easy's forgetting doesn't show.");
    }

    [Fact]
    public void ScoringIsDeterministic()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string game = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", "playtest-w5-urtica");
        Assert.Equal(BeliefAccuracy.Score(LadderGameArchive.Read(game)), BeliefAccuracy.Score(LadderGameArchive.Read(game)));
    }
}
