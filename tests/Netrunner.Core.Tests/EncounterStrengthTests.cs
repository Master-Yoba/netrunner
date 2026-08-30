using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Pure CR 6.5/9.8 encounter effective-strength tests.  Spec instances are
/// constructed directly, so no Cards-assembly registration is involved.
/// </summary>
public sealed class EncounterStrengthTests
{
    private static readonly CardDefinitionId Definition = new("fixture:strength-definition");
    private static readonly CardInstanceId Breaker = new("card:strength-breaker");
    private static readonly CardInstanceId OtherBreaker = new("card:strength-other-breaker");

    [Fact]
    public void IceUsesBaseStrengthWhenNoModifiersApply()
    {
        IceBehaviorSpec spec = new(
            Definition,
            strength: 4,
            iceType: IceType.Barrier,
            rezCost: 1,
            subroutines: Array.Empty<IceSubroutineSpec>(),
            strengthModifiers: Array.Empty<IceStrengthModifierSpec>());

        Assert.Equal(4, EncounterStrength.Ice(spec, ServerKind.Headquarters));
        Assert.Equal(4, EncounterStrength.Ice(spec, ServerKind.RAndD));
        Assert.Equal(4, EncounterStrength.Ice(spec, ServerKind.Archives));
    }

    [Fact]
    public void IceRemoteProtectingModifierAppliesOnlyOnRemotes()
    {
        IceBehaviorSpec spec = new(
            Definition,
            strength: 4,
            iceType: IceType.Barrier,
            rezCost: 1,
            subroutines: Array.Empty<IceSubroutineSpec>(),
            strengthModifiers: new[]
            {
                new IceStrengthModifierSpec(2, IceStrengthModifierCondition.ProtectingRemoteServer),
            });

        Assert.Equal(6, EncounterStrength.Ice(spec, ServerKind.Remote));
        Assert.Equal(4, EncounterStrength.Ice(spec, ServerKind.Headquarters));
        Assert.Equal(4, EncounterStrength.Ice(spec, ServerKind.RAndD));
        Assert.Equal(4, EncounterStrength.Ice(spec, ServerKind.Archives));
    }

    [Fact]
    public void BreakerUsesBaseStrengthWithoutActivePumps()
    {
        IcebreakerBehaviorSpec spec = BreakerSpec();

        Assert.Equal(3, EncounterStrength.Breaker(spec, Array.Empty<RunLingeringEffect>(), Breaker));
    }

    [Fact]
    public void BreakerAddsPumpStrengthPerEncounterExpiringPumpItSources()
    {
        IcebreakerBehaviorSpec spec = BreakerSpec();

        Assert.Equal(4, EncounterStrength.Breaker(
            spec,
            new[] { Pump(Breaker, RunLingeringEffectExpiry.Encounter) },
            Breaker));
        Assert.Equal(5, EncounterStrength.Breaker(
            spec,
            new[]
            {
                Pump(Breaker, RunLingeringEffectExpiry.Encounter),
                Pump(Breaker, RunLingeringEffectExpiry.Encounter),
            },
            Breaker));
    }

    [Fact]
    public void BreakerIgnoresForeignSourcesRunExpiryAndOtherBehaviors()
    {
        IcebreakerBehaviorSpec spec = BreakerSpec();

        Assert.Equal(3, EncounterStrength.Breaker(
            spec,
            new[] { Pump(OtherBreaker, RunLingeringEffectExpiry.Encounter) },
            Breaker));
        Assert.Equal(3, EncounterStrength.Breaker(
            spec,
            new[] { Pump(Breaker, RunLingeringEffectExpiry.Run) },
            Breaker));
        Assert.Equal(3, EncounterStrength.Breaker(
            spec,
            new[] { OtherBehavior(Breaker) },
            Breaker));
    }

    private static IcebreakerBehaviorSpec BreakerSpec() => new(
        Definition,
        baseStrength: 3,
        interfaceType: IceType.Barrier,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 2,
        pumpStrength: 1);

    private static RunLingeringEffect Pump(CardInstanceId source, RunLingeringEffectExpiry expiry) =>
        new(new RunEffectId($"effect:pump-{source.Value}"), EncounterStrength.IcebreakerPumpBehavior, source, expiry);

    private static RunLingeringEffect OtherBehavior(CardInstanceId source) =>
        new(new RunEffectId($"effect:other-{source.Value}"), "fixture:other-behavior", source, RunLingeringEffectExpiry.Encounter);
}
