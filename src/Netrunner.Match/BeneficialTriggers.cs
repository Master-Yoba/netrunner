using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Which of the player's trigger decisions the client resolves for them
/// (playtest, 29 September): an optional trigger whose effect only gains
/// credits, counters, clicks or cards, with nothing to pay, target or choose
/// (Pennyshaver, Conduit, Zahya), is used; and when every trigger waiting to
/// be ordered is such a trigger, their order can't matter and the first goes.
/// A trigger with a cost, a choice or a target, or one that harms, stays the
/// player's decision.
/// </summary>
public static class BeneficialTriggers
{
    /// <summary>How a single effect counts toward resolving a trigger for the player.</summary>
    public enum EffectVerdict
    {
        /// <summary>Always a gain for the player or their own card.</summary>
        Gain,
        /// <summary>A gain only in some situations; <see cref="IsGainNow"/> decides.</summary>
        Conditional,
        /// <summary>Changes nothing that matters: allowed, but not a reason to use a trigger.</summary>
        Harmless,
        /// <summary>
        /// Harmless inside a mandatory trigger, which happens anyway (an emptied card trashing
        /// itself), but a real cost in an optional one, so the player decides there.
        /// </summary>
        HarmlessWhenMandatory,
        /// <summary>Can cost, harm, target or choose: the player decides.</summary>
        PlayerDecides,
    }

