using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>Bot plan S1.5: the beam over lines of the root side's turn.</summary>
public sealed class TurnLineSearchTests
{
    [Fact]
    public void TheBeamFindsALineThatDepartsFromTheHeuristicLate()
    {
        // Only the heuristic's choice at the first later decision, then its third at the second, wins.
        (double, int) Play(int index, int[] ranks) => (ranks is [0, 2] ? 1 : 0.5, 3);

        (double[] values, int[] line) = TurnPlanner.BestLine(8, Play, 3);

        Assert.Equal([0, 2], line);
        Assert.Equal(Enumerable.Repeat(1.0, 8), values);
    }

    [Fact]
    public void ALineThatMeetsNoLaterDecisionIsTheHeuristicsContinuation()
    {
        int played = 0;
        (double, int) Play(int index, int[] ranks)
        {
            played++;
            return (index / 10.0, 0);
        }

        (double[] values, int[] line) = TurnPlanner.BestLine(8, Play, 3);

        Assert.Empty(line);
        Assert.Equal(Enumerable.Range(0, 8).Select(index => index / 10.0), values);
        Assert.Equal(2 + 8, played); // a quarter of the samples to screen it, then all of them
    }

    [Fact]
    public void ScreeningKeepsTheBeamAndTheBetterHalfIsPlayedOnEverySample()
    {
        var full = new HashSet<string>();
        (double, int) Play(int index, int[] ranks)
        {
            if (index >= 2) full.Add(string.Join(',', ranks));
            // Every departure at the first later decision is worse; the heuristic's own line is best.
            return (ranks.Length == 0 ? 0.9 : 0.9 - 0.1 * ranks.Sum(), 2);
        }

        (double[] values, int[] line) = TurnPlanner.BestLine(8, Play, 3);

        Assert.Empty(line);
        Assert.All(values, value => Assert.Equal(0.9, value, 9));
        // 1 + 2 + 6 lines screened; the better half (5) played on the rest of the samples.
        Assert.Equal(5, full.Count);
    }
}
