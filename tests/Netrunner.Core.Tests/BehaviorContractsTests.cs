using Xunit;

namespace Netrunner.Core.Tests;

public sealed class BehaviorContractsTests
{
    private static readonly CardDefinitionId IcebreakerId = new("test:contract-icebreaker");
    private static readonly CardDefinitionId SearchCardId = new("test:contract-search-card");

    [Fact]
    public void StructuredChoiceMetadataAndPlayerRecipientRoundTripWithoutLegacyFields()
    {
        BehaviorTarget privateCards = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            new BehaviorCardFilter(ownerSide: PlayerSide.Corp));
        BehaviorChoiceOption option = new(
            "fallback",
            [new BehaviorInstruction(
                BehaviorInstructionKind.AddTags,
                amount: 1,
                recipient: BehaviorPlayerSelector.ActingPlayer)],
            costs:
            [
                new BehaviorCost(BehaviorCostKind.Credits, 2),
                new BehaviorCost(
                    BehaviorCostKind.Additional,
                    additionalKey: "trash-card",
                    additionalTarget: privateCards,
                    additionalKind: BehaviorAdditionalCostKind.TrashCards),
            ],
            semantic: BehaviorChoiceOptionSemantic.Fallback);

        string json = System.Text.Json.JsonSerializer.Serialize(option, EngineJson.Options);
        BehaviorChoiceOption restored = System.Text.Json.JsonSerializer.Deserialize<BehaviorChoiceOption>(
            json,
            EngineJson.Options)!;

