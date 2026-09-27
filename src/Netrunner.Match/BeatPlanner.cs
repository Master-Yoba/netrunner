using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.Match;

public enum BeatEmphasis
{
    Low,
    Normal,
    High,
}

/// <summary>
/// One readable moment on the board (UI plan U3). The client plays beats in
/// order: <see cref="Seconds"/> is the base duration before the speed setting,
/// and <see cref="Hold"/> marks a result worth pausing on.
/// </summary>
public sealed record Beat(PresentationEventKind Kind, string Caption, double Seconds, BeatEmphasis Emphasis)
{
    public bool Hold { get; init; }
    public PlayerSide? Side { get; init; }
    public string? CardKey { get; init; }
    public string? ServerId { get; init; }
    public int? Ordinal { get; init; }
    public int? Total { get; init; }
}

/// <summary>
/// Maps presentation events to beats for the Wave 2 slice: run start,
/// approach, encounter, subroutines broken or fired, run end, advance, rez,
/// score and steal. Installs, draws and credits are shown by board motion
/// (U2) and get no beat.
/// </summary>
public static class BeatPlanner
{
    // Base seconds before the speed setting: long enough to read a caption.
    private const double Low = 0.7, Normal = 1.0, High = 1.3, Result = 1.6;

    public static IReadOnlyList<Beat> Plan(IReadOnlyList<PresentationEvent> events, MatchViewModel before, MatchViewModel after,
        Func<string, string> serverName)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(serverName);
        var beats = new List<Beat>();
        for (int index = 0; index < events.Count; index++)
        {
            PresentationEvent value = events[index];
            // One action often both approaches and encounters, or reaches the
            // server and succeeds: show only the step that matters.
            if (Superseded(value, events.Skip(index + 1))) continue;
            string card = Name(value.CardKey, before, after);
            string server = value.ServerId is { } id ? serverName(id) : "a server";
            Beat? beat = value.Kind switch
            {
                PresentationEventKind.TurnStarted => new(value.Kind, $"{value.Side} turn {value.Amount}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.RunStarted => new(value.Kind, $"{value.Side} runs {server}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.IceApproached => new(value.Kind, $"Approaching {card}", Low, BeatEmphasis.Low),
                PresentationEventKind.CardRezzed => new(value.Kind, $"Corp rezzes {card}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.EncounterStarted => new(value.Kind,
                    $"Encountering {card} · {Count(value.Total ?? 0, "subroutine")}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.SubroutinesBroken => new(value.Kind,
                    $"Runner breaks {Count(value.Amount ?? 0, "subroutine")} on {card}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.SubroutineFired => new(value.Kind,
                    $"{Capitalize(card)}: subroutine {(value.Ordinal ?? 0) + 1} fires", High, BeatEmphasis.High),
                PresentationEventKind.ServerApproached => new(value.Kind, $"Runner reaches {server}", Low, BeatEmphasis.Low),
                PresentationEventKind.RunSuccessful => new(value.Kind, $"Run on {server} is successful", Normal, BeatEmphasis.Normal),
                PresentationEventKind.CardAccessed => new(value.Kind, $"Runner accesses {card}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.RunEnded => new(value.Kind, RunEnded(value), High, BeatEmphasis.High) { Hold = true },
                PresentationEventKind.CardAdvanced => new(value.Kind,
                    $"Corp advances {card} · {Count(value.Total ?? 0, "counter")}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.AgendaScored => new(value.Kind, $"Corp scores {card} · +{value.Amount}", Result, BeatEmphasis.High)
                { Hold = true },
                PresentationEventKind.AgendaStolen => new(value.Kind, $"Runner steals {card} · +{value.Amount}", Result, BeatEmphasis.High)
                { Hold = true },
                _ => null,
            };
            if (beat is not null)
                beats.Add(beat with
                {
                    Side = value.Side,
                    CardKey = value.CardKey,
                    ServerId = value.ServerId,
                    Ordinal = value.Ordinal,
                    Total = value.Total,
                });
        }
        return beats;
    }

    private static bool Superseded(PresentationEvent value, IEnumerable<PresentationEvent> later) => value.Kind switch
    {
        PresentationEventKind.IceApproached => later.Any(next => next.Kind == PresentationEventKind.EncounterStarted
            && next.CardKey == value.CardKey),
        PresentationEventKind.ServerApproached => later.Any(next => next.Kind == PresentationEventKind.RunSuccessful),
        _ => false,
    };

    private static string RunEnded(PresentationEvent value) => value.Successful == true
        ? "Run successful"
        : value.RunEndReason switch
        {
            RunEndReason.JackedOut => "Runner jacks out",
            RunEndReason.EndedByRule => "The run is ended",
            _ => "Run over",
        };

    private static string Count(int value, string noun) => $"{value} {noun}{(value == 1 ? "" : "s")}";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>A known card's title, or how the viewer sees a face-down one.</summary>
    private static string Name(string? key, MatchViewModel before, MatchViewModel after)
    {
        if (key is null) return "a card";
        foreach (MatchViewModel view in new[] { after, before })
        {
            CardViewModel? known = Cards(view).FirstOrDefault(card => card.Id == key && card.IsKnown);
            if (known is not null) return known.Title;
        }
        bool ice = after.Servers.Concat(before.Servers).Any(server => server.IceKeys.Contains(key));
        return ice ? "face-down ICE" : "a face-down card";
    }

    internal static IEnumerable<CardViewModel> Cards(MatchViewModel view) =>
        view.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<CardViewModel>()
            .Concat(view.CorpScore).Concat(view.RunnerScore).Concat(view.CorpDiscard).Concat(view.RunnerHeap)
            .Concat(view.RunnerRig).Concat(view.PlayArea)
            .Concat(view.Run is { } run ? run.Access.Accessed.Concat(run.Access.Candidates).OfType<CardViewModel>() : []);
}

/// <summary>
/// The slice's board facts as a viewer sees them: advancement per card, points,
/// the run's server and whether an encounter is on. The presenter applies each
/// event to the previous view's state; any difference from the next view is a
/// snap mismatch, meaning the events didn't explain a visible change (U3 gate).
/// </summary>
public sealed record BoardDisplayState(
    ImmutableSortedDictionary<string, int> Advancement,
    int CorpPoints,
    int RunnerPoints,
    string? RunServer,
    bool Encountering)
{
    public static BoardDisplayState From(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        bool active = view.Run is { EndReason: null };
        return new(view.AdvancementByKey.Where(pair => pair.Value > 0).ToImmutableSortedDictionary(StringComparer.Ordinal),
            view.Corp.AgendaPoints, view.Runner.AgendaPoints,
            active ? view.Run!.AttackedServerId : null,
            active && view.Run!.Encounter is { Status: RunEncounterStatus.Initialized });
    }

    public BoardDisplayState Apply(PresentationEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ImmutableSortedDictionary<string, int> advancement = Advancement;
        if (value.PreviousCardKey is { } previous && advancement.TryGetValue(previous, out int moved) && value.CardKey is { } now)
            advancement = advancement.Remove(previous).SetItem(now, moved);
        return value.Kind switch
        {
            PresentationEventKind.CardAdvanced when value.CardKey is { } key =>
                this with { Advancement = advancement.SetItem(key, value.Total ?? 0) },
            PresentationEventKind.AgendaScored => this with
            {
                Advancement = Without(advancement, value.CardKey),
                CorpPoints = CorpPoints + (value.Amount ?? 0),
            },
            PresentationEventKind.AgendaStolen => this with
            {
                Advancement = Without(advancement, value.CardKey),
                RunnerPoints = RunnerPoints + (value.Amount ?? 0),
            },
            PresentationEventKind.CardLeftPlay => this with { Advancement = Without(advancement, value.CardKey) },
            PresentationEventKind.RunStarted => this with { Advancement = advancement, RunServer = value.ServerId },
            PresentationEventKind.EncounterStarted => this with { Advancement = advancement, Encountering = true },
            PresentationEventKind.EncounterEnded => this with { Advancement = advancement, Encountering = false },
            PresentationEventKind.RunEnded => this with { Advancement = advancement, RunServer = null, Encountering = false },
            _ => this with { Advancement = advancement },
        };
    }

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
        if (CorpPoints != settled.CorpPoints) differences.Add($"Corp points: {CorpPoints} → {settled.CorpPoints}");
        if (RunnerPoints != settled.RunnerPoints) differences.Add($"Runner points: {RunnerPoints} → {settled.RunnerPoints}");
        if (RunServer != settled.RunServer) differences.Add($"run server: {RunServer ?? "none"} → {settled.RunServer ?? "none"}");
        if (Encountering != settled.Encountering) differences.Add($"encounter: {Encountering} → {settled.Encountering}");
        return differences;
    }

    private static ImmutableSortedDictionary<string, int> Without(ImmutableSortedDictionary<string, int> advancement, string? key) =>
        key is null ? advancement : advancement.Remove(key);
}
