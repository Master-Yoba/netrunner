namespace Netrunner.Core;

public static partial class GameEngine
{
    // Generic abilities and choice options have a fixed credit cost rather than
    // a selectable source allocation. Prefer the expiring run pool for those
    // costs, using the same authoritative payment reducer as breakers/access.
    // Hosted card counters retain their explicit spend restrictions and costs.
    private static RunState? CreditPaymentRun(WorkingState working, PlayerId payer) =>
        GetPlayer(working, payer).Side == PlayerSide.Runner
        && working.RunState is { Phase: not RunCrPhase.RunEnd, EndReason: null } run
        && run.InitiatingPlayer == payer ? run : null;

    private static long AvailableCreditsForPayment(WorkingState working, PlayerId payer) =>
        (long)GetPlayer(working, payer).Credits + (CreditPaymentRun(working, payer)?.RunCredits ?? 0);

    private static void PayCreditCost(WorkingState working, PlayerId payer, int amount)
    {
        if (amount < 0 || amount > AvailableCreditsForPayment(working, payer))
            throw new ResolutionFailureException("The credit cost is no longer affordable.");
        if (CreditPaymentRun(working, payer) is { } run)
            working.RunState = PayRunnerRunCredits(working, run, amount, Math.Min(amount, run.RunCredits));
        else ApplyCreditDeltaIfNonZero(working, payer, -amount);
    }
}