        Assert.Equal(option, restored);
        Assert.Equal(BehaviorChoiceOptionSemantic.Fallback, restored.Semantic);
        Assert.Equal(BehaviorPlayerSelector.ActingPlayer, restored.Instructions[0].Recipient);
        Assert.DoesNotContain("costs", System.Text.Json.JsonSerializer.Serialize(
            new BehaviorChoiceOption("legacy"),
            EngineJson.Options),
            StringComparison.Ordinal);
    }

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
        BehaviorCondition runnerTagged = BehaviorCondition.Tagged(PlayerSide.Runner);
        BehaviorCondition corpNotTagged = BehaviorCondition.NotTagged(PlayerSide.Corp);
        BehaviorCondition unqualifiedTagged = BehaviorCondition.Tagged();
        BehaviorCondition unqualifiedNotTagged = BehaviorCondition.NotTagged();
        BehaviorCondition clicks = BehaviorCondition.ClicksRemainingAtLeast(2);
        BehaviorCondition installed = BehaviorCondition.InstalledThisTurn();
        BehaviorCondition successfulRun = BehaviorCondition.SuccessfulRunThisTurn();

        Assert.Equal(BehaviorConditionKind.Tagged, tagged.Kind);
        Assert.Null(tagged.Fact);
        Assert.Null(tagged.SubjectSide);
        Assert.Equal(PlayerSide.Runner, runnerTagged.SubjectSide);
        Assert.Equal(PlayerSide.Corp, corpNotTagged.SubjectSide);
        Assert.NotEqual(tagged, runnerTagged);
        Assert.NotEqual(BehaviorCondition.Tagged(PlayerSide.Corp), runnerTagged);
        Assert.Equal(BehaviorConditionKind.NotTagged, notTagged.Kind);
        Assert.Null(unqualifiedTagged.Fact);
        Assert.Null(unqualifiedNotTagged.Fact);
        Assert.Equal(2, clicks.Value);
        Assert.Equal(BehaviorConditionKind.InstalledThisTurn, installed.Kind);
        Assert.Equal(BehaviorConditionKind.SuccessfulRunThisTurn, successfulRun.Kind);
        Assert.Throws<ArgumentException>(() =>
            new BehaviorCondition(BehaviorConditionKind.Tagged, fact: "unexpected"));
        Assert.Equal(PlayerSide.Runner, BehaviorCondition.ClicksAtLeast(1, PlayerSide.Runner).SubjectSide);
        Assert.Throws<ArgumentException>(() =>
            new BehaviorCondition(
                BehaviorConditionKind.InstalledThisTurn,
                subjectSide: PlayerSide.Runner));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BehaviorCondition.Tagged((PlayerSide)999));
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

        string runnerJson = System.Text.Json.JsonSerializer.Serialize(
            runnerTagged,
            EngineJson.Options);
        Assert.Contains("\"subjectSide\":\"runner\"", runnerJson, StringComparison.Ordinal);
        BehaviorCondition restoredRunner = System.Text.Json.JsonSerializer.Deserialize<BehaviorCondition>(
            runnerJson,
            EngineJson.Options)!;
        Assert.Equal(runnerTagged, restoredRunner);

        BehaviorCondition restoredLegacy = System.Text.Json.JsonSerializer.Deserialize<BehaviorCondition>(
            "{\"kind\":\"tagged\",\"fact\":null,\"value\":null}",
            EngineJson.Options)!;
        Assert.Null(restoredLegacy.SubjectSide);
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
        BehaviorTarget installedCorpCard = new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Corp));

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
        Assert.Equal(BehaviorTargetKind.InstalledCardNotThisTurn, installedCorpCard.Kind);
        Assert.Equal(PlayerSide.Corp, installedCorpCard.Filter?.OwnerSide);
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
        BehaviorInstruction chosenAdvance = new(
            BehaviorInstructionKind.AdvanceCard,
            amount: 1,
            target: chosenCard);
        Assert.Equal(BehaviorTargetKind.ChosenCard, chosenAdvance.Target?.Kind);
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
    public void HostedCreditInstructionsValidateAmountAndRestrictionMatrix()
    {
        BehaviorInstruction load = new(
            BehaviorInstructionKind.LoadHostedCredits,
            amount: 3);
        BehaviorInstruction place = new(
            BehaviorInstructionKind.PlaceHostedCredits,
            amount: 2,
            spendRestriction: HostedCreditSpendRestriction.RunCost);
        BehaviorInstruction take = new(
            BehaviorInstructionKind.TakeHostedCredits,
            amount: 1);
        BehaviorInstruction takeAll = new(BehaviorInstructionKind.TakeAllHostedCredits);

        Assert.Equal(HostedCreditSpendRestriction.CardEffectOnly, load.SpendRestriction);
        Assert.Equal(HostedCreditSpendRestriction.RunCost, place.SpendRestriction);
        Assert.Null(take.SpendRestriction);
        Assert.Equal(0, takeAll.Amount);

        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.LoadHostedCredits));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.PlaceHostedCredits));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TakeHostedCredits));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TakeAllHostedCredits,
            amount: 1));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TakeHostedCredits,
            amount: 1,
            spendRestriction: HostedCreditSpendRestriction.AbilityCost));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TakeAllHostedCredits,
            spendRestriction: HostedCreditSpendRestriction.RunCost));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.GainCredits,
            amount: 1,
            spendRestriction: HostedCreditSpendRestriction.RunCost));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.LoadHostedCredits,
            amount: 1,
            spendRestriction: (HostedCreditSpendRestriction)99));

        string json = EngineJson.Serialize(place);
        Assert.Contains("\"kind\":\"placeHostedCredits\"", json, StringComparison.Ordinal);
        Assert.Contains("\"spendRestriction\":\"runCost\"", json, StringComparison.Ordinal);
        BehaviorInstruction restored = EngineJson.Deserialize<BehaviorInstruction>(json);
        Assert.Equal(place, restored);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    public void RemoveSelfFromGameCostHasAClosedSchemaAndFingerprint()
    {
        BehaviorCost remove = new(BehaviorCostKind.RemoveSelfFromGame);
        BehaviorCost restored = EngineJson.Deserialize<BehaviorCost>(EngineJson.Serialize(remove));

        Assert.Equal(BehaviorCostKind.RemoveSelfFromGame, remove.Kind);
        Assert.Equal(1, remove.Amount);
        Assert.Equal(remove, restored);
        Assert.Contains("\"kind\":\"removeSelfFromGame\"", EngineJson.Serialize(remove), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame, 2));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.RemoveSelfFromGame,
            counterName: "counter"));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.RemoveSelfFromGame,
            additionalKey: "additional"));

        CardBehaviorDefinition removeDefinition = new(
            SearchCardId,
            PlayerSide.Runner,
            costs: [remove]);
        CardBehaviorDefinition trashDefinition = new(
            SearchCardId,
            PlayerSide.Runner,
            costs: [new BehaviorCost(BehaviorCostKind.TrashSelf)]);
        Assert.NotEqual(removeDefinition.Costs[0], trashDefinition.Costs[0]);
        Assert.NotEqual(
            new BehaviorCatalogBuilder("test:remove-cost").Add(removeDefinition).Build().Fingerprint,
            new BehaviorCatalogBuilder("test:remove-cost").Add(trashDefinition).Build().Fingerprint);
        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            SearchCardId,
            PlayerSide.Runner,
            costs: [remove, new BehaviorCost(BehaviorCostKind.TrashSelf)]));
    }

    [Fact]
    public void StructuredTrashCardsAdditionalCostHasAClosedSchemaAndFingerprint()
    {
        BehaviorTarget runnerGrip = BehaviorTarget.CardsInZone(
            BehaviorZone.Grip,
            new BehaviorCardFilter(ownerSide: PlayerSide.Runner));
        BehaviorTarget runnerHeap = BehaviorTarget.CardsInZone(
            BehaviorZone.Heap,
            new BehaviorCardFilter(ownerSide: PlayerSide.Runner));
        BehaviorCost trashGrip = new(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: runnerGrip,
            additionalKind: BehaviorAdditionalCostKind.TrashCards);

        string json = EngineJson.Serialize(trashGrip);
        BehaviorCost restored = EngineJson.Deserialize<BehaviorCost>(json);
        Assert.Equal(trashGrip, restored);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Contains("\"additionalKind\":\"trashCards\"", json, StringComparison.Ordinal);
        Assert.Contains("\"zone\":\"grip\"", json, StringComparison.Ordinal);
        Assert.Equal(BehaviorAdditionalCostKind.TrashCards, restored.AdditionalKind);
        Assert.Equal(runnerGrip, restored.AdditionalTarget);

        BehaviorCost legacyAdditional = new(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards");
        BehaviorCost changedTarget = new(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: runnerHeap,
            additionalKind: BehaviorAdditionalCostKind.TrashCards);
        string fingerprint = new BehaviorCatalogBuilder("test:structured-cost")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Runner, costs: [trashGrip]))
            .Build()
            .Fingerprint;
        string legacyFingerprint = new BehaviorCatalogBuilder("test:structured-cost")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Runner, costs: [legacyAdditional]))
            .Build()
            .Fingerprint;
        string targetFingerprint = new BehaviorCatalogBuilder("test:structured-cost")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Runner, costs: [changedTarget]))
            .Build()
            .Fingerprint;
        Assert.NotEqual(fingerprint, legacyFingerprint);
        Assert.NotEqual(fingerprint, targetFingerprint);
        Assert.NotEqual(trashGrip, legacyAdditional);
        Assert.NotEqual(trashGrip, changedTarget);

        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalKind: BehaviorAdditionalCostKind.TrashCards));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: runnerGrip));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Credits,
            amount: 2,
            additionalKind: BehaviorAdditionalCostKind.TrashCards));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Credits,
            amount: 2,
            additionalTarget: runnerGrip));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: BehaviorTarget.CardsInZone(BehaviorZone.Grip),
            additionalKind: BehaviorAdditionalCostKind.TrashCards));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: BehaviorTarget.CardsInZone(
                BehaviorZone.Grip,
                new BehaviorCardFilter(ownerSide: PlayerSide.Runner),
                reveal: true),
            additionalKind: BehaviorAdditionalCostKind.TrashCards));
        Assert.Throws<ArgumentException>(() => new BehaviorCost(
            BehaviorCostKind.Additional,
            amount: 2,
            additionalKey: "trash-cards",
            additionalTarget: BehaviorTarget.CardInZone(
                BehaviorZone.Grip,
                new BehaviorCardFilter(ownerSide: PlayerSide.Runner)),
            additionalKind: BehaviorAdditionalCostKind.TrashCards));
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
    public void StaticModifierJsonPreservesTypedKindAndAcceptsLegacyStringShape()
    {
        BehaviorStaticModifier typed = new(
            BehaviorStaticModifierKind.MemoryUnits,
            delta: 1,
            reasonKey: "pantograph-memory");
        string typedJson = EngineJson.Serialize(typed);
        BehaviorStaticModifier typedRestored = EngineJson.Deserialize<BehaviorStaticModifier>(typedJson);

        Assert.Equal(typed, typedRestored);
        Assert.Equal(BehaviorStaticModifierKind.MemoryUnits, typedRestored.Kind);
        Assert.Equal(typedJson, EngineJson.Serialize(typedRestored));

        BehaviorStaticModifier legacy = new("legacy-value", delta: 2, reasonKey: "legacy-reason");
        string legacyJson = EngineJson.Serialize(legacy);
        BehaviorStaticModifier legacyRestored = EngineJson.Deserialize<BehaviorStaticModifier>(legacyJson);

        Assert.Equal(legacy, legacyRestored);
        Assert.Null(legacyRestored.Kind);
        Assert.Equal(legacyJson, EngineJson.Serialize(legacyRestored));
    }

    [Fact]
    public void StaticModifierJsonRejectsForgedTypedKindValueKeyMismatch()
    {
        string forged =
            "{\"valueKey\":\"maximum-hand-size\",\"delta\":1,\"reasonKey\":null,\"kind\":\"memoryUnits\"}";

        Assert.Throws<System.Text.Json.JsonException>(() =>
            EngineJson.Deserialize<BehaviorStaticModifier>(forged));
        Assert.Throws<ArgumentException>(() =>
            new BehaviorStaticModifier(
                "maximum-hand-size",
                delta: 1,
                kind: BehaviorStaticModifierKind.MemoryUnits));
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

        BehaviorCatalog omittedSubject = new BehaviorCatalogBuilder("test:subject")
            .Add(CreateTaggedDefinition(BehaviorCondition.Tagged()))
            .Build();
        BehaviorCatalog explicitRunnerSubject = new BehaviorCatalogBuilder("test:subject")
            .Add(CreateTaggedDefinition(BehaviorCondition.Tagged(PlayerSide.Runner)))
            .Build();
        BehaviorCatalog explicitCorpSubject = new BehaviorCatalogBuilder("test:subject")
            .Add(CreateTaggedDefinition(BehaviorCondition.Tagged(PlayerSide.Corp)))
            .Build();
        Assert.NotEqual(
            omittedSubject.GetDefinitionFingerprint(SearchCardId),
            explicitRunnerSubject.GetDefinitionFingerprint(SearchCardId));
        Assert.NotEqual(
            explicitRunnerSubject.GetDefinitionFingerprint(SearchCardId),
            explicitCorpSubject.GetDefinitionFingerprint(SearchCardId));
        Assert.Equal(
            omittedSubject.GetDefinitionFingerprint(SearchCardId),
            new BehaviorCatalogBuilder("test:subject")
                .Add(CreateTaggedDefinition(new BehaviorCondition(BehaviorConditionKind.Tagged)))
                .Build()
                .GetDefinitionFingerprint(SearchCardId));
    }

    [Fact]
    public void ADefinitionFingerprintIsComputedOnceAndStaysTheSame()
    {
        CardBehaviorDefinition definition = CreateTaggedDefinition(BehaviorCondition.Tagged(PlayerSide.Runner));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:memo").Add(definition).Build();
        string first = catalog.GetDefinitionFingerprint(SearchCardId);

        Assert.Equal(BehaviorCatalogFingerprint.ComputeDefinition(definition), first);
        Assert.Same(first, catalog.GetDefinitionFingerprint(SearchCardId));
        Assert.Throws<KeyNotFoundException>(() => catalog.GetDefinitionFingerprint(new CardDefinitionId("unregistered")));
    }

    [Fact]
    public void HostedCreditOperandAndCostParticipateInCatalogEqualityAndFingerprint()
    {
        CardBehaviorDefinition defaultLoad = new(
            SearchCardId,
            PlayerSide.Runner,
            costs: [new BehaviorCost(BehaviorCostKind.HostedCredits, 1)],
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.LoadHostedCredits,
                amount: 2)]);
        CardBehaviorDefinition explicitLoad = new(
            SearchCardId,
            PlayerSide.Runner,
            costs: [new BehaviorCost(BehaviorCostKind.HostedCredits, 1)],
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.LoadHostedCredits,
                amount: 2,
                spendRestriction: HostedCreditSpendRestriction.CardEffectOnly)]);
        CardBehaviorDefinition runLoad = new(
            SearchCardId,
            PlayerSide.Runner,
            costs: [new BehaviorCost(BehaviorCostKind.HostedCredits, 2)],
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.LoadHostedCredits,
                amount: 2,
                spendRestriction: HostedCreditSpendRestriction.RunCost)]);

        BehaviorCatalog defaultCatalog = new BehaviorCatalogBuilder("test:hosted-credit")
            .Add(defaultLoad)
            .Build();
        BehaviorCatalog explicitCatalog = new BehaviorCatalogBuilder("test:hosted-credit")
            .Add(explicitLoad)
            .Build();
        BehaviorCatalog changedCatalog = new BehaviorCatalogBuilder("test:hosted-credit")
            .Add(runLoad)
            .Build();

        Assert.Equal(defaultLoad.Instructions[0], explicitLoad.Instructions[0]);
        Assert.Equal(defaultCatalog.Fingerprint, explicitCatalog.Fingerprint);
        Assert.NotEqual(defaultCatalog.Fingerprint, changedCatalog.Fingerprint);
        Assert.NotEqual(defaultLoad.Costs[0], runLoad.Costs[0]);
    }

    [Fact]
    public void TriggerContractValidatesOccurrenceScopeFilterAndInstructionMatrix()
    {
        BehaviorInstruction instruction = new(BehaviorInstructionKind.GainCredits, amount: 1);
        BehaviorCardFilter filter = new(
            BehaviorCardType.Asset,
            definitionId: SearchCardId,
            ownerSide: PlayerSide.Corp);

        BehaviorTrigger trigger = new(
            "contract-trigger",
            OccurrenceKind.CardInstalled,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Self,
            filter,
            [instruction]);

        Assert.Equal("contract-trigger", trigger.Key);
        Assert.Equal(OccurrenceKind.CardInstalled, trigger.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Self, trigger.Scope);
        Assert.Equal(filter, trigger.SubjectCardFilter);
        Assert.Single(trigger.Instructions);

        foreach (OccurrenceKind occurrence in new[]
        {
            OccurrenceKind.CardInstalled,
            OccurrenceKind.CardRezzed,
            OccurrenceKind.IceEncountered,
            OccurrenceKind.RunSuccessful,
            OccurrenceKind.RunEnded,
            OccurrenceKind.AgendaScoredOrStolen,
            OccurrenceKind.CardAccessed,
            OccurrenceKind.HostedCreditPoolEmptied,
            OccurrenceKind.CardAdvancedFromZero,
            OccurrenceKind.AccessedCardTrashed,
        })
        {
            _ = new BehaviorTrigger(
                $"self-{occurrence}",
                occurrence,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                filter,
                [instruction]);
        }

        foreach (OccurrenceKind occurrence in Enum.GetValues<OccurrenceKind>())
        {
            _ = new BehaviorTrigger(
                $"controller-{occurrence}",
                occurrence,
                TriggerMode.Optional,
                BehaviorTriggerScope.Controller,
                null,
                [instruction]);
            _ = new BehaviorTrigger(
                $"any-{occurrence}",
                occurrence,
                TriggerMode.Optional,
                BehaviorTriggerScope.Any,
                null,
                [instruction]);
        }

        foreach (OccurrenceKind occurrence in Enum.GetValues<OccurrenceKind>()
            .Except(new[]
            {
                OccurrenceKind.CardInstalled,
                OccurrenceKind.CardRezzed,
                OccurrenceKind.IceEncountered,
                OccurrenceKind.IceApproached,
                OccurrenceKind.RunSuccessful,
                OccurrenceKind.RunEnded,
                OccurrenceKind.AgendaScoredOrStolen,
                OccurrenceKind.CardAccessed,
                OccurrenceKind.HostedCreditPoolEmptied,
                OccurrenceKind.CardAdvancedFromZero,
                OccurrenceKind.AccessedCardTrashed,
                OccurrenceKind.AgendaForfeited,
                OccurrenceKind.FirstCardCreditGainThisTurn,
                OccurrenceKind.FirstOperationPlayedThisTurn,
                OccurrenceKind.FirstEventPlayedThisTurn,
                OccurrenceKind.CardAdvanced,
            }))
        {
            Assert.Throws<ArgumentException>(() => new BehaviorTrigger(
                $"invalid-self-{occurrence}",
                occurrence,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                null,
                [instruction]));
            Assert.Throws<ArgumentException>(() => new BehaviorTrigger(
                $"invalid-filter-{occurrence}",
                occurrence,
                TriggerMode.Optional,
                BehaviorTriggerScope.Controller,
                filter,
                [instruction]));
        }

        Assert.Throws<ArgumentException>(() => new BehaviorTrigger(
            "empty-trigger",
            OccurrenceKind.TurnBegin,
            TriggerMode.Optional,
            BehaviorTriggerScope.Controller,
            null,
            []));
        Assert.Throws<ArgumentException>(() => new BehaviorTrigger(
            "invalid key",
            OccurrenceKind.TurnBegin,
            TriggerMode.Optional,
            BehaviorTriggerScope.Controller,
            null,
            [instruction]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorTrigger(
            "invalid-occurrence",
            (OccurrenceKind)999,
            TriggerMode.Optional,
            BehaviorTriggerScope.Controller,
            null,
            [instruction]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorTrigger(
            "invalid-mode",
            OccurrenceKind.TurnBegin,
            (TriggerMode)999,
            BehaviorTriggerScope.Controller,
            null,
            [instruction]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorTrigger(
            "invalid-scope",
            OccurrenceKind.TurnBegin,
            TriggerMode.Optional,
            (BehaviorTriggerScope)999,
            null,
            [instruction]));
        Assert.Throws<ArgumentNullException>(() => new BehaviorTrigger(
            "null-instructions",
            OccurrenceKind.TurnBegin,
            TriggerMode.Optional,
            BehaviorTriggerScope.Controller,
            null,
            null!));
    }

    [Fact]
    public void TriggerContractFreezesInstructionsRejectsDuplicateKeysAndRoundTrips()
    {
        var instructions = new List<BehaviorInstruction>
        {
            new(BehaviorInstructionKind.GainCredits, amount: 1),
        };
        BehaviorTrigger trigger = new(
            "immutable-trigger",
            OccurrenceKind.CardRezzed,
            TriggerMode.Optional,
            BehaviorTriggerScope.Self,
            new BehaviorCardFilter(BehaviorCardType.Ice),
            instructions);

        instructions.Add(new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2));
        Assert.Single(trigger.Instructions);
        Assert.IsAssignableFrom<IList<BehaviorInstruction>>(trigger.Instructions);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<BehaviorInstruction>)trigger.Instructions).Clear());

        Assert.Throws<ArgumentException>(() => new CardBehaviorDefinition(
            SearchCardId,
            PlayerSide.Corp,
            triggers:
            [
                trigger,
                new BehaviorTrigger(
                    "immutable-trigger",
                    OccurrenceKind.TurnBegin,
                    TriggerMode.Optional,
                    BehaviorTriggerScope.Controller,
                    null,
                    [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1)]),
            ]));
    }

    [Fact]
    public void TriggerContractEqualityFingerprintAndJsonIncludeNestedFields()
    {
        BehaviorCardFilter filter = new(
            BehaviorCardType.Asset,
            definitionId: SearchCardId,
            ownerSide: PlayerSide.Corp,
            subtype: "campaign");
        BehaviorTrigger first = new(
            "nested-trigger",
            OccurrenceKind.HostedCreditPoolEmptied,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Self,
            filter,
            [
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1),
                new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1),
            ]);
        BehaviorTrigger equivalent = new(
            "nested-trigger",
            OccurrenceKind.HostedCreditPoolEmptied,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Self,
            new BehaviorCardFilter(
                BehaviorCardType.Asset,
                definitionId: SearchCardId,
                ownerSide: PlayerSide.Corp,
                subtype: "campaign"),
            [
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1),
                new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1),
            ]);
        BehaviorTrigger changedInstruction = new(
            "nested-trigger",
            OccurrenceKind.HostedCreditPoolEmptied,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Self,
            filter,
            [
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2),
                new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1),
            ]);
        BehaviorTrigger changedFilter = new(
            "nested-trigger",
            OccurrenceKind.HostedCreditPoolEmptied,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Self,
            new BehaviorCardFilter(BehaviorCardType.Asset, ownerSide: PlayerSide.Runner),
            [
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1),
                new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1),
            ]);

        Assert.Equal(first, equivalent);
        Assert.NotEqual(first, changedInstruction);
        Assert.NotEqual(first, changedFilter);

        string json = EngineJson.Serialize(first);
        Assert.Contains("\"key\":\"nested-trigger\"", json, StringComparison.Ordinal);
        Assert.Contains("\"occurrence\":\"hostedCreditPoolEmptied\"", json, StringComparison.Ordinal);
        Assert.Contains("\"scope\":\"self\"", json, StringComparison.Ordinal);
        Assert.Contains("\"subjectCardFilter\"", json, StringComparison.Ordinal);
        Assert.Contains("\"instructions\"", json, StringComparison.Ordinal);
        BehaviorTrigger restored = EngineJson.Deserialize<BehaviorTrigger>(json);
        Assert.Equal(first, restored);
        Assert.Equal(json, EngineJson.Serialize(restored));

        BehaviorCatalog firstCatalog = new BehaviorCatalogBuilder("test:trigger-catalog")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Corp, triggers: [first]))
            .Build();
        BehaviorCatalog equivalentCatalog = new BehaviorCatalogBuilder("test:trigger-catalog")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Corp, triggers: [equivalent]))
            .Build();
        BehaviorCatalog changedInstructionCatalog = new BehaviorCatalogBuilder("test:trigger-catalog")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Corp, triggers: [changedInstruction]))
            .Build();
        BehaviorCatalog changedFilterCatalog = new BehaviorCatalogBuilder("test:trigger-catalog")
            .Add(new CardBehaviorDefinition(SearchCardId, PlayerSide.Corp, triggers: [changedFilter]))
            .Build();

        Assert.Equal(firstCatalog.Fingerprint, equivalentCatalog.Fingerprint);
        Assert.NotEqual(firstCatalog.Fingerprint, changedInstructionCatalog.Fingerprint);
        Assert.NotEqual(firstCatalog.Fingerprint, changedFilterCatalog.Fingerprint);
    }

    [Fact]
    public void DynamicGainAmountIsTypedStructuralAndMutuallyExclusiveWithStaticAmount()
    {
        BehaviorAmountOperand operand = new("virus", 2);
        BehaviorInstruction gain = new(
            BehaviorInstructionKind.GainCredits,
            amountOperand: operand);
        BehaviorInstruction equivalent = EngineJson.Deserialize<BehaviorInstruction>(
            EngineJson.Serialize(gain));

        Assert.Equal(0, gain.Amount);
        Assert.Equal(operand, gain.AmountOperand);
        Assert.Equal(gain, equivalent);
        string json = EngineJson.Serialize(gain);
        Assert.Contains("\"amountOperand\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceCounterName\":\"virus\"", json, StringComparison.Ordinal);
        Assert.Contains("\"multiplier\":2", json, StringComparison.Ordinal);
        Assert.Equal(json, EngineJson.Serialize(equivalent));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorAmountOperand("virus", 0));
        Assert.Throws<ArgumentException>(() => new BehaviorAmountOperand("not stable", 2));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.GainCredits,
            amount: 1,
            amountOperand: operand));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Draw,
            amount: 1,
            amountOperand: operand));

        BehaviorCatalog first = new BehaviorCatalogBuilder("test:dynamic-amount")
            .Add(new CardBehaviorDefinition(
                SearchCardId,
                PlayerSide.Runner,
                instructions: [gain]))
            .Build();
        BehaviorCatalog changed = new BehaviorCatalogBuilder("test:dynamic-amount")
            .Add(new CardBehaviorDefinition(
                SearchCardId,
                PlayerSide.Runner,
                instructions:
                [new BehaviorInstruction(
                    BehaviorInstructionKind.GainCredits,
                    amountOperand: new BehaviorAmountOperand("virus", 3))]))
            .Build();
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void BehaviorContractStructureIncludesBehaviorRunOperandInInstructionAndDefinitionEquality()
    {
        BehaviorRunOperand rnd = new(ServerKind.RAndD);
        BehaviorRunOperand headquarters = new(ServerKind.Headquarters);
        BehaviorInstruction rndInstruction = new(
            BehaviorInstructionKind.StartRun,
            runOperand: rnd);
        BehaviorInstruction headquartersInstruction = new(
            BehaviorInstructionKind.StartRun,
            runOperand: headquarters);

        Assert.NotEqual(rndInstruction, headquartersInstruction);
        Assert.False(BehaviorContractStructure.AreEqual(rndInstruction, headquartersInstruction));

        BehaviorPaidAbility rndAbility = new(
            "run",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks)],
            [],
            [],
            [rndInstruction],
            BehaviorAbilityUsageLimit.Unlimited);
        BehaviorPaidAbility headquartersAbility = new(
            "run",
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            [new BehaviorCost(BehaviorCostKind.Clicks)],
            [],
            [],
            [headquartersInstruction],
            BehaviorAbilityUsageLimit.Unlimited);
        CardBehaviorDefinition rndDefinition = new(
            SearchCardId,
            PlayerSide.Runner,
            paidAbilities: [rndAbility]);
        CardBehaviorDefinition headquartersDefinition = new(
            SearchCardId,
            PlayerSide.Runner,
            paidAbilities: [headquartersAbility]);

        Assert.False(BehaviorCatalogStructure.AreEqual(rndDefinition, headquartersDefinition));
    }

    [Fact]
    public void StableChosenTargetSupportsCounterPlacementAndGripInstallOrigin()
    {
        BehaviorInstruction counter = new(
            BehaviorInstructionKind.AddCounter,
            amount: 1,
            argument: "virus",
            target: new BehaviorTarget(
                BehaviorTargetKind.ChosenCard,
                selector: "trigger-subject"));
        BehaviorInstruction install = new(
            BehaviorInstructionKind.InstallCard,
            target: new BehaviorTarget(
                BehaviorTargetKind.ChosenCard,
                selector: "pantograph-install"),
            sourceZone: BehaviorZone.Grip,
            destinationZone: BehaviorZone.Rig,
            optional: true);

        Assert.Equal("trigger-subject", counter.Target?.Selector);
        Assert.Equal(BehaviorZone.Grip, install.SourceZone);
        Assert.Equal("pantograph-install", install.Target?.Selector);
        Assert.True(install.Optional);
        Assert.Equal(install, EngineJson.Deserialize<BehaviorInstruction>(EngineJson.Serialize(install)));
    }

    private static CardBehaviorDefinition CreateTaggedDefinition(BehaviorCondition condition) =>
        new(
            SearchCardId,
            PlayerSide.Corp,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Branch,
                    branch: new BehaviorBranchSpec(
                        condition,
                        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)])),
            ]);

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
