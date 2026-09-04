using Xunit;

namespace Netrunner.Core.Tests;

public sealed class ValueCalculationTests
{
    private static readonly CardDefinitionId Definition = new("fixture:value-ice");
    private static readonly CardInstanceId Source = new("card:value-source");

    [Fact]
    public void ValueQuoteChecksBaseTermsAndCanonicalizesTermOrder()
    {
        ValueCalculationTerm perInstalled = new(
            BehaviorValueSourceKind.PerInstalledCard,
            BehaviorValueOperation.Add,
            2,
            "fixture-per-installed",
            Definition,
            Source,
            quantity: 2,
            unitDelta: 1);
        ValueCalculationTerm staticTerm = new(
            BehaviorValueSourceKind.Static,
            BehaviorValueOperation.Add,
            1,
            "fixture-static",
            Definition,
            Source);

        ValueQuote quote = new(
            BehaviorValueKind.Strength,
            3,
            6,
            [perInstalled, staticTerm]);

        Assert.Equal(6, quote.EffectiveValue);
        Assert.Equal(
            [BehaviorValueSourceKind.Static, BehaviorValueSourceKind.PerInstalledCard],
            quote.Terms.Select(term => term.Source).ToArray());
        Assert.Equal(quote, EngineJson.Deserialize<ValueQuote>(EngineJson.Serialize(quote)));
        Assert.Throws<ArgumentException>(() => new ValueCalculationTerm(
            BehaviorValueSourceKind.Variable,
            BehaviorValueOperation.Add,
            1,
            "fixture-variable",
            variableKey: "fixture-variable",
            quantity: 2,
            unitDelta: 1));
        Assert.Throws<ArgumentException>(() => new ValueQuote(
            BehaviorValueKind.Strength,
            3,
            5,
            [staticTerm, perInstalled]));
    }

    [Fact]
    public void ValueModifierRejectsUnsupportedShape()
    {
        Assert.Throws<ArgumentNullException>(() => new BehaviorValueModifier(
            BehaviorValueSourceKind.PerInstalledCard,
            delta: 1,
            reasonKey: "fixture-per-installed"));
        Assert.Throws<ArgumentException>(() => new BehaviorValueModifier(
            BehaviorValueSourceKind.Variable,
            delta: 1,
            reasonKey: "fixture-variable",
            variableKey: "fixture-variable",
            condition: new BehaviorCondition(BehaviorConditionKind.Encountering)));
        Assert.Throws<ArgumentException>(() => new BehaviorValueModifier(
            BehaviorValueSourceKind.CounterThreshold,
            delta: 1,
            reasonKey: "fixture-threshold",
            counterName: "virus"));
        Assert.Throws<ArgumentException>(() => new BehaviorValueModifier(
            BehaviorValueSourceKind.Static,
            delta: 1,
            reasonKey: "fixture-static",
            condition: new BehaviorCondition(BehaviorConditionKind.Encountering)));
    }

    [Fact]
    public void PureIceAndBreakerProjectionRetainTypedReasons()
    {
        IceBehaviorSpec ice = new(
            Definition,
            strength: 3,
            iceType: IceType.Barrier,
            rezCost: 1,
            subroutines: [],
            strengthModifiers:
            [new IceStrengthModifierSpec(2, IceStrengthModifierCondition.ProtectingRemoteServer)]);
        IcebreakerBehaviorSpec breaker = new(
            new CardDefinitionId("fixture:value-breaker"),
            baseStrength: 2,
            interfaceType: IceType.Barrier,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 2);
        RunLingeringEffect pump = new(
            new RunEffectId("effect:value-pump"),
            EncounterStrength.IcebreakerPumpBehavior,
            Source,
            RunLingeringEffectExpiry.Encounter);

        ValueQuote iceQuote = EncounterStrength.IceQuote(ice, ServerKind.Remote);
        ValueQuote breakerQuote = EncounterStrength.BreakerQuote(breaker, [pump], Source);

        Assert.Equal(5, iceQuote.EffectiveValue);
        Assert.Equal(4, breakerQuote.EffectiveValue);
        Assert.Equal(BehaviorValueSourceKind.Static, Assert.Single(iceQuote.Terms).Source);
        Assert.Equal(BehaviorValueSourceKind.Variable, Assert.Single(breakerQuote.Terms).Source);
        Assert.DoesNotContain("+", EngineJson.Serialize(iceQuote));
    }

