using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>Bot plan S1.5: the beam over lines of the root side's turn.</summary>
public sealed class TurnLineSearchTests
{
    [Fact]
    public void TheBeamFindsALineThatDepartsFromTheHeuristicLate()
    {
        // Only the heuristic's choice at the first later decision, then its third at the second, wins.
        (double, int) Screen(int index, int[] ranks) => (ranks is [0, 2] ? 1 : 0.5, 3);

        (double[] values, int[] line) = TurnPlanner.BestLine(8, Screen, 3);

        Assert.Equal([0, 2], line);
        Assert.Equal(Enumerable.Repeat(1.0, 8), values);
    }

    [Fact]
    public void ALineThatMeetsNoLaterDecisionIsTheHeuristicsContinuation()
    {
        int screened = 0, played = 0;
        (double, int) Screen(int index, int[] ranks)
        {
            screened++;
            return (0, 0);
        }
        (double, int) Play(int index, int[] ranks)
        {
            played++;
            return (index / 10.0, 0);
        }

        (double[] values, int[] line) = TurnPlanner.BestLine(8, Screen, 3, Play);

        // The heuristic's line met no later decision, so nothing is screened.
        Assert.Empty(line);
        Assert.Equal(Enumerable.Range(0, 8).Select(index => index / 10.0), values);
        Assert.Equal((0, 8), (screened, played));
    }

    [Fact]
    public void TheHeuristicsLineAndTheBestScreenedOtherArePlayedOn()
    {
        var played = new HashSet<string>();
        // The screen prefers departing at the first later decision; the play-out says the heuristic was right.
        (double, int) Screen(int index, int[] ranks) => (0.5 + 0.1 * ranks.Sum(), 2);
        (double, int) Play(int index, int[] ranks)
        {
            played.Add(string.Join(',', ranks));
            return (ranks.Length == 0 ? 0.9 : 0.8, 2);
        }

        (double[] values, int[] line) = TurnPlanner.BestLine(8, Screen, 3, Play);

        Assert.Empty(line);
        Assert.All(values, value => Assert.Equal(0.9, value, 9));
        Assert.Equal(["", "2,2"], played.Order());
    }

    [Fact]
    public void BeforeAPlayOutTheScreenSeesAQuarterOfTheSamples()
    {
        var screened = new HashSet<int>();
        (double, int) Screen(int index, int[] ranks)
        {
            screened.Add(index);
            return (0.5, 1);
        }

        TurnPlanner.BestLine(8, Screen, 3, (index, ranks) => (0.5, 1));

        Assert.Equal([0, 1], screened.Order());
    }
}
