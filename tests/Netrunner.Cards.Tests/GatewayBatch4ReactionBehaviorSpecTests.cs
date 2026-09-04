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

        Assert.Equal("gateway-m4-4.5-c1", catalog.Version);
        Assert.Equal(
            GatewayBatch4BehaviorSpecs.AssignedDefinitionIds,
            GatewayBatch4BehaviorSpecs.GenericSpecs.Select(static definition => definition.DefinitionId));
        Assert.All(
            GatewayBatch4BehaviorSpecs.AssignedDefinitionIds,
            definitionId => Assert.Contains(definitionId, catalog.AllDefinitionIds));
        Assert.DoesNotContain(
            catalog.IceSpecs,
            spec => GatewayBatch4BehaviorSpecs.AssignedDefinitionIds.Contains(spec.DefinitionId));
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
        Assert.False(catalog.TryGetIce(GatewayBatch4CardCodes.Funhouse, out _));

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

    private static CardBehaviorDefinition Definition(CardDefinitionId definitionId) =>
        GatewayBehaviorCatalog.Create().GenericSpecs.Single(
            definition => definition.DefinitionId == definitionId);
}
