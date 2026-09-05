using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Structural proofs for the Slice 4.5 generic bindings owned by this
/// checkpoint. Runtime lifecycle evidence for the three reaction cards lives
/// alongside these declarations and injects the same immutable Gateway
/// catalog into Core.
/// </summary>
public sealed class GatewayBatch4ReactionBehaviorSpecTests
{
    [Fact]
    public void CatalogComposesBatch4BindingsAtTheCheckpointVersion()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.Equal("gateway-m4-4.6", catalog.Version);
        Assert.Equal(
            GatewayBatch4BehaviorSpecs.AssignedDefinitionIds,
            GatewayBatch4BehaviorSpecs.GenericSpecs.Select(static definition => definition.DefinitionId));
        Assert.All(
            GatewayBatch4BehaviorSpecs.AssignedDefinitionIds,
            definitionId => Assert.Contains(definitionId, catalog.AllDefinitionIds));
        Assert.Equal(
            [GatewayBatch4CardCodes.Funhouse],
            catalog.IceSpecs
                .Where(spec => GatewayBatch4BehaviorSpecs.AssignedDefinitionIds.Contains(spec.DefinitionId))
                .Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30042")]
    public void ManegarmDeclaresMandatorySameServerRunnerAlternatives()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch4CardCodes.Manegarm);
        BehaviorTrigger trigger = Assert.Single(definition.Triggers);

        Assert.Equal(OccurrenceKind.ServerApproached, trigger.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Any, trigger.Scope);
        Assert.True(trigger.RequireSourceOnAttackedServer);

