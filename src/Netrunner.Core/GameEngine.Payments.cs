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
        CreditPaymentRun(working, payer) is { } run
            ? (long)WalletCreditsForRunPayment(working, GetPlayer(working, payer), run) + OutsidePoolCredits(working, run)
            : GetPlayer(working, payer).Credits;

    private static void PayCreditCost(WorkingState working, PlayerId payer, int amount)
    {
        if (amount < 0 || amount > AvailableCreditsForPayment(working, payer))
            throw new ResolutionFailureException("The credit cost is no longer affordable.");
        if (CreditPaymentRun(working, payer) is { } run)
            working.RunState = PayRunnerRunCredits(working, run, amount, Math.Min(amount, OutsidePoolCredits(working, run)));
        else ApplyCreditDeltaIfNonZero(working, payer, -amount);
    }

    /// <summary>VP-STEALTH-CREDITS: every credit outside the Runner's pool a payment during this run may spend.</summary>
    private static int OutsidePoolCredits(WorkingState working, RunState run) =>
        CreditSources.OutsidePool(run, working.Cards, working.Zones, working.RulesState, working.RuntimeCardCatalog);

    /// <summary>
    /// VP-STEALTH-CREDITS: the pool credits a Runner payment during this run may spend: none while the active run event
    /// locks the pool (Aircheck), otherwise the whole pool.
    /// </summary>
    private static int WalletCreditsForRunPayment(WorkingState working, PlayerState runner, RunState run) =>
        CreditPoolLocked(working, run) ? 0 : runner.Credits;

    /// <summary>VP-STEALTH-CREDITS: whether the active run event stops the Runner spending or losing pool credits.</summary>
    private static bool CreditPoolLocked(WorkingState working, RunState? run) =>
        run is { EndReason: null, ActiveRunEventId: { } eventId }
        && working.Cards.FirstOrDefault(card => card.Id == eventId) is { } runEvent
        && working.BehaviorCatalog.TryGetRunEvent(runEvent.DefinitionId, out RunEventBehaviorSpec? spec)
        && spec.LocksCreditPool;

    /// <summary>
    /// VP-STEALTH-CREDITS: spends run-spendable hosted credits on Runner cards, in card order, optionally only those of cards
    /// with the subtype.
    /// </summary>
    private static void SpendRunSpendableHostedCredits(WorkingState working, int amount, string? subtype)
    {
        int spent = 0;
        foreach ((CardInstanceId card, int credits) in CreditSources.RunSpendablePools(
            working.Cards, working.Zones, working.RulesState, working.RuntimeCardCatalog, subtype).ToArray())
        {
            int take = Math.Min(credits, amount - spent);
            if (take <= 0)
            {
                break;
            }

            ChangeHostedCreditPool(working, card, HostedCreditPoolOperation.Spend, take, HostedCreditSpendRestriction.RunCost, recipient: null);
            spent += take;
            if (take == credits)
            {
                DeferOccurrence(working, OccurrenceKind.HostedCreditPoolEmptied, PlayerSide.Runner, card);
            }
        }

        if (spent != amount)
        {
            throw new ResolutionFailureException($"The Runner could spend only {spent} of {amount} run-spendable hosted credits.");
        }
    }

    /// <summary>VP-STEALTH-CREDITS: the credits a cost payable only from cards of the subtype may spend.</summary>
    private static int SourceRestrictedCredits(WorkingState working, PlayerId payer, string subtype) =>
        GetPlayer(working, payer).Side == PlayerSide.Runner
            ? CreditSources.RestrictedTo(subtype, CreditPaymentRun(working, payer), working.Cards, working.Zones,
                working.RulesState, working.RuntimeCardCatalog)
            : 0;

    /// <summary>
    /// VP-STEALTH-CREDITS: pays a cost only from credits on cards of the subtype: the active run event's own credits when
    /// it has the subtype (never the bad publicity fund), then such cards' run-spendable hosted credits.
    /// </summary>
    private static void PaySourceRestrictedCredits(WorkingState working, PlayerId payer, int amount, string subtype)
    {
        if (amount < 0 || amount > SourceRestrictedCredits(working, payer, subtype))
            throw new ResolutionFailureException($"The {subtype}-only credit cost is no longer affordable.");
        if (amount == 0)
            return;
        RunState run = CreditPaymentRun(working, payer)
            ?? throw new ResolutionFailureException("Credits from cards are spent only during the Runner's run.");
        int eventCredits = run.ActiveRunEventId is { } eventId
            && CreditSources.HasSubtype(working.RuntimeCardCatalog, GetCard(working, eventId).DefinitionId, subtype)
                ? run.RunCredits - run.BadPublicityCredits
                : 0;
        int fromEvent = Math.Min(amount, eventCredits);
        if (fromEvent > 0)
        {
            RunState spentRun = run.WithRunCredits(checked(run.RunCredits - fromEvent));
            working.RunState = spentRun;
            Emit(working, EngineEventKinds.RunCreditsAdjusted, new RunCreditsAdjustedEventPayload(run.Id, -fromEvent, spentRun.RunCredits));
        }

        SpendRunSpendableHostedCredits(working, checked(amount - fromEvent), subtype);
        DeferOccurrence(working, OccurrenceKind.CreditsSpentFromOutsidePool, PlayerSide.Runner);
    }
}
