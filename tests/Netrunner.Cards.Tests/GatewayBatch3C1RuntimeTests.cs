using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Runtime evidence for the C1 strength cards.  These tests build a small
/// state with the Gateway catalog and drive the public run reducer, so the
/// declarations are checked at the same materialization, payment, and
/// encounter boundaries used by a real game.
/// </summary>
public sealed class GatewayBatch3C1RuntimeTests
{
    private static readonly CardDefinitionId BarrierIce = new("fixture:batch3-c1-barrier");
    private static readonly CardDefinitionId SentryIce = new("fixture:batch3-c1-sentry");
    private static readonly CardDefinitionId LeechSentryIce = new("fixture:batch3-c1-leech-sentry");
    private static readonly CardDefinitionId LeechBarrierIce = new("fixture:batch3-c1-leech-barrier");
    private static readonly CardDefinitionId EndRunBarrierIce = new("fixture:batch3-c1-end-run-barrier");
    private static readonly CardDefinitionId CodeGateIce = new("fixture:batch3-c1-code-gate");
    private static readonly CardDefinitionId RAndDCard = new("fixture:batch3-c1-r-and-d-card");

    private static readonly IceBehaviorSpec BarrierIceSpec = new(
        BarrierIce,
        strength: 1,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec SentryIceSpec = new(
        SentryIce,
        strength: 1,
        iceType: IceType.Sentry,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec LeechSentryIceSpec = new(
        LeechSentryIce,
        strength: 2,
        iceType: IceType.Sentry,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec CodeGateIceSpec = new(
        CodeGateIce,
        strength: 3,
        iceType: IceType.CodeGate,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec LeechBarrierIceSpec = new(
        LeechBarrierIce,
        strength: 2,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines: [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)],
        strengthModifiers: []);

    private static readonly IceBehaviorSpec EndRunBarrierIceSpec = new(
        EndRunBarrierIce,
        strength: 0,
        iceType: IceType.Barrier,
        rezCost: 0,
        subroutines: [new EndRunSubroutineSpec()],
        strengthModifiers: []);

    [Fact]
    [Trait("GatewayCard", "30025")]
    public void EchelonCountsItselfAndItsBreakOfferUsesThePassiveStrength()
    {
        BehaviorCatalog catalog = Catalog();
        GameState preRun = BuildState(catalog, SentryIce, [GatewayBatch3CardCodes.Echelon]);
        CardInstanceId echelonId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Echelon).Id;

        GameState encounter = ReachEncounter(preRun, catalog);
        ValueQuote quote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectBreakerQuote(encounter, catalog, echelonId));
        Assert.Equal(0, quote.BaseValue);
        Assert.Equal(1, quote.EffectiveValue);
        ValueCalculationTerm passive = Assert.Single(quote.Terms);
        Assert.Equal(BehaviorValueSourceKind.PerInstalledCard, passive.Source);
        Assert.Equal(1, passive.Quantity);
        Assert.Equal(1, passive.Delta);
        Assert.Equal("echelon-installed-icebreakers", passive.ReasonKey);

        LegalAction breakAction = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines);
        RunBreakParameters parameters = Assert.IsType<RunBreakParameters>(breakAction.Parameters);
        Assert.Equal(echelonId, parameters.BreakerId);
        Assert.Equal(1, parameters.Quantity);

        ApplyResult broken = Apply(encounter, breakAction, catalog);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        Assert.True(broken.State.RunState!.Encounter!.Subroutines.Single().Broken);
        Assert.Empty(StateInvariantValidator.Collect(broken.State, catalog));
    }

