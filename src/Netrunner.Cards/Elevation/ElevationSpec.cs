using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Terse constructors for the Elevation behavior declarations. They build the
/// same immutable Core contracts the Gateway modules spell out in full; no
/// card text is parsed or embedded.
/// </summary>
internal static class ElevationSpec
{
    public static readonly BehaviorTarget Self = new(BehaviorTargetKind.Self);

    public static readonly BehaviorTarget Chosen = new(BehaviorTargetKind.ChosenCard);

    /// <summary>The cards a named choice bound, so later choices in the same behavior can't shadow them.</summary>
    public static BehaviorTarget ChosenBy(string key) => new(BehaviorTargetKind.ChosenCard, selector: key);

    public static BehaviorCost Credits(int amount) => new(BehaviorCostKind.Credits, amount);

    public static BehaviorCost Click(int amount = 1) => new(BehaviorCostKind.Clicks, amount);

    public static BehaviorCost TrashSelfCost() => new(BehaviorCostKind.TrashSelf);

    public static BehaviorCost CounterCost(string counter, int amount = 1) =>
        new(BehaviorCostKind.Counter, amount, counterName: counter);

    public static BehaviorInstruction Gain(int amount) => new(BehaviorInstructionKind.GainCredits, amount: amount);

    public static BehaviorInstruction Draw(int amount) => new(BehaviorInstructionKind.Draw, amount: amount);

    public static BehaviorInstruction GainClicks(int amount) => new(BehaviorInstructionKind.GainClicks, amount: amount);

    public static BehaviorInstruction TrashSelf() => new(BehaviorInstructionKind.TrashSelf);

    public static BehaviorInstruction LoadHosted(int amount, HostedCreditSpendRestriction? restriction = null) =>
        new(BehaviorInstructionKind.LoadHostedCredits, amount: amount, spendRestriction: restriction);

    /// <summary>EL-RECURRING: refill the source's recurring credits ("when you install / rez this").</summary>
    public static BehaviorInstruction RefillRecurring() => new(BehaviorInstructionKind.RefillRecurringCredits);

    public static BehaviorInstruction TakeHosted(int amount) =>
        new(BehaviorInstructionKind.TakeHostedCredits, amount: amount);

    public static BehaviorInstruction AddCounter(string counter, int amount) =>
        new(BehaviorInstructionKind.AddCounter, amount: amount, argument: counter, target: Self);

    public static BehaviorInstruction NetDamage(int amount) =>
        new(BehaviorInstructionKind.DealDamage, amount: amount, damageKind: DamageKind.Net);

    public static BehaviorInstruction Branch(
        BehaviorCondition condition,
        IReadOnlyList<BehaviorInstruction> instructions,
        IReadOnlyList<BehaviorInstruction>? otherwise = null) =>
        new(BehaviorInstructionKind.Branch, branch: new BehaviorBranchSpec(condition, instructions, otherwise));

    /// <summary>EL-NESTED-PLAY: play the target (a chosen card, or the source) from inside this behavior.</summary>
    public static BehaviorInstruction Play(BehaviorTarget target, bool removeFromGameAfter = false) =>
        new(BehaviorInstructionKind.PlayCard, target: target,
            destinationZone: removeFromGameAfter ? BehaviorZone.RemovedFromGame : null);

    /// <summary>EL-GENERIC-SUBROUTINE: the instruction list a <see cref="BehaviorSubroutineSpec"/> names.</summary>
    public static BehaviorTrigger Subroutine(string key, IReadOnlyList<BehaviorInstruction> instructions) =>
        new(key, OccurrenceKind.IceSubroutine, TriggerMode.Mandatory, BehaviorTriggerScope.Controller, null, instructions);

    public static BehaviorInstruction Choose(BehaviorChoiceSpec choice) =>
        new(BehaviorInstructionKind.Choose, choice: choice);

