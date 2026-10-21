using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Player-facing wording for the current decision and run. Core's prompt text
/// carries rules citations and step names; it stays available as diagnostic
/// metadata and is never shown by this presenter. Only card titles already
/// visible to the viewer are named.
/// </summary>
public static class DecisionPromptPresenter
{
    public static string Prompt(DecisionViewModel decision, MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(view);
        return decision.Kind switch
        {
            DecisionKind.TurnAction => "Choose a card, a server or a basic action.",
            DecisionKind.TurnTiming => decision.OwnerSide == PlayerSide.Corp
                ? "Rez, score or use a paid ability — or pass priority."
                : "Use a paid ability — or pass priority.",
            DecisionKind.Setup => "Keep this hand or take one mulligan.",
            DecisionKind.TurnDiscard => "Discard down to your maximum hand size.",
            DecisionKind.MemoryCleanup => "Choose programs to trash to free memory.",
            DecisionKind.RunTiming => RunPrompt(decision, view.Run),
            DecisionKind.InstallChoice => "Choose where to install, and any cards to replace.",
            DecisionKind.BehaviorInstallChoice => "Choose a card to install, and where.",
            DecisionKind.TriggerChoice => "Choose whether to use an optional effect.",
            DecisionKind.TriggerOrdering => "Choose the order of simultaneous effects.",
            DecisionKind.InterruptChoice => "Choose whether to prevent or replace an effect.",
            DecisionKind.BehaviorChoice => "Choose one of the card's options.",
            DecisionKind.IceSubroutineChoice => "Choose how the subroutine resolves.",
            DecisionKind.ChooseCard => "Choose a card.",
            DecisionKind.ChooseNumber => "Choose a number.",
            _ => "Choose an action.",
        };
    }

    /// <summary>Short run context such as "Run on Remote 1 · encountering Whitespace".</summary>
    public static string RunContext(RunFocusViewModel run, Func<string, string> serverName)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(serverName);
        return $"Run on {serverName(run.AttackedServerId)} · {StepPhrase(run)}";
    }

    public static string StepPhrase(RunFocusViewModel run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Step switch
        {
            RunTimingStep.InitiationPaidWindow => "starting the run",
            RunTimingStep.Approach or RunTimingStep.ApproachPaidWindow => $"approaching {IceName(run.CurrentIce)}",
            RunTimingStep.EncounterInitialization or RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter
                => $"encountering {IceName(run.Encounter?.Ice ?? run.CurrentIce)}",
            RunTimingStep.MovementPaidWindow or RunTimingStep.JackOutDecision => $"passing {IceName(run.CurrentIce)}",
            RunTimingStep.MoveInward or RunTimingStep.AfterMovePaidWindow => "moving inward",
            RunTimingStep.ApproachServer => "approaching the server",
            RunTimingStep.Success => "run successful",
            RunTimingStep.BreachBoundary or RunTimingStep.BreachCandidateSelection or RunTimingStep.BreachAccessAbility
                => "accessing cards",
            RunTimingStep.RunEnd => "run ending",
            _ => "running",
        };
    }

    private static string RunPrompt(DecisionViewModel decision, RunFocusViewModel? run)
    {
        if (run is null)
        {
            return PriorityPrompt(decision.OwnerSide, corpMayRez: false);
        }

        return run.Step switch
        {
            RunTimingStep.EncounterPaidWindow when decision.OwnerSide == PlayerSide.Runner
                => $"{Capitalize(IceName(run.Encounter?.Ice ?? run.CurrentIce))} blocks the way — break its subroutines, or let them fire.",
            RunTimingStep.ApproachPaidWindow when decision.OwnerSide == PlayerSide.Corp && run.Priority?.CorpMayRezApproachedIce == true
                => $"The Runner approaches {IceName(run.CurrentIce)} — rez it, or pass.",
            RunTimingStep.JackOutDecision => "Continue the run, or jack out.",
            RunTimingStep.BreachCandidateSelection => "Choose the next card to access.",
            RunTimingStep.BreachAccessAbility => "Decide what to do with the accessed card.",
            _ => PriorityPrompt(decision.OwnerSide, run.Priority?.CorpMayRezNonIce == true),
        };
    }

    private static string PriorityPrompt(PlayerSide side, bool corpMayRez) =>
        side == PlayerSide.Corp && corpMayRez
            ? "Rez a card or use a paid ability — or pass priority."
            : "Use a paid ability — or pass priority.";

    private static string IceName(CardViewModel? ice) =>
        ice is { IsKnown: true, Title: { Length: > 0 } title } ? title : "a piece of ICE";

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
