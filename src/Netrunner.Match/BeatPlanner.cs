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
/// order: <see cref="Seconds"/> is the base duration before pacing (see
/// <see cref="BeatPacing"/>), and <see cref="Hold"/> marks a result worth
/// pausing on.
/// </summary>
public sealed record Beat(PresentationEventKind Kind, string Caption, double Seconds, BeatEmphasis Emphasis)
{
    public bool Hold { get; init; }
    public PlayerSide? Side { get; init; }
    public string? CardKey { get; init; }
    public string? ServerId { get; init; }
    public int? Ordinal { get; init; }
    public int? Amount { get; init; }
    public int? Total { get; init; }

    /// <summary>Whether a run that ended was successful.</summary>
    public bool? Successful { get; init; }

    /// <summary>The card that caused the beat: the breaker, the damage source.</summary>
    public string? SourceCardKey { get; init; }

    /// <summary>Other cards the beat moves, such as the cards trashed by damage.</summary>
    public IReadOnlyList<string> RelatedCardKeys { get; init; } = [];

    /// <summary>Where a moved card came from and went.</summary>
    public ZoneKind? FromZone { get; init; }

    public ZoneKind? ToZone { get; init; }
}

/// <summary>
/// Maps presentation events to beats for the whole U3 catalog: turns, draws,
/// credits, installs, plays, rezzes, advances, scores, runs, encounters,
/// breaks, fired subroutines, accesses, steals, trashes, damage, tags and the
/// end of the game. Clicks and encounter ends have no beat of their own; the
/// board shows them as they settle.
/// </summary>
public static class BeatPlanner
{
    // Base seconds before pacing: long enough to read a caption.
    public const double Brief = 0.5, Low = 0.7, Normal = 1.0, High = 1.3, Result = 1.6;

