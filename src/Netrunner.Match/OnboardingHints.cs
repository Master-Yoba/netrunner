using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>One first-game hint: a short title and a sentence or two of how to play.</summary>
public sealed record OnboardingHint(string Id, string Title, string Text);

/// <summary>
/// First-game hints (U6). Each hint is shown once, the first time the player's
/// own decision offers what it explains, and is then recorded as seen by the
/// client. Hints read only the viewer's own decision and legal actions.
/// </summary>
public static class OnboardingHints
{
    public const string Basics = "basics";
    public const string CorpIce = "corp-ice";
    public const string CorpAdvance = "corp-advance";
    public const string CorpRez = "corp-rez";
    public const string RunnerRun = "runner-run";
    public const string RunnerBreak = "runner-break";
    public const string RunnerAccess = "runner-access";

    private static readonly OnboardingHint[] All =
    [
        new(Basics, "Your turn",
            "Each click is one action: draw a card, gain a credit, or drag a card from your hand onto the table. End your turn when your clicks are spent."),
        new(CorpIce, "Protect your servers",
            "Drag ICE onto a server to install it in front, face down. It costs 1 credit for each piece of ICE already protecting that server."),
        new(CorpAdvance, "Advance and score",
            "Click a card in a remote to advance it for a click and a credit. Once an agenda has enough counters, score it. Face-down cards keep the Runner guessing which is which."),
        new(CorpRez, "Rez when it matters",
            "Your ICE stays off until you pay its rez cost. Rez it when the Runner approaches, so they can't plan around it."),
        new(RunnerRun, "Make a run",
            "Click HQ, R&D, Archives or a remote and choose Run. Get past the ICE in front to access the Corp's cards, and steal agendas to score."),
        new(RunnerBreak, "Break the ICE",
            "Use an icebreaker that matches the ICE to break its subroutines, raising its strength first if needed. Subroutines you leave unbroken fire when you continue."),
        new(RunnerAccess, "Accessing cards",
            "Steal agendas for points. Other cards can often be trashed by paying their trash cost, so the Corp can't use them."),
    ];

    public static IReadOnlyList<OnboardingHint> Hints => All;

    /// <summary>
    /// The first unseen hint the viewer's current decision calls for, or null.
    /// Only a decision the viewer owns counts.
    /// </summary>
    public static OnboardingHint? Next(MatchViewModel view, IReadOnlySet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(seen);
        if (view.Outcome.IsTerminal || view.Decision is not { } decision || decision.OwnerSide != view.Viewer) return null;
        HashSet<ActionKind> kinds = [.. view.LegalActions.Select(action => action.Kind)];
        foreach (string id in Candidates(view.Viewer, kinds))
            if (!seen.Contains(id))
                return All.First(hint => hint.Id == id);
        return null;
    }

    private static IEnumerable<string> Candidates(PlayerSide viewer, HashSet<ActionKind> kinds)
    {
        bool actionWindow = kinds.Contains(ActionKind.GainCredit) || kinds.Contains(ActionKind.DrawCard);
        if (viewer == PlayerSide.Corp)
        {
            if (kinds.Contains(ActionKind.RezIce)) yield return CorpRez;
            if (actionWindow) yield return Basics;
            if (actionWindow && kinds.Contains(ActionKind.InstallCard)) yield return CorpIce;
            if (kinds.Contains(ActionKind.AdvanceCard) || kinds.Contains(ActionKind.AdvanceAgenda) || kinds.Contains(ActionKind.ScoreAgenda))
                yield return CorpAdvance;
            yield break;
        }
        if (kinds.Contains(ActionKind.BreakSubroutines) || kinds.Contains(ActionKind.PumpBreaker)) yield return RunnerBreak;
        if (kinds.Contains(ActionKind.StealAgenda) || kinds.Contains(ActionKind.TrashAccessedCard)) yield return RunnerAccess;
        if (actionWindow) yield return Basics;
        if (kinds.Contains(ActionKind.StartRun)) yield return RunnerRun;
    }
}
