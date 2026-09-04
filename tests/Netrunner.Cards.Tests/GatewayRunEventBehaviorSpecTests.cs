using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayRunEventBehaviorSpecTests
{
    [Fact]
    public void OverclockSpecMatchesTheOfficialCardText()
    {
        RunEventBehaviorSpec spec = GatewayRunEventBehaviorSpecs.RunEventSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Overclock);

        Assert.Equal(1, spec.Cost);
        Assert.Equal(5, spec.HostedCredits);
        Assert.Null(spec.AllowedTargetServers);
        Assert.Null(spec.SuccessDraw);
        Assert.Null(spec.BreachAccessModifier);
    }

    [Fact]
    public void JailbreakSpecDeclaresItsTypedRunVariants()
    {
        RunEventBehaviorSpec spec = GatewayRunEventBehaviorSpecs.RunEventSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayBatch4CardCodes.Jailbreak);

        Assert.Equal(0, spec.Cost);
        Assert.Equal(0, spec.HostedCredits);
        Assert.Equal(
            new[] { ServerKind.Headquarters, ServerKind.RAndD },
            spec.AllowedTargetServers);

        BehaviorRunSuccessDraw draw = Assert.IsType<BehaviorRunSuccessDraw>(spec.SuccessDraw);
        Assert.Equal(1, draw.DrawCount);
        Assert.Equal("jailbreak-success-draw", draw.ReasonKey);

        BehaviorRunBreachAccessModifier access =
            Assert.IsType<BehaviorRunBreachAccessModifier>(spec.BreachAccessModifier);
        Assert.Equal(1, access.Delta);
        Assert.Equal("jailbreak-additional-access", access.ReasonKey);
    }

    [Fact]
    public void RunEventSpecsBindTheCompiledRunEventDefinitions()
    {
        Assert.Equal(
            new[]
            {
                GatewayCardCodes.Overclock,
                GatewayBatch3CardCodes.TreadLightly,
                GatewayBatch4CardCodes.Jailbreak,
            },
            GatewayRunEventBehaviorSpecs.RunEventSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());
    }

    [Fact]
    public void ImmutableGatewayCatalogContainsTheCompiledRunEventSpec()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.Equal(
            GatewayRunEventBehaviorSpecs.RunEventSpecs[0],
            catalog.RunEventSpecs.Single(spec => spec.DefinitionId == GatewayCardCodes.Overclock));
    }

    [Fact]
    public void GetRunEventFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.False(catalog.TryGetRunEvent(unregistered, out _));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        RunEventBehaviorSpec canonical = GatewayRunEventBehaviorSpecs.RunEventSpecs[0];
        Assert.Equal(GatewayCardCodes.Overclock, canonical.DefinitionId);
        BehaviorCatalogBuilder builder = new(GatewayBehaviorCatalog.Version);
        builder.AddRunEvent(canonical);

        RunEventBehaviorSpec conflicting = new(
            canonical.DefinitionId,
            cost: canonical.Cost,
            hostedCredits: canonical.HostedCredits + 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddRunEvent(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, builder.Build().RunEventSpecs.Single());
    }

    [Fact]
    public void CatalogVersionIsPinnedAfterTheRunEventAddition()
    {
        // The run-event registration bumped the catalog version; the proof
        // fixtures in Slice 4 key replay hashes off this exact version.
        Assert.Equal("gateway-m2-3", EngineCompatibility.GatewayBehaviorRegistryVersion);
    }

    [Fact]
    public void RunEventSpecRejectsNegativeCostAndHostedCredits()
    {
        Assert.Throws<ArgumentException>(
            () => new RunEventBehaviorSpec(GatewayCardCodes.Overclock, cost: -1, hostedCredits: 5));
        Assert.Throws<ArgumentException>(
            () => new RunEventBehaviorSpec(GatewayCardCodes.Overclock, cost: 1, hostedCredits: -1));
    }
}
