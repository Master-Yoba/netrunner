using Netrunner.Core;

namespace Netrunner.Match;

public sealed record BoardIcebreakerViewModel(
    string EncounterId, CardViewModel Card, EncounterViewModel Encounter,
    EncounterBreakerViewModel? Strength, IReadOnlyList<LegalActionViewModel> Actions);

/// <summary>Projects board controls from issued actions; never calculates legality or payments.</summary>
public static class BoardIcebreakerPresenter
{
    public static string? Source(LegalActionViewModel action) => (action.Kind, action.Parameters) switch
    {
        (ActionKind.PumpBreaker, RunBreakerParameters pump) => pump.BreakerId.Value,
        (ActionKind.BreakSubroutines, RunBreakParameters broken) => broken.BreakerId.Value,
        _ => null,
    };

    public static BoardIcebreakerViewModel? Present(MatchViewModel view, string cardId)
    {
        if (view.Viewer != PlayerSide.Runner || view.Decision is not { OwnerSide: PlayerSide.Runner, Kind: DecisionKind.RunTiming }
            || view.Run is not
            {
                Step: RunTimingStep.EncounterPaidWindow,
                Encounter: { Status: RunEncounterStatus.Initialized, Ice.IsKnown: true } encounter
            }
            || view.Run.Priority?.Holder != PlayerSide.Runner
            || view.RunnerRig.FirstOrDefault(card => card.Id == cardId && card.IsKnown) is not { } card)
            return null;
        LegalActionViewModel[] actions = view.LegalActions.Where(action => Source(action) == cardId).ToArray();
        EncounterBreakerViewModel? strength = encounter.Breakers.FirstOrDefault(breaker => breaker.CardId == cardId);
        return actions.Length == 0 && strength is null ? null : new(encounter.Id, card, encounter, strength, actions);
    }

    /// <summary>Targets from the issued action, with a fallback for saved legacy count-only decisions.</summary>
    public static IReadOnlyList<int> BrokenOrdinals(EncounterViewModel encounter, LegalActionViewModel action) =>
        action.Parameters is RunBreakParameters broken
            ? broken.SubroutineOrdinals ?? (broken.SubroutineId is null
                ? encounter.Subroutines.Where(subroutine => !subroutine.Broken && !subroutine.Resolved).Take(broken.Quantity)
                    .Select(subroutine => subroutine.Ordinal).ToArray()
                : [])
            : [];

    public static IReadOnlyList<LegalActionViewModel> SelectedBreaks(BoardIcebreakerViewModel breaker, IEnumerable<int> selected)
    {
        int[] targets = selected.Order().ToArray();
        return breaker.Actions.Where(action => action.Kind == ActionKind.BreakSubroutines
            && targets.Length > 0 && BrokenOrdinals(breaker.Encounter, action).Order().SequenceEqual(targets)).ToArray();
    }
}
