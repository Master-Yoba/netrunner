using System.Text.Json;

using Xunit;

namespace Netrunner.Core.Tests;

public sealed class BehaviorCatalogTests
{
    private static readonly CardDefinitionId IceId = new("test:catalog-ice");
    private static readonly CardDefinitionId AgendaId = new("test:catalog-agenda");

    [Fact]
    public void BuilderProducesAnImmutableVersionedCatalogWithStableFingerprint()
    {
        IceBehaviorSpec ice = new(
            IceId,
            strength: 2,
            iceType: IceType.Barrier,
            rezCost: 3,
            subroutines: [new EndRunSubroutineSpec()],
            strengthModifiers: []);
        AgendaBehaviorSpec agenda = new(AgendaId, agendaPoints: 2);

        BehaviorCatalog first = new BehaviorCatalogBuilder("test:catalog")
            .AddAgenda(agenda)
            .AddIce(ice)
            .Build();
        BehaviorCatalog second = new BehaviorCatalogBuilder("test:catalog")
            .AddIce(ice)
            .AddAgenda(agenda)
            .Build();

        Assert.Equal("test:catalog", first.Version);
        Assert.Equal(first.Version, first.BehaviorRegistryVersion);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.True(first.TryGetIce(IceId, out _));
        Assert.True(first.TryGetAgenda(AgendaId, out _));
        Assert.Contains(IceId, first.AllDefinitionIds);
        Assert.Contains(AgendaId, first.AllDefinitionIds);
        Assert.IsAssignableFrom<IList<IceBehaviorSpec>>(first.IceSpecs);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<IceBehaviorSpec>)first.IceSpecs).Clear());
    }

    [Fact]
    public void BuilderRejectsConflictingDuplicatesButAcceptsStructuralDuplicates()
    {
        IceBehaviorSpec canonical = new(
            IceId,
            strength: 2,
            iceType: IceType.Barrier,
            rezCost: 3,
            subroutines: [new EndRunSubroutineSpec()],
            strengthModifiers: []);
        var builder = new BehaviorCatalogBuilder("test:duplicates");

        builder.AddIce(canonical).AddIce(canonical);
        Assert.Throws<InvalidOperationException>(() => builder.AddIce(new IceBehaviorSpec(
            IceId,
            strength: 3,
            iceType: canonical.IceType,
            rezCost: canonical.RezCost,
            subroutines: canonical.Subroutines,
            strengthModifiers: canonical.StrengthModifiers)));
    }

    [Fact]
    public void BuilderRejectsReuseAcrossTypedBehaviorFamilies()
    {
        var id = new CardDefinitionId("test:cross-family");
        IceBehaviorSpec ice = new(
            id,
            strength: 2,
            iceType: IceType.Barrier,
            rezCost: 3,
            subroutines: [new EndRunSubroutineSpec()],
            strengthModifiers: []);
        AgendaBehaviorSpec agenda = new(id, agendaPoints: 2);

        BehaviorCatalogBuilder builder = new("test:cross-family-catalog");
        builder.AddIce(ice);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => builder.AddAgenda(agenda));
        Assert.Contains("both ICE and agenda", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ICE", "icebreaker")]
    [InlineData("ICE", "asset")]
    [InlineData("ICE", "run event")]
    [InlineData("icebreaker", "asset")]
    [InlineData("icebreaker", "agenda")]
    [InlineData("icebreaker", "run event")]
    [InlineData("asset", "agenda")]
    [InlineData("asset", "run event")]
    [InlineData("agenda", "run event")]
    public void BuilderRejectsReuseAcrossEveryTypedBehaviorFamilyPair(
        string firstFamily,
        string secondFamily)
    {
        string idSuffix = $"{firstFamily}-{secondFamily}".Replace(' ', '-');
        var id = new CardDefinitionId($"test:cross-family-{idSuffix}");
        BehaviorCatalogBuilder builder = new("test:all-cross-family-pairs");
        AddTyped(builder, firstFamily, id);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => AddTyped(builder, secondFamily, id));

        Assert.Contains("cannot be registered as both", exception.Message, StringComparison.Ordinal);
        Assert.Contains(firstFamily, exception.Message, StringComparison.Ordinal);
        Assert.Contains(secondFamily, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCompatibilityRegistrationRejectsCrossFamilyConflictsBeforeSnapshot()
    {
        var id = new CardDefinitionId("test:legacy-cross-family-conflict");
        CardBehaviorSpecCatalog.RegisterIce(new IceBehaviorSpec(
            id,
            strength: 1,
            iceType: IceType.Barrier,
            rezCost: 1,
            subroutines: [new EndRunSubroutineSpec()],
            strengthModifiers: []));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            CardBehaviorSpecCatalog.RegisterAgenda(new AgendaBehaviorSpec(id, agendaPoints: 1)));

        Assert.Contains("both ICE and agenda", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericBehaviorMayIntentionallyOverlayOneLegacyTypedFamily()
    {
        var id = new CardDefinitionId("test:generic-overlay");
        IceBehaviorSpec ice = new(
            id,
            strength: 2,
            iceType: IceType.Barrier,
            rezCost: 3,
            subroutines: [new EndRunSubroutineSpec()],
            strengthModifiers: []);
        CardBehaviorDefinition generic = new(id, PlayerSide.Corp, instructions: [
            new BehaviorInstruction(BehaviorInstructionKind.NoOp),
        ]);

        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:generic-overlay-catalog")
            .AddIce(ice)
            .Add(generic)
            .Build();

        Assert.True(catalog.TryGetIce(id, out _));
        Assert.True(catalog.TryGet(id, out _));
    }

    [Fact]
    public void ClassificationCannotCreateAnExecutableDefinitionByItself()
    {
        var classificationOnlyId = new CardDefinitionId("test:classification-only");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new BehaviorCatalogBuilder("test:classification-only-catalog")
                .AddCardType(classificationOnlyId, BehaviorCardType.Hardware)
                .Build());

        Assert.Contains("requires a corresponding typed or generic behavior", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassificationIsAnAdjunctToExecutableIdsAndSlicesDeterministically()
    {
        var firstId = new CardDefinitionId("test:classification-first");
        var secondId = new CardDefinitionId("test:classification-second");
        CardBehaviorDefinition firstDefinition = new(firstId, PlayerSide.Runner);
        CardBehaviorDefinition secondDefinition = new(secondId, PlayerSide.Runner);

        BehaviorCatalog first = new BehaviorCatalogBuilder("test:classification-order")
            .Add(firstDefinition)
            .Add(secondDefinition)
            .AddCardType(firstId, BehaviorCardType.Hardware)
            .AddCardType(secondId, BehaviorCardType.Resource)
            .Build();
        BehaviorCatalog second = new BehaviorCatalogBuilder("test:classification-order")
            .Add(secondDefinition)
            .Add(firstDefinition)
            .AddCardType(secondId, BehaviorCardType.Resource)
            .AddCardType(firstId, BehaviorCardType.Hardware)
            .Build();

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.AllDefinitionIds, second.AllDefinitionIds);
        Assert.Equal(2, first.AllDefinitionIds.Count);
        Assert.True(first.TryGetCardType(firstId, out BehaviorCardType firstType));
        Assert.Equal(BehaviorCardType.Hardware, firstType);

        BehaviorCatalog slice = first.CreateSlice([firstId]);
        Assert.Equal([firstId], slice.AllDefinitionIds);
        Assert.True(slice.TryGetCardType(firstId, out BehaviorCardType slicedType));
        Assert.Equal(BehaviorCardType.Hardware, slicedType);
        Assert.DoesNotContain(secondId, slice.AllDefinitionIds);
    }

    [Fact]
    public void ImmutableCatalogDefinitionIdsIncludeGenericOnlyContracts()
    {
        var id = new CardDefinitionId("test:generic-only");
        CardBehaviorDefinition generic = new(
            id,
            PlayerSide.Runner,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);

        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:generic-only-catalog")
            .Add(generic)
            .Build();

        Assert.Contains(id, catalog.AllDefinitionIds);
        Assert.DoesNotContain(id, catalog.DefinitionIds);
        Assert.True(catalog.TryGet(id, out CardBehaviorDefinition? resolved));
        Assert.Same(generic, resolved);
    }

    [Fact]
    public void DefinitionSliceRetainsTypedAndGenericContractsAndDropsUnrelatedDefinitions()
    {
        var requested = new CardDefinitionId("test:slice-requested");
        var unrelated = new CardDefinitionId("test:slice-unrelated");
        IceBehaviorSpec ice = new(
            requested,
            strength: 2,
            iceType: IceType.Barrier,
            rezCost: 3,
            subroutines: [new EndRunSubroutineSpec()],
            strengthModifiers: []);
        CardBehaviorDefinition generic = new(
            requested,
            PlayerSide.Corp,
            instructions: [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]);
        AssetBehaviorSpec extra = new(unrelated, trashCost: 1);
        BehaviorCatalog source = new BehaviorCatalogBuilder("test:slice")
            .AddIce(ice)
            .Add(generic)
            .AddAsset(extra)
            .Build();

        BehaviorCatalog slice = BehaviorCatalog.CreateSlice(source, [requested, requested]);

        Assert.Equal(source.Version, slice.Version);
        Assert.Same(ice, Assert.IsType<IceBehaviorSpec>(slice.IceSpecs.Single()));
        Assert.Same(generic, Assert.IsType<CardBehaviorDefinition>(slice.GenericSpecs.Single()));
        Assert.False(slice.TryGetAsset(unrelated, out _));
        Assert.Contains(requested, slice.AllDefinitionIds);
        Assert.DoesNotContain(unrelated, slice.AllDefinitionIds);
    }

    [Fact]
    public void CompatibilityCatalogSnapshotDoesNotChangeWhenLegacyRegistrationIsAdded()
    {
        BehaviorCatalog before = CardBehaviorSpecCatalog.Default;
        CardDefinitionId extra = new("test:catalog-snapshot");

        CardBehaviorSpecCatalog.RegisterAsset(new AssetBehaviorSpec(extra, trashCost: 1));

        Assert.False(before.TryGetAsset(extra, out _));
        Assert.True(CardBehaviorSpecCatalog.Default.TryGetAsset(extra, out _));
    }

    [Fact]
    public void ExplicitSessionUsesOnlyItsInjectedCatalogAndVersionPin()
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:session").Build();
        GameState state = GameState.CreateNew(
            seed: 7,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        GameEngineSession session = GameEngine.CreateSession(catalog);
        PlayerId corpId = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState prompt = session.BeginResolution(
            state,
            new ChooseNumberFrame(corpId, 1, 2));

        Assert.NotNull(prompt.PendingDecision);
        Assert.Equal(catalog.Version, prompt.BehaviorRegistryVersion);
        Assert.Equal(catalog.Fingerprint, prompt.BehaviorCatalogFingerprint);
    }

    [Fact]
    public void MetadataSnapshotAndCompiledBehaviorPinsRemainIndependent()
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:gateway-behavior").Build();
        GameState fixture = GameState.CreateNew(seed: 5);
        GameState contentState = GameState.CreateNew(
            seed: 5,
            cardDataVersion: EngineCompatibility.GatewayCardDataVersion,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);

        Assert.Equal(EngineCompatibility.CardDataVersion, fixture.CardDataVersion);
        Assert.Equal(EngineCompatibility.BehaviorRegistryVersion, fixture.BehaviorRegistryVersion);
        Assert.Null(fixture.BehaviorCatalogFingerprint);
        Assert.NotEqual(contentState.CardDataVersion, contentState.BehaviorRegistryVersion);
        StateInvariantValidator.Validate(contentState, catalog);
    }

    [Fact]
    public void AdditiveCompatibilityRegistrationDoesNotChurnAnInjectedSemanticPin()
    {
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:semantic-pin").Build();
        GameState state = GameState.CreateNew(
            seed: 19,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        PlayerId corpId = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = GameEngine.BeginResolution(
            state,
            new ChooseNumberFrame(corpId, 1, 1),
            catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        var submission = new ActionSubmission(
            decision.Owner,
            decision.Id,
            decision.Revision,
            Assert.Single(decision.LegalActions).Id);

        ApplyResult before = GameEngine.Apply(prompt, submission, catalog);
        CardBehaviorSpecCatalog.RegisterAsset(new AssetBehaviorSpec(
            new CardDefinitionId("test:semantic-pin-additive"),
            trashCost: 2));
        ApplyResult after = GameEngine.Apply(prompt, submission, catalog);

        Assert.True(before.Accepted, before.Rejection?.Message);
        Assert.True(after.Accepted, after.Rejection?.Message);
        Assert.Equal(DeterministicHash.ForState(before.State), DeterministicHash.ForState(after.State));
        Assert.Equal(DeterministicHash.ForEvents(before.Events), DeterministicHash.ForEvents(after.Events));
        Assert.Equal(before.AcceptedAction!.ResultingStateHash, after.AcceptedAction!.ResultingStateHash);
        Assert.Equal(before.AcceptedAction.ResultingEventBatchHash, after.AcceptedAction.ResultingEventBatchHash);
    }

    [Fact]
    public void StateSchemaMigrationRequiresBehaviorRegistryFieldAndRejectsSchemaOne()
    {
        GameState state = GameState.CreateNew(seed: 11);
        string json = EngineJson.Serialize(state);
        Assert.Contains("\"behaviorRegistryVersion\"", json, StringComparison.Ordinal);

        string oldSchema = json.Replace(
            $"\"schemaVersion\":{GameState.CurrentSchemaVersion}",
            "\"schemaVersion\":1",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(oldSchema));

        string missingBehavior = RemoveTopLevelProperty(json, "behaviorRegistryVersion");
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(missingBehavior));
    }

    [Fact]
    public void StateMigrationDoesNotBumpTheReplayActionArtifactContract()
    {
        // Slice 4.3 A2 expands persisted turn facts for the once-per-turn
        // ability ledger; replay/action artifacts remain schema 1.
        Assert.Equal(3, BuildMetadata.SaveSchemaVersion);
        Assert.Equal(EngineCompatibility.ArtifactSchemaVersion, BuildMetadata.ActionLogSchemaVersion);
        Assert.Equal(1, EngineCompatibility.ArtifactSchemaVersion);
    }

    private static string RemoveTopLevelProperty(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var output = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                output[property.Name] = property.Value.Clone();
            }
        }

        return JsonSerializer.Serialize(output);
    }

    private static void AddTyped(BehaviorCatalogBuilder builder, string family, CardDefinitionId id)
    {
        switch (family)
        {
            case "ICE":
                builder.AddIce(new IceBehaviorSpec(
                    id,
                    strength: 1,
                    iceType: IceType.Barrier,
                    rezCost: 1,
                    subroutines: [new EndRunSubroutineSpec()],
                    strengthModifiers: []));
                return;
            case "icebreaker":
                builder.AddIcebreaker(new IcebreakerBehaviorSpec(
                    id,
                    baseStrength: 1,
                    interfaceType: IceType.Barrier,
                    breakCost: 1,
                    breakLimit: 1,
                    pumpCost: 1,
                    pumpStrength: 1));
                return;
            case "asset":
                builder.AddAsset(new AssetBehaviorSpec(id, trashCost: 1));
                return;
            case "agenda":
                builder.AddAgenda(new AgendaBehaviorSpec(id, agendaPoints: 1));
                return;
            case "run event":
                builder.AddRunEvent(new RunEventBehaviorSpec(id, cost: 1, hostedCredits: 0));
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown behavior family.");
        }
    }
}
