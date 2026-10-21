using System.Diagnostics;

namespace Netrunner.AI;

/// <summary>One profiled segment: its name, total time and the number of times it ran.</summary>
public sealed record ProfileSegment(string Name, TimeSpan Elapsed, long Count)
{
    public double MeanMicroseconds => Count == 0 ? 0 : Elapsed.TotalMicroseconds / Count;
}

/// <summary>
/// Bot plan S0.1: where a game's or a plan's time goes. A profiler sets <see cref="Current"/> for the code it measures;
/// the match runner, the lifecycle host and the planner add their segments to it. It is null everywhere else, so
/// ordinary play only pays a null check. It times work and never changes it: no decision reads it.
/// </summary>
public sealed class ProfileTimings
{
    private static readonly AsyncLocal<ProfileTimings?> Ambient = new();
    private readonly Dictionary<string, (long Ticks, long Count)> _segments = new(StringComparer.Ordinal);

    /// <summary>The timings the current flow adds to, or null when nothing is being profiled.</summary>
    public static ProfileTimings? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    /// <summary>A timestamp to pass to <see cref="Add(string, long)"/>, or 0 when nothing is being profiled.</summary>
    public static long Start() => Ambient.Value is null ? 0 : Stopwatch.GetTimestamp();

    /// <summary>Adds the time since <paramref name="started"/> (from <see cref="Start"/>) to a segment of the current timings.</summary>
    public static void Stop(string segment, long started)
    {
        if (started != 0) Ambient.Value?.Add(segment, started);
    }

    public void Add(string segment, long started) => AddTicks(segment, Stopwatch.GetTimestamp() - started);

    public void AddTicks(string segment, long ticks, long count = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(segment);
        (long total, long runs) = _segments.GetValueOrDefault(segment);
        _segments[segment] = (total + ticks, runs + count);
    }

    public IReadOnlyList<ProfileSegment> Segments => [.. _segments
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new ProfileSegment(pair.Key, Stopwatch.GetElapsedTime(0, pair.Value.Ticks), pair.Value.Count))];

    public ProfileSegment? this[string segment] => _segments.TryGetValue(segment, out (long Ticks, long Count) value)
        ? new ProfileSegment(segment, Stopwatch.GetElapsedTime(0, value.Ticks), value.Count) : null;
}
