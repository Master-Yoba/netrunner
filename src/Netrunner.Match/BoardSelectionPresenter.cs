using Netrunner.Core;

namespace Netrunner.Match;

public sealed record BoardSelectionGuidance(string Instruction, string ConfirmationLabel, bool CanConfirm);

/// <summary>Explains the next local selection using published constraints and issued target sets.</summary>
public static class BoardSelectionPresenter
{
    public static BoardSelectionGuidance Incremental(ObservationCardSelection context, IReadOnlyList<string> selected)
    {
        bool ready = BoardSelectionSubmission.CanConfirm(context, selected);
        string optional = context.Selected.Count == 0 ? ", or continue without cards" : "";
        string requirement = context.Cardinality switch
        {
            BehaviorChoiceCardinalityKind.Exactly => $"Select exactly {Cards(context.Count ?? 0)}.",
            BehaviorChoiceCardinalityKind.UpTo => $"Select up to {Cards(context.Count ?? 0)}{optional}.",
            BehaviorChoiceCardinalityKind.AnyNumber => $"Select any number of cards{optional}.",
            _ => "Select highlighted cards.",
        };
        int remaining = Math.Max(0, (context.Count ?? 0) - selected.Count);
        if (!ready && (remaining == 0 || !selected.Take(context.Selected.Count).SequenceEqual(context.Selected)))
            return new("Reset unconfirmed choices with Esc to choose again.", "Confirm · reset selection", false);
        string next = ready ? "Ready to confirm." : $"Select {remaining} more {(remaining == 1 ? "card" : "cards")}.";
        string confirm = ready ? selected.Count == 0 ? "Continue without selecting cards" : $"Confirm selection · {Cards(selected.Count)}"
            : $"Confirm · select {remaining} more {(remaining == 1 ? "card" : "cards")}";
        return new($"{requirement} {selected.Count} selected. {next}", confirm, ready);
    }

    public static BoardSelectionGuidance Spatial(BoardSpatialSelection selection, MatchViewModel view)
    {
        IReadOnlyList<BoardTarget> candidates = selection.Candidates;
        if (selection.CompleteActions.Count > 0)
            return new(candidates.Count > 0 ? "Ready to confirm, or select another highlighted target." : "Ready to confirm.", "Confirm", true);
        if (candidates.Count == 0) return new("Reset the selection with Esc to choose again.", "Confirm · select targets", false);
        if (candidates.All(target => target.PaymentKind == BehaviorAdditionalCostKind.TrashCards))
        {
            IReadOnlyList<CardViewModel> hand = view.Viewer == PlayerSide.Corp ? view.CorpHand.VisibleCards : view.RunnerHand.VisibleCards;
            bool fromHand = candidates.All(target => hand.Any(card => card.Id == target.Id));
            return new($"Select a {(fromHand ? "card from your hand" : "highlighted card")} to trash as payment.", "Confirm · select a payment card first", false);
        }
        if (candidates.All(target => target.Role == "Destination"))
        {
            string destination = candidates.Any(target => target.Kind == BoardTargetKind.ServerIce) ? "an ICE position"
                : candidates.Any(target => target.Kind == BoardTargetKind.ServerRoot) ? "a server root" : "a destination";
            return new($"Select {destination}{(candidates.Any(target => target.Kind == BoardTargetKind.NewRemote) ? " or New remote" : "")} for the installed card.",
                "Confirm · select the install destination", false);
        }
        string role = candidates[0].Role;
        if (role.StartsWith("Position ", StringComparison.Ordinal) || role.StartsWith("Target ", StringComparison.Ordinal))
            return new($"Select a highlighted card for {role.ToLowerInvariant()}.", $"Confirm · select {role.ToLowerInvariant()}", false);
        int remaining = selection.Remaining.Min(action => action.Targets.Except(selection.Selected).Count());
        return new($"Select {remaining}{(selection.Selected.Count > 0 ? " more" : "")} highlighted {(remaining == 1 ? "target" : "targets")}.",
            $"Confirm · select {remaining} more {(remaining == 1 ? "target" : "targets")}", false);
    }

    public static BoardSelectionGuidance Breaks(BoardIcebreakerViewModel breaker, IEnumerable<int> selected)
    {
        int[] targets = selected.Distinct().ToArray();
        int[] counts = breaker.Actions.Where(action => action.Kind == ActionKind.BreakSubroutines)
            .Select(action => BoardIcebreakerPresenter.BrokenOrdinals(breaker.Encounter, action))
            .Where(ordinals => targets.All(ordinals.Contains)).Select(ordinals => ordinals.Count).Distinct().Order().ToArray();
        bool ready = BoardIcebreakerPresenter.SelectedBreaks(breaker, targets).Count > 0;
        if (counts.Length == 0) return new("No break action matches this selection. Back or Esc resets it.", "Confirm break · select targets", false);
        string allowed = string.Join(" or ", counts);
        int more = counts[0] - targets.Length;
        return new($"Select {allowed} {(counts[^1] == 1 ? "subroutine" : "subroutines")}. {targets.Length} selected. "
            + (ready ? "Ready to confirm." : $"Select {more} more."),
            ready ? "Confirm break" : $"Confirm break · select {more} more", ready);
    }

    private static string Cards(int count) => $"{count} {(count == 1 ? "card" : "cards")}";
}
