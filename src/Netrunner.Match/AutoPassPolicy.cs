using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Which of a player's priority windows the client passes for them (UI plan U3).
/// About 70% of all decisions are passes, so a player who opts in only stops
/// where passing is a real choice: an encounter (breaking, bioroid clicks) or
/// any rez. Rez windows are where the Corp rezzes its assets and upgrades, so
/// passing them for the player would leave it unable to rez at all (playtest,
/// 27 September). Paid abilities and pumps outside an encounter pass; the
/// player can still stop at the next window on request.
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
    public static bool OptionalWindow(IReadOnlyList<LegalAction> actions, bool inRun, bool encountering)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.Any(action => action.Kind == ActionKind.PassPriority)
            && actions.All(action => action.Kind switch
            {
                ActionKind.PassPriority or ActionKind.UseAbility => true,
                ActionKind.PumpBreaker => !encountering,
                _ => false,
            });
    }

    public static ActionId PassAction(IReadOnlyList<LegalAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.First(action => action.Kind == ActionKind.PassPriority).Id;
    }
}
