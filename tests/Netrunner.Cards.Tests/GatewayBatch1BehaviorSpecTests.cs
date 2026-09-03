using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch1BehaviorSpecTests
{
    private static readonly string[] WildcatOptionKeys = ["gain", "draw"];

    private static readonly string[] PlanogramTaggedKeys = ["gain", "draw", "both"];

    private static readonly string[] PlanogramOrderKeys = ["gain-first", "draw-first"];

    private static readonly BehaviorInstructionKind[] CreativeInstructionKinds =
    [
        BehaviorInstructionKind.GainCredits,
        BehaviorInstructionKind.LoseClicks,
    ];

    private static readonly BehaviorStaticModifierKind[] T400ModifierKinds =
    [
        BehaviorStaticModifierKind.MemoryUnits,
        BehaviorStaticModifierKind.MaximumHandSize,
    ];

    private static readonly CardDefinitionId[] ExpectedIds =
    [
        GatewayBatch1CardCodes.WildcatStrike,
        GatewayBatch1CardCodes.MutualFavor,
        GatewayBatch1CardCodes.CreativeCommission,
        GatewayBatch1CardCodes.VRcation,
        GatewayBatch1CardCodes.SureGamble,
        GatewayBatch1CardCodes.T400MemoryDiamond,
        GatewayBatch1CardCodes.SeamlessLaunch,
        GatewayBatch1CardCodes.Sprint,
        GatewayBatch1CardCodes.HanseiReview,
        GatewayBatch1CardCodes.PredictivePlanogram,
        GatewayBatch1CardCodes.GovernmentSubsidy,
        GatewayBatch1CardCodes.HedgeFund,
    ];

    private static readonly CardDefinitionId[] CompleteIds =
    [
        GatewayBatch1CardCodes.WildcatStrike,
        GatewayBatch1CardCodes.MutualFavor,
        GatewayBatch1CardCodes.CreativeCommission,
        GatewayBatch1CardCodes.VRcation,
        GatewayBatch1CardCodes.SureGamble,
        GatewayBatch1CardCodes.T400MemoryDiamond,
        GatewayBatch1CardCodes.SeamlessLaunch,
        GatewayBatch1CardCodes.Sprint,
        GatewayBatch1CardCodes.HanseiReview,
        GatewayBatch1CardCodes.PredictivePlanogram,
        GatewayBatch1CardCodes.GovernmentSubsidy,
        GatewayBatch1CardCodes.HedgeFund,
    ];

    private static readonly CardDefinitionId[] BlockedIds = [];

    [Fact]
    public void BatchContainsExactlyTheTwelveAssignedDefinitionIds()
    {
        Assert.Equal(12, GatewayBatch1BehaviorSpecs.AssignedDefinitionIds.Count);
        Assert.Equal(
            ExpectedIds.ToHashSet(),
            GatewayBatch1BehaviorSpecs.AssignedDefinitionIds.ToHashSet());
        Assert.Equal(12, GatewayBatch1BehaviorSpecs.GenericSpecs.Count);
        Assert.Equal(CompleteIds.ToHashSet(), GatewayBatch1BehaviorSpecs.GenericSpecs
            .Select(static spec => spec.DefinitionId)
            .ToHashSet());
        Assert.Equal(BlockedIds.ToHashSet(), GatewayBatch1BehaviorSpecs.BlockedDefinitionIds.ToHashSet());
    }

    [Fact]
    public void EveryBatchDefinitionIsExposedThroughTheImmutableGatewayCatalog()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        foreach (CardDefinitionId definitionId in CompleteIds)
        {
            Assert.Contains(definitionId, catalog.AllDefinitionIds);
            Assert.True(catalog.TryGet(definitionId, out CardBehaviorDefinition? definition));
            Assert.Equal(definitionId, definition.DefinitionId);
        }

        Assert.Equal(
            GatewayBatch1BehaviorSpecs.GenericSpecs.Select(static spec => spec.DefinitionId),
            catalog.GenericSpecs
                .Where(spec => CompleteIds.Contains(spec.DefinitionId))
                .Select(static spec => spec.DefinitionId));

        foreach (CardDefinitionId definitionId in BlockedIds)
        {
            Assert.DoesNotContain(definitionId, catalog.AllDefinitionIds);
            Assert.False(catalog.TryGet(definitionId, out _));
        }
    }

    [Fact]
    public void BatchDeclarationsHaveStableStructureAndFingerprint()
    {
        BehaviorCatalog first = GatewayBehaviorCatalog.Create();
        BehaviorCatalog second = GatewayBehaviorCatalog.Create();

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.BehaviorRegistryFingerprint, first.Fingerprint);
        Assert.Equal(first.AllDefinitionIds, second.AllDefinitionIds);

        CardBehaviorDefinition wildcat = Definition(GatewayBatch1CardCodes.WildcatStrike);
        Assert.Equal(PlayerSide.Runner, wildcat.Controller);
        Assert.Equal((BehaviorCostKind.Credits, 2), Cost(wildcat));
        BehaviorChoiceSpec wildcatChoice = Assert.Single(wildcat.Instructions).Choice!;
        Assert.Equal(BehaviorChooser.Corp, wildcatChoice.Chooser);
        Assert.Equal(BehaviorChoiceMode.Mode, wildcatChoice.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), wildcatChoice.Cardinality);
        Assert.Equal(
            WildcatOptionKeys,
            wildcatChoice.Options.Select(static option => option.Key));
        Assert.Equal(6, Assert.Single(wildcatChoice.Options[0].Instructions).Amount);
        Assert.Equal(4, Assert.Single(wildcatChoice.Options[1].Instructions).Amount);

        CardBehaviorDefinition mutual = Definition(GatewayBatch1CardCodes.MutualFavor);
        Assert.Equal(PlayerSide.Runner, mutual.Controller);
        Assert.Empty(mutual.Targets);
        Assert.Equal((BehaviorCostKind.Credits, 0), Cost(mutual));
        Assert.Equal(
            [
                BehaviorInstructionKind.Search,
                BehaviorInstructionKind.ShuffleIntoDeck,
                BehaviorInstructionKind.Branch,
            ],
            mutual.Instructions.Select(static instruction => instruction.Kind));

        BehaviorInstruction search = mutual.Instructions[0];
        Assert.Equal("mutual-favor-search", search.Argument);
        Assert.Equal(BehaviorZone.Stack, search.SourceZone);
        Assert.Equal(BehaviorTargetKind.ZoneCard, search.Target?.Kind);
        Assert.Equal(BehaviorZone.Stack, search.Target?.Zone);
        Assert.False(search.Target?.Reveal);
        Assert.Equal(BehaviorCardType.Icebreaker, search.Filter?.CardType);
        Assert.Equal(PlayerSide.Runner, search.Filter?.OwnerSide);
        Assert.Null(search.Filter?.DefinitionId);
        Assert.Null(search.Filter?.Subtype);

        BehaviorInstruction shuffle = mutual.Instructions[1];
        Assert.Equal(BehaviorTargetKind.ChosenCard, shuffle.Target?.Kind);
        Assert.Equal(BehaviorZone.Stack, shuffle.SourceZone);
        Assert.Equal(BehaviorZone.Stack, shuffle.DestinationZone);

        BehaviorBranchSpec foundBranch = mutual.Instructions[2].Branch!;
        Assert.Equal("mutual-favor-found-card", foundBranch.Key);
        Assert.Equal(BehaviorConditionKind.FoundCard, foundBranch.Condition.Kind);
        Assert.Empty(foundBranch.WhenFalse);
        Assert.Equal(BehaviorInstructionKind.Reveal, foundBranch.WhenTrue[0].Kind);
        Assert.Equal(BehaviorTargetKind.ChosenCard, foundBranch.WhenTrue[0].Target?.Kind);

        BehaviorBranchSpec runBranch = Assert.IsType<BehaviorBranchSpec>(foundBranch.WhenTrue[1].Branch);
        Assert.Equal("mutual-favor-successful-run", runBranch.Key);
        Assert.Equal(BehaviorConditionKind.SuccessfulRunThisTurn, runBranch.Condition.Kind);
        Assert.Equal(BehaviorInstructionKind.Choose, Assert.Single(runBranch.WhenTrue).Kind);
        BehaviorChoiceSpec disposition = Assert.Single(runBranch.WhenTrue).Choice!;
        Assert.Equal("mutual-favor-disposition", disposition.Key);
        Assert.Equal(BehaviorChooser.Controller, disposition.Chooser);
        Assert.Equal(BehaviorChoiceMode.Mode, disposition.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), disposition.Cardinality);
        Assert.Equal(["install", "grip"], disposition.Options.Select(static option => option.Key));
        BehaviorInstruction install = Assert.Single(disposition.Options[0].Instructions);
        Assert.Equal(BehaviorInstructionKind.InstallCard, install.Kind);
        Assert.Equal(BehaviorTargetKind.ChosenCard, install.Target?.Kind);
        Assert.Equal(BehaviorZone.Stack, install.SourceZone);
        Assert.Equal(BehaviorZone.Rig, install.DestinationZone);
        BehaviorInstruction grip = Assert.Single(disposition.Options[1].Instructions);
        Assert.Equal(BehaviorInstructionKind.AddToHand, grip.Kind);
        Assert.Equal(BehaviorTargetKind.ChosenCard, grip.Target?.Kind);
        Assert.Equal(BehaviorZone.Stack, grip.SourceZone);
        Assert.Equal(BehaviorZone.Grip, grip.DestinationZone);
        Assert.Single(runBranch.WhenFalse);
        Assert.Equal(BehaviorInstructionKind.AddToHand, runBranch.WhenFalse[0].Kind);
        Assert.Equal(BehaviorTargetKind.ChosenCard, runBranch.WhenFalse[0].Target?.Kind);
        Assert.Equal(BehaviorZone.Stack, runBranch.WhenFalse[0].SourceZone);
        Assert.Equal(BehaviorZone.Grip, runBranch.WhenFalse[0].DestinationZone);

        CardBehaviorDefinition creative = Definition(GatewayBatch1CardCodes.CreativeCommission);
        Assert.Equal(
            CreativeInstructionKinds,
            creative.Instructions.Select(static instruction => instruction.Kind));
        Assert.Equal(5, creative.Instructions[0].Amount);
        Assert.Equal(BehaviorConditionKind.ClicksRemainingAtLeast, creative.Instructions[1].Condition?.Kind);
        Assert.Equal(1, creative.Instructions[1].Condition?.Value);

        CardBehaviorDefinition vr = Definition(GatewayBatch1CardCodes.VRcation);
        Assert.Equal(4, vr.Instructions[0].Amount);
        Assert.Equal(BehaviorInstructionKind.LoseClicks, vr.Instructions[1].Kind);

        CardBehaviorDefinition t400 = Definition(GatewayBatch1CardCodes.T400MemoryDiamond);
        Assert.Equal(
            T400ModifierKinds,
            t400.StaticModifiers.Select(static modifier => modifier.Kind!.Value));
        Assert.All(t400.StaticModifiers, static modifier => Assert.Equal(1, modifier.Delta));

        Assert.Equal(
            (BehaviorInstructionKind.GainCredits, 15),
            Instruction(GatewayBatch1CardCodes.GovernmentSubsidy));
        Assert.Equal(
            (BehaviorInstructionKind.GainCredits, 9),
            Instruction(GatewayBatch1CardCodes.HedgeFund));
        Assert.Equal(
            (BehaviorInstructionKind.GainCredits, 9),
            Instruction(GatewayBatch1CardCodes.SureGamble));

        CardBehaviorDefinition sprint = Definition(GatewayBatch1CardCodes.Sprint);
        Assert.Equal(BehaviorInstructionKind.Draw, sprint.Instructions[0].Kind);
        Assert.Equal(3, sprint.Instructions[0].Amount);
        Assert.Equal(
            BehaviorChoiceCardinality.Exactly(2),
            sprint.Instructions[1].Choice?.Cardinality);
        Assert.Equal(BehaviorZone.Headquarters, sprint.Instructions[1].Choice?.Target?.Zone);
        Assert.Equal(BehaviorInstructionKind.ShuffleIntoDeck, sprint.Instructions[2].Kind);
        Assert.Equal(BehaviorZone.Deck, sprint.Instructions[2].DestinationZone);

        CardBehaviorDefinition hansei = Definition(GatewayBatch1CardCodes.HanseiReview);
        Assert.Equal(10, hansei.Instructions[0].Amount);
        Assert.Equal(BehaviorChoiceMode.Mandatory, hansei.Instructions[1].Choice?.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), hansei.Instructions[1].Choice?.Cardinality);
        Assert.Equal(BehaviorInstructionKind.TrashCard, hansei.Instructions[2].Kind);

        CardBehaviorDefinition seamless = Definition(GatewayBatch1CardCodes.SeamlessLaunch);
        Assert.Equal(PlayerSide.Corp, seamless.Controller);
        Assert.Equal((BehaviorCostKind.Credits, 1), Cost(seamless));
        Assert.Single(seamless.Targets);
        Assert.Equal(BehaviorTargetKind.InstalledCardNotThisTurn, seamless.Targets[0].Kind);
        Assert.Equal(PlayerSide.Corp, seamless.Targets[0].Filter?.OwnerSide);
        Assert.Equal(BehaviorChoiceMode.Mandatory, seamless.Instructions[0].Choice?.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), seamless.Instructions[0].Choice?.Cardinality);
        Assert.Equal(BehaviorInstructionKind.AdvanceCard, seamless.Instructions[1].Kind);
        Assert.Equal(BehaviorTargetKind.ChosenCard, seamless.Instructions[1].Target?.Kind);
        Assert.Equal(2, seamless.Instructions[1].Amount);

        CardBehaviorDefinition planogram = Definition(GatewayBatch1CardCodes.PredictivePlanogram);
        BehaviorBranchSpec planogramBranch = Assert.Single(planogram.Instructions).Branch!;
        Assert.Equal(BehaviorConditionKind.Tagged, planogramBranch.Condition.Kind);
        Assert.Equal(PlayerSide.Runner, planogramBranch.Condition.SubjectSide);
        BehaviorChoiceSpec tagged = Assert.Single(planogramBranch.WhenTrue).Choice!;
        Assert.Equal(BehaviorChoiceMode.Mode, tagged.Mode);
        Assert.Equal(PlanogramTaggedKeys, tagged.Options.Select(static option => option.Key));
        BehaviorChoiceSpec order = Assert.Single(tagged.Options[2].Instructions).Choice!;
        Assert.Equal(BehaviorChoiceMode.OrderSelection, order.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), order.Cardinality);
        Assert.Equal(PlanogramOrderKeys, order.Options.Select(static option => option.Key));
        Assert.Equal(BehaviorInstructionKind.Choose, Assert.Single(tagged.Options[2].Instructions).Kind);

    }

    [Fact]
    public void GenericBatchRegistrationIsSafeToComposeRepeatedly()
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:batch-1")
            .Add(GatewayBatch1BehaviorSpecs.GenericSpecs)
            .Add(GatewayBatch1BehaviorSpecs.GenericSpecs)
            .Build();

        Assert.Equal(12, catalog.GenericSpecs.Count);
        Assert.Equal(CompleteIds.ToHashSet(), catalog.AllDefinitionIds.ToHashSet());
    }

    [Fact]
    public void BlockedBatchCardsRemainOutsideTheExecutableBehaviorGate()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.Equal(BlockedIds.ToHashSet(), GatewayBatch1BehaviorSpecs.BlockedDefinitionIds.ToHashSet());
        Assert.All(BlockedIds, definitionId =>
        {
            Assert.DoesNotContain(definitionId, catalog.AllDefinitionIds);
            Assert.False(catalog.TryGet(definitionId, out _));
        });
    }

    private static CardBehaviorDefinition Definition(CardDefinitionId definitionId) =>
        GatewayBehaviorCatalog.Create().GenericSpecs.Single(spec => spec.DefinitionId == definitionId);

    private static (BehaviorCostKind Kind, int Amount) Cost(CardBehaviorDefinition definition)
    {
        BehaviorCost cost = Assert.Single(definition.Costs);
        return (cost.Kind, cost.Amount);
    }

    private static (BehaviorInstructionKind Kind, int Amount) Instruction(CardDefinitionId definitionId)
    {
        BehaviorInstruction instruction = Assert.Single(Definition(definitionId).Instructions);
        return (instruction.Kind, instruction.Amount);
    }
}
