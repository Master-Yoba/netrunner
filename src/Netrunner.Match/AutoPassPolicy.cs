using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>What auto-pass needs to know about a priority window besides its actions.</summary>
public sealed record PriorityWindow(bool InRun, bool Encountering)
{
    /// <summary>The Runner is past every piece of ICE protecting the attacked server.</summary>
    public bool PastIce { get; init; }

    /// <summary>The last windows before the window owner's own turn: the other side's discard step, or the owner's draw step.</summary>
    public bool BeforeOwnTurn { get; init; }

    /// <summary>Cards installed in the attacked server, by instance ID.</summary>
    public IReadOnlySet<string> AttackedServerCards { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public static PriorityWindow From(GameState state, PlayerSide owner)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool inRun = state.RunState is { EndReason: null };
        RunState? run = inRun ? state.RunState : null;
        return new(inRun, run?.Encounter is { Status: RunEncounterStatus.Initialized })
        {
            PastIce = run?.CurrentIceIndex < 0,
            BeforeOwnTurn = BeforeTurn(owner, state.Turn.ActiveSide, state.Turn.Step),
            AttackedServerCards = run is null ? new HashSet<string>(StringComparer.Ordinal)
                : state.Zones.Where(zone => zone.ServerId == run.AttackedServer).SelectMany(zone => zone.CardIds)
                    .Select(id => id.Value).ToHashSet(StringComparer.Ordinal),
        };
    }

    public static PriorityWindow From(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        RunFocusViewModel? run = view.Run is { EndReason: null } active ? active : null;
        return new(run is not null, run?.Encounter is { Status: RunEncounterStatus.Initialized })
        {
            PastIce = run?.CurrentIceIndex < 0,
            BeforeOwnTurn = view.Turn.ActiveSide is { } side && view.Turn.Step is { } step && BeforeTurn(view.Viewer, side, step),
            AttackedServerCards = view.Servers.Where(server => server.Id == run?.AttackedServerId)
                .SelectMany(server => server.Root.Concat(server.Ice)).OfType<CardViewModel>().Select(card => card.Id)
                .ToHashSet(StringComparer.Ordinal),
        };
    }

    private static bool BeforeTurn(PlayerSide owner, PlayerSide active, TurnStep step) => active == owner
        ? step == TurnStep.CorpDraw
        : step is TurnStep.RunnerDiscard or TurnStep.CorpDiscard;
}

/// <summary>
/// Which of a player's priority windows the client passes for them (UI plan U3).
/// About 70% of all decisions are passes, so a player who opts in only stops
/// where passing is a real choice: an encounter (breaking, bioroid clicks), an
/// ICE rez, a score, or a rez at a moment that matters. After chiriboga's
/// usability rule, a rez matters in the last windows before the player's own
/// turn (when assets are usually rezzed) and during a run once the Runner is
/// past the ICE, for a card in the attacked server. Other rez windows, paid
/// abilities and pumps outside an encounter pass; the player can still stop at
/// the next window on request (playtests, 27 September).
/// </summary>
public static class AutoPassPolicy
{
    /// <summary>Whether a pass is the only legal action; such windows always pass when auto-pass is on at all.</summary>
    public static bool OnlyPass(IReadOnlyList<LegalAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.Count == 1 && actions[0].Kind == ActionKind.PassPriority;
    }

    /// <summary>Whether a window with other options is optional enough to pass for the player.</summary>
    public static bool OptionalWindow(IReadOnlyList<LegalAction> actions, PriorityWindow window)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(window);
        return actions.Any(action => action.Kind == ActionKind.PassPriority)
            && actions.All(action => action.Kind switch
            {
                ActionKind.PassPriority or ActionKind.UseAbility => true,
                ActionKind.PumpBreaker => !window.Encountering,
                ActionKind.RezCard => !RezMatters(action, window),
                _ => false,
            });
    }

    private static bool RezMatters(LegalAction action, PriorityWindow window) =>
        window.BeforeOwnTurn
        || window.InRun && window.PastIce && action.Parameters is RezCardParameters rez
            && window.AttackedServerCards.Contains(rez.CardId.Value);

    public static ActionId PassAction(IReadOnlyList<LegalAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.First(action => action.Kind == ActionKind.PassPriority).Id;
    }
}
