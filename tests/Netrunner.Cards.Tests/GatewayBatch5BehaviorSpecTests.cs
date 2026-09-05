using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch5BehaviorSpecTests
{
    [Fact]
    [Trait("GatewayCard", "30045")]
    public void UrticaAccessIsInstalledOnlyAndWorksUnrezzed()
    {
        BehaviorTrigger trigger = Assert.Single(Definition(GatewayBatch5CardCodes.UrticaCipher).Triggers);
        Assert.Equal(OccurrenceKind.CardAccessed, trigger.Occurrence);
        Assert.Equal(BehaviorTriggerScope.Self, trigger.Scope);
        Assert.True(trigger.RequireSourceInstalled);
        Assert.True(trigger.ActiveWhileUnrezzed);
        BehaviorInstruction damage = Assert.Single(trigger.Instructions);
        Assert.Equal(DamageKind.Net, damage.DamageKind);
        Assert.Equal(2, damage.DamageAmountOperand?.BaseAmount);
        Assert.Equal(BehaviorDamageAmountKind.SourceAdvancementCounters, damage.DamageAmountOperand?.Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30061")]
    public void ClearinghouseUsesOptionalControllerTurnTriggerAndTrashCostSnapshot()
    {
        BehaviorTrigger trigger = Assert.Single(Definition(GatewayBatch5CardCodes.Clearinghouse).Triggers);
        Assert.Equal(OccurrenceKind.TurnBegin, trigger.Occurrence);
        Assert.Equal(TriggerMode.Optional, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Controller, trigger.Scope);
        BehaviorInstruction damage = Assert.Single(trigger.Instructions);
        Assert.Equal(DamageKind.Meat, damage.DamageKind);
        Assert.True(damage.DamageAmountOperand?.TrashSourceAsCost);
    }

    [Fact]
    [Trait("GatewayCard", "30049")]
    public void NeurospikeUsesScoredPrintedPointsAndCanDealZeroDamage()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch5CardCodes.Neurospike);
        Assert.Equal(3, Assert.Single(definition.Costs).Amount);
        Assert.Empty(definition.Conditions);
        BehaviorInstruction damage = Assert.Single(definition.Instructions);
        Assert.Equal(DamageKind.Net, damage.DamageKind);
        Assert.Equal(BehaviorDamageAmountKind.ScoredAgendaPrintedPointsThisTurn, damage.DamageAmountOperand?.Kind);
    }

    [Fact]
    [Trait("GatewayCard", "30057")]
    public void PublicTrailChecksPriorRunnerTurnAndRunnerChoosesTagOrPayEight()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch5CardCodes.PublicTrail);
        Assert.Equal(BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn, Assert.Single(definition.Conditions).Kind);
        BehaviorChoiceSpec choice = Assert.IsType<BehaviorChoiceSpec>(Assert.Single(definition.Instructions).Choice);
        Assert.Equal(BehaviorChooser.Runner, choice.Chooser);
        Assert.Equal(8, Assert.Single(choice.Options.Single(option => option.Key == "pay-eight").Costs!).Amount);
        BehaviorInstruction tag = Assert.Single(choice.Options.Single(option => option.Key == "take-tag").Instructions);
        Assert.Equal(1, tag.Amount);
        Assert.Equal(BehaviorPlayerSelector.Runner, tag.Recipient);
    }

    [Fact]
    [Trait("GatewayCard", "30065")]
    public void RetributionRequiresTaggedRunnerAndExcludesResourcesFromItsTargets()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch5CardCodes.Retribution);
        Assert.Equal(BehaviorCondition.Tagged(PlayerSide.Runner), Assert.Single(definition.Conditions));
        BehaviorTarget target = Assert.Single(definition.Targets);
        Assert.Equal(BehaviorTargetKind.InstalledCard, target.Kind);
        Assert.Equal(BehaviorCardType.ProgramOrHardware, target.Filter?.CardType);
        Assert.Equal(PlayerSide.Runner, target.Filter?.OwnerSide);
        Assert.Equal(BehaviorChooser.Corp, definition.Instructions[0].Choice?.Chooser);
    }

    private static CardBehaviorDefinition Definition(CardDefinitionId id) =>
        Assert.Single(GatewayBatch5BehaviorSpecs.GenericSpecs, definition => definition.DefinitionId == id);
}
