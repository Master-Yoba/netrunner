using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>A frozen viewer-scoped account of an accepted action, independent of animation.</summary>
public sealed record MatchHistoryEntry(
    string Key,
    long ActionSequence,
    int TurnNumber,
    PlayerSide Side,
    string Summary,
    IReadOnlyList<string> Details)
{
    public IReadOnlyList<CardViewModel> Cards { get; init; } = Array.Empty<CardViewModel>();
}

/// <summary>Exactly-once in-session history; rendering/skip never owns retention.</summary>
public sealed class MatchHistoryStore
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly List<MatchHistoryEntry> _entries = [];

    public IReadOnlyList<MatchHistoryEntry> Entries => _entries.AsReadOnly();

    public void Append(IEnumerable<MatchHistoryEntry> entries)
    {
        foreach (MatchHistoryEntry entry in entries)
        {
            if (_keys.Add(entry.Key)) _entries.Add(entry);
        }
    }

    public void Clear()
    {
        _keys.Clear();
        _entries.Clear();
    }
}