    [Fact]
    [Trait("GatewayCard", "30026")]
    public void UnitySnapshotsAllInstalledIcebreakersAndTheSnapshotEnablesBreaking()
    {
        BehaviorCatalog catalog = Catalog();
        GameState preRun = BuildState(
            catalog,
            CodeGateIce,
            [GatewayBatch3CardCodes.Unity, GatewayBatch3CardCodes.Echelon]);
        CardInstanceId unityId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Unity).Id;
        CardInstanceId echelonId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Echelon).Id;

        GameState encounter = ReachEncounter(preRun, catalog);
        Assert.DoesNotContain(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && Assert.IsType<RunBreakParameters>(action.Parameters).BreakerId == unityId);
        LegalAction pump = Assert.Single(
            encounter.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.PumpBreaker
                && Assert.IsType<RunBreakerParameters>(action.Parameters).BreakerId == unityId);

        ApplyResult pumped = Apply(encounter, pump, catalog);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);
        RunLingeringEffect effect = Assert.Single(pumped.State.RunState!.LingeringEffects);
        RunValueAdjustment adjustment = Assert.IsType<RunValueAdjustment>(effect.ValueAdjustment);
        Assert.Equal(BehaviorValueSourceKind.Variable, adjustment.Source);
        Assert.Equal(unityId, adjustment.SourceCardId);
        Assert.Equal(unityId, adjustment.TargetCardId);
        Assert.Equal(2, adjustment.Quantity);
        Assert.Equal(1, adjustment.UnitDelta);
        Assert.Equal(2, adjustment.Delta);
        Assert.Equal("unity-installed-icebreakers", adjustment.ReasonKey);

        ValueQuote quote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectBreakerQuote(pumped.State, catalog, unityId));
        Assert.Equal(3, quote.EffectiveValue);
        ValueCalculationTerm dynamic = Assert.Single(quote.Terms);
        Assert.Equal(BehaviorValueSourceKind.Variable, dynamic.Source);
        Assert.Equal(2, dynamic.Quantity);
        Assert.Equal(2, dynamic.Delta);
        Assert.Equal("unity-installed-icebreakers", dynamic.ReasonKey);

        // The quantity is frozen at activation. Removing the other breaker
        // from the rig must not change the already-realized pump.
        GameState moved = MoveRunnerRigCardToHeap(pumped.State, echelonId);
        ValueQuote movedQuote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectBreakerQuote(moved, catalog, unityId));
        Assert.Equal(3, movedQuote.EffectiveValue);

        LegalAction breakAction = Assert.Single(
            pumped.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && Assert.IsType<RunBreakParameters>(action.Parameters).BreakerId == unityId);
        ApplyResult broken = Apply(pumped.State, breakAction, catalog);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        Assert.True(broken.State.RunState!.Encounter!.Subroutines.Single().Broken);
        Assert.Empty(StateInvariantValidator.Collect(broken.State, catalog));
    }

    [Fact]
    [Trait("GatewayCard", "30025")]
    public void EchelonCountsMultipleInstalledIcebreakersInItsPassiveQuote()
    {
        BehaviorCatalog catalog = Catalog();
        GameState preRun = BuildState(
            catalog,
            SentryIce,
            [GatewayBatch3CardCodes.Echelon, GatewayBatch3CardCodes.Echelon]);
        CardInstanceId first = preRun.Cards.First(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Echelon).Id;

        GameState encounter = ReachEncounter(preRun, catalog);
        ValueQuote quote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectBreakerQuote(encounter, catalog, first));
        Assert.Equal(2, quote.EffectiveValue);
        ValueCalculationTerm passive = Assert.Single(quote.Terms);
        Assert.Equal(2, passive.Quantity);
        Assert.Equal(2, passive.Delta);
        Assert.Equal(2, encounter.PendingDecision!.LegalActions.Count(action =>
            action.Kind == ActionKind.BreakSubroutines));
    }

    [Fact]
    [Trait("GatewayCard", "30026")]
    public void UnityCanStackTwoActivationSnapshotsDeterministically()
    {
        BehaviorCatalog catalog = Catalog();
        GameState preRun = BuildState(
            catalog,
            CodeGateIce,
            [GatewayBatch3CardCodes.Unity, GatewayBatch3CardCodes.Echelon]);
        CardInstanceId unityId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Unity).Id;

        GameState encounter = ReachEncounter(preRun, catalog);
        LegalAction firstPump = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PumpBreaker
                && Assert.IsType<RunBreakerParameters>(action.Parameters).BreakerId == unityId);
        ApplyResult first = Apply(encounter, firstPump, catalog);
        Assert.True(first.Accepted, first.Rejection?.Message);
        LegalAction secondPump = Assert.Single(
            first.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PumpBreaker
                && Assert.IsType<RunBreakerParameters>(action.Parameters).BreakerId == unityId);
        ApplyResult second = Apply(first.State, secondPump, catalog);
        Assert.True(second.Accepted, second.Rejection?.Message);

        Assert.Equal(2, second.State.RunState!.LingeringEffects.Count);
        Assert.All(second.State.RunState.LingeringEffects, effect =>
        {
            RunValueAdjustment adjustment = Assert.IsType<RunValueAdjustment>(effect.ValueAdjustment);
            Assert.Equal(2, adjustment.Quantity);
            Assert.Equal(2, adjustment.Delta);
        });
        ValueQuote quote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectBreakerQuote(second.State, catalog, unityId));
        Assert.Equal(5, quote.EffectiveValue);
        Assert.Equal(
            [2, 2],
            quote.Terms.Select(term => term.Delta).ToArray());
    }

    [Fact]
    [Trait("GatewayCard", "30008")]
    public void LeechCentralSuccessAddsVirusAndItsPaidEncounterAdjustmentExpires()
    {
        BehaviorCatalog catalog = Catalog();
        GameState central = BuildState(
            catalog,
            BarrierIce,
            [GatewayBatch3CardCodes.Leech],
            counterCount: 1,
            targetServer: ServerKind.RAndD,
            includeIce: false,
            includeRAndDCard: true);
        CardInstanceId leechId = central.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;

        GameState afterSuccess = FinishRun(
            GameEngine.BeginRun(
                central,
                central.Servers.Single(server => server.Kind == ServerKind.RAndD).Id,
                catalog),
            catalog);
        Assert.Equal(2, CounterCount(afterSuccess, leechId, "virus"));

        GameState encounterSource = BuildState(
            catalog,
            BarrierIce,
            [GatewayBatch3CardCodes.Leech],
            counterCount: 1);
        leechId = encounterSource.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;
        GameState encounter = ReachEncounter(encounterSource, catalog);
        LegalAction use = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == leechId);

        ApplyResult applied = Apply(encounter, use, catalog);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.Equal(0, CounterCount(applied.State, leechId, "virus"));
        RunLingeringEffect effect = Assert.Single(applied.State.RunState!.LingeringEffects);
        Assert.Equal($"behavior:encounter-strength:{GatewayBatch3CardCodes.Leech.Value}:leech-encounter-strength", effect.BehaviorId);
        Assert.Equal(leechId, effect.SourceCardId);
        Assert.Equal(RunLingeringEffectExpiry.Encounter, effect.Expiry);
        RunValueAdjustment adjustment = Assert.IsType<RunValueAdjustment>(effect.ValueAdjustment);
        Assert.Equal(BehaviorValueSourceKind.EncounterScoped, adjustment.Source);
        Assert.Equal(leechId, adjustment.SourceCardId);
        Assert.Equal(applied.State.RunState.Encounter!.IceId, adjustment.TargetCardId);
        Assert.Equal(-1, adjustment.Delta);
        Assert.Equal("leech-encounter-ice-strength", adjustment.ReasonKey);

        ValueQuote quote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectIceQuote(
                applied.State,
                catalog,
                applied.State.RunState.Encounter!.IceId));
        Assert.Equal(0, quote.EffectiveValue);
        ValueCalculationTerm term = Assert.Single(quote.Terms);
        Assert.Equal(BehaviorValueSourceKind.EncounterScoped, term.Source);
        Assert.Equal(-1, term.Delta);
        Assert.Equal(leechId, term.SourceCardId);
        Assert.Equal("leech-encounter-ice-strength", term.ReasonKey);

        GameState movedSource = MoveRunnerRigCardToHeap(applied.State, leechId);
        ValueQuote movedQuote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectIceQuote(
                movedSource,
                catalog,
                movedSource.RunState!.Encounter!.IceId));
        Assert.Equal(0, movedQuote.EffectiveValue);

        GameState afterEncounter = FinishEncounter(movedSource, catalog);
        Assert.Equal(RunTimingStep.MovementPaidWindow, afterEncounter.RunState!.Step);
        Assert.Empty(afterEncounter.RunState.LingeringEffects);
        Assert.Empty(StateInvariantValidator.Collect(afterEncounter, catalog));

        string json = EngineJson.Serialize(applied.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(applied.State), DeterministicHash.ForState(restored));
    }

    [Fact]
    [Trait("GatewayCard", "30008")]
    public void LeechDoesNotTriggerOnACompletedRemoteRunAndCannotPayWithoutVirus()
    {
        BehaviorCatalog catalog = Catalog();
        GameState remote = BuildState(
            catalog,
            BarrierIce,
            [GatewayBatch3CardCodes.Leech],
            counterCount: 1,
            targetServer: ServerKind.Remote,
            includeIce: false);
        CardInstanceId leechId = remote.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;
        GameState completed = FinishRun(
            GameEngine.BeginRun(
                remote,
                remote.Servers.Single(server => server.Kind == ServerKind.Remote).Id,
                catalog),
            catalog);
        Assert.Equal(1, CounterCount(completed, leechId, "virus"));

        GameState noCounter = BuildState(
            catalog,
            BarrierIce,
            [GatewayBatch3CardCodes.Leech]);
        GameState encounter = ReachEncounter(noCounter, catalog);
        Assert.DoesNotContain(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility);

        GameState unsuccessfulCentral = BuildState(
            catalog,
            EndRunBarrierIce,
            [GatewayBatch3CardCodes.Leech],
            counterCount: 1,
            targetServer: ServerKind.RAndD);
        CardInstanceId unsuccessfulLeechId = unsuccessfulCentral.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;
        GameState ended = FinishRun(
            GameEngine.BeginRun(
                unsuccessfulCentral,
                unsuccessfulCentral.Servers.Single(server => server.Kind == ServerKind.RAndD).Id,
                catalog),
            catalog);
        Assert.False(ended.Turn.ThisTurnFacts?.SuccessfulRun ?? false);
        Assert.Equal(1, CounterCount(ended, unsuccessfulLeechId, "virus"));
    }

    [Fact]
    [Trait("GatewayCard", "30008")]
    public void LeechStacksEncounterAdjustmentsAndMakesAnEchelonBreakLegal()
    {
        BehaviorCatalog catalog = Catalog();
        GameState preRun = BuildState(
            catalog,
            LeechSentryIce,
            [GatewayBatch3CardCodes.Leech, GatewayBatch3CardCodes.Echelon],
            counterCount: 2);
        CardInstanceId leechId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;
        CardInstanceId echelonId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Echelon).Id;

        GameState encounter = ReachEncounter(preRun, catalog);
        LegalAction firstUse = LeechAction(encounter, leechId);
        ApplyResult first = Apply(encounter, firstUse, catalog);
        Assert.True(first.Accepted, first.Rejection?.Message);
        Assert.Equal(1, CounterCount(first.State, leechId, "virus"));
        Assert.Single(first.State.RunState!.LingeringEffects);
        LegalAction secondUse = LeechAction(first.State, leechId);
        ApplyResult second = Apply(first.State, secondUse, catalog);
        Assert.True(second.Accepted, second.Rejection?.Message);
        Assert.Equal(0, CounterCount(second.State, leechId, "virus"));
        Assert.Equal(2, second.State.RunState!.LingeringEffects.Count);
        Assert.Equal(
            0,
            EncounterStrength.ProjectIceQuote(
                second.State,
                catalog,
                second.State.RunState.Encounter!.IceId)!.EffectiveValue);

        // Echelon is only strength 1 here (Leech is not an icebreaker). The
        // two-point ICE becomes strength 0 after one Leech payment, which is
        // why its actual break action proves the new adjustment is live.
        GameState oneUse = BuildState(
            catalog,
            LeechSentryIce,
            [GatewayBatch3CardCodes.Leech, GatewayBatch3CardCodes.Echelon],
            counterCount: 1);
        CardInstanceId oneUseLeechId = oneUse.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;
        CardInstanceId oneUseEchelonId = oneUse.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Echelon).Id;
        GameState oneEncounter = ReachEncounter(oneUse, catalog);
        ApplyResult applied = Apply(oneEncounter, LeechAction(oneEncounter, oneUseLeechId), catalog);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        LegalAction breakAction = Assert.Single(
            applied.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && Assert.IsType<RunBreakParameters>(action.Parameters).BreakerId == oneUseEchelonId);
        ApplyResult broken = Apply(applied.State, breakAction, catalog);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        Assert.True(broken.State.RunState!.Encounter!.Subroutines.Single().Broken);
    }

    [Fact]
    [Trait("GatewayCard", "30008")]
    public void LeechCatalogEffectRejectsForgedBehaviorDeltaReasonAndSourceProvenance()
    {
        BehaviorCatalog catalog = Catalog();
        GameState preRun = BuildState(
            catalog,
            BarrierIce,
            [GatewayBatch3CardCodes.Leech],
            counterCount: 1);
        CardInstanceId leechId = preRun.Cards.Single(card =>
            card.DefinitionId == GatewayBatch3CardCodes.Leech).Id;
        GameState encounter = ReachEncounter(preRun, catalog);
        ApplyResult applied = Apply(encounter, LeechAction(encounter, leechId), catalog);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        RunLingeringEffect valid = Assert.Single(applied.State.RunState!.LingeringEffects);
        RunValueAdjustment adjustment = valid.ValueAdjustment!;

        GameState forgedBehavior = WithRunEffects(
            applied.State,
            [new RunLingeringEffect(
                valid.Id,
                "behavior:encounter-strength:fixture:forged:ability",
                valid.SourceCardId,
                valid.Expiry,
                adjustment)]);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedBehavior, catalog),
            violation => violation.Code == "run.value-adjustment-provenance-behavior");

        GameState forgedDelta = WithRunEffects(
            applied.State,
            [new RunLingeringEffect(
                valid.Id,
                valid.BehaviorId,
                valid.SourceCardId,
                valid.Expiry,
                new RunValueAdjustment(
                    adjustment.SourceCardId,
                    adjustment.TargetCardId,
                    -2,
                    adjustment.ReasonKey))]);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedDelta, catalog),
            violation => violation.Code == "run.value-adjustment-provenance-declaration");

        GameState forgedReason = WithRunEffects(
            applied.State,
            [new RunLingeringEffect(
                valid.Id,
                valid.BehaviorId,
                valid.SourceCardId,
                valid.Expiry,
                new RunValueAdjustment(
                    adjustment.SourceCardId,
                    adjustment.TargetCardId,
                    adjustment.Delta,
                    "fixture-forged-reason"))]);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedReason, catalog),
            violation => violation.Code == "run.value-adjustment-provenance-declaration");

        CardInstanceId missingSource = new("card:batch3-c1-missing-source");
        GameState forgedSource = WithRunEffects(
            applied.State,
            [new RunLingeringEffect(
                valid.Id,
                valid.BehaviorId,
                missingSource,
                valid.Expiry,
                new RunValueAdjustment(
                    missingSource,
                    adjustment.TargetCardId,
                    adjustment.Delta,
                    adjustment.ReasonKey))]);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedSource, catalog),
            violation => violation.Code == "run.value-adjustment-provenance-definition");

        GameState forgedTarget = WithRunEffects(
            applied.State,
            [new RunLingeringEffect(
                valid.Id,
                valid.BehaviorId,
                valid.SourceCardId,
                valid.Expiry,
                new RunValueAdjustment(
                    adjustment.SourceCardId,
                    new CardInstanceId("card:batch3-c1-forged-target"),
                    adjustment.Delta,
                    adjustment.ReasonKey))]);
        Assert.Contains(
            StateInvariantValidator.Collect(forgedTarget, catalog),
            violation => violation.Code == "run.value-adjustment-provenance-target");

        GameState strippedBehavior = WithRunEffects(
            applied.State,
            [new RunLingeringEffect(
                valid.Id,
                "fixture:stripped-encounter-adjustment",
                valid.SourceCardId,
                valid.Expiry,
                adjustment)]);
        Assert.Contains(
            StateInvariantValidator.Collect(strippedBehavior, catalog),
            violation => violation.Code == "run.value-adjustment-provenance-behavior");

        Assert.Throws<ArgumentException>(() => new RunLingeringEffect(
            valid.Id,
            valid.BehaviorId,
            valid.SourceCardId,
            valid.Expiry,
            new RunValueAdjustment(
                new CardInstanceId("card:batch3-c1-other-source"),
                adjustment.TargetCardId,
                adjustment.Delta,
                adjustment.ReasonKey)));
    }

    private static BehaviorCatalog Catalog()
    {
        BehaviorCatalog gateway = GatewayBehaviorCatalog.Create();
        return new BehaviorCatalogBuilder(gateway.Version, gateway)
            .AddIce(BarrierIceSpec)
            .AddIce(SentryIceSpec)
            .AddIce(LeechSentryIceSpec)
            .AddIce(LeechBarrierIceSpec)
            .AddIce(EndRunBarrierIceSpec)
            .AddIce(CodeGateIceSpec)
            .Build();
    }

    private static GameState BuildState(
        BehaviorCatalog catalog,
        CardDefinitionId iceDefinition,
        IReadOnlyList<CardDefinitionId> sourceDefinitions,
        int counterCount = 0,
        ServerKind targetServer = ServerKind.Remote,
        bool includeIce = true,
        bool includeRAndDCard = false)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44325,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerState[] servers = baseline.Servers.ToArray();
        ZoneState[] baseZones = baseline.Zones.ToArray();
        if (servers.All(candidate => candidate.Kind != ServerKind.Remote))
        {
            ServerId remoteId = allocator.NextServerId();
            ZoneId remoteIceId = allocator.NextZoneId();
            ZoneId remoteRootId = allocator.NextZoneId();
            servers = servers.Append(new ServerState(
                remoteId,
                ServerKind.Remote,
                remoteIceId,
                remoteRootId)).ToArray();
            baseZones = baseZones
                .Append(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId))
                .Append(new ZoneState(remoteRootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
                .ToArray();
        }

        ServerState server = servers.Single(candidate => candidate.Kind == targetServer);
        ZoneState rig = baseZones.Single(zone => zone.Kind == ZoneKind.RunnerRig);
        ZoneState iceZone = baseZones.Single(zone => zone.Id == server.IceZoneId);
        ZoneState rAndD = baseZones.Single(zone => zone.Kind == ZoneKind.CorpDeck);

        CardInstanceId[] sourceIds = sourceDefinitions
            .Select(_ => allocator.NextCardInstanceId())
            .ToArray();
        CardInstanceId? iceId = includeIce ? allocator.NextCardInstanceId() : null;
        CardInstanceId? rAndDId = includeRAndDCard ? allocator.NextCardInstanceId() : null;
        var cards = new List<CardInstanceState>();
        for (int index = 0; index < sourceDefinitions.Count; index++)
        {
            cards.Add(new CardInstanceState(
                sourceIds[index],
                sourceDefinitions[index],
                rig.Id,
                PlayerSide.Runner,
                faceUp: true));
        }

        if (iceId is { } installedIce)
        {
            cards.Add(new CardInstanceState(
                installedIce,
                iceDefinition,
                iceZone.Id,
                PlayerSide.Corp,
                faceUp: true,
                rezzed: true));
        }

        if (rAndDId is { } deckCard)
        {
            cards.Add(new CardInstanceState(
                deckCard,
                RAndDCard,
                rAndD.Id,
                PlayerSide.Corp,
                faceUp: false));
        }

        ZoneState[] zones = baseZones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, sourceIds)
                : zone.Id == iceZone.Id
                    ? new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        iceId is { } installedIce ? [installedIce] : [])
                    : zone.Id == rAndD.Id && rAndDId is { } deckCard
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [deckCard])
                        : zone)
            .ToArray();
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState[] players =
        [
            new PlayerState(runner.Id, runner.Side, credits: 10, clicks: 4, runner.AgendaPoints),
            corp,
        ];
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            baseline.Turn.TurnNumber,
            TurnStep.RunnerAction,
            new TurnHistoryState(1, null, null, facts, null));
        RulesState defaults = RulesState.CreateDefault(players);
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counterCount > 0
                ? [new CardCounterState(sourceIds[0], "virus", counterCount)]
                : [],
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            resolutionStack: [],
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        StateInvariantValidator.Validate(state, catalog);
        return state;
    }

    private static GameState ReachEncounter(GameState preRun, BehaviorCatalog catalog)
    {
        ServerId remote = preRun.Servers.Single(server => server.Kind == ServerKind.Remote).Id;
        GameState state = GameEngine.BeginRun(preRun, remote, catalog);
        state = Pass(state, catalog);
        state = Pass(state, catalog);
        state = Pass(state, catalog);
        state = Pass(state, catalog);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, state.RunState!.Step);
        return state;
    }

    private static GameState FinishEncounter(GameState state, BehaviorCatalog catalog)
    {
        state = Pass(state, catalog);
        state = Pass(state, catalog);
        return state;
    }

    private static LegalAction LeechAction(GameState state, CardInstanceId leechId) => Assert.Single(
        state.PendingDecision!.LegalActions,
        action => action.Kind == ActionKind.UseAbility
            && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == leechId);

    private static GameState FinishRun(GameState state, BehaviorCatalog catalog)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            if (state.RunState is null)
            {
                if (state.PendingDecision is null)
                {
                    return state;
                }

                LegalAction? triggerDecline = state.PendingDecision.LegalActions
                    .FirstOrDefault(action => action.Kind == ActionKind.DeclineTrigger);
                if (triggerDecline is null)
                {
                    return state;
                }

                state = Apply(state, triggerDecline, catalog).State;
                continue;
            }

            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction? choose = decision.LegalActions
                .FirstOrDefault(action => action.Kind == ActionKind.ChooseBreachCandidate);
            LegalAction? noAccess = decision.LegalActions
                .FirstOrDefault(action => action.Kind == ActionKind.NoAccessAction);
            LegalAction? next = choose
                ?? noAccess
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun)
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority)
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.DeclineTrigger);
            if (next is null)
            {
                return state;
            }

            ApplyResult applied = Apply(state, next, catalog);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        throw new Xunit.Sdk.XunitException("The central run did not complete within the test guard.");
    }

    private static GameState MoveRunnerRigCardToHeap(GameState state, CardInstanceId cardId)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState rig = state.GetZone(ZoneKind.RunnerRig);
        ZoneState heap = state.GetZone(ZoneKind.RunnerHeap);
        CardInstanceState moved = new(card.Id, card.DefinitionId, heap.Id, card.OwnerSide, faceUp: true);
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards.Select(candidate => candidate.Id == cardId ? moved : candidate).ToArray(),
            state.Zones.Select(zone => zone.Id == rig.Id
                ? new ZoneState(rig.Id, rig.Kind, rig.OwnerSide, rig.ServerId, rig.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == heap.Id
                    ? new ZoneState(heap.Id, heap.Kind, heap.OwnerSide, heap.ServerId, heap.CardIds.Append(cardId).ToArray())
                    : zone).ToArray(),
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            eventSequence: state.EventSequence,
            decisionRevision: state.DecisionRevision,
            initialSeed: state.InitialSeed,
            runState: state.RunState,
            rulesState: state.RulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint);
    }

    private static GameState WithRunEffects(
        GameState state,
        IReadOnlyList<RunLingeringEffect> effects) =>
        new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: state.Cards,
            zones: state.Zones,
            servers: state.Servers,
            turn: state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: state.Allocator,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: CopyRunWithEffects(state.RunState!, effects),
            rulesState: state.RulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint);

    private static RunState CopyRunWithEffects(
        RunState run,
        IReadOnlyList<RunLingeringEffect> effects) =>
        new(
            run.Version,
            run.Id,
            run.AttackedServer,
            run.InitiatingSide,
            run.InitiatingPlayer,
            run.InitiatingSource,
            run.Phase,
            run.Step,
            run.CurrentIceIndex,
            run.CurrentIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            run.Priority,
            run.Encounter,
            run.BreachCandidates,
            run.RandomAccessLimit,
            run.ChosenAccessCards,
            run.AccessedCards,
            run.AccessState,
            run.RunCredits,
            run.ActiveRunEventId,
            effects,
            run.BreachAccessModifiers,
            run.RezCostModifiers);

    private static ApplyResult Apply(GameState state, LegalAction action, BehaviorCatalog catalog) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            catalog);

    private static GameState Pass(GameState state, BehaviorCatalog catalog)
    {
        LegalAction pass = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = Apply(state, pass, catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static int CounterCount(GameState state, CardInstanceId cardId, string counterName) =>
        state.RulesState?.Counters
            .SingleOrDefault(counter => counter.CardId == cardId && counter.CounterName == counterName)
            ?.Count
        ?? 0;
}
