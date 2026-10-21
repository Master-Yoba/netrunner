using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch2TriggerBehaviorSpecTests
{
    [Fact]
    public void TriggerCardsAreComposedInNumericCodeOrder()
    {
        Assert.Equal(
            new CardDefinitionId[]
            {
                GatewayBatch2CardCodes.Carnivore,
                GatewayBatch2CardCodes.Fermenter,
                GatewayBatch2CardCodes.Cookbook,
                GatewayBatch2CardCodes.Pennyshaver,
                GatewayBatch2CardCodes.Carmen,
                GatewayBatch2CardCodes.Marjanah,
                GatewayBatch2CardCodes.RedTeam,
                GatewayBatch2CardCodes.Pantograph,
                GatewayBatch2CardCodes.Conduit,
                GatewayBatch2CardCodes.TeleworkContract,
                GatewayBatch2CardCodes.SmartwareDistributor,
                GatewayCardCodes.NicoCampaign,
                GatewayBatch2CardCodes.SpinDoctor,
                GatewayBatch2CardCodes.RegolithMiningLicense,
            },
            GatewayBatch2BehaviorSpecs.GenericSpecs.Select(static definition => definition.DefinitionId));

        BehaviorCatalog first = GatewayBehaviorCatalog.Create();
        BehaviorCatalog second = GatewayBehaviorCatalog.Create();
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Contains(GatewayBatch2CardCodes.Fermenter, first.AllDefinitionIds);
        Assert.Contains(GatewayBatch2CardCodes.Cookbook, first.AllDefinitionIds);
        Assert.Contains(GatewayBatch2CardCodes.Pantograph, first.AllDefinitionIds);
    }

    [Fact]
    public void FermenterDeclaresMandatoryLoadsAndDynamicCashout()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch2CardCodes.Fermenter);

        Assert.Equal(PlayerSide.Runner, definition.Controller);
        Assert.Empty(definition.StaticModifiers);
        Assert.Equal(
            ["fermenter-install", "fermenter-turn-begin"],
            definition.Triggers.Select(static trigger => trigger.Key));
        Assert.All(definition.Triggers, trigger =>
        {
            Assert.Equal(OccurrenceKind.CardInstalled == trigger.Occurrence
                ? BehaviorTriggerScope.Self
                : BehaviorTriggerScope.Controller, trigger.Scope);
            Assert.Equal(TriggerMode.Mandatory, trigger.Mode);
            Assert.Equal(BehaviorInstructionKind.AddCounter, Assert.Single(trigger.Instructions).Kind);
            Assert.Equal("virus", Assert.Single(trigger.Instructions).Argument);
            Assert.Equal(1, Assert.Single(trigger.Instructions).Amount);
        });

        BehaviorPaidAbility ability = Assert.Single(definition.PaidAbilities);
        Assert.Equal(BehaviorAbilityActivation.ClickAction, ability.Activation);
        Assert.Equal(
            [BehaviorCostKind.Clicks, BehaviorCostKind.TrashSelf],
            ability.Costs.Select(static cost => cost.Kind));
        BehaviorInstruction gain = Assert.Single(ability.Instructions);
        Assert.Equal(BehaviorInstructionKind.GainCredits, gain.Kind);
        Assert.Equal(0, gain.Amount);
        Assert.Equal(new BehaviorAmountOperand("virus", 2), gain.AmountOperand);
    }

    [Fact]
    public void CookbookUsesTheEngineSeededTriggerSubjectForOnlyRunnerVirusPrograms()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch2CardCodes.Cookbook);
        BehaviorTrigger trigger = Assert.Single(definition.Triggers);

        Assert.Equal(OccurrenceKind.CardInstalled, trigger.Occurrence);
        Assert.Equal(TriggerMode.Optional, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Controller, trigger.Scope);
        Assert.Equal(BehaviorCardType.Program, trigger.SubjectCardFilter?.CardType);
        Assert.Equal(PlayerSide.Runner, trigger.SubjectCardFilter?.OwnerSide);
        Assert.Equal("virus", trigger.SubjectCardFilter?.Subtype);

        BehaviorInstruction counter = Assert.Single(trigger.Instructions);
        Assert.Equal(BehaviorInstructionKind.AddCounter, counter.Kind);
        Assert.Equal(BehaviorTargetKind.ChosenCard, counter.Target?.Kind);
        Assert.Equal("trigger-subject", counter.Target?.Selector);
        Assert.Equal("virus", counter.Argument);
    }

    [Fact]
    public void PantographOffersOptionalStableGripInstallAndPersistsItsTypedShape()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch2CardCodes.Pantograph);
        Assert.Equal(
            new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, 1),
            Assert.Single(definition.StaticModifiers));

        BehaviorTrigger trigger = Assert.Single(definition.Triggers);
        Assert.Equal(OccurrenceKind.AgendaScoredOrStolen, trigger.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Any, trigger.Scope);
        Assert.Equal(
            [
                BehaviorInstructionKind.GainCredits,
                BehaviorInstructionKind.Choose,
                BehaviorInstructionKind.InstallCard,
            ],
            trigger.Instructions.Select(static instruction => instruction.Kind));

        BehaviorChoiceSpec choice = Assert.IsType<BehaviorChoiceSpec>(trigger.Instructions[1].Choice);
        Assert.Equal("pantograph-install", choice.Key);
        Assert.Equal(BehaviorChoiceMode.Optional, choice.Mode);
        Assert.Equal(BehaviorChoiceCardinality.UpTo(1), choice.Cardinality);
        Assert.Equal(BehaviorZone.Grip, choice.Target?.Zone);
        Assert.Equal(PlayerSide.Runner, choice.Target?.Filter?.OwnerSide);

        BehaviorInstruction install = trigger.Instructions[2];
        Assert.True(install.Optional);
        Assert.Equal("pantograph-install", install.Target?.Selector);
        Assert.Equal(BehaviorZone.Grip, install.SourceZone);
        Assert.Equal(BehaviorZone.Rig, install.DestinationZone);

        string json = EngineJson.Serialize(definition);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
        Assert.Equal(json, EngineJson.Serialize(restored));
        BehaviorCatalog first = new BehaviorCatalogBuilder("test:pantograph-json")
            .Add(definition)
            .Build();
        BehaviorCatalog second = new BehaviorCatalogBuilder("test:pantograph-json")
            .Add(restored)
            .Build();
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    private static CardBehaviorDefinition Definition(CardDefinitionId definitionId) =>
        GatewayBehaviorCatalog.Create().GenericSpecs.Single(
            definition => definition.DefinitionId == definitionId);
}
