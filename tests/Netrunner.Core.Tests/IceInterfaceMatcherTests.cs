using Xunit;

namespace Netrunner.Core.Tests;

public sealed class IceInterfaceMatcherTests
{
    [Theory]
    [InlineData(IceType.Barrier)]
    [InlineData(IceType.CodeGate)]
    [InlineData(IceType.Sentry)]
    public void AConcreteInterfaceMatchesOnlyTheSameConcreteIceType(IceType iceType)
    {
        Assert.True(IceInterfaceMatcher.Matches(iceType, iceType));

        foreach (IceType other in new[] { IceType.Barrier, IceType.CodeGate, IceType.Sentry })
        {
            if (other != iceType)
            {
                Assert.False(IceInterfaceMatcher.Matches(other, iceType));
            }
        }
    }

    [Theory]
    [InlineData(IceType.Barrier)]
    [InlineData(IceType.CodeGate)]
    [InlineData(IceType.Sentry)]
    public void WildcardInterfaceMatchesEveryConcreteIceType(IceType iceType)
    {
        Assert.True(IceInterfaceMatcher.Matches(iceType, IceType.Any));
    }

    [Fact]
    public void MatcherRejectsAnActualWildcardEncounteredIce()
    {
        Assert.False(IceInterfaceMatcher.Matches(IceType.Any, IceType.Any));
    }

    [Fact]
    public void IceSpecRejectsWildcardSubtypeButBreakerSpecAcceptsIt()
    {
        Assert.Throws<ArgumentException>(() => new IceBehaviorSpec(
            new CardDefinitionId("test:ice-interface-wildcard"),
            strength: 0,
            iceType: IceType.Any,
            rezCost: 0,
            subroutines: [],
            strengthModifiers: []));

        IcebreakerBehaviorSpec wildcard = new(
            new CardDefinitionId("test:breaker-interface-wildcard"),
            baseStrength: 1,
            interfaceType: IceType.Any,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 1);

        Assert.Equal(IceType.Any, wildcard.InterfaceType);
    }