        BehaviorChoiceSpec choice = Assert.IsType<BehaviorChoiceSpec>(
            Assert.Single(trigger.Instructions).Choice);
        Assert.Equal("manegarm-approach-choice", choice.Key);
        Assert.Equal(BehaviorChooser.ActingPlayer, choice.Chooser);
        Assert.Equal(BehaviorChoiceMode.Mode, choice.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), choice.Cardinality);
        Assert.Equal(
            ["spend-clicks", "pay-credits", "end-run"],
            choice.Options.Select(static option => option.Key));

        BehaviorChoiceOption clicks = choice.Options[0];
        BehaviorCost[] clickCosts = clicks.Costs?.ToArray()
            ?? throw new Xunit.Sdk.XunitException("Manegarm click option has no costs.");
        Assert.Equal(
            [BehaviorCostKind.Clicks],
            clickCosts.Select(static cost => cost.Kind));
        Assert.Equal(2, clickCosts.Single().Amount);
        Assert.Equal(BehaviorChoiceOptionSemantic.Normal, clicks.Semantic);

        BehaviorChoiceOption credits = choice.Options[1];
        BehaviorCost[] creditCosts = credits.Costs?.ToArray()
            ?? throw new Xunit.Sdk.XunitException("Manegarm credit option has no costs.");
        Assert.Equal(
            [BehaviorCostKind.Credits],
            creditCosts.Select(static cost => cost.Kind));
        Assert.Equal(5, creditCosts.Single().Amount);
        Assert.Equal(BehaviorChoiceOptionSemantic.Normal, credits.Semantic);

        BehaviorChoiceOption fallback = choice.Options[2];
        Assert.Equal(BehaviorChoiceOptionSemantic.Fallback, fallback.Semantic);
        Assert.Equal(
            BehaviorInstructionKind.EndRun,
            Assert.Single(fallback.Instructions).Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30050")]
    public void AnoeticVoidDeclaresOptionalCorpCompoundPrivateCost()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch4CardCodes.AnoeticVoid);
        BehaviorTrigger trigger = Assert.Single(definition.Triggers);

        Assert.Equal(OccurrenceKind.ServerApproached, trigger.Occurrence);
        Assert.Equal(TriggerMode.Optional, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Any, trigger.Scope);
        Assert.True(trigger.RequireSourceOnAttackedServer);

        BehaviorChoiceSpec choice = Assert.IsType<BehaviorChoiceSpec>(
            Assert.Single(trigger.Instructions).Choice);
        Assert.Equal("anoetic-void-approach-choice", choice.Key);
        Assert.Equal(BehaviorChooser.Controller, choice.Chooser);
        Assert.Equal(BehaviorChoiceMode.Mode, choice.Mode);
        Assert.Equal(BehaviorChoiceCardinality.Exactly(1), choice.Cardinality);

        BehaviorChoiceOption pay = Assert.Single(
            choice.Options,
            static option => option.Key == "pay-and-trash-hq");
        BehaviorCost[] payCosts = pay.Costs?.ToArray()
            ?? throw new Xunit.Sdk.XunitException("Anoetic Void pay option has no costs.");
        Assert.Equal(BehaviorChoiceOptionSemantic.Normal, pay.Semantic);
        Assert.Equal(BehaviorInstructionKind.EndRun, Assert.Single(pay.Instructions).Kind);
        Assert.Equal(
            [BehaviorCostKind.Credits, BehaviorCostKind.Additional],
            payCosts.Select(static cost => cost.Kind));
        Assert.Equal(2, payCosts[0].Amount);

        BehaviorCost trash = payCosts[1];
        Assert.Equal(2, trash.Amount);
        Assert.Equal("anoetic-void-trash-hq", trash.AdditionalKey);
        Assert.Equal(BehaviorAdditionalCostKind.TrashCards, trash.AdditionalKind);
        Assert.Equal(BehaviorTargetKind.ZoneCards, trash.AdditionalTarget?.Kind);
        Assert.Equal(BehaviorZone.Headquarters, trash.AdditionalTarget?.Zone);
        Assert.Equal(PlayerSide.Corp, trash.AdditionalTarget?.Filter?.OwnerSide);
        Assert.False(trash.AdditionalTarget?.Reveal);

        BehaviorChoiceOption fallback = Assert.Single(
            choice.Options,
            static option => option.Key == "decline");
        Assert.Equal(BehaviorChoiceOptionSemantic.Fallback, fallback.Semantic);
        Assert.Equal(BehaviorInstructionKind.NoOp, Assert.Single(fallback.Instructions).Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30054")]
    public void FunhouseBindsOnlyItsMandatoryEncounterTagOrEndRunTrigger()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardBehaviorDefinition definition = Definition(GatewayBatch4CardCodes.Funhouse);
        BehaviorTrigger trigger = Assert.Single(definition.Triggers);

        Assert.Equal(OccurrenceKind.IceEncountered, trigger.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Self, trigger.Scope);
        Assert.False(trigger.RequireSourceOnAttackedServer);
        Assert.Empty(definition.PaidAbilities);
        Assert.Empty(definition.Instructions);
        Assert.True(catalog.TryGetIce(GatewayBatch4CardCodes.Funhouse, out IceBehaviorSpec? ice));
        Assert.NotNull(ice);
        Assert.IsType<PayCreditsOrTagSubroutineSpec>(Assert.Single(ice.Subroutines));

        BehaviorChoiceSpec choice = Assert.IsType<BehaviorChoiceSpec>(
            Assert.Single(trigger.Instructions).Choice);
        Assert.Equal("funhouse-encounter-choice", choice.Key);
        Assert.Equal(BehaviorChooser.ActingPlayer, choice.Chooser);
        Assert.Equal(BehaviorChoiceMode.Mode, choice.Mode);
        Assert.Equal(
            ["take-tag", "end-run"],
            choice.Options.Select(static option => option.Key));

        BehaviorChoiceOption tag = choice.Options[0];
        Assert.Equal(BehaviorChoiceOptionSemantic.Normal, tag.Semantic);
        BehaviorInstruction tagInstruction = Assert.Single(tag.Instructions);
        Assert.Equal(BehaviorInstructionKind.AddTags, tagInstruction.Kind);
        Assert.Equal(1, tagInstruction.Amount);
        Assert.Equal(BehaviorPlayerSelector.ActingPlayer, tagInstruction.Recipient);

        BehaviorChoiceOption fallback = choice.Options[1];
        Assert.Equal(BehaviorChoiceOptionSemantic.Fallback, fallback.Semantic);
        Assert.Equal(BehaviorInstructionKind.EndRun, Assert.Single(fallback.Instructions).Kind);
    }

    [Fact]
    public void ReactionDefinitionsRoundTripWithoutChangingCatalogFingerprints()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        foreach (CardDefinitionId definitionId in GatewayBatch4BehaviorSpecs.AssignedDefinitionIds)
        {
            CardBehaviorDefinition definition = Definition(definitionId);
            string json = EngineJson.Serialize(definition);
            CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);

            Assert.Equal(json, EngineJson.Serialize(restored));
            BehaviorCatalog restoredCatalog = new BehaviorCatalogBuilder(catalog.Version)
                .Add(restored)
                .Build();
            Assert.Equal(
                new BehaviorCatalogBuilder(catalog.Version).Add(definition).Build().Fingerprint,
                restoredCatalog.Fingerprint);
        }
    }

    [Fact]
    [Trait("GatewayCard", "30032")]
    public void MayflyDeclaresTypedAiBreakerAndRunEndCleanup()
    {
        IcebreakerBehaviorSpec mayfly = Assert.Single(
            GatewayBatch4IcebreakerBehaviorSpecs.IcebreakerSpecs);

        Assert.Equal(GatewayBatch4CardCodes.Mayfly, mayfly.DefinitionId);
        Assert.Equal(1, mayfly.BaseStrength);
        Assert.Equal(IceType.Any, mayfly.InterfaceType);
        Assert.Equal(1, mayfly.BreakCost);
        Assert.Equal(1, mayfly.BreakLimit);
        Assert.Equal(1, mayfly.PumpCost);
        Assert.Equal(1, mayfly.PumpStrength);
        Assert.Equal(
            GatewayBatch4IcebreakerBehaviorSpecs.MayflyRunEndCleanupBehaviorId,
            mayfly.RunEndCleanup?.BehaviorId);
    }

    [Fact]
    [Trait("GatewayCard", "30038")]
    [Trait("GatewayCard", "30039")]
    public void AnselAndBranShareOneTypedRunnerBioroidDeclaration()
    {
        Assert.Equal(
            [GatewayBatch4CardCodes.Ansel, GatewayBatch4CardCodes.Bran],
            GatewayBatch4BioroidBehaviorSpecs.BoundIceDefinitionIds);
        EncounteredBioroidAbilitySpec shared =
            Assert.Single(GatewayBatch4BioroidBehaviorSpecs.BioroidAbilities);
        Assert.Equal(GatewayBatch4BioroidBehaviorSpecs.BioroidBreakAbility, shared);
        Assert.Equal(BehaviorCostKind.LoseClick, shared.Cost.Kind);
        Assert.Equal(1, shared.Cost.Amount);
        Assert.Equal(1, shared.BreakCount);

        IceBehaviorSpec ansel = GatewayBatch4BioroidBehaviorSpecs.Bind(
            new IceBehaviorSpec(
                GatewayBatch4CardCodes.Ansel,
                strength: 4,
                iceType: IceType.Sentry,
                rezCost: 6,
                subroutines: [new EndRunSubroutineSpec()],
                strengthModifiers: []));
        IceBehaviorSpec bran = GatewayBatch4BioroidBehaviorSpecs.Bind(
            new IceBehaviorSpec(
                GatewayBatch4CardCodes.Bran,
                strength: 6,
                iceType: IceType.Barrier,
                rezCost: 6,
                subroutines: [new EndRunSubroutineSpec(), new EndRunSubroutineSpec()],
                strengthModifiers: []));

        Assert.Same(shared, Assert.Single(ansel.EncounteredBioroidAbilities!));
        Assert.Same(shared, Assert.Single(bran.EncounteredBioroidAbilities!));
        Assert.Single(ansel.Subroutines);
        Assert.Equal(2, bran.Subroutines.Count);
        Assert.Throws<ArgumentException>(() => GatewayBatch4BioroidBehaviorSpecs.Bind(
            new IceBehaviorSpec(
                GatewayCardCodes.Palisade,
                strength: 2,
                iceType: IceType.Barrier,
                rezCost: 3,
                subroutines: [new EndRunSubroutineSpec()],
                strengthModifiers: [])));
    }

    private static CardBehaviorDefinition Definition(CardDefinitionId definitionId) =>
        GatewayBehaviorCatalog.Create().GenericSpecs.Single(
            definition => definition.DefinitionId == definitionId);
}