    /// <summary>EL-BEHAVIOR-INSTALL: install one card from a zone, card and destination chosen together.</summary>
    public static BehaviorInstruction InstallFrom(
        BehaviorZone zone,
        PlayerSide owner,
        BehaviorCardType cardType = BehaviorCardType.Any,
        bool optional = true,
        BehaviorInstallCostRule costRule = BehaviorInstallCostRule.Normal,
        int discount = 0,
        BehaviorZone? destination = null,
        string? subtype = null,
        int top = 0,
        string? key = null) =>
        new(
            BehaviorInstructionKind.InstallCard,
            amount: discount,
            argument: key,
            target: top > 0
                ? BehaviorTarget.TopOfZone(zone, top, new BehaviorCardFilter(cardType, subtype: subtype, ownerSide: owner))
                : BehaviorTarget.CardInZone(zone, new BehaviorCardFilter(cardType, subtype: subtype, ownerSide: owner)),
            sourceZone: zone,
            destinationZone: destination,
            optional: optional,
            costRule: costRule);

    /// <summary>
    /// Choose one installed card (by default one that can be advanced), then
    /// place counters on it; optionally the Corp cannot score it this turn.
    /// </summary>
    public static IReadOnlyList<BehaviorInstruction> AdvanceInstalled(
        string key,
        int counters,
        bool optional = false,
        BehaviorCardType cardType = BehaviorCardType.Advanceable,
        bool? rezzed = null,
        bool cannotScoreThisTurn = false)
    {
        var instructions = new List<BehaviorInstruction>
        {
            Choose(new BehaviorChoiceSpec(
                key,
                BehaviorChooser.Controller,
                optional ? BehaviorChoiceMode.Optional : BehaviorChoiceMode.Mandatory,
                optional ? BehaviorChoiceCardinality.UpTo(1) : BehaviorChoiceCardinality.Exactly(1),
                target: new BehaviorTarget(
                    BehaviorTargetKind.InstalledCard,
                    filter: new BehaviorCardFilter(cardType, ownerSide: PlayerSide.Corp, rezzed: rezzed)))),
            new BehaviorInstruction(BehaviorInstructionKind.AdvanceCard, amount: counters, target: ChosenBy(key)),
        };
        if (cannotScoreThisTurn)
        {
            instructions.Add(new BehaviorInstruction(BehaviorInstructionKind.ProhibitScoringTargetThisTurn, target: ChosenBy(key)));
        }

        return instructions;
    }

    public static BehaviorTrigger Trigger(
        string key,
        OccurrenceKind occurrence,
        BehaviorTriggerScope scope,
        IReadOnlyList<BehaviorInstruction> instructions,
        TriggerMode mode = TriggerMode.Mandatory,
        BehaviorCardFilter? subject = null,
        ServerKind? server = null,
        bool oncePerTurn = false,
        PlayerSide? occurrenceController = null,
        BehaviorCondition? requirement = null,
        BehaviorFace? face = null,
        bool onAttackedServer = false,
        BehaviorTriggerCondition? condition = null) =>
        new(
            key,
            occurrence,
            mode,
            scope,
            subject,
            instructions,
            serverKindRequirement: server,
            requireSourceOnAttackedServer: onAttackedServer,
            occurrenceControllerSide: occurrenceController,
            oncePerTurn: oncePerTurn,
            requirement: requirement,
            face: face,
            condition: condition);

    /// <summary>"When you score this agenda" (or, with <paramref name="onSteal"/>, scored or stolen).</summary>
    public static BehaviorTrigger Score(string key, IReadOnlyList<BehaviorInstruction> instructions, bool onSteal = false) =>
        new(key, OccurrenceKind.AgendaScoredOrStolen, TriggerMode.Mandatory, BehaviorTriggerScope.Self, null, instructions,
            occurrenceControllerSide: onSteal ? null : PlayerSide.Corp);

