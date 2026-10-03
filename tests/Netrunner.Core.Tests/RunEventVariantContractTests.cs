using Xunit;

namespace Netrunner.Core.Tests;

public sealed class RunEventVariantContractTests
{
    private static readonly CardDefinitionId DefinitionId = new("test:run-event-variants");

    [Fact]
    public void VariantDeclarationsFreezeAndRoundTrip()
    {
        List<ServerKind> servers = [ServerKind.Headquarters, ServerKind.RAndD];
        BehaviorRunSuccessDraw draw = new(1, "success-draw");
        BehaviorRunBreachAccessModifier access = new(1, "breach-access");
        RunEventBehaviorSpec spec = new(
            DefinitionId,
            cost: 0,
            hostedCredits: 0,
            allowedTargetServers: servers,
            successDraw: draw,
            breachAccessModifier: access);

        servers[0] = ServerKind.Archives;

        Assert.Equal(
            new[] { ServerKind.Headquarters, ServerKind.RAndD },
            spec.AllowedTargetServers);
        Assert.Same(draw, spec.SuccessDraw);
        Assert.Same(access, spec.BreachAccessModifier);

        string json = EngineJson.Serialize(spec);
        RunEventBehaviorSpec restored = EngineJson.Deserialize<RunEventBehaviorSpec>(json);

        Assert.Contains("allowedTargetServers", json, StringComparison.Ordinal);
        Assert.Contains("successDraw", json, StringComparison.Ordinal);
        Assert.Contains("breachAccessModifier", json, StringComparison.Ordinal);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(spec.AllowedTargetServers, restored.AllowedTargetServers);
        Assert.Equal(spec.SuccessDraw, restored.SuccessDraw);
        Assert.Equal(spec.BreachAccessModifier, restored.BreachAccessModifier);
    }

    [Fact]
    public void AllowedTargetServersRejectInvalidKindsAndDuplicates()
    {
        Assert.Throws<ArgumentException>(() => new RunEventBehaviorSpec(
            DefinitionId,
            cost: 0,
            hostedCredits: 0,
            allowedTargetServers: [ServerKind.Remote]));
        Assert.Throws<ArgumentException>(() => new RunEventBehaviorSpec(
            DefinitionId,
            cost: 0,
            hostedCredits: 0,
            allowedTargetServers: [ServerKind.Headquarters, ServerKind.Headquarters]));

        RunEventBehaviorSpec empty = new(
            DefinitionId,
            cost: 0,
            hostedCredits: 0,
            allowedTargetServers: []);
        Assert.Null(empty.AllowedTargetServers);
    }

    [Fact]
    public void VariantDeclarationsRejectInvalidAmountsAndReasonKeys()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorRunSuccessDraw(0, "success-draw"));
        Assert.Throws<ArgumentException>(() => new BehaviorRunSuccessDraw(1, "not stable"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorRunBreachAccessModifier(0, "breach-access"));
        Assert.Throws<ArgumentException>(() => new BehaviorRunBreachAccessModifier(1, "not stable"));
    }

    [Fact]
    public void AbsentAndEmptyVariantFieldsPreserveLegacyJsonAndFingerprint()
    {
        RunEventBehaviorSpec absent = new(DefinitionId, cost: 1, hostedCredits: 5);
        RunEventBehaviorSpec empty = new(
            DefinitionId,
            cost: 1,
            hostedCredits: 5,
            allowedTargetServers: [],
            successDraw: null,
            breachAccessModifier: null);

        Assert.Equal(EngineJson.Serialize(absent), EngineJson.Serialize(empty));

        BehaviorCatalog first = new BehaviorCatalogBuilder(
                "test:run-event-variant-catalog",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(absent)
            .Build();
        BehaviorCatalog second = new BehaviorCatalogBuilder(
                "test:run-event-variant-catalog",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(empty)
            .Build();
        Assert.Equal(first.Fingerprint, second.Fingerprint);

        RunEventBehaviorSpec modified = new(
            DefinitionId,
            cost: 1,
            hostedCredits: 5,
            allowedTargetServers: [ServerKind.Headquarters],
            successDraw: new BehaviorRunSuccessDraw(1, "success-draw"));
        BehaviorCatalog modifiedCatalog = new BehaviorCatalogBuilder(
                "test:run-event-variant-catalog",
                FixtureBehaviorCatalog.Catalog)
            .AddRunEvent(modified)
            .Build();
        Assert.NotEqual(first.Fingerprint, modifiedCatalog.Fingerprint);
    }
}
