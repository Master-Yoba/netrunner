using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch3C1BehaviorSpecTests
{
    [Fact]
    public void EchelonHasItsTypedPassiveAndPrintedStrengthContract()
    {
        IcebreakerBehaviorSpec spec = GatewayBehaviorCatalog.Create().IcebreakerSpecs.Single(
            candidate => candidate.DefinitionId == GatewayBatch3CardCodes.Echelon);

        Assert.Equal(0, spec.BaseStrength);
        Assert.Equal(IceType.Sentry, spec.InterfaceType);
        Assert.Equal(1, spec.BreakCost);
        Assert.Equal(1, spec.BreakLimit);
        Assert.Equal(3, spec.PumpCost);
        Assert.Equal(2, spec.PumpStrength);
        BehaviorValueModifier modifier = Assert.Single(spec.ValueModifiers!);
        Assert.Equal(BehaviorValueSourceKind.PerInstalledCard, modifier.Source);
        Assert.Equal(BehaviorValueOperation.Add, modifier.Operation);
        Assert.Equal(1, modifier.Delta);
        Assert.Equal("echelon-installed-icebreakers", modifier.ReasonKey);
        Assert.Equal(BehaviorCardType.Icebreaker, modifier.InstalledFilter!.CardType);
        Assert.Equal(PlayerSide.Runner, modifier.InstalledFilter.OwnerSide);
    }

    [Fact]
    public void UnitySnapshotsItsTypedDynamicIcebreakerPump()
    {
        IcebreakerBehaviorSpec spec = GatewayBehaviorCatalog.Create().IcebreakerSpecs.Single(
            candidate => candidate.DefinitionId == GatewayBatch3CardCodes.Unity);

        Assert.Equal(1, spec.BaseStrength);
        Assert.Equal(IceType.CodeGate, spec.InterfaceType);
        Assert.Equal(1, spec.BreakCost);
        Assert.Equal(1, spec.BreakLimit);
        Assert.Equal(1, spec.PumpCost);
        Assert.Equal(0, spec.PumpStrength);
        BehaviorDynamicPumpDeclaration pump = Assert.IsType<BehaviorDynamicPumpDeclaration>(spec.DynamicPump);
        Assert.Equal(1, pump.UnitDelta);
        Assert.Equal("unity-installed-icebreakers", pump.VariableKey);
        Assert.Equal("unity-installed-icebreakers", pump.ReasonKey);
        Assert.Equal(BehaviorCardType.Icebreaker, pump.InstalledFilter.CardType);
        Assert.Equal(PlayerSide.Runner, pump.InstalledFilter.OwnerSide);
    }

    [Fact]
    public void LeechUsesCentralSuccessTriggersAndTypedEncounterAbility()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        Assert.True(catalog.TryGet(GatewayBatch3CardCodes.Leech, out CardBehaviorDefinition? leech));

        Assert.Equal(PlayerSide.Runner, leech.Controller);
        Assert.Equal(
            new[] { ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives },
            leech.Triggers.Select(trigger => trigger.ServerKindRequirement!.Value).ToArray());
        Assert.All(leech.Triggers, trigger =>
        {
            Assert.Equal(OccurrenceKind.RunSuccessful, trigger.Occurrence);
            Assert.Equal(TriggerMode.Mandatory, trigger.Mode);
            Assert.Equal(BehaviorTriggerScope.Controller, trigger.Scope);
            BehaviorInstruction counter = Assert.Single(trigger.Instructions);
            Assert.Equal(BehaviorInstructionKind.AddCounter, counter.Kind);
            Assert.Equal(1, counter.Amount);
            Assert.Equal("virus", counter.Argument);
            Assert.Equal(BehaviorTargetKind.Self, counter.Target!.Kind);
        });

        BehaviorPaidAbility ability = Assert.Single(leech.PaidAbilities);
        Assert.Equal("leech-encounter-strength", ability.Key);
        Assert.Equal(BehaviorAbilityTiming.EncounterPaidWindow, ability.Timing);
        Assert.Equal(BehaviorAbilityActivation.PaidAbility, ability.Activation);
        Assert.Equal(BehaviorAbilityUsageLimit.Unlimited, ability.UsageLimit);
        BehaviorCost cost = Assert.Single(ability.Costs);
        Assert.Equal(BehaviorCostKind.Counter, cost.Kind);
        Assert.Equal(1, cost.Amount);
        Assert.Equal("virus", cost.CounterName);
        Assert.Equal(BehaviorConditionKind.Encountering, Assert.Single(ability.Conditions).Kind);
        Assert.Empty(ability.Targets);
        BehaviorInstruction modifier = Assert.Single(ability.Instructions);
        Assert.Equal(BehaviorInstructionKind.ModifyEncounteredIceStrength, modifier.Kind);
        Assert.Equal(-1, modifier.Amount);
        Assert.Equal("leech-encounter-ice-strength", modifier.Argument);
    }

    [Fact]
    public void EncounterStrengthInstructionRejectsZeroAndArbitraryOperands()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.ModifyEncounteredIceStrength,
            amount: 0,
            argument: "reason"));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.ModifyEncounteredIceStrength,
            amount: -1,
            argument: "reason",
            target: new BehaviorTarget(BehaviorTargetKind.Self)));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.ModifyEncounteredIceStrength,
            amount: -1));
    }

    [Fact]
    public void C1DefinitionsRoundTripWithStableDefinitionAndCatalogFingerprints()
    {
        BehaviorCatalog first = GatewayBehaviorCatalog.Create();
        BehaviorCatalog second = GatewayBehaviorCatalog.Create();
        Assert.Equal("gateway-m4-4.6", first.Version);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(
            first.GetDefinitionFingerprint(GatewayBatch3CardCodes.Leech),
            second.GetDefinitionFingerprint(GatewayBatch3CardCodes.Leech));

        CardBehaviorDefinition definition = first.GenericSpecs.Single(
            candidate => candidate.DefinitionId == GatewayBatch3CardCodes.Leech);
        string json = EngineJson.Serialize(definition);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(definition.DefinitionId, restored.DefinitionId);
        Assert.Equal(definition.PaidAbilities, restored.PaidAbilities);
    }
}
