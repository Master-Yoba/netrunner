using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>A slice of a version's choices: mean regret with its bootstrap interval, and agreement with the teacher.</summary>
public sealed record RegretSummary(string Slice, int Roots, int Labelled, double MeanRegret, double Lower, double Upper,
    double Agreement, double AgreementWithinNoise);

/// <summary>Two versions on the roots both chose at: the mean of (first's regret − second's), with its bootstrap interval.</summary>
public sealed record RegretComparison(string First, string Second, int Roots, double MeanDifference, double Lower, double Upper)
{
    /// <summary>Whether the interval excludes zero.</summary>
    public bool Separated => Lower > 0 || Upper < 0;
}

/// <summary>The noise floor (bot plan 5.1): how often two independent labellings of the same roots disagree on the best group.</summary>
public sealed record RegretNoise(int Roots, double BestDisagreement, double MedianStandardError);

public static class RegretReport
{
    public static RegretSummary Summarize(string slice, IReadOnlyList<RegretChoice> choices, int bootstrap = 200, ulong seed = 1)
    {
        ArgumentNullException.ThrowIfNull(choices);
        double[] regrets = [.. choices.Where(choice => choice.Regret is not null).Select(choice => choice.Regret!.Value)];
        if (regrets.Length == 0) return new RegretSummary(slice, choices.Count, 0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        (double lower, double upper) = Interval(regrets, bootstrap, seed);
        return new RegretSummary(slice, choices.Count, regrets.Length, regrets.Average(), lower, upper,
            choices.Count(choice => choice.Agrees == true) / (double)regrets.Length,
            choices.Count(choice => choice.AgreesWithinNoise == true) / (double)regrets.Length);
    }

    public static RegretComparison Compare(string first, IReadOnlyList<RegretChoice> firstChoices, string second,
        IReadOnlyList<RegretChoice> secondChoices, int bootstrap = 200, ulong seed = 1)
    {
        ArgumentNullException.ThrowIfNull(firstChoices);
        ArgumentNullException.ThrowIfNull(secondChoices);
        var other = secondChoices.Where(choice => choice.Regret is not null).ToDictionary(choice => choice.RootId, choice => choice.Regret!.Value);
        double[] differences = [.. firstChoices.Where(choice => choice.Regret is not null && other.ContainsKey(choice.RootId))
            .OrderBy(choice => choice.RootId, StringComparer.Ordinal)
            .Select(choice => choice.Regret!.Value - other[choice.RootId])];
        if (differences.Length == 0) return new RegretComparison(first, second, 0, double.NaN, double.NaN, double.NaN);
        (double lower, double upper) = Interval(differences, bootstrap, seed);
        return new RegretComparison(first, second, differences.Length, differences.Average(), lower, upper);
    }

    public static RegretNoise Noise(IReadOnlyList<RegretLabel> first, IReadOnlyList<RegretLabel> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var again = second.ToDictionary(label => label.RootId);
        var pairs = first.Where(label => again.ContainsKey(label.RootId) && label.Best is not null && again[label.RootId].Best is not null)
            .Select(label => (First: label, Second: again[label.RootId])).ToList();
        double[] errors = [.. pairs.SelectMany(pair => pair.First.Values.Concat(pair.Second.Values))
            .Where(value => !double.IsNaN(value.StandardError)).Select(value => value.StandardError).Order()];
        return new RegretNoise(pairs.Count,
            pairs.Count == 0 ? double.NaN : pairs.Count(pair => pair.First.Best!.Group != pair.Second.Best!.Group) / (double)pairs.Count,
            errors.Length == 0 ? double.NaN : errors[errors.Length / 2]);
    }

    /// <summary>The 95% percentile bootstrap interval of a mean, resampling the values with replacement.</summary>
    private static (double Lower, double Upper) Interval(double[] values, int bootstrap, ulong seed)
    {
        if (bootstrap <= 0) return (double.NaN, double.NaN);
        var random = new DeterministicRandom(seed);
        double[] means = new double[bootstrap];
        for (int draw = 0; draw < bootstrap; draw++)
        {
            double sum = 0;
            for (int index = 0; index < values.Length; index++) sum += values[random.NextInt(values.Length)];
            means[draw] = sum / values.Length;
        }
        Array.Sort(means);
        return (means[(int)Math.Floor(0.025 * (bootstrap - 1))], means[(int)Math.Ceiling(0.975 * (bootstrap - 1))]);
    }
}
