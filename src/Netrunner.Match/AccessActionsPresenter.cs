using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Direct controls for the card already being accessed. No target reselection is needed.</summary>
public static class AccessActionsPresenter
{
    public static CardViewModel? Card(MatchViewModel view) => view.Outcome.Kind == GameOutcomeKind.Ongoing
        && view.Run is { Step: RunTimingStep.BreachAccessAbility, Access.Accessed.Count: > 0 } run
        && run.Access.Accessed[^1] is { IsKnown: true } card ? card : null;

    public static IReadOnlyList<LegalActionViewModel> Actions(MatchViewModel view)
    {
        if (Card(view) is not { } card || view.Viewer != PlayerSide.Runner
            || view.Decision is not { OwnerSide: PlayerSide.Runner, Kind: DecisionKind.RunTiming }) return [];
        return RunPaymentPreference.Apply(view.LegalActions.Where(action => (action.Kind, action.Parameters) switch
        {
            (ActionKind.TrashAccessedCard, RunCardPaymentParameters p) => p.CardId.Value == card.Id,
            (ActionKind.StealAgenda, CardChoiceParameters p) => p.CardId.Value == card.Id,
            (ActionKind.NoAccessAction, NoActionParameters) => true,
            _ => false,
        }));
    }

    public static string Label(LegalActionViewModel action, CardViewModel card)
    {
        if (action.Kind == ActionKind.NoAccessAction)
            // Current rules automatically resolve the agenda steal after this window.
            // Do not label this as leaving an agenda, or promise a steal under a restriction.
            return card.Type == "Agenda" ? "Continue access" : "Leave card";
        if (action.Kind == ActionKind.StealAgenda) return "Steal agenda";
        // Shorten presentation text only; price/legality still come from the issued action.
        string prefix = $"Trash {card.Id} for ";
        string label = action.Description.StartsWith(prefix, StringComparison.Ordinal)
            ? "Trash · " + action.Description[prefix.Length..] : action.Description;
        return label.Replace(" hosted", " run", StringComparison.Ordinal);
    }
}
