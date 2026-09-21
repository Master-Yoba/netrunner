using Netrunner.Core;

namespace Netrunner.Match;

public sealed record BoardIcebreakerViewModel(
    string EncounterId, CardViewModel Card, EncounterViewModel Encounter,
    EncounterBreakerViewModel? Strength, IReadOnlyList<LegalActionViewModel> Actions);

/// <summary>Projects matching breaker controls and prefers hosted-credit payments among issued actions.</summary>
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
        if (!IsEncounterWindow(view)
            || view.RunnerRig.FirstOrDefault(card => card.Id == cardId && card.IsKnown) is not { } card)
            return null;
        EncounterViewModel encounter = view.Run!.Encounter!;
        LegalActionViewModel[] actions = EncounterActions(view, view.LegalActions).Where(action => Source(action) == cardId).ToArray();
        EncounterBreakerViewModel? strength = encounter.Breakers.FirstOrDefault(breaker => breaker.CardId == cardId);
        return actions.Length == 0 && strength?.CanInterfaceEncounter != true ? null : new(encounter.Id, card, encounter, strength, actions);
    }

    /// <summary>Applies the same compatibility and hosted-first policy to board controls and the action list.</summary>
    public static IReadOnlyList<LegalActionViewModel> EncounterActions(MatchViewModel view, IReadOnlyList<LegalActionViewModel> actions)
    {
        if (!IsEncounterWindow(view)) return actions;
        HashSet<string> compatible = view.Run!.Encounter!.Breakers.Where(breaker => breaker.CanInterfaceEncounter)
            .Select(breaker => breaker.CardId)
            // An issued break action also covers hosted abilities and custom sources without strength metadata.
            .Concat(actions.Where(action => action.Kind == ActionKind.BreakSubroutines).Select(Source).OfType<string>())
            .Intersect(view.RunnerRig.Where(card => card.IsKnown).Select(card => card.Id), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        return RunPaymentPreference.Apply(actions.Where(action => Source(action) is not { } source || compatible.Contains(source)));
    }

    private static bool IsEncounterWindow(MatchViewModel view) =>
        view.Viewer == PlayerSide.Runner
        && view.Decision is { OwnerSide: PlayerSide.Runner, Kind: DecisionKind.RunTiming }
        && view.Run is
        {
            Step: RunTimingStep.EncounterPaidWindow,
            Priority.Holder: PlayerSide.Runner,
            Encounter: { Status: RunEncounterStatus.Initialized, Ice.IsKnown: true }
        };

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
        return RunPaymentPreference.Apply(breaker.Actions.Where(action => action.Kind == ActionKind.BreakSubroutines
            && targets.Length > 0 && BrokenOrdinals(breaker.Encounter, action).Order().SequenceEqual(targets)));
    }
}
