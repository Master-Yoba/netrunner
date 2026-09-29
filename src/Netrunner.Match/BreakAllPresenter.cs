using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// One step of "break all" for a breaker (roadmap U8): the issued action to
/// submit next and a printed-cost estimate for the whole encounter.
/// </summary>
/// <param name="BreakerId">The breaker the plan uses.</param>
/// <param name="Next">An issued action from the current decision: a boost while the breaker is weaker than the ICE, then the break covering the most remaining subroutines.</param>
/// <param name="Boosts">Boosts still needed; null when the boost amount depends on the board.</param>
/// <param name="EstimatedCredits">Printed-cost estimate for every remaining boost and break; null when a boost amount is unknown.</param>
/// <param name="Affordable">False when the estimate exceeds the Runner's credits plus run credits.</param>
public sealed record BreakAllPlan(string BreakerId, LegalActionViewModel Next, int? Boosts, int? EstimatedCredits, bool Affordable);

/// <summary>
/// Plans "break all" over issued actions only. It never builds an action: each
/// step picks from the legal actions of the current decision, so the UI
/// re-plans after every snapshot and stops as soon as no step is issued.
/// </summary>
public static class BreakAllPresenter
{
    public static BreakAllPlan? Plan(MatchViewModel view, string breakerId)
    {
        if (BoardIcebreakerPresenter.Present(view, breakerId) is not { } board) return null;
        EncounterViewModel encounter = board.Encounter;
        int remaining = encounter.Subroutines.Count(subroutine => !subroutine.Broken && !subroutine.Resolved);
        if (remaining == 0) return null;
        EncounterBreakerViewModel? breaker = board.Strength;
        LegalActionViewModel[] issued = BoardIcebreakerPresenter.EncounterActions(view, view.LegalActions)
            .Where(action => BoardIcebreakerPresenter.Source(action) == breakerId).ToArray();
        int deficit = breaker is { } known && encounter.EffectiveStrength is { } ice ? Math.Max(0, ice - known.EffectiveStrength) : 0;
        int limit = breaker?.BreakLimit ?? 0;
        int breaks = limit <= 0 ? 1 : (remaining + limit - 1) / limit;
        int available = view.Runner.Credits + (view.Run?.RunCredits ?? 0);
        if (deficit > 0)
        {
            if (issued.FirstOrDefault(action => action.Kind == ActionKind.PumpBreaker) is not { } boost) return null;
            int? boosts = breaker!.PumpStrength is int amount and > 0 ? (deficit + amount - 1) / amount : null;
            int? estimate = boosts * breaker.PumpCost + breaks * breaker.BreakCost;
            return new(breakerId, boost, boosts, estimate, estimate is not { } cost || cost <= available);
        }

        LegalActionViewModel? best = issued.Where(action => action.Kind == ActionKind.BreakSubroutines)
            .OrderByDescending(action => BoardIcebreakerPresenter.BrokenOrdinals(encounter, action).Count)
            .ThenBy(action => action.CostQuote?.EffectiveCost ?? int.MaxValue)
            .FirstOrDefault();
        if (best is null) return null;
        // The issued quote prices the whole action, which may cover several subroutines.
        int covered = Math.Max(1, BoardIcebreakerPresenter.BrokenOrdinals(encounter, best).Count);
        int each = best.CostQuote?.EffectiveCost ?? breaker?.BreakCost ?? 0;
        int total = (remaining + covered - 1) / covered * each;
        return new(breakerId, best, 0, total, total <= available);
    }
}
