using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// One deck pairing's paired comparison of a candidate against a baseline in a
/// seat: the mean seat-score difference over the deals it appeared in, with a
/// bootstrap interval over those deals.
/// </summary>
public sealed record LadderDeckComparison(string ContentId, PlayerSide Side, string Baseline, string Candidate, int Deals,
    double ScoreDifference, double Lower, double Upper)
{
    /// <summary>
    /// A warning, not a gate: the candidate is worse on this pairing by more than
    /// 14 score points (about 100 Elo near an even score), with the interval clear of zero.
    /// </summary>
    public bool Warning => ScoreDifference <= -WarningScoreDifference && Upper < 0;

    public const double WarningScoreDifference = 0.14;
}

/// <summary>
/// A candidate's paired comparison against a baseline in one seat over every deal (after chiriboga's seeded gates): the
/// mean seat-score difference, its bootstrap interval over deals, and how many deals the candidate changed at all. A
/// change that alters no deal says nothing, and one that alters few is judged by those few without being diluted: the
/// unchanged deals add nothing to the difference or its noise.
/// </summary>
public sealed record LadderPairedComparison(PlayerSide Side, string Baseline, string Candidate, int Deals, int ChangedDeals,
    double ScoreDifference, double Lower, double Upper)
{
    public bool Clear => Lower > 0 || Upper < 0;
}

public static partial class RatingLadder
{
    /// <summary>
    /// A fixed game budget over a deck pool (A1b): <paramref name="deals"/> deals,
    /// each a seed on one content ID taken in turn from <paramref name="contentIds"/>,
    /// and every Corp entrant meets every Runner entrant on every deal. Adding decks
    /// to the pool then costs no time; it spreads the same deals over more pairings.
    /// </summary>
    public static IReadOnlyList<LadderGame> PlanDeals(IReadOnlyList<string> entrants, int deals, ulong baseSeed,
        IReadOnlyList<string> contentIds, int firstDeal = 0)
    {
        ArgumentNullException.ThrowIfNull(entrants);
        ArgumentNullException.ThrowIfNull(contentIds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deals);
        ArgumentOutOfRangeException.ThrowIfNegative(firstDeal);
        if (contentIds.Count == 0) throw new ArgumentException("At least one content ID is required.", nameof(contentIds));
        foreach (string entrant in entrants)
            if (!EntrantOrder.Contains(entrant)) throw new ArgumentException($"Unknown ladder entrant '{entrant}'.", nameof(entrants));
        var games = new List<LadderGame>();
        for (int deal = firstDeal; deal < firstDeal + deals; deal++)
        {
            string content = contentIds[deal % contentIds.Count];
            ulong gameSeed = RulesFuzzRunner.DeriveGameSeed(baseSeed, deal);
            foreach (string corp in entrants)
                foreach (string runner in entrants)
                    games.Add(new LadderGame($"{corp}-vs-{runner}-{content}-{deal + 1:D5}", content, gameSeed, corp, runner, deal));
        }
        return games;
    }

    /// <summary>
    /// Per-deck diagnostics for a two-entrant ladder: in each seat and on each
    /// content ID, the candidate's paired score difference against the baseline.
    /// On a deal, the candidate and the baseline in one seat both faced every
    /// opponent entrant on the same seed, so the difference cancels the deal.
    /// </summary>
    public static IReadOnlyList<LadderDeckComparison> CompareByDeck(IReadOnlyList<LadderGameResult> results, string baseline,
        string candidate, int bootstrap = 200, ulong bootstrapSeed = 1)
    {
        ArgumentNullException.ThrowIfNull(results);
        var comparisons = new List<LadderDeckComparison>();
        var random = new DeterministicRandom(bootstrapSeed);
        LadderGameResult[] scored = results.Where(result => result.Error is null && result.Game.Deal >= 0).ToArray();
        foreach (IGrouping<string, LadderGameResult> content in scored.GroupBy(result => result.Game.ContentId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                // Per deal: mean seat score as the candidate minus as the baseline, over the opponents faced.
                double[] differences = DealDifferences(content, side, baseline, candidate);
                if (differences.Length == 0) continue;
                double mean = differences.Average();
                var draws = new List<double>(bootstrap);
                for (int iteration = 0; iteration < bootstrap; iteration++)
                {
                    double sum = 0;
                    for (int index = 0; index < differences.Length; index++) sum += differences[random.NextInt(differences.Length)];
                    draws.Add(sum / differences.Length);
                }
                draws.Sort();
                comparisons.Add(new LadderDeckComparison(content.Key, side, baseline, candidate, differences.Length, mean,
                    draws.Count == 0 ? mean : Percentile(draws, 0.025), draws.Count == 0 ? mean : Percentile(draws, 0.975)));
            }
        }
        return comparisons;
    }

    /// <summary>
    /// The paired comparison of a two-entrant ladder in each seat, over all its deals: per deal, the candidate's mean seat
    /// score less the baseline's against the same opponents on the same seed.
    /// </summary>
    public static IReadOnlyList<LadderPairedComparison> ComparePaired(IReadOnlyList<LadderGameResult> results, string baseline,
        string candidate, int bootstrap = 200, ulong bootstrapSeed = 1, double confidence = 0.95)
    {
        ArgumentNullException.ThrowIfNull(results);
        double tail = (1 - confidence) / 2;
        var random = new DeterministicRandom(bootstrapSeed);
        LadderGameResult[] scored = results.Where(result => result.Error is null && result.Game.Deal >= 0).ToArray();
        var comparisons = new List<LadderPairedComparison>();
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            double[] differences = DealDifferences(scored, side, baseline, candidate);
            if (differences.Length == 0) continue;
            double mean = differences.Average();
            var draws = new List<double>(bootstrap);
            for (int iteration = 0; iteration < bootstrap; iteration++)
            {
                double sum = 0;
                for (int index = 0; index < differences.Length; index++) sum += differences[random.NextInt(differences.Length)];
                draws.Add(sum / differences.Length);
            }
            draws.Sort();
            comparisons.Add(new LadderPairedComparison(side, baseline, candidate, differences.Length,
                differences.Count(difference => Math.Abs(difference) > 1e-9), mean,
                draws.Count == 0 ? mean : Percentile(draws, tail), draws.Count == 0 ? mean : Percentile(draws, 1 - tail)));
        }
        return comparisons;
    }

    /// <summary>Per deal, the candidate's mean seat score less the baseline's, over the opponents each faced; deals without both are left out.</summary>
    private static double[] DealDifferences(IEnumerable<LadderGameResult> scored, PlayerSide side, string baseline, string candidate) =>
        scored.GroupBy(result => result.Game.Deal)
            .Select(deal =>
            {
                double Seat(string entrant) => deal.Where(result => (side == PlayerSide.Corp ? result.Game.Corp : result.Game.Runner) == entrant)
                    .Select(result => side == PlayerSide.Corp ? result.CorpScore : 1 - result.CorpScore).DefaultIfEmpty(double.NaN).Average();
                return Seat(candidate) - Seat(baseline);
            })
            .Where(value => !double.IsNaN(value)).ToArray();

    /// <summary>
    /// Sequential stopping: every seat's adjacent difference is clear, above or below
    /// zero, at the given report's confidence. Look with a wider interval than the
    /// final report's (the CLI uses 99%), since each extra look adds a chance to stop wrongly.
    /// </summary>
    public static bool IsClear(LadderReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Differences.Count > 0 && report.Differences.All(difference => difference.Lower > 0 || difference.Upper < 0);
    }
}