    [Fact]
    public void ValueQuotePreservesNegativeEffectiveStrength()
    {
        ValueQuote quote = new(
            BehaviorValueKind.Strength,
            baseValue: 1,
            effectiveValue: -1,
            terms:
            [
                new ValueCalculationTerm(
                    BehaviorValueSourceKind.Static,
                    delta: -2,
                    reasonKey: "fixture-negative"),
            ]);

        Assert.Equal(-1, quote.EffectiveValue);
    }

    [Fact]
    public void DynamicPumpSnapshotsInstalledCountAndComposesAcrossActivations()
    {
        CardDefinitionId dynamicDefinition = new("fixture:dynamic-pump-breaker");
        IcebreakerBehaviorSpec dynamicSpec = new(
            dynamicDefinition,
            baseStrength: 2,
            interfaceType: IceType.Barrier,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 0,
            dynamicPump: new BehaviorDynamicPumpDeclaration(
                new BehaviorCardFilter(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner),
                unitDelta: 1,
                variableKey: "installed-icebreakers",
                reasonKey: "fixture-dynamic-pump"));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(
                FixtureBehaviorCatalog.Catalog.Version,
                FixtureBehaviorCatalog.Catalog)
            .AddIcebreaker(dynamicSpec)
            .Build();
        GameState preRun = DynamicPumpState(dynamicDefinition, out CardInstanceId dynamicId, out CardInstanceId fixedId);
        Assert.Equal(dynamicDefinition, preRun.GetCard(dynamicId).DefinitionId);
        Assert.Equal(1, catalog.IcebreakerSpecs.Single(spec => spec.DefinitionId == dynamicDefinition).PumpCost);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun), catalog);
        prompt = PassTwiceWithCatalog(prompt, catalog);
        GameState encounter = PassTwiceWithCatalog(prompt, catalog);
        LegalAction pump = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == dynamicId);

        ApplyResult first = ApplyWithCatalog(encounter, pump, catalog);
        Assert.True(first.Accepted, first.Rejection?.Message);
        RunLingeringEffect firstEffect = Assert.Single(first.State.RunState!.LingeringEffects);
        Assert.NotNull(firstEffect.ValueAdjustment);
        RunValueAdjustment firstAdjustment = firstEffect.ValueAdjustment!;
        Assert.Equal(BehaviorValueSourceKind.Variable, firstAdjustment.Source);
        Assert.Equal(dynamicId, firstAdjustment.SourceCardId);
        Assert.Equal(dynamicId, firstAdjustment.TargetCardId);
        Assert.Equal(2, firstAdjustment.Quantity);
        Assert.Equal(1, firstAdjustment.UnitDelta);
        Assert.Equal(2, firstAdjustment.Delta);
        Assert.Equal("installed-icebreakers", firstAdjustment.VariableKey);
        string saved = EngineJson.Serialize(first.State);
        GameState restored = EngineJson.DeserializeState(saved, catalog);
        Assert.Equal(saved, EngineJson.Serialize(restored));
        Assert.Equal(
            4,
            EncounterStrength.ProjectBreakerQuote(restored, catalog, dynamicId)!.EffectiveValue);

        ValueQuote? maybeAfterFirst = EncounterStrength.ProjectBreakerQuote(first.State, catalog, dynamicId);
        Assert.NotNull(maybeAfterFirst);
        ValueQuote afterFirst = maybeAfterFirst!;
        Assert.Equal(4, afterFirst.EffectiveValue);
        ValueCalculationTerm firstTerm = Assert.Single(afterFirst.Terms);
        Assert.Equal(BehaviorValueSourceKind.Variable, firstTerm.Source);
        Assert.Equal(2, firstTerm.Quantity);
        Assert.Equal(1, firstTerm.UnitDelta);

        LegalAction secondPump = Assert.Single(first.State.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == dynamicId);
        ApplyResult second = ApplyWithCatalog(first.State, secondPump, catalog);
        Assert.True(second.Accepted, second.Rejection?.Message);
        ValueQuote? maybeAfterSecond = EncounterStrength.ProjectBreakerQuote(second.State, catalog, dynamicId);
        Assert.NotNull(maybeAfterSecond);
        ValueQuote afterSecond = maybeAfterSecond!;
        Assert.Equal(6, afterSecond.EffectiveValue);
        Assert.Equal(2, second.State.RunState!.LingeringEffects.Count);

        // Moving the counted breaker after activation must not re-derive either
        // already-realized quantity. Both snapshots remain +2.
        GameState moved = MoveRunnerRigCardToHeap(second.State, fixedId);
        ValueQuote? maybeAfterMove = EncounterStrength.ProjectBreakerQuote(moved, catalog, dynamicId);
        Assert.NotNull(maybeAfterMove);
        ValueQuote afterMove = maybeAfterMove!;
        Assert.Equal(6, afterMove.EffectiveValue);
        Assert.Equal([2, 2], afterMove.Terms.Select(term => term.Quantity).ToArray());
    }

    [Fact]
    public void DynamicPumpRejectsFakePrintedStrengthAndMalformedRunAdjustment()
    {
        BehaviorCardFilter filter = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        Assert.Throws<ArgumentException>(() => new IcebreakerBehaviorSpec(
            new CardDefinitionId("fixture:dynamic-fake"),
            2,
            IceType.Barrier,
            1,
            1,
            1,
            1,
            dynamicPump: new BehaviorDynamicPumpDeclaration(
                filter,
                1,
                "installed-icebreakers",
                "fixture-dynamic-pump")));
        Assert.Throws<ArgumentException>(() => new RunValueAdjustment(
            BehaviorValueKind.Strength,
            BehaviorValueSourceKind.Variable,
            BehaviorValueOperation.Add,
            Source,
            Source,
            delta: 1,
            reasonKey: "fixture-variable",
            variableKey: "installed-icebreakers",
            quantity: 2,
            unitDelta: 1));
    }

    [Fact]
    public void DynamicPumpCountsOnlyActiveRunnerIcebreakersIncludingSelf()
    {
        CardDefinitionId dynamicDefinition = new("fixture:dynamic-filter-breaker");
        BehaviorCardFilter breakers = new(
            BehaviorCardType.Icebreaker,
            ownerSide: PlayerSide.Runner);
        IcebreakerBehaviorSpec dynamicSpec = new(
            dynamicDefinition,
            baseStrength: 2,
            interfaceType: IceType.Barrier,
            breakCost: 1,
            breakLimit: 1,
            pumpCost: 1,
            pumpStrength: 0,
            dynamicPump: new BehaviorDynamicPumpDeclaration(
                breakers,
                unitDelta: 1,
                variableKey: "installed-icebreakers",
                reasonKey: "fixture-dynamic-filter"));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("fixture-dynamic-filter")
            .AddIcebreaker(dynamicSpec)
            .AddIcebreaker(FixtureBehaviorCatalog.Catalog.IcebreakerSpecs.Single(spec =>
                spec.DefinitionId == FixtureIceBehaviorSpecs.BreakerBarrier))
            .AddIce(FixtureBehaviorCatalog.Catalog.IceSpecs.Single(spec =>
                spec.DefinitionId == FixtureIceBehaviorSpecs.Plain))
            .Build();
        GameState state = DynamicPumpState(dynamicDefinition, out CardInstanceId dynamicId, out CardInstanceId fixedId);
        ZoneState rig = state.Zones.Single(zone =>
            zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner);
        ZoneState root = state.Zones.First(zone => zone.Kind == ZoneKind.ServerRoot);
        StableIdAllocator allocator = new(state.Allocator);
        CardInstanceId inactiveId = allocator.NextCardInstanceId();
        CardInstanceId nonBreakerId = allocator.NextCardInstanceId();
        CardInstanceId wrongSideId = allocator.NextCardInstanceId();
        CardInstanceState inactiveBreaker = new(
            inactiveId,
            FixtureIceBehaviorSpecs.BreakerBarrier,
            rig.Id,
            PlayerSide.Runner,
            faceUp: false);
        CardInstanceState activeNonBreaker = new(
            nonBreakerId,
            FixtureIceBehaviorSpecs.Plain,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        CardInstanceState wrongSideBreaker = new(
            wrongSideId,
            dynamicDefinition,
            root.Id,
            PlayerSide.Corp,
            faceUp: true,
            rezzed: true);
        IReadOnlyList<CardInstanceState> cards = state.Cards
            .Append(inactiveBreaker)
            .Append(activeNonBreaker)
            .Append(wrongSideBreaker)
            .ToArray();
        IReadOnlyList<ZoneState> zones = state.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(
                    rig.Id,
                    rig.Kind,
                    rig.OwnerSide,
                    rig.ServerId,
                    rig.CardIds.Append(inactiveId).Append(nonBreakerId).ToArray())
                : zone.Id == root.Id
                    ? new ZoneState(
                        root.Id,
                        root.Kind,
                        root.OwnerSide,
                        root.ServerId,
                        root.CardIds.Append(wrongSideId).ToArray())
                    : zone)
            .ToArray();

        Assert.True(EncounterStrength.TryResolveDynamicPump(
            dynamicSpec,
            dynamicId,
            cards,
            zones,
            catalog,
            out int quantity,
            out int delta));
        Assert.Equal(2, quantity);
        Assert.Equal(2, delta);

        IReadOnlyList<ZoneState> afterRemoval = zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(
                    rig.Id,
                    rig.Kind,
                    rig.OwnerSide,
                    rig.ServerId,
                    rig.CardIds.Where(id => id != fixedId).ToArray())
                : zone)
            .ToArray();
        Assert.True(EncounterStrength.TryResolveDynamicPump(
            dynamicSpec,
            dynamicId,
            cards,
            afterRemoval,
            catalog,
            out quantity,
            out delta));
        Assert.Equal(1, quantity);
        Assert.Equal(1, delta);
    }

    [Fact]
    public void CatalogFingerprintAndEqualityIncludeDynamicPumpDeclaration()
    {
        CardDefinitionId definition = new("fixture:dynamic-catalog-breaker");
        BehaviorCardFilter filter = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        IcebreakerBehaviorSpec first = new(
            definition,
            2,
            IceType.Barrier,
            1,
            1,
            1,
            0,
            dynamicPump: new BehaviorDynamicPumpDeclaration(filter, 1, "installed-icebreakers", "fixture-dynamic"));
        IcebreakerBehaviorSpec second = new(
            definition,
            2,
            IceType.Barrier,
            1,
            1,
            1,
            0,
            dynamicPump: new BehaviorDynamicPumpDeclaration(filter, 2, "installed-icebreakers", "fixture-dynamic"));

        BehaviorCatalog firstCatalog = new BehaviorCatalogBuilder("fixture-dynamic-catalog")
            .AddIcebreaker(first)
            .Build();
        BehaviorCatalog secondCatalog = new BehaviorCatalogBuilder("fixture-dynamic-catalog")
            .AddIcebreaker(second)
            .Build();
        Assert.NotEqual(firstCatalog.Fingerprint, secondCatalog.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => new BehaviorCatalogBuilder("fixture-dynamic-catalog")
            .AddIcebreaker(first)
            .AddIcebreaker(second));

        string json = EngineJson.Serialize(first);
        IcebreakerBehaviorSpec restored = EngineJson.Deserialize<IcebreakerBehaviorSpec>(json);
        Assert.Equal(first.DynamicPump, restored.DynamicPump);
        Assert.Contains("dynamicPump", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EncounterAdjustmentTargetsCurrentIceSurvivesSourceMovementAndExpires()
    {
        CardDefinitionId dynamicDefinition = new("fixture:leech-proof-breaker");
        IcebreakerBehaviorSpec dynamicSpec = new(
            dynamicDefinition,
            2,
            IceType.Barrier,
            1,
            1,
            1,
            0,
            dynamicPump: new BehaviorDynamicPumpDeclaration(
                new BehaviorCardFilter(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner),
                1,
                "installed-icebreakers",
                "fixture-dynamic"));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(
                FixtureBehaviorCatalog.Catalog.Version,
                FixtureBehaviorCatalog.Catalog)
            .AddIcebreaker(dynamicSpec)
            .Build();
        GameState preRun = DynamicPumpState(dynamicDefinition, out _, out CardInstanceId leechSourceId);
        preRun = AddInnerIce(preRun, out CardInstanceId otherIceId);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun), catalog);
        prompt = PassTwiceWithCatalog(prompt, catalog);
        GameState encounter = PassTwiceWithCatalog(prompt, catalog);
        CardInstanceId currentIceId = encounter.RunState!.Encounter!.IceId;
        RunValueAdjustment leechAdjustment = new(
            leechSourceId,
            currentIceId,
            -2,
            "fixture-leech");
        RunLingeringEffect leechEffect = new(
            new RunEffectId("effect:fixture-leech"),
            "fixture:leech",
            leechSourceId,
            RunLingeringEffectExpiry.Encounter,
            leechAdjustment);
        GameState withLeech = WithRunEffects(encounter, [leechEffect]);

        ValueQuote? currentQuote = EncounterStrength.ProjectIceQuote(
            withLeech,
            catalog,
            currentIceId);
        Assert.NotNull(currentQuote);
        Assert.Equal(-2, currentQuote!.EffectiveValue);
        ValueCalculationTerm leechTerm = Assert.Single(currentQuote.Terms);
        Assert.Equal(BehaviorValueSourceKind.EncounterScoped, leechTerm.Source);
        Assert.Equal(leechSourceId, leechTerm.SourceCardId);
        Assert.Equal("fixture-leech", leechTerm.ReasonKey);
        Assert.Null(EncounterStrength.ProjectIceQuote(withLeech, catalog, otherIceId));

        // The effect is sourced by a physical Runner card, but its realized
        // target survives source movement until the encounter boundary.
        GameState movedSource = MoveRunnerRigCardToHeap(withLeech, leechSourceId);
        ValueQuote? afterMove = EncounterStrength.ProjectIceQuote(
            movedSource,
            catalog,
            currentIceId);
        Assert.NotNull(afterMove);
        Assert.Equal(-2, afterMove!.EffectiveValue);

        RunValueAdjustment wrongTargetAdjustment = new(
            leechSourceId,
            otherIceId,
            -2,
            "fixture-leech");
        GameState wrongTarget = WithRunEffects(
            encounter,
            [new RunLingeringEffect(
                new RunEffectId("effect:fixture-leech-wrong-target"),
                "fixture:leech",
                leechSourceId,
                RunLingeringEffectExpiry.Encounter,
                wrongTargetAdjustment)]);
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(wrongTarget, catalog);
        Assert.Contains(violations, violation => violation.Code == "run.value-adjustment-target");

        ApplyResult runnerPass = ApplyWithCatalog(
            withLeech,
            Assert.Single(withLeech.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority),
            catalog);
        Assert.True(runnerPass.Accepted, runnerPass.Rejection?.Message);
        ApplyResult corpPass = ApplyWithCatalog(
            runnerPass.State,
            Assert.Single(runnerPass.State.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority),
            catalog);
        Assert.True(corpPass.Accepted, corpPass.Rejection?.Message);
        Assert.Empty(corpPass.State.RunState!.LingeringEffects);
    }

    [Fact]
    public void AuthoritativeQuoteComposesStaticInstalledVariableAndCounterTerms()
    {
        CardDefinitionId dynamicDefinition = new("fixture:composed-breaker");
        BehaviorCardFilter breakers = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        IcebreakerBehaviorSpec dynamicSpec = new(
            dynamicDefinition,
            2,
            IceType.Barrier,
            1,
            1,
            1,
            0,
            valueModifiers:
            [
                BehaviorValueModifier.Static(1, "fixture-static"),
                BehaviorValueModifier.PerInstalled(breakers, 1, "fixture-per-installed"),
                BehaviorValueModifier.CounterThreshold("advancement", 2, 3, "fixture-threshold"),
            ],
            dynamicPump: new BehaviorDynamicPumpDeclaration(
                breakers,
                1,
                "installed-icebreakers",
                "fixture-dynamic"));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(
                FixtureBehaviorCatalog.Catalog.Version,
                FixtureBehaviorCatalog.Catalog)
            .AddIcebreaker(dynamicSpec)
            .Build();
        GameState preRun = DynamicPumpState(
            dynamicDefinition,
            out CardInstanceId dynamicId,
            out _,
            dynamicAdvancementCounters: 2);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun), catalog);
        GameState encounter = PassTwiceWithCatalog(PassTwiceWithCatalog(prompt, catalog), catalog);
        ValueQuote? atThresholdQuote = EncounterStrength.ProjectBreakerQuote(
            encounter,
            catalog,
            dynamicId);
        Assert.NotNull(atThresholdQuote);
        Assert.Equal(8, atThresholdQuote!.EffectiveValue);
        Assert.Contains(
            atThresholdQuote.Terms,
            term => term.Source == BehaviorValueSourceKind.CounterThreshold);

        GameState belowThresholdPreRun = DynamicPumpState(
            dynamicDefinition,
            out CardInstanceId belowThresholdId,
            out _,
            dynamicAdvancementCounters: 1);
        GameState belowThresholdPrompt = GameEngine.BeginRun(
            belowThresholdPreRun,
            RunTimingTests.RemoteId(belowThresholdPreRun),
            catalog);
        GameState belowThresholdEncounter = PassTwiceWithCatalog(
            PassTwiceWithCatalog(belowThresholdPrompt, catalog),
            catalog);
        ValueQuote? belowThresholdQuote = EncounterStrength.ProjectBreakerQuote(
            belowThresholdEncounter,
            catalog,
            belowThresholdId);
        Assert.NotNull(belowThresholdQuote);
        Assert.Equal(5, belowThresholdQuote!.EffectiveValue);
        Assert.DoesNotContain(
            belowThresholdQuote.Terms,
            term => term.Source == BehaviorValueSourceKind.CounterThreshold);
        LegalAction pump = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == dynamicId);
        ApplyResult pumped = ApplyWithCatalog(encounter, pump, catalog);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);

        ValueQuote? quote = EncounterStrength.ProjectBreakerQuote(pumped.State, catalog, dynamicId);
        Assert.NotNull(quote);
        Assert.Equal(10, quote!.EffectiveValue);
        Assert.Equal(
            [
                BehaviorValueSourceKind.Static,
                BehaviorValueSourceKind.PerInstalledCard,
                BehaviorValueSourceKind.Variable,
                BehaviorValueSourceKind.CounterThreshold,
            ],
            quote.Terms.Select(term => term.Source).ToArray());
        Assert.Equal(2, quote.Terms.Single(term => term.Source == BehaviorValueSourceKind.PerInstalledCard).Quantity);
        Assert.Equal(2, quote.Terms.Single(term => term.Source == BehaviorValueSourceKind.Variable).Quantity);
        Assert.Equal(3, quote.Terms.Single(term => term.Source == BehaviorValueSourceKind.CounterThreshold).Delta);
    }

    private static GameState DynamicPumpState(
        CardDefinitionId dynamicDefinition,
        out CardInstanceId dynamicId,
        out CardInstanceId fixedId,
        int dynamicAdvancementCounters = 0)
    {
        GameState baseState = RunTimingTests.CreateRunState(1, rezzed: true);
        ZoneState rig = baseState.Zones.Single(zone =>
            zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner);
        StableIdAllocator allocator = new(baseState.Allocator);
        dynamicId = allocator.NextCardInstanceId();
        fixedId = allocator.NextCardInstanceId();
        CardInstanceState dynamic = new(
            dynamicId,
            dynamicDefinition,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true,
            advancementCounters: dynamicAdvancementCounters);
        CardInstanceState fixedBreaker = new(
            fixedId,
            FixtureIceBehaviorSpecs.BreakerBarrier,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true);
        ZoneState populatedRig = new(
            rig.Id,
            rig.Kind,
            rig.OwnerSide,
            rig.ServerId,
            rig.CardIds.Append(dynamicId).Append(fixedId).ToArray());
        return new GameState(
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players: baseState.Players,
            cards: baseState.Cards.Append(dynamic).Append(fixedBreaker),
            zones: baseState.Zones.Select(zone => zone.Id == rig.Id ? populatedRig : zone),
            servers: baseState.Servers,
            turn: baseState.Turn,
            outcome: baseState.Outcome,
            randomState: baseState.RandomState,
            allocator: allocator.State,
            schemaVersion: baseState.SchemaVersion,
            decisionRevision: baseState.DecisionRevision,
            eventSequence: baseState.EventSequence,
            resolutionStack: baseState.ResolutionStack,
            pendingDecision: baseState.PendingDecision,
            actionSequence: baseState.ActionSequence,
            initialSeed: baseState.InitialSeed,
            runState: baseState.RunState);
    }

    private static GameState AddInnerIce(GameState state, out CardInstanceId innerIceId)
    {
        ServerState remote = state.Servers.Single(server => server.Kind == ServerKind.Remote);
        ZoneState iceZone = state.Zones.Single(zone => zone.Id == remote.IceZoneId);
        StableIdAllocator allocator = new(state.Allocator);
        innerIceId = allocator.NextCardInstanceId();
        CardInstanceState innerIce = new(
            innerIceId,
            FixtureIceBehaviorSpecs.Plain,
            iceZone.Id,
            PlayerSide.Corp,
            faceUp: true,
            rezzed: true);
        ZoneState populated = new(
            iceZone.Id,
            iceZone.Kind,
            iceZone.OwnerSide,
            iceZone.ServerId,
            new[] { innerIceId }.Concat(iceZone.CardIds).ToArray());
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: state.Cards.Append(innerIce),
            zones: state.Zones.Select(zone => zone.Id == iceZone.Id ? populated : zone),
            servers: state.Servers,
            turn: state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: allocator.State,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState);
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
            runState: state.RunState!.WithLingeringEffects(effects));

    private static ApplyResult ApplyWithCatalog(
        GameState state,
        LegalAction action,
        IBehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);
    }

    private static GameState PassTwiceWithCatalog(GameState state, IBehaviorCatalog catalog)
    {
        ApplyResult first = ApplyWithCatalog(
            state,
            Assert.Single(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority),
            catalog);
        Assert.True(first.Accepted, first.Rejection?.Message);
        ApplyResult second = ApplyWithCatalog(
            first.State,
            Assert.Single(first.State.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority),
            catalog);
        Assert.True(second.Accepted, second.Rejection?.Message);
        return second.State;
    }

    private static GameState MoveRunnerRigCardToHeap(GameState state, CardInstanceId cardId)
    {
        CardInstanceState card = state.Cards.Single(candidate => candidate.Id == cardId);
        ZoneState rig = state.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner);
        ZoneState heap = state.Zones.Single(zone => zone.Kind == ZoneKind.RunnerHeap && zone.OwnerSide == PlayerSide.Runner);
        CardInstanceState movedCard = new(card.Id, card.DefinitionId, heap.Id, card.OwnerSide, faceUp: true);
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: state.Cards.Select(candidate => candidate.Id == cardId ? movedCard : candidate),
            zones: state.Zones.Select(zone => zone.Id == rig.Id
                ? new ZoneState(rig.Id, rig.Kind, rig.OwnerSide, rig.ServerId, rig.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == heap.Id
                    ? new ZoneState(heap.Id, heap.Kind, heap.OwnerSide, heap.ServerId, heap.CardIds.Append(cardId).ToArray())
                    : zone),
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
            runState: state.RunState);
    }
}