    /// <summary>EL-DIVIDENDS N: on score, N agenda counters per advancement counter beyond the requirement.</summary>
    public static BehaviorTrigger Dividends(string key, int perExcessCounter) =>
        Score(key, [new BehaviorInstruction(
            BehaviorInstructionKind.AddCounter,
            argument: AgendaCounter,
            target: Self,
            amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.SourceExcessAdvancement, perExcessCounter))]);

    public const string AgendaCounter = "agenda";

    public const string PowerCounter = "power";

    public static BehaviorCondition CounterAtLeast(string counter, int amount) =>
        new(BehaviorConditionKind.CounterAtLeast, fact: counter, value: amount);

    public static BehaviorInstruction RemoveCounter(string counter, int amount) =>
        new(BehaviorInstructionKind.RemoveCounter, amount: amount, argument: counter, target: Self);

    public static BehaviorInstruction PlaceHosted(int amount) =>
        new(BehaviorInstructionKind.PlaceHostedCredits, amount: amount);

    public static BehaviorInstruction TakeAllHosted() => new(BehaviorInstructionKind.TakeAllHostedCredits);

    public static BehaviorInstruction FlipSelf() => new(BehaviorInstructionKind.FlipSelf);

    public static BehaviorInstruction RunnerLoses(int amount, BehaviorAmountOperand? operand = null) =>
        new(BehaviorInstructionKind.LoseCredits, amount: operand is null ? amount : 0, amountOperand: operand,
            recipient: BehaviorPlayerSelector.Runner);

    public static BehaviorInstruction GiveTags(int amount) =>
        new(BehaviorInstructionKind.AddTags, amount: amount, recipient: BehaviorPlayerSelector.Runner);

    public static BehaviorInstruction RemoveTags(int amount) => new(BehaviorInstructionKind.RemoveTags, amount: amount);

    public static BehaviorChoiceOption Option(
        string key,
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorCondition? condition = null,
        IReadOnlyList<BehaviorCost>? costs = null) =>
        new(key, instructions, costs: costs, condition: condition);

    /// <summary>The "do nothing" fallback option of an optional payment choice.</summary>
    public static BehaviorChoiceOption Decline() =>
        new("decline", [], semantic: BehaviorChoiceOptionSemantic.Fallback);

    /// <summary>A mode choice ("choose one") among options.</summary>
    public static BehaviorInstruction ChooseOne(string key, BehaviorChooser chooser, params BehaviorChoiceOption[] options) =>
        Choose(new BehaviorChoiceSpec(key, chooser, BehaviorChoiceMode.Mode, BehaviorChoiceCardinality.Exactly(1), options: options));

    /// <summary>Choose cards matching a target; the chosen cards are the <see cref="Chosen"/> target afterwards.</summary>
    public static BehaviorInstruction ChooseCards(
        string key,
        BehaviorTarget target,
        BehaviorChoiceCardinality cardinality,
        BehaviorChooser chooser = BehaviorChooser.Controller,
        bool optional = false) =>
        Choose(new BehaviorChoiceSpec(
            key,
            chooser,
            optional ? BehaviorChoiceMode.Optional : BehaviorChoiceMode.Mandatory,
            cardinality,
            target: target));

    public static BehaviorTarget Installed(BehaviorCardFilter filter, bool other = false) =>
        new(BehaviorTargetKind.InstalledCard, selector: other ? BehaviorTarget.OtherSelector : null, filter: filter);

    public static BehaviorPaidAbility ClickAbility(
        string key,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorAbilityUsageLimit limit = BehaviorAbilityUsageLimit.Unlimited,
        IReadOnlyList<BehaviorCondition>? conditions = null,
        IReadOnlyList<BehaviorTarget>? targets = null,
        bool usableFromRunnerScoreArea = false,
        bool usableFromArchives = false) =>
        new(
            key,
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            costs,
            conditions ?? [],
            targets ?? [],
            instructions,
            limit,
            usableFromRunnerScoreArea,
            usableFromArchives);

    /// <summary>
    /// Search a deck for one card matching the filter and reveal it; the deck
    /// is shuffled and the found card is the <see cref="Chosen"/> target.
    /// </summary>
    public static IReadOnlyList<BehaviorInstruction> SearchAndReveal(string key, BehaviorZone deck, BehaviorCardFilter filter) =>
    [
        new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            argument: key,
            target: BehaviorTarget.CardInZone(deck, filter),
            sourceZone: deck,
            filter: filter),
        new BehaviorInstruction(BehaviorInstructionKind.ShuffleSearchedDeck, sourceZone: deck),
        Branch(BehaviorCondition.FoundCard(), [new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: Chosen)]),
    ];

    public static BehaviorPaidAbility PaidAbility(
        string key,
        BehaviorAbilityTiming timing,
        IReadOnlyList<BehaviorCost> costs,
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorAbilityUsageLimit limit = BehaviorAbilityUsageLimit.Unlimited,
        IReadOnlyList<BehaviorCondition>? conditions = null,
        IReadOnlyList<BehaviorTarget>? targets = null) =>
        new(
            key,
            timing,
            BehaviorAbilityActivation.PaidAbility,
            costs,
            conditions ?? [],
            targets ?? [],
            instructions,
            limit);
}
