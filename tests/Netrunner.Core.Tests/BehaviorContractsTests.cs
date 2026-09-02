using Xunit;

namespace Netrunner.Core.Tests;

public sealed class BehaviorContractsTests
{
    private static readonly CardDefinitionId IcebreakerId = new("test:contract-icebreaker");
    private static readonly CardDefinitionId SearchCardId = new("test:contract-search-card");

    [Fact]
    public void CardinalityAndChooserContractsValidateBoundsAndFreezeOptions()
    {
        Assert.Equal(
            new BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind.Exactly, 2),
            BehaviorChoiceCardinality.Exactly(2));
        Assert.Equal(
            new BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind.UpTo, 3),
            BehaviorChoiceCardinality.UpTo(3));
        Assert.Equal(
            new BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind.AnyNumber),
            BehaviorChoiceCardinality.AnyNumber);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind.Exactly, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind.UpTo, -1));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorChoiceCardinality(BehaviorChoiceCardinalityKind.AnyNumber, 1));

        var options = new List<BehaviorChoiceOption>
        {
            new("gain", [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)]),
            new("draw", [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 2)]),
        };
        BehaviorChoiceSpec choice = new(
            "contract-mode",
            BehaviorChooser.ActingPlayer,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options);

        options.Add(new BehaviorChoiceOption("unexpected"));

        Assert.Equal(BehaviorChooser.ActingPlayer, choice.Chooser);
        Assert.Equal(BehaviorChoiceMode.Mode, choice.Mode);
        Assert.Equal(2, choice.Options.Count);
        Assert.IsAssignableFrom<IList<BehaviorChoiceOption>>(choice.Options);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<BehaviorChoiceOption>)choice.Options).Clear());
        Assert.Throws<ArgumentException>(() => new BehaviorChoiceSpec(
            "duplicate-options",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("same"), new BehaviorChoiceOption("same")]));
        Assert.Throws<ArgumentException>(() => new BehaviorChoiceSpec(
            "one-mode-option",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("only")]));
        Assert.Throws<ArgumentException>(() => new BehaviorChoiceSpec(
            "multi-mode-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.UpTo(1),
            [new BehaviorChoiceOption("first"), new BehaviorChoiceOption("second")]));
        Assert.Throws<ArgumentException>(() => new BehaviorChoiceSpec(
            "optional-many-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Optional,
            BehaviorChoiceCardinality.AnyNumber,
            [new BehaviorChoiceOption("first"), new BehaviorChoiceOption("second")]));
        BehaviorChoiceSpec optionalChoice = new(
            "optional-one-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Optional,
            BehaviorChoiceCardinality.UpTo(1),
            [new BehaviorChoiceOption("first")]);
        Assert.Equal(BehaviorChoiceCardinalityKind.UpTo, optionalChoice.Cardinality.Kind);
        Assert.Equal(1, optionalChoice.Cardinality.Count);
    }

    [Fact]
    public void TypedConditionsZonesAndFiltersRejectUnboundOperands()
    {
        BehaviorCondition tagged = BehaviorCondition.Tagged();
        BehaviorCondition notTagged = BehaviorCondition.NotTagged();
        BehaviorCondition unqualifiedTagged = BehaviorCondition.Tagged();
        BehaviorCondition unqualifiedNotTagged = BehaviorCondition.NotTagged();
        BehaviorCondition clicks = BehaviorCondition.ClicksRemainingAtLeast(2);
        BehaviorCondition installed = BehaviorCondition.InstalledThisTurn();
        BehaviorCondition successfulRun = BehaviorCondition.SuccessfulRunThisTurn();

        Assert.Equal(BehaviorConditionKind.Tagged, tagged.Kind);
        Assert.Null(tagged.Fact);
        Assert.Equal(BehaviorConditionKind.NotTagged, notTagged.Kind);
        Assert.Null(unqualifiedTagged.Fact);
        Assert.Null(unqualifiedNotTagged.Fact);
        Assert.Equal(2, clicks.Value);
        Assert.Equal(BehaviorConditionKind.InstalledThisTurn, installed.Kind);
        Assert.Equal(BehaviorConditionKind.SuccessfulRunThisTurn, successfulRun.Kind);
        Assert.Throws<ArgumentException>(() =>
            new BehaviorCondition(BehaviorConditionKind.Tagged, fact: "unexpected"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BehaviorCondition.ClicksRemainingAtLeast(-1));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorCondition(BehaviorConditionKind.InstalledThisTurn, fact: "unexpected"));

        BehaviorCardFilter filter = new(
            BehaviorCardType.Icebreaker,
            definitionId: IcebreakerId,
            ownerSide: PlayerSide.Runner,
            subtype: "program");
        BehaviorTarget hiddenCards = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            filter,
            reveal: true);
        BehaviorTarget hiddenDeck = BehaviorTarget.CardInZone(BehaviorZone.Deck, filter);

        Assert.Equal(BehaviorTargetKind.ZoneCards, hiddenCards.Kind);
        Assert.Equal(BehaviorZone.Headquarters, hiddenCards.Zone);
        Assert.Same(filter, hiddenCards.Filter);
        Assert.True(hiddenCards.Reveal);
        Assert.Equal(BehaviorTargetKind.ZoneCard, hiddenDeck.Kind);
        Assert.Throws<ArgumentException>(() =>
            new BehaviorTarget(BehaviorTargetKind.ZoneCard));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorTarget(BehaviorTargetKind.NamedCard, selector: null));
    }

    [Fact]
    public void DeclarativeInstructionsCoverSearchMovementInstallAndAdvanceVocabulary()
    {
        BehaviorCardFilter filter = new(
            BehaviorCardType.Icebreaker,
            definitionId: IcebreakerId,
            ownerSide: PlayerSide.Runner);
        BehaviorTarget searchTarget = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            filter);
        BehaviorTarget runnerSearchTarget = BehaviorTarget.CardsInZone(
            BehaviorZone.Stack,
            filter);
        BehaviorCardFilter equivalentFilter = new(
            BehaviorCardType.Icebreaker,
            definitionId: IcebreakerId,
            ownerSide: PlayerSide.Runner);
        BehaviorTarget equivalentSearchTarget = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            equivalentFilter);
        BehaviorTarget chosenCard = new(BehaviorTargetKind.ChosenCard);
        BehaviorTarget installedCard = new(BehaviorTargetKind.InstalledCard);

        BehaviorInstruction search = new(
            BehaviorInstructionKind.Search,
            target: searchTarget,
            sourceZone: BehaviorZone.Headquarters,
            filter: filter);
        BehaviorInstruction runnerSearch = new(
            BehaviorInstructionKind.Search,
            target: runnerSearchTarget,
            sourceZone: BehaviorZone.Stack,
            filter: filter);
        BehaviorInstruction equivalentSearch = new(
            BehaviorInstructionKind.Search,
            target: equivalentSearchTarget,
            sourceZone: BehaviorZone.Headquarters,
            filter: filter);
        BehaviorInstruction reveal = new(BehaviorInstructionKind.Reveal, target: chosenCard);
        BehaviorInstruction shuffle = new(
            BehaviorInstructionKind.ShuffleIntoDeck,
            target: chosenCard,
            sourceZone: BehaviorZone.Headquarters,
            destinationZone: BehaviorZone.Deck);
        BehaviorInstruction addToHand = new(
            BehaviorInstructionKind.AddToHand,
            target: chosenCard,
            destinationZone: BehaviorZone.Grip);
        BehaviorInstruction runnerAddToHand = new(
            BehaviorInstructionKind.AddToHand,
            target: chosenCard,
            sourceZone: BehaviorZone.Stack,
            destinationZone: BehaviorZone.Grip);
        BehaviorInstruction trash = new(BehaviorInstructionKind.TrashCard, target: chosenCard);
        BehaviorInstruction install = new(
            BehaviorInstructionKind.InstallCard,
            target: chosenCard,
            sourceZone: BehaviorZone.Grip,
            destinationZone: BehaviorZone.Rig,
            optional: true);
        BehaviorInstruction advance = new(
            BehaviorInstructionKind.AdvanceCard,
            amount: 1,
            target: installedCard);
        BehaviorInstruction conditionalLoss = new(
            BehaviorInstructionKind.LoseClicks,
            amount: 1,
            condition: BehaviorCondition.ClicksRemainingAtLeast(1));

        Assert.Equal(BehaviorZone.Headquarters, search.SourceZone);
        Assert.Same(filter, search.Filter);
        Assert.Equal(BehaviorZone.Stack, runnerSearch.SourceZone);
        Assert.Equal(BehaviorZone.Stack, runnerSearch.Target?.Zone);
        Assert.Equal(BehaviorZone.Headquarters, equivalentSearch.Target?.Zone);
        Assert.Same(chosenCard, reveal.Target);
        Assert.Equal(BehaviorInstructionKind.ShuffleIntoDeck, shuffle.Kind);
        Assert.Equal(BehaviorZone.Grip, addToHand.DestinationZone);
        Assert.Equal(BehaviorZone.Stack, runnerAddToHand.SourceZone);
        Assert.Equal(BehaviorInstructionKind.TrashCard, trash.Kind);
        Assert.True(install.Optional);
        Assert.Equal(BehaviorTargetKind.InstalledCard, advance.Target?.Kind);
        Assert.Equal(BehaviorConditionKind.ClicksRemainingAtLeast, conditionalLoss.Condition?.Kind);

        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            sourceZone: BehaviorZone.Headquarters));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            sourceZone: BehaviorZone.Grip,
            filter: filter));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            target: searchTarget,
            filter: filter));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            target: chosenCard,
            sourceZone: BehaviorZone.Headquarters,
            filter: filter));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            target: runnerSearchTarget,
            sourceZone: BehaviorZone.Headquarters,
            filter: filter));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            target: searchTarget,
            sourceZone: BehaviorZone.Headquarters,
            filter: new BehaviorCardFilter(BehaviorCardType.Asset)));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.ShuffleIntoDeck,
            destinationZone: BehaviorZone.Deck));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.ShuffleIntoDeck,
            target: chosenCard));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.AddToHand,
            destinationZone: BehaviorZone.Grip));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.AddToHand,
            target: chosenCard));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.InstallCard,
            destinationZone: BehaviorZone.Rig));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.InstallCard,
            target: chosenCard));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.ShuffleIntoDeck,
            target: chosenCard,
            destinationZone: BehaviorZone.Headquarters));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.AddToHand,
            target: chosenCard,
            destinationZone: BehaviorZone.Deck));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.InstallCard,
            target: chosenCard,
            destinationZone: BehaviorZone.Headquarters));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Reveal));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TrashCard));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.AdvanceCard,
            amount: 1,
            target: chosenCard));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.AdvanceCard,
            amount: 1));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Choose));
        Assert.Throws<ArgumentNullException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Branch));
        BehaviorChoiceSpec wrongKindChoice = new(
            "wrong-kind-choice",
            BehaviorChooser.ActingPlayer,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("first"), new BehaviorChoiceOption("second")]);
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.GainCredits,
            amount: 1,
            choice: wrongKindChoice));
        BehaviorBranchSpec wrongKindBranch = new(
            BehaviorCondition.Tagged(),
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]);
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Draw,
            amount: 1,
            branch: wrongKindBranch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.GainCredits));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Draw));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.LoseClicks));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.AdvanceCard,
            target: installedCard));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.GainCredits,
            amount: 1,
            optional: true));
    }

    [Fact]
    public void CardDefinitionFreezesInstructionTreeAndStaticModifiers()
    {
        BehaviorChoiceSpec choice = new(
            "planogram-mode",
            BehaviorChooser.Corp,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption(
                    "gain",
                    [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)]),
                new BehaviorChoiceOption(
                    "draw",
                    [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 3)]),
            ]);
        BehaviorBranchSpec branch = new(
            BehaviorCondition.Tagged(),
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)],
            [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 3)],
            key: "tagged-branch");
        var instructions = new List<BehaviorInstruction>
        {
            new(BehaviorInstructionKind.Choose, choice: choice),
            new(BehaviorInstructionKind.Branch, branch: branch),
        };
        CardBehaviorDefinition definition = new(
            SearchCardId,
            PlayerSide.Corp,
            instructions: instructions,
            staticModifiers:
            [
                new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, 1),
                new BehaviorStaticModifier(BehaviorStaticModifierKind.MaximumHandSize, 2),
            ]);

        instructions.Clear();

        Assert.Equal(2, definition.Instructions.Count);
        Assert.Same(choice, definition.Instructions[0].Choice);
        Assert.Same(branch, definition.Instructions[1].Branch);
        Assert.Equal(
            BehaviorStaticModifierKind.MemoryUnits,
            definition.StaticModifiers[0].Kind);
        Assert.Equal("memory-units", definition.StaticModifiers[0].ValueKey);
        Assert.Equal("maximum-hand-size", definition.StaticModifiers[1].ValueKey);
        Assert.IsAssignableFrom<IList<BehaviorInstruction>>(definition.Instructions);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<BehaviorInstruction>)definition.Instructions).Clear());
    }

    [Fact]
    public void GenericCatalogFingerprintIsStableStructuralAndSensitive()
    {
        CardBehaviorDefinition firstDefinition = CreateDefinition(drawAmount: 3);
        CardBehaviorDefinition equivalentDefinition = CreateDefinition(drawAmount: 3);
        CardBehaviorDefinition changedDefinition = CreateDefinition(drawAmount: 4);

        BehaviorCatalog first = new BehaviorCatalogBuilder("test:contract-catalog")
            .Add(firstDefinition)
            .Build();
        BehaviorCatalog second = new BehaviorCatalogBuilder("test:contract-catalog")
            .Add(equivalentDefinition)
            .Build();
        Assert.Equal(first.Fingerprint, second.Fingerprint);

        var duplicateBuilder = new BehaviorCatalogBuilder("test:contract-duplicate");
        duplicateBuilder.Add(firstDefinition).Add(equivalentDefinition);
        Assert.Single(duplicateBuilder.Build().GenericSpecs);

        Assert.Throws<InvalidOperationException>(() =>
            new BehaviorCatalogBuilder("test:contract-conflict")
                .Add(firstDefinition)
                .Add(changedDefinition));

        BehaviorCatalog changed = new BehaviorCatalogBuilder("test:contract-catalog")
            .Add(changedDefinition)
            .Build();
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
    }

    private static CardBehaviorDefinition CreateDefinition(int drawAmount)
    {
        BehaviorCardFilter filter = new(
            BehaviorCardType.Icebreaker,
            definitionId: IcebreakerId,
            ownerSide: PlayerSide.Runner,
            subtype: "program");
        BehaviorTarget target = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            filter,
            reveal: true);
        BehaviorChoiceSpec choice = new(
            "contract-choice",
            BehaviorChooser.ActingPlayer,
            BehaviorChoiceMode.TargetSelection,
            BehaviorChoiceCardinality.UpTo(1),
            target: target);
        BehaviorBranchSpec branch = new(
            BehaviorCondition.SuccessfulRunThisTurn(),
            [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: drawAmount)]);

        return new CardBehaviorDefinition(
            SearchCardId,
            PlayerSide.Runner,
            instructions:
            [
                new(
                    BehaviorInstructionKind.Search,
                    amount: drawAmount,
                    target: target,
                    sourceZone: BehaviorZone.Headquarters,
                    filter: filter),
                new(BehaviorInstructionKind.Branch, branch: branch),
                new(BehaviorInstructionKind.Choose, choice: choice),
            ],
            staticModifiers:
            [new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, drawAmount)]);
    }
}
