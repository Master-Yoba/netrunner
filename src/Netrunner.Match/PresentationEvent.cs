using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>What happened on the board, in the order it should be shown.</summary>
public enum PresentationEventKind
{
    TurnStarted,
    ClickSpent,
    CreditsChanged,
    CardDrawn,
    CardInstalled,
    CardRezzed,
    CardAdvanced,
    AgendaScored,
    AgendaStolen,
    RunStarted,
    IceApproached,
    EncounterStarted,
    SubroutinesBroken,
    SubroutineFired,
    EncounterEnded,
    ServerApproached,
    RunSuccessful,
    CardAccessed,
    RunEnded,
    /// <summary>
    /// A card changed zones. Every physical move gets one, so zone counts and
    /// the installed cards can be followed; draws, installs, plays, scores and
    /// steals also get their own event.
    /// </summary>
    CardMoved,
    /// <summary>An installed card was revealed or concealed without another event saying so.</summary>
    CardKeyChanged,
    /// <summary>An Operation or Event was played.</summary>
    CardPlayed,
    ClicksChanged,
    DamageDealt,
    TagsChanged,
    GameEnded,
    /// <summary>The Runner raised a breaker's strength.</summary>
    BreakerPumped,
    /// <summary>The Corp purged virus counters.</summary>
    VirusPurged,
}

/// <summary>
/// A viewer-safe board change for animation. Card references are presentation
/// keys from <see cref="CardKeyTracker"/>: a visible card's ID or an opaque
/// <c>tracked:N</c> handle, never a hidden card's instance ID.
/// </summary>
public sealed record PresentationEvent(PresentationEventKind Kind, PlayerSide? Side)
{
    /// <summary>The card the event is about, after the change.</summary>
    public string? CardKey { get; init; }

    /// <summary>The card's key before the change, when it differs (a revealed tracked card).</summary>
    public string? PreviousCardKey { get; init; }

    public string? ServerId { get; init; }

    /// <summary>The size of the change: credits, counters placed, subroutines broken, points.</summary>
    public int? Amount { get; init; }

    /// <summary>The resulting total: credits, counters on the card, clicks left.</summary>
    public int? Total { get; init; }

    /// <summary>The subroutine's printed position, from 0.</summary>
    public int? Ordinal { get; init; }

    public RunEndReason? RunEndReason { get; init; }
    public bool? Successful { get; init; }

    /// <summary>What caused the change, when known: the breaker, the paid card, the damage source.</summary>
    public string? SourceCardKey { get; init; }

    public ZoneKind? FromZone { get; init; }
    public ZoneKind? ToZone { get; init; }
    public DamageKind? Damage { get; init; }
    public GameWinReason? WinReason { get; init; }
}