    /// <summary>
    /// Every effect kind, classified once. A new <see cref="BehaviorInstructionKind"/> must be
    /// added here: the tests fail until it is, so a new card's effect never falls silently
    /// into "ask the player" (or worse, into "automatic").
    /// </summary>
    public static IReadOnlyDictionary<BehaviorInstructionKind, EffectVerdict> Verdicts { get; } =
        new Dictionary<BehaviorInstructionKind, EffectVerdict>
        {
            [BehaviorInstructionKind.GainCredits] = EffectVerdict.Gain,
            [BehaviorInstructionKind.GainClicks] = EffectVerdict.Gain,
            [BehaviorInstructionKind.AddCounter] = EffectVerdict.Gain,
            [BehaviorInstructionKind.LoadHostedCredits] = EffectVerdict.Gain,
            [BehaviorInstructionKind.PlaceHostedCredits] = EffectVerdict.Gain,
            // Credits taken from the player's own card (Red Team) are a pure gain.
            [BehaviorInstructionKind.TakeHostedCredits] = EffectVerdict.Gain,
            [BehaviorInstructionKind.TakeAllHostedCredits] = EffectVerdict.Gain,
            // "Up to" takes what the card holds, up to the amount; the engine asks nothing (Smartware).
            [BehaviorInstructionKind.TakeUpToHostedCredits] = EffectVerdict.Gain,
            // A draw can deck out the Corp; see IsGainNow.
            [BehaviorInstructionKind.Draw] = EffectVerdict.Conditional,
            // Zahya: worth nothing after a run with no accesses, and it is once per turn.
            [BehaviorInstructionKind.GainCreditsPerAccess] = EffectVerdict.Conditional,
            [BehaviorInstructionKind.LoseCredits] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.LoseClicks] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.MoveCard] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.DealDamage] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.AddTags] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.StartRun] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.Choose] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.Branch] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.EndRun] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.TrashSelf] = EffectVerdict.HarmlessWhenMandatory,
            [BehaviorInstructionKind.NoOp] = EffectVerdict.Harmless,
            [BehaviorInstructionKind.Search] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.Reveal] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ShuffleIntoDeck] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.AddToHand] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.TrashCard] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.InstallCard] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.AdvanceCard] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ModifyEncounteredIceStrength] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.SwapInstalledIce] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ProhibitScoringThisTurn] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.RezIgnoringCosts] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ShuffleSearchedDeck] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.RemoveTags] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ModifySourceStrength] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.FlipSelf] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.RemoveCounter] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ForfeitAgenda] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.GainAllottedClicksNextTurn] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ProhibitScoringTargetThisTurn] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.Sabotage] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.PlaceOnTopOfDeck] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ShuffleDeck] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.AddBreachAccess] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.RefillRecurringCredits] = EffectVerdict.Gain,
            [BehaviorInstructionKind.PlayCard] = EffectVerdict.PlayerDecides,
            [BehaviorInstructionKind.ReturnSetAside] = EffectVerdict.Harmless,
            [BehaviorInstructionKind.TurnFaceUp] = EffectVerdict.PlayerDecides,
        };

    /// <summary>The verdict for an effect kind; an unclassified kind is the player's decision.</summary>
    public static EffectVerdict Verdict(BehaviorInstructionKind kind) =>
        Verdicts.TryGetValue(kind, out EffectVerdict verdict) ? verdict : EffectVerdict.PlayerDecides;

    /// <summary>Whether a conditional effect is a gain in the current situation.</summary>
    private static bool IsGainNow(BehaviorInstruction instruction, AgentFeatures features, PlayerSide side) => instruction.Kind switch
    {
        BehaviorInstructionKind.GainCreditsPerAccess => features.Board.RunEnd is { AccessOccurrenceCount: > 0 },
        // The Corp loses when it must draw from an empty R&D; keep a margin, as a player would.
        // A Runner drawing from an empty stack loses nothing, so the Runner's draw is always a gain.
        BehaviorInstructionKind.Draw => side == PlayerSide.Runner
            || features.Board.HiddenZoneCounts.CorpDeck > Math.Max(1, instruction.Amount),
        _ => false,
    };

    /// <summary>The action to take for the player, or null when the decision is theirs to make.</summary>
    public static ActionId? AutoAction(PendingDecision pending, AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(catalog);
        switch (pending.Kind)
        {
            case DecisionKind.TriggerChoice:
                LegalAction? use = pending.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.UseTrigger
                    && action.Parameters is TriggerChoiceParameters parameters && IsBeneficial(parameters.TriggerId.Value, features, catalog));
                return use?.Id;
            case DecisionKind.TriggerOrdering:
                LegalAction[] orders = [.. pending.LegalActions.Where(action => action.Kind == ActionKind.OrderTriggers)];
                if (orders.Length == 0 || orders.Length != pending.LegalActions.Count) return null;
                return orders.All(action => action.Parameters is TriggerOrderParameters parameters
                    && IsBeneficial(parameters.TriggerId.Value, features, catalog)) ? orders[0].Id : null;
            default:
                return null;
        }
    }

    public static bool IsBeneficial(string triggerId, AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        AgentTrigger? offered = features.PrivateCards.Triggers.FirstOrDefault(trigger => trigger.Id == triggerId);
        if (offered is null || !catalog.TryGet(new CardDefinitionId(offered.Source.DefinitionId), out CardEvaluationDescriptor card))
            return false;
        BehaviorTrigger? declaration = card.Behavior?.Triggers.FirstOrDefault(trigger => trigger.Key == offered.Key);
        if (declaration is null || Blocker(declaration) is not null) return false;
        if (declaration.Instructions.Any(instruction => Verdict(instruction.Kind) == EffectVerdict.Conditional
            && !IsGainNow(instruction, features, features.Side))) return false;
        // Using an optional trigger needs something to gain; a mandatory one happens anyway,
        // so only its order is in question.
        return declaration.Mode == TriggerMode.Mandatory
            || declaration.Instructions.Any(instruction => Verdict(instruction.Kind) is EffectVerdict.Gain or EffectVerdict.Conditional);
    }

    /// <summary>
    /// Why a trigger is always the player's decision, or null when it can resolve for them
    /// (always, or when its conditions hold). Shared by the runtime check and the card audit.
    /// </summary>
    public static string? Blocker(BehaviorTrigger trigger)
    {
        if (trigger.Instructions.Count == 0) return "no effect";
        foreach (BehaviorInstruction instruction in trigger.Instructions)
        {
            EffectVerdict verdict = Verdict(instruction.Kind);
            if (verdict == EffectVerdict.PlayerDecides
                || verdict == EffectVerdict.HarmlessWhenMandatory && trigger.Mode == TriggerMode.Optional)
                return instruction.Kind.ToString();
            if (instruction.Choice is not null) return $"{instruction.Kind} with a choice";
            if (instruction.Branch is not null) return $"{instruction.Kind} with a branch";
            // Nothing goes to the opponent: only the player or its own card.
            if (instruction.Target is { Kind: not (BehaviorTargetKind.Self or BehaviorTargetKind.Controller) } target)
                return $"{instruction.Kind} targeting {target.Kind}";
        }
        return null;
    }

    /// <summary>One reviewed line of the card audit: when the client resolves this trigger for the player.</summary>
    public static string Describe(CardBehaviorDefinition card, BehaviorTrigger trigger)
    {
        string verdict = Blocker(trigger) is { } blocker
            ? $"player decides ({blocker})"
            : trigger.Instructions.Where(instruction => Verdict(instruction.Kind) == EffectVerdict.Conditional)
                .Select(instruction => Condition(instruction.Kind, card.Controller)).OfType<string>().Distinct().ToArray() is { Length: > 0 } conditions
                ? $"automatic when {string.Join(" and ", conditions)}"
                : "automatic";
        return $"{card.DefinitionId.Value} {trigger.Key} [{trigger.Mode}] → {verdict}";
    }

    /// <summary>The situation a conditional effect needs, or null when it always holds for that side.</summary>
    private static string? Condition(BehaviorInstructionKind kind, PlayerSide side) => kind switch
    {
        BehaviorInstructionKind.GainCreditsPerAccess => "the run accessed a card",
        BehaviorInstructionKind.Draw => side == PlayerSide.Corp ? "the draw leaves R&D a card" : null,
        _ => kind.ToString(),
    };
}