    [Fact]
    public void ExactSentryBreakerIsMaterializedConsumedAndInvariantValidated()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerSentry,
            iceDefinition: FixtureIceBehaviorSpecs.SentryLoseCredits);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        LegalAction breakAction = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && ((RunBreakParameters)action.Parameters).BreakerId == breakerId);

        ApplyResult result = RunTimingTests.ApplyAction(encounter, breakAction);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Contains(result.State.RunState!.Encounter!.Subroutines, subroutine => subroutine.Broken);
        StateInvariantValidator.Validate(result.State, FixtureBehaviorCatalog.Catalog);
    }

    [Theory]
    [InlineData(IceType.Barrier)]
    [InlineData(IceType.CodeGate)]
    [InlineData(IceType.Sentry)]
    public void WildcardBreakerIsMaterializedAndConsumedAgainstEveryConcreteIce(IceType iceType)
    {
        CardDefinitionId iceDefinition = iceType switch
        {
            IceType.Barrier => FixtureIceBehaviorSpecs.TwoSubroutines,
            IceType.CodeGate => FixtureIceBehaviorSpecs.CodeGateLoseCredits,
            IceType.Sentry => FixtureIceBehaviorSpecs.SentryLoseCredits,
            _ => throw new ArgumentOutOfRangeException(nameof(iceType), iceType, null),
        };
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerAny,
            iceDefinition: iceDefinition);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        LegalAction breakAction = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
                && ((RunBreakParameters)action.Parameters).Quantity == 1
                && ((RunBreakParameters)action.Parameters).SubroutineOrdinals!.SequenceEqual([0]));
        ApplyResult result = RunTimingTests.ApplyAction(encounter, breakAction);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Contains(result.State.RunState!.Encounter!.Subroutines, subroutine => subroutine.Broken);
        StateInvariantValidator.Validate(result.State, FixtureBehaviorCatalog.Catalog);
    }

    [Fact]
    public void ConcreteMismatchIsNotOfferedAndAForgeIsRejectedBeforeConsumption()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerCodeGate,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        Assert.DoesNotContain(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines);

        CardInstanceId iceId = encounter.RunState!.Encounter!.IceId;
        PendingDecision pending = encounter.PendingDecision;
        LegalAction forged = new(
            new StableIdAllocator(encounter.Allocator).NextActionId(),
            ActionKind.BreakSubroutines,
            $"Break 1 subroutine on {iceId.Value} with {breakerId.Value}",
            new RunBreakParameters(breakerId, quantity: 1));
        GameState tampered = EncounterBreakerTests.WithPendingDecision(
            encounter,
            [.. pending.LegalActions, forged]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered, FixtureBehaviorCatalog.Catalog);
        Assert.Contains(violations, violation => violation.Code == "run.extra-action");

        ApplyResult rejected = GameEngine.Apply(
            tampered,
            new ActionSubmission(
                tampered.PendingDecision!.Owner,
                tampered.PendingDecision.Id,
                tampered.PendingDecision.Revision,
                forged.Id),
            FixtureBehaviorCatalog.Catalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(tampered, rejected.State);
    }

    [Fact]
    public void SentryAndWildcardMetadataChangeCatalogEqualityAndFingerprint()
    {
        CardDefinitionId iceId = new("test:catalog-sentry-ice");
        IceBehaviorSpec barrier = new(
            iceId,
            strength: 0,
            iceType: IceType.Barrier,
            rezCost: 0,
            subroutines: [],
            strengthModifiers: []);
        IceBehaviorSpec sentry = new(
            iceId,
            strength: 0,
            iceType: IceType.Sentry,
            rezCost: 0,
            subroutines: [],
            strengthModifiers: []);

        BehaviorCatalog barrierCatalog = new BehaviorCatalogBuilder("test:ice-interface-fingerprint")
            .AddIce(barrier)
            .Build();
        BehaviorCatalog sentryCatalog = new BehaviorCatalogBuilder("test:ice-interface-fingerprint")
            .AddIce(sentry)
            .Build();

        Assert.NotEqual(barrierCatalog.Fingerprint, sentryCatalog.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => new BehaviorCatalogBuilder("test:ice-interface-equality")
            .AddIce(barrier)
            .AddIce(sentry));

        CardDefinitionId breakerId = new("test:catalog-wildcard-breaker");
        IcebreakerBehaviorSpec concrete = new(
            breakerId,
            baseStrength: 1,
            interfaceType: IceType.Sentry,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 1);
        IcebreakerBehaviorSpec wildcard = new(
            breakerId,
            baseStrength: 1,
            interfaceType: IceType.Any,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 1);
        BehaviorCatalog concreteCatalog = new BehaviorCatalogBuilder("test:breaker-interface-fingerprint")
            .AddIcebreaker(concrete)
            .Build();
        BehaviorCatalog wildcardCatalog = new BehaviorCatalogBuilder("test:breaker-interface-fingerprint")
            .AddIcebreaker(wildcard)
            .Build();

        Assert.NotEqual(concreteCatalog.Fingerprint, wildcardCatalog.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => new BehaviorCatalogBuilder("test:breaker-interface-equality")
            .AddIcebreaker(concrete)
            .AddIcebreaker(wildcard));
    }

    [Fact]
    public void WildcardInterfaceUsesNamedJsonEnumAndRoundTrips()
    {
        IcebreakerBehaviorSpec wildcard = new(
            new CardDefinitionId("test:json-wildcard-breaker"),
            baseStrength: 1,
            interfaceType: IceType.Any,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 1);

        string json = EngineJson.Serialize(wildcard);

        Assert.Contains("\"interfaceType\":\"any\"", json, StringComparison.Ordinal);
        IcebreakerBehaviorSpec restored = EngineJson.Deserialize<IcebreakerBehaviorSpec>(json);
        Assert.Equal(wildcard, restored);

        IceBehaviorSpec sentry = new(
            new CardDefinitionId("test:json-sentry-ice"),
            strength: 0,
            iceType: IceType.Sentry,
            rezCost: 0,
            subroutines: [],
            strengthModifiers: []);
        Assert.Contains(
            "\"iceType\":\"sentry\"",
            EngineJson.Serialize(sentry),
            StringComparison.Ordinal);
    }
}
