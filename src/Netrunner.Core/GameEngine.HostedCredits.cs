namespace Netrunner.Core;

/// <summary>
/// EL-RECURRING: hosted credits a card declares a use for (trash costs,
/// installing job and connection resources, rezzing in its server), and
/// recurring credits refilled before each of the controller's turns.
/// Declared-use credits are spent automatically and before the wallet: they
/// can pay for nothing else, so spending them first never costs the player a
/// choice that matters (a recorded engine ruling).
/// </summary>
public static partial class GameEngine
{
    private const string HostedCreditCounter = "credit";

    /// <summary>The active cards whose declared-use credits can pay for this target, in card order.</summary>
    private static IEnumerable<(CardInstanceId Source, int Credits)> DeclaredHostedCreditSources(
        WorkingState working,
        PlayerSide side,
        BehaviorHostedCreditUseKind use,
        CardInstanceState target)
    {
        // Few cards declare a use, so find them before sorting and before the activity check (S5.0: this ran for every
        // install candidate and every card).
        List<(CardInstanceState Card, IReadOnlyList<BehaviorHostedCreditUse> Uses)>? declaring = null;
        foreach (CardInstanceState card in working.Cards)
        {
            if (card.OwnerSide == side
                && working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                && definition.HostedCreditUses is { } declaredUses)
            {
                (declaring ??= []).Add((card, declaredUses));
            }
        }

        if (declaring is null)
        {
            yield break;
        }

        foreach ((CardInstanceState card, IReadOnlyList<BehaviorHostedCreditUse> uses) in declaring.OrderBy(
            static pair => pair.Card.Id.Value, StringComparer.Ordinal))
        {
            if (!RuntimeCardActivity.IsActive(card, working.Zones)
                || !uses.Any(declared => declared.Kind == use && HostedCreditUseMatches(working, card, declared, target)))
            {
                continue;
            }

            int credits = working.RulesState?.Counters.SingleOrDefault(counter =>
                counter.CardId == card.Id
                && counter.Purpose == CardCounterPurpose.HostedCredit
                && counter.SpendRestriction == HostedCreditSpendRestriction.DeclaredUseOnly)?.Count ?? 0;
            if (credits > 0)
            {
                yield return (card.Id, credits);
            }
        }
    }

    private static bool HostedCreditUseMatches(
        WorkingState working,
        CardInstanceState source,
        BehaviorHostedCreditUse use,
        CardInstanceState target)
    {
        CardRuntimeSpec? spec = working.RuntimeCardCatalog?.TryGet(target.DefinitionId, out CardRuntimeSpec? found) == true
            ? found
            : null;
        switch (use.Kind)
        {
            case BehaviorHostedCreditUseKind.TrashCost:
                return true;
            case BehaviorHostedCreditUseKind.InstallResource:
                return spec?.CardClass == RuntimeCardClass.Resource
                    && working.BehaviorCatalog.TryGetCardSubtypes(target.DefinitionId, out IReadOnlyList<string> subtypes)
                    && subtypes.Any(subtype => use.Subtypes.Contains(subtype, StringComparer.OrdinalIgnoreCase));
            case BehaviorHostedCreditUseKind.RezInServer:
                ZoneState? sourceZone = working.Zones.FirstOrDefault(zone => zone.Id == source.ZoneId);
                ZoneState? targetZone = working.Zones.FirstOrDefault(zone => zone.Id == target.ZoneId);
                return sourceZone?.ServerId is { } server
                    && targetZone?.ServerId == server
                    && (targetZone.Kind == ZoneKind.ServerRoot && spec?.CardClass == RuntimeCardClass.Asset
                        || targetZone.Kind == ZoneKind.ServerIce && spec?.CardClass == RuntimeCardClass.Ice);
            default:
                return false;
        }
    }

    /// <summary>Declared-use hosted credits available to pay for this target.</summary>
    private static int DeclaredHostedCredits(
        WorkingState working,
        PlayerSide side,
        BehaviorHostedCreditUseKind use,
        CardInstanceState target) =>
        DeclaredHostedCreditSources(working, side, use, target).Sum(static source => source.Credits);

    /// <summary>Spends up to the amount from declared-use pools; returns what they paid.</summary>
    private static int SpendDeclaredHostedCredits(
        WorkingState working,
        PlayerSide side,
        BehaviorHostedCreditUseKind use,
        CardInstanceState target,
        int amount)
    {
        int spent = 0;
        foreach ((CardInstanceId source, int credits) in DeclaredHostedCreditSources(working, side, use, target).ToArray())
        {
            int take = Math.Min(credits, amount - spent);
            if (take <= 0)
            {
                break;
            }

            ChangeHostedCreditPool(working, source, HostedCreditPoolOperation.Spend, take,
                HostedCreditSpendRestriction.DeclaredUseOnly, recipient: null);
            spent += take;
            if (take == credits)
            {
                DeferOccurrence(working, OccurrenceKind.HostedCreditPoolEmptied, side, source);
            }
        }

        return spent;
    }

