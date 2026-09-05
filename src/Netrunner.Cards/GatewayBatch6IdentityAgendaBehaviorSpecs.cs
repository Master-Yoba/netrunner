using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Printed Gateway identities, agendas and score-location search, expressed as generic rules.</summary>
public static class GatewayBatch6IdentityAgendaBehaviorSpecs
{
    private static readonly BehaviorTarget Chosen = new(BehaviorTargetKind.ChosenCard);
    private static readonly BehaviorCardFilter CorpCards = new(ownerSide: PlayerSide.Corp);
    private static readonly BehaviorCardFilter NonAgenda = new(BehaviorCardType.NonAgenda, ownerSide: PlayerSide.Corp);
    private static readonly BehaviorTarget InstalledIce = new(BehaviorTargetKind.InstalledCard,
        filter: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp));

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } = new ReadOnlyCollection<CardBehaviorDefinition>(
    [
        new(new("30001"), PlayerSide.Runner, triggers:
        [new("loup-access-trash", OccurrenceKind.AccessedCardTrashed, TriggerMode.Mandatory,
            BehaviorTriggerScope.Controller, null, [Gain(1), Draw(1)], oncePerTurn: true)]),
        new(new("30010"), PlayerSide.Runner, triggers:
        [new("zahya-run-end", OccurrenceKind.RunEnded, TriggerMode.Optional,
            BehaviorTriggerScope.Controller, null, [new(BehaviorInstructionKind.GainCreditsPerAccess)],
            oncePerTurn: true, condition: BehaviorTriggerCondition.HqOrRdRun)]),
        new(new("30019"), PlayerSide.Runner, triggers:
        [new("tao-agenda-swap", OccurrenceKind.AgendaScoredOrStolen, TriggerMode.Optional,
            BehaviorTriggerScope.Any, null,
            [Select("tao-ice", InstalledIce, BehaviorChoiceCardinality.Exactly(2)),
             new(BehaviorInstructionKind.SwapInstalledIce, target: Chosen)])]),
        new(new("30035"), PlayerSide.Corp,
            staticModifiers: [new(BehaviorStaticModifierKind.MaximumHandSize, 1)], triggers:
        [new("precision-design-score", OccurrenceKind.AgendaScoredOrStolen, TriggerMode.Optional,
            BehaviorTriggerScope.Controller, null,
            [Select("precision-design-archives", BehaviorTarget.CardsInZone(BehaviorZone.Archives, CorpCards), BehaviorChoiceCardinality.UpTo(1)),
             new(BehaviorInstructionKind.AddToHand, target: Chosen, sourceZone: BehaviorZone.Archives,
                 destinationZone: BehaviorZone.Headquarters)])]),
        new(new("30043"), PlayerSide.Corp, triggers:
        [new("restoring-humanity-discard", OccurrenceKind.DiscardPhaseEnded, TriggerMode.Mandatory,
            BehaviorTriggerScope.Controller, null, [Gain(1)], condition: BehaviorTriggerCondition.FacedownArchives)]),
        new(new("30051"), PlayerSide.Corp, triggers:
        [new("reality-plus-first-tag", OccurrenceKind.Tags, TriggerMode.Mandatory,
            BehaviorTriggerScope.Any, null,
            [new(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec("reality-plus-benefit",
                BehaviorChooser.Controller, BehaviorChoiceMode.Mode, BehaviorChoiceCardinality.Exactly(1),
                options: [new("gain-two", [Gain(2)]), new("draw-two", [Draw(2)])]))],
            condition: BehaviorTriggerCondition.FirstRunnerTag)]),
        new(new("30059"), PlayerSide.Corp, triggers:
        [new("built-to-last-advance", OccurrenceKind.CardAdvancedFromZero, TriggerMode.Mandatory,
            BehaviorTriggerScope.Controller, null, [Gain(2)])]),
        new(new("30076"), PlayerSide.Runner),
        new(new("30077"), PlayerSide.Corp),
        Agenda("30036", "luminal-score", [new(BehaviorInstructionKind.GainClicks, 3), new(BehaviorInstructionKind.ProhibitScoringThisTurn)]),
        Agenda("30044", "longevity-score",
            [Select("longevity-hq", BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, CorpCards), BehaviorChoiceCardinality.AnyNumber),
             new(BehaviorInstructionKind.TrashCard, target: Chosen),
             Select("longevity-archives", BehaviorTarget.CardsInZone(BehaviorZone.Archives, CorpCards), BehaviorChoiceCardinality.UpTo(3)),
             new(BehaviorInstructionKind.ShuffleIntoDeck, target: new(BehaviorTargetKind.ChosenCard, selector: "longevity-archives"), sourceZone: BehaviorZone.Archives, destinationZone: BehaviorZone.Deck)]),
        Agenda("30052", "headline-score-steal", [Tag()], onSteal: true),
        Agenda("30060", "above-law-score",
            [Select("above-law-resource", new(BehaviorTargetKind.InstalledCard,
                 filter: new BehaviorCardFilter(BehaviorCardType.Resource, ownerSide: PlayerSide.Runner)), BehaviorChoiceCardinality.UpTo(1)),
             new(BehaviorInstructionKind.TrashCard, target: Chosen)], optional: true),
        Agenda("30067", "offworld-score", [Gain(7)]),
        Agenda("30068", "orbital-score",
            [new(BehaviorInstructionKind.Branch, branch: new BehaviorBranchSpec(BehaviorCondition.Tagged(PlayerSide.Runner),
                [new(BehaviorInstructionKind.DealDamage, 4, damageKind: DamageKind.Meat)], [Tag()]))]),
        Agenda("30069", "send-message-score-steal",
            [Select("send-message-ice", new(BehaviorTargetKind.InstalledCard, filter: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp, rezzed: false)), BehaviorChoiceCardinality.UpTo(1)),
             new(BehaviorInstructionKind.RezIgnoringCosts, target: Chosen)], onSteal: true, optional: true),
        new(new("30070"), PlayerSide.Corp, staticModifiers: [new(BehaviorStaticModifierKind.MaximumHandSize, 2)],
            triggers: [Score("hub-score", [Draw(2)], optional: true)]),
        new(new("30066"), PlayerSide.Corp, triggers:
        [new("malapert-score-search", OccurrenceKind.AgendaScoredOrStolen, TriggerMode.Optional,
            BehaviorTriggerScope.Controller, null,
            [new(BehaviorInstructionKind.Search, argument: "malapert-search", target: BehaviorTarget.CardInZone(BehaviorZone.Deck, NonAgenda),
                 sourceZone: BehaviorZone.Deck, filter: NonAgenda),
             new(BehaviorInstructionKind.ShuffleSearchedDeck, sourceZone: BehaviorZone.Deck),
             new(BehaviorInstructionKind.Branch, branch: new BehaviorBranchSpec(BehaviorCondition.FoundCard(),
                 [new(BehaviorInstructionKind.Reveal, target: Chosen)])),
             new(BehaviorInstructionKind.Branch, branch: new BehaviorBranchSpec(BehaviorCondition.FoundCard(),
                 [new(BehaviorInstructionKind.AddToHand, target: Chosen, sourceZone: BehaviorZone.Deck, destinationZone: BehaviorZone.Headquarters)]))],
            requireSourceOnAttackedServer: true)]),
    ]);

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } = new ReadOnlyCollection<AgendaBehaviorSpec>(
    [new(new("30036"), 2), new(new("30044"), 2), new(new("30052"), 2), new(new("30060"), 2),
     new(new("30068"), 2), new(new("30069"), 3), new(new("30070"), 1)]);

    private static CardBehaviorDefinition Agenda(string code, string key, IReadOnlyList<BehaviorInstruction> instructions,
        bool onSteal = false, bool optional = false) => new(new(code), PlayerSide.Corp,
            triggers: [Score(key, instructions, onSteal, optional)]);

    private static BehaviorTrigger Score(string key, IReadOnlyList<BehaviorInstruction> instructions,
        bool onSteal = false, bool optional = false) => new(key, OccurrenceKind.AgendaScoredOrStolen,
            optional ? TriggerMode.Optional : TriggerMode.Mandatory, BehaviorTriggerScope.Self, null, instructions,
            occurrenceControllerSide: onSteal ? null : PlayerSide.Corp);

    private static BehaviorInstruction Gain(int amount) => new(BehaviorInstructionKind.GainCredits, amount);
    private static BehaviorInstruction Draw(int amount) => new(BehaviorInstructionKind.Draw, amount);
    private static BehaviorInstruction Tag() => new(BehaviorInstructionKind.AddTags, 1, recipient: BehaviorPlayerSelector.Runner);
    private static BehaviorInstruction Select(string key, BehaviorTarget target, BehaviorChoiceCardinality cardinality) =>
        new(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(key, BehaviorChooser.Controller,
            BehaviorChoiceMode.TargetSelection, cardinality, target: target));
}
