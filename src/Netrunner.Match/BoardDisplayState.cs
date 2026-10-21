using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// The board facts a viewer sees: credits, clicks, tags, points, zone counts,
/// the installed cards, advancement per card, the run's server and whether an
/// encounter is on. The presenter applies each event to the previous view's
/// state; any difference from the next view is a snap mismatch, meaning the
/// events didn't explain a visible change (UI plan U3 gate).
/// </summary>
public sealed record BoardDisplayState(
    ImmutableSortedDictionary<string, int> Advancement,
    int CorpPoints,
    int RunnerPoints,
    string? RunServer,
    bool Encountering)
{
    /// <summary>The zones whose card counts are public and shown on the board.</summary>
    public static IReadOnlyList<ZoneKind> CountedZones { get; } =
    [
        ZoneKind.CorpDeck, ZoneKind.CorpHand, ZoneKind.CorpDiscard, ZoneKind.RunnerStack, ZoneKind.RunnerGrip, ZoneKind.RunnerHeap,
    ];

    public int CorpCredits { get; init; }
    public int RunnerCredits { get; init; }
    public int CorpClicks { get; init; }
    public int RunnerClicks { get; init; }
    public int? Tags { get; init; }
    public ImmutableSortedDictionary<ZoneKind, int> ZoneCounts { get; init; } = ImmutableSortedDictionary<ZoneKind, int>.Empty;

    /// <summary>Presentation keys of the installed cards the viewer can follow.</summary>
    public ImmutableSortedSet<string> Installed { get; init; } = ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.Ordinal);

    public static BoardDisplayState From(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        bool active = view.Run is { EndReason: null };
        IEnumerable<string?> installed = view.Servers.SelectMany(server => server.IceKeys.Concat(server.RootKeys))
            .Concat(view.RunnerRig.Select(card => card.Id));
        return new(view.AdvancementByKey.Where(pair => pair.Value > 0).ToImmutableSortedDictionary(StringComparer.Ordinal),
            view.Corp.AgendaPoints, view.Runner.AgendaPoints,
            active ? view.Run!.AttackedServerId : null,
            active && view.Run!.Encounter is { Status: RunEncounterStatus.Initialized })
        {
            CorpCredits = view.Corp.Credits,
            RunnerCredits = view.Runner.Credits,
            CorpClicks = view.Corp.Clicks,
            RunnerClicks = view.Runner.Clicks,
            Tags = view.Runner.Tags,
            ZoneCounts = new Dictionary<ZoneKind, int>
            {
                [ZoneKind.CorpDeck] = view.CorpDeck.Count,
                [ZoneKind.CorpHand] = view.CorpHand.Count,
                [ZoneKind.CorpDiscard] = view.CorpDiscardCount,
                [ZoneKind.RunnerStack] = view.RunnerDeck.Count,
                [ZoneKind.RunnerGrip] = view.RunnerHand.Count,
                [ZoneKind.RunnerHeap] = view.RunnerHeap.Count,
            }.ToImmutableSortedDictionary(),
            Installed = installed.OfType<string>().ToImmutableSortedSet(StringComparer.Ordinal),
        };
    }

    public BoardDisplayState Apply(PresentationEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BoardDisplayState state = this;
        // A revealed or concealed card keeps what the board shows for it under its new key.
        if (value is { PreviousCardKey: { } previous, CardKey: { } now })
        {
            ImmutableSortedDictionary<string, int> advancement = Advancement;
            if (advancement.TryGetValue(previous, out int moved)) advancement = advancement.Remove(previous).SetItem(now, moved);
            ImmutableSortedSet<string> installed = Installed.Remove(previous);
            state = state with { Advancement = advancement, Installed = installed == Installed ? installed : installed.Add(now) };
        }
        return value.Kind switch
        {
            PresentationEventKind.CardAdvanced when value.CardKey is { } key =>
                state with { Advancement = state.Advancement.SetItem(key, value.Total ?? 0) },
            PresentationEventKind.AgendaScored => state with
            {
                Advancement = Without(state.Advancement, value.CardKey),
                CorpPoints = CorpPoints + (value.Amount ?? 0),
            },
            PresentationEventKind.AgendaStolen => state with
            {
                Advancement = Without(state.Advancement, value.CardKey),
                RunnerPoints = RunnerPoints + (value.Amount ?? 0),
            },
            PresentationEventKind.CardMoved => state.Moved(value),
            PresentationEventKind.CreditsChanged when value.Total is { } total => value.Side == PlayerSide.Corp
                ? state with { CorpCredits = total }
                : state with { RunnerCredits = total },
            PresentationEventKind.ClickSpent or PresentationEventKind.ClicksChanged
                when value.Total is { } clicks => value.Side == PlayerSide.Corp
                    ? state with { CorpClicks = clicks }
                    : state with { RunnerClicks = clicks },
            PresentationEventKind.TagsChanged => state with { Tags = value.Total },
            // Core clears both sides' clicks when the game ends.
            PresentationEventKind.GameEnded => state with { CorpClicks = 0, RunnerClicks = 0 },
            PresentationEventKind.RunStarted => state with { RunServer = value.ServerId },
            PresentationEventKind.EncounterStarted => state with { Encountering = true },
            PresentationEventKind.EncounterEnded => state with { Encountering = false },
            PresentationEventKind.RunEnded => state with { RunServer = null, Encountering = false },
            _ => state,
        };
    }

    private BoardDisplayState Moved(PresentationEvent value)
    {
        ImmutableSortedDictionary<ZoneKind, int> counts = ZoneCounts;
        if (value.FromZone is { } from && counts.TryGetValue(from, out int left)) counts = counts.SetItem(from, left - 1);
        if (value.ToZone is { } to && counts.TryGetValue(to, out int arrived)) counts = counts.SetItem(to, arrived + 1);
        ImmutableSortedSet<string> installed = Installed;
        ImmutableSortedDictionary<string, int> advancement = Advancement;
        bool wasInstalled = IsInstalled(value.FromZone), isInstalled = IsInstalled(value.ToZone);
        if (value.CardKey is { } key && wasInstalled != isInstalled)
        {
            installed = isInstalled ? installed.Add(key) : installed.Remove(key);
            if (!isInstalled) advancement = advancement.Remove(key);
        }
        return this with { ZoneCounts = counts, Installed = installed, Advancement = advancement };
    }

    public static bool IsInstalled(ZoneKind? zone) => zone is ZoneKind.ServerIce or ZoneKind.ServerRoot or ZoneKind.RunnerRig;

    /// <summary>Human-readable differences from <paramref name="settled"/>; empty when the events explained everything.</summary>
    public IReadOnlyList<string> Differences(BoardDisplayState settled)
    {
        ArgumentNullException.ThrowIfNull(settled);
        var differences = new List<string>();
        foreach (string key in Advancement.Keys.Union(settled.Advancement.Keys).Order(StringComparer.Ordinal))
        {
            int shown = Advancement.GetValueOrDefault(key), actual = settled.Advancement.GetValueOrDefault(key);
            if (shown != actual) differences.Add($"advancement {key}: {shown} → {actual}");
        }
        Compare("Corp points", CorpPoints, settled.CorpPoints);
        Compare("Runner points", RunnerPoints, settled.RunnerPoints);
        Compare("Corp credits", CorpCredits, settled.CorpCredits);
        Compare("Runner credits", RunnerCredits, settled.RunnerCredits);
        Compare("Corp clicks", CorpClicks, settled.CorpClicks);
        Compare("Runner clicks", RunnerClicks, settled.RunnerClicks);
        if (Tags != settled.Tags) differences.Add($"tags: {Tags} → {settled.Tags}");
        foreach (ZoneKind zone in CountedZones)
            Compare($"{zone} count", ZoneCounts.GetValueOrDefault(zone), settled.ZoneCounts.GetValueOrDefault(zone));
        foreach (string key in Installed.Except(settled.Installed)) differences.Add($"installed {key}: shown → gone");
        foreach (string key in settled.Installed.Except(Installed)) differences.Add($"installed {key}: missing → shown");
        if (RunServer != settled.RunServer) differences.Add($"run server: {RunServer ?? "none"} → {settled.RunServer ?? "none"}");
        if (Encountering != settled.Encountering) differences.Add($"encounter: {Encountering} → {settled.Encountering}");
        return differences;

        void Compare(string name, int shown, int actual)
        {
            if (shown != actual) differences.Add($"{name}: {shown} → {actual}");
        }
    }

    private static ImmutableSortedDictionary<string, int> Without(ImmutableSortedDictionary<string, int> advancement, string? key) =>
        key is null ? advancement : advancement.Remove(key);
}