    /// <summary>Pays a cost: declared-use hosted credits first, then the wallet.</summary>
    private static void PayCreditsWithDeclaredHosted(
        WorkingState working,
        PlayerId payer,
        BehaviorHostedCreditUseKind use,
        CardInstanceState target,
        int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        int fromHosted = SpendDeclaredHostedCredits(working, GetPlayer(working, payer).Side, use, target, amount);
        ApplyCreditDeltaIfNonZero(working, payer, -(amount - fromHosted));
    }

    /// <summary>EL-RECURRING: refills a card's recurring credits to its declared amount.</summary>
    private static void RefillRecurringCredits(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        if (!working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
            || definition.RecurringCredits <= 0)
        {
            return;
        }

        int current = CurrentCounter(working, cardId, HostedCreditCounter);
        if (current < definition.RecurringCredits)
        {
            ChangeHostedCreditPool(working, cardId, HostedCreditPoolOperation.Load, definition.RecurringCredits - current,
                HostedCreditSpendRestriction.DeclaredUseOnly, recipient: null);
        }
    }

    /// <summary>Before a turn begins, the active side's recurring credits refill.</summary>
    private static void RefillActiveRecurringCredits(WorkingState working, PlayerSide side)
    {
        foreach (CardInstanceState card in working.Cards.OrderBy(static card => card.Id.Value, StringComparer.Ordinal).ToArray())
        {
            if (card.OwnerSide == side && RuntimeCardActivity.IsActive(card, working.Zones))
            {
                RefillRecurringCredits(working, card.Id);
            }
        }
    }

    /// <summary>The persistent-context key that retains a trashed card's trash-cost increase for the run.</summary>
    internal const string RootAssetTrashCostKey = "root-asset-trash-cost";

    /// <summary>
    /// EL-PERSISTENT: the trash-cost increase on an accessed asset in a server
    /// root from rezzed cards in that root, and from such cards the Runner
    /// trashed earlier in this run.
    /// </summary>
    private static int RootAssetTrashCostIncrease(WorkingState working, CardInstanceState accessed)
    {
        ZoneState? zone = working.Zones.FirstOrDefault(candidate => candidate.Id == accessed.ZoneId);
        if (zone is not { Kind: ZoneKind.ServerRoot, ServerId: { } server }
            || working.RuntimeCardCatalog?.TryGet(accessed.DefinitionId, out CardRuntimeSpec? spec) != true
            || spec!.CardClass != RuntimeCardClass.Asset)
        {
            return 0;
        }

        int increase = 0;
        foreach (CardInstanceId cardId in zone.CardIds)
        {
            CardInstanceState card = GetCard(working, cardId);
            if (card.Rezzed
                && working.BehaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition))
            {
                increase += definition.RootAssetTrashCostIncrease;
            }
        }

        if (working.RunState?.PersistentContext is { } context && context.ServerId == server)
        {
            foreach (PersistentRunAbility retained in context.Abilities.Where(static ability =>
                string.Equals(ability.TriggerKey, RootAssetTrashCostKey, StringComparison.Ordinal)))
            {
                if (!zone.CardIds.Contains(retained.SourceCardId)
                    && working.BehaviorCatalog.TryGet(retained.DefinitionId, out CardBehaviorDefinition? definition))
                {
                    increase += definition.RootAssetTrashCostIncrease;
                }
            }
        }

        return increase;
    }

    /// <summary>
    /// The trash cost of the accessed card and the part of it left after
    /// declared-use hosted credits (Azimat), or null when it has none.
    /// </summary>
    private static (int Cost, int Remaining)? AccessTrashPayment(WorkingState working, CardInstanceState accessed)
    {
        if (AccessTrashCost(working.BehaviorCatalog, working.RuntimeCardCatalog, accessed.DefinitionId) is not { } baseCost)
        {
            return null;
        }

        int cost = checked(baseCost + RootAssetTrashCostIncrease(working, accessed));
        int declared = DeclaredHostedCredits(working, PlayerSide.Runner, BehaviorHostedCreditUseKind.TrashCost, accessed);
        return (cost, cost - Math.Min(cost, declared));
    }

    /// <summary>Validator seam: <see cref="AccessTrashPayment(WorkingState, CardInstanceState)"/> over a state.</summary>
    internal static (int Cost, int Remaining)? AccessTrashPayment(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        CardInstanceId accessed)
    {
        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        return AccessTrashPayment(working, GetCard(working, accessed));
    }
}