    public static IReadOnlyList<Beat> Plan(IReadOnlyList<PresentationEvent> events, MatchViewModel before, MatchViewModel after,
        Func<string, string> serverName)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(serverName);
        var names = new CardNames(before, after);
        var beats = new List<Beat>();
        // Payments named after a card are shown on that card's beat, once.
        var paid = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PresentationEvent value in events)
        {
            if (value is { Kind: PresentationEventKind.CreditsChanged, Amount: < 0, SourceCardKey: { } source })
                paid[source] = paid.GetValueOrDefault(source) - value.Amount.Value;
        }
        // Cards trashed by damage in this action belong to the damage beat.
        List<string> damaged = events.Any(value => value.Kind == PresentationEventKind.DamageDealt)
            ? events.Where(IsDamageTrash).Select(value => value.CardKey).OfType<string>().ToList()
            : [];
        for (int index = 0; index < events.Count; index++)
        {
            PresentationEvent value = events[index];
            // One action often both approaches and encounters, or reaches the
            // server and succeeds: show only the step that matters.
            if (Superseded(value, events.Skip(index + 1))) continue;
            if (damaged.Count > 0 && IsDamageTrash(value)) continue;
            string card = names.Name(value.CardKey);
            string server = value.ServerId is { } id ? serverName(id) : "a server";
            Beat? beat = value.Kind switch
            {
                PresentationEventKind.TurnStarted => new(value.Kind, $"{value.Side} turn {value.Amount}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.CardDrawn => new(value.Kind, $"{value.Side} draws {names.Drawn(value.CardKey)}", Brief,
                    BeatEmphasis.Low)
                { Amount = 1 },
                PresentationEventKind.CreditsChanged => Credits(value, beats, events.Take(index)),
                PresentationEventKind.CardInstalled => new(value.Kind,
                    $"{value.Side} installs {Installed(value, card, server, names)}{Cost(value.Amount)}", Low, BeatEmphasis.Low),
                PresentationEventKind.CardPlayed => new(value.Kind, $"{value.Side} plays {card}{Cost(value.Amount)}", Normal,
                    BeatEmphasis.Normal),
                PresentationEventKind.RunStarted => new(value.Kind, $"{value.Side} runs {server}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.IceApproached => new(value.Kind, $"Approaching {card}", Low, BeatEmphasis.Low),
                PresentationEventKind.CardRezzed => new(value.Kind, $"Corp rezzes {card}{Cost(value.Amount)}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.EncounterStarted => new(value.Kind,
                    $"Encountering {card} · {Count(value.Total ?? 0, "subroutine")}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.SubroutinesBroken => new(value.Kind,
                    $"Runner breaks {Count(value.Amount ?? 0, "subroutine")} on {card}"
                    + (value.SourceCardKey is null ? "" : $" with {names.Name(value.SourceCardKey)}") + Paid(value.SourceCardKey),
                    Normal, BeatEmphasis.Normal),
                PresentationEventKind.BreakerPumped => new(value.Kind, $"Runner boosts {card}{Paid(value.CardKey)}", Low, BeatEmphasis.Low),
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
                PresentationEventKind.CardMoved => Moved(value, card, names, Paid(value.CardKey)),
                PresentationEventKind.DamageDealt => Damage(value, damaged, names),
                PresentationEventKind.TagsChanged when value.Amount is { } delta and not 0 => new(value.Kind,
                    delta > 0 ? $"Runner takes {Count(delta, "tag")}" : $"Runner removes {Count(-delta, "tag")}", Normal, BeatEmphasis.Normal),
                PresentationEventKind.VirusPurged => new(value.Kind,
                    $"Corp purges virus counters{(value.Amount is > 0 ? $" · {Count(value.Amount.Value, "counter")} removed" : "")}",
                    Normal, BeatEmphasis.Normal),
                PresentationEventKind.GameEnded => new(value.Kind, GameEnded(value), Result, BeatEmphasis.High) { Hold = true },
                _ => null,
            };
            if (beat is null) continue;
            beat = beat with
            {
                Side = value.Side,
                CardKey = beat.CardKey ?? value.CardKey,
                ServerId = value.ServerId,
                Ordinal = value.Ordinal,
                Amount = beat.Amount ?? value.Amount,
                Total = value.Total,
                Successful = value.Successful,
                SourceCardKey = beat.SourceCardKey ?? value.SourceCardKey,
                FromZone = value.FromZone,
                ToZone = value.ToZone,
            };
            // Consecutive draws by one side read as one beat.
            if (beat.Kind == PresentationEventKind.CardDrawn && beats.Count > 0
                && beats[^1] is { Kind: PresentationEventKind.CardDrawn } last && last.Side == beat.Side)
            {
                int drawn = (last.Amount ?? 1) + 1;
                beats[^1] = last with
                {
                    Caption = $"{beat.Side} draws {Count(drawn, "card")}",
                    Amount = drawn,
                    RelatedCardKeys = [.. last.RelatedCardKeys, .. beat.CardKey is null ? Array.Empty<string>() : [beat.CardKey]],
                };
                continue;
            }
            // So do consecutive moves of one side's cards between the same zones.
            if (beat.Kind == PresentationEventKind.CardMoved && beats.Count > 0
                && beats[^1] is { Kind: PresentationEventKind.CardMoved } previous && previous.Side == beat.Side
                && previous.FromZone == beat.FromZone && previous.ToZone == beat.ToZone && !beat.Caption.Contains('·'))
            {
                int moved = (previous.Amount ?? 1) + 1;
                beats[^1] = previous with
                {
                    Caption = MovedMany(beat, moved),
                    Amount = moved,
                    RelatedCardKeys = [.. previous.RelatedCardKeys, .. beat.CardKey is null ? Array.Empty<string>() : [beat.CardKey]],
                };
                continue;
            }
            beats.Add(beat);
        }
        return beats;

        string Paid(string? key)
        {
            if (key is null || !paid.Remove(key, out int amount)) return "";
            return Cost(amount);
        }
    }

    /// <summary>
    /// A payment already shown on its install, rez or play needs no beat; a
    /// played card's gain is added to its beat; anything else is its own.
    /// </summary>
    private static Beat? Credits(PresentationEvent value, List<Beat> beats, IEnumerable<PresentationEvent> earlier)
    {
        if (value.Amount is not { } delta || delta == 0) return null;
        if (value.SourceCardKey is { } source)
        {
            int shown = beats.FindLastIndex(beat => beat.CardKey == source
                && beat.Kind is PresentationEventKind.CardInstalled or PresentationEventKind.CardRezzed or PresentationEventKind.CardPlayed);
            if (delta < 0) return null;
            if (shown >= 0)
            {
                beats[shown] = beats[shown] with { Caption = $"{beats[shown].Caption} · +{Count(delta, "credit")}" };
                return null;
            }
        }
        string verb = delta > 0 ? "gains" : earlier.Any(entry => entry.Kind == PresentationEventKind.SubroutineFired) ? "loses" : "spends";
        return new(PresentationEventKind.CreditsChanged, $"{value.Side} {verb} {Count(Math.Abs(delta), "credit")}", Brief, BeatEmphasis.Low);
    }

    private static string Installed(PresentationEvent value, string card, string server, CardNames names)
    {
        if (value.Side == PlayerSide.Runner) return card;
        string what = names.IsKnown(value.CardKey) ? card : names.IsIce(value.CardKey) ? "ICE" : "a card";
        return names.IsIce(value.CardKey) ? $"{what} protecting {server}" : $"{what} in {server}";
    }

    private static Beat? Moved(PresentationEvent value, string card, CardNames names, string paid)
    {
        (ZoneKind? from, ZoneKind? to) = (value.FromZone, value.ToZone);
        // Draws, installs, plays, scores and steals have their own beats; a
        // played card going to the discard after resolving is not news.
        if (from is null || to is null || from is ZoneKind.CorpPlayArea or ZoneKind.RunnerPlayArea or ZoneKind.BehaviorSetAside
            || to is ZoneKind.CorpPlayArea or ZoneKind.RunnerPlayArea or ZoneKind.BehaviorSetAside
            or ZoneKind.CorpScore or ZoneKind.RunnerScore
            || BoardDisplayState.IsInstalled(to)
            || from is ZoneKind.CorpDeck && to is ZoneKind.CorpHand || from is ZoneKind.RunnerStack && to is ZoneKind.RunnerGrip)
            return null;
        string subject = names.IsKnown(value.CardKey) ? card : $"a {value.Side} card";
        string caption = to switch
        {
            ZoneKind.CorpDiscard or ZoneKind.RunnerHeap when from is ZoneKind.CorpHand or ZoneKind.RunnerGrip =>
                $"{value.Side} discards {(names.IsKnown(value.CardKey) ? card : "a card")}",
            ZoneKind.CorpDiscard or ZoneKind.RunnerHeap => $"{Capitalize(subject)} is trashed",
            ZoneKind.CorpHand => $"{Capitalize(subject)} returns to HQ",
            ZoneKind.RunnerGrip => $"{Capitalize(subject)} returns to the grip",
            ZoneKind.CorpDeck => $"{Capitalize(subject)} goes into R&D",
            ZoneKind.RunnerStack => $"{Capitalize(subject)} goes into the stack",
            ZoneKind.CorpRemovedFromGame or ZoneKind.RunnerRemovedFromGame => $"{Capitalize(subject)} is removed from the game",
            _ => $"{Capitalize(subject)} moves",
        };
        return new(PresentationEventKind.CardMoved, caption + paid, Low, BeatEmphasis.Normal);
    }

    private static string MovedMany(Beat beat, int count)
    {
        string cards = Count(count, "card");
        string caption = beat.ToZone switch
        {
            ZoneKind.CorpDiscard or ZoneKind.RunnerHeap when beat.FromZone is ZoneKind.CorpHand or ZoneKind.RunnerGrip =>
                $"{beat.Side} discards {cards}",
            ZoneKind.CorpDiscard or ZoneKind.RunnerHeap => $"{cards} are trashed",
            ZoneKind.CorpHand => $"{cards} return to HQ",
            ZoneKind.RunnerGrip => $"{cards} return to the grip",
            ZoneKind.CorpDeck => $"{cards} go into R&D",
            ZoneKind.RunnerStack => $"{cards} go into the stack",
            ZoneKind.CorpRemovedFromGame or ZoneKind.RunnerRemovedFromGame => $"{cards} are removed from the game",
            _ => $"{cards} move",
        };
        return Capitalize(caption);
    }

    private static Beat Damage(PresentationEvent value, List<string> trashed, CardNames names)
    {
        string kind = value.Damage switch
        {
            DamageKind.Meat => "meat",
            DamageKind.Core => "core",
            _ => "net",
        };
        string lost = trashed.Count == 0 ? "" : $" · loses {string.Join(", ", trashed.Select(names.Name))}";
        return new(PresentationEventKind.DamageDealt, $"Runner takes {value.Amount} {kind} damage{lost}", High, BeatEmphasis.High)
        { RelatedCardKeys = trashed };
    }

    private static bool IsDamageTrash(PresentationEvent value) =>
        value is { Kind: PresentationEventKind.CardMoved, FromZone: ZoneKind.RunnerGrip, ToZone: ZoneKind.RunnerHeap };

    private static bool Superseded(PresentationEvent value, IEnumerable<PresentationEvent> later) => value.Kind switch
    {
        PresentationEventKind.IceApproached => later.Any(next => next.Kind == PresentationEventKind.EncounterStarted
            && next.CardKey == value.CardKey),
        PresentationEventKind.ServerApproached => later.Any(next => next.Kind == PresentationEventKind.RunSuccessful),
        _ => false,
    };

    private static string RunEnded(PresentationEvent value) => value.Successful == true
        ? "Run complete"
        : value.RunEndReason switch
        {
            RunEndReason.JackedOut => "Runner jacks out",
            RunEndReason.EndedByRule => "The run is ended",
            _ => "Run over",
        };

    private static string GameEnded(PresentationEvent value) => value.WinReason switch
    {
        GameWinReason.Flatline => "Runner is flatlined · Corp wins",
        GameWinReason.DeckOut => $"R&D is empty · {value.Side} wins",
        _ => $"{value.Side} wins on agenda points",
    };

    private static string Cost(int? amount) => amount is > 0 ? $" · {Count(amount.Value, "credit")}" : "";

    private static string Count(int value, string noun) => $"{value} {noun}{(value == 1 ? "" : "s")}";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    internal static IEnumerable<CardViewModel> Cards(MatchViewModel view) =>
        view.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<CardViewModel>()
            .Concat(view.CorpScore).Concat(view.RunnerScore).Concat(view.CorpDiscard).Concat(view.RunnerHeap)
            .Concat(view.RunnerRig).Concat(view.PlayArea).Concat(view.CorpHand.VisibleCards).Concat(view.RunnerHand.VisibleCards)
            .Concat(view.Run is { } run ? run.Access.Accessed.Concat(run.Access.Candidates).OfType<CardViewModel>() : []);

    /// <summary>A known card's title, or how the viewer sees a face-down one.</summary>
    private sealed class CardNames(MatchViewModel before, MatchViewModel after)
    {
        public string Name(string? key)
        {
            if (key is null) return "a card";
            if (Known(key) is { } known) return known.Title;
            return IsIce(key) ? "face-down ICE" : "a face-down card";
        }

        public string Drawn(string? key) => key is not null && Known(key) is { } known ? known.Title : "a card";

        public bool IsKnown(string? key) => key is not null && Known(key) is not null;

        public bool IsIce(string? key) => key is not null && after.Servers.Concat(before.Servers).Any(server => server.IceKeys.Contains(key));

        private CardViewModel? Known(string key) =>
            Cards(after).Concat(Cards(before)).FirstOrDefault(card => card.Id == key && card.IsKnown);
    }
}

/// <summary>
/// How long beats play (UI plan U3 pacing). The bot still decides instantly;
/// the presenter drains publications at this pace. The viewer's own
/// low-emphasis beats are brief because the player just did them, and "fast
/// opponent" makes the opponent's brief too.
/// </summary>
public sealed record BeatPacing(double Speed = 1, bool FastOpponent = false, bool ReducedMotion = false)
{
    /// <summary>A compressed low-emphasis beat, in seconds before speed.</summary>
    public const double Compressed = 0.25;

    /// <summary>Extra time on a held result, when motion is on.</summary>
    public const double HoldSeconds = 0.6;

    public double Seconds(Beat beat, PlayerSide viewer)
    {
        ArgumentNullException.ThrowIfNull(beat);
        double seconds = beat.Emphasis == BeatEmphasis.Low && (FastOpponent || beat.Side == viewer)
            ? Math.Min(beat.Seconds, Compressed)
            : beat.Seconds;
        // Reduced motion removes movement, not reading time.
        if (beat.Hold && !ReducedMotion) seconds += HoldSeconds;
        return seconds / Math.Max(0.1, Speed);
    }
}
