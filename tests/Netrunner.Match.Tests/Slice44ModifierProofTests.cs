using System.Text;

using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Production-path timing evidence for the Slice 4.4 modifiers that cross
/// the run-event, rez, and encounter boundaries.  The state is deliberately
/// compact, but every transition is performed by the public engine reducers
/// against the Gateway behavior and runtime metadata composition roots.
/// </summary>
public sealed class Slice44ModifierProofTests
{
    private static readonly CardDefinitionId IceDefinition = GatewayCardCodes.Palisade;

    [Fact]
    [Trait("GatewayCard", "30012")]
    [Trait("GatewayCard", "30008")]
    public void TreadLightlyRezModifierAndLeechEncounterModifierShareOneRunAndIce()
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtimeCatalog = CreateRuntimeCatalog();
        ProofFixture fixture = BuildFixture(behaviorCatalog, runtimeCatalog);

        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            fixture.State,
            fixture.ServerId,
            behaviorCatalog,
            runtimeCatalog,
            fixture.RunnerId,
            fixture.TreadLightlyId);
        RunState startedRun = Assert.IsType<RunState>(started.State.RunState);
        Assert.Equal(fixture.TreadLightlyId, startedRun.ActiveRunEventId);
        Assert.Equal(fixture.TreadLightlyId, startedRun.InitiatingSource);
        Assert.Equal(fixture.RunnerId, startedRun.InitiatingPlayer);
        Assert.Equal(9, started.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(
            ZoneKind.RunnerPlayArea,
            started.State.GetZone(started.State.GetCard(fixture.TreadLightlyId).ZoneId).Kind);
        Assert.True(started.State.GetCard(fixture.TreadLightlyId).FaceUp);

        RunEventPlayedEventPayload played = Assert.IsType<RunEventPlayedEventPayload>(
            started.Events.Single(value => value.Kind == EngineEventKinds.RunEventPlayed).Payload);
        Assert.Equal(startedRun.Id, played.RunId);
        Assert.Equal(fixture.TreadLightlyId, played.EventCardId);
        Assert.Equal(GatewayBatch3CardCodes.TreadLightly, played.DefinitionId);
        Assert.Equal(1, played.Cost);
        Assert.Equal(0, played.HostedCredits);
        Assert.Equal(9, played.RunnerCreditsAfter);

        RunRezCostModifier runRezModifier = Assert.Single(startedRun.RezCostModifiers!);
        Assert.Equal(GatewayBatch3CardCodes.TreadLightly, runRezModifier.SourceDefinitionId);
        Assert.Equal(fixture.TreadLightlyId, runRezModifier.SourceCardId);
        Assert.Equal(3, runRezModifier.Delta);
        Assert.Equal("tread-lightly-ice-rez-cost", runRezModifier.ReasonKey);
        Assert.Empty(StateInvariantValidator.Collect(started.State, behaviorCatalog, runtimeCatalog));

        GameState corpApproach = ReachCorpApproach(
            started.State,
            fixture,
            behaviorCatalog,
            runtimeCatalog);
        Assert.Equal(startedRun.Id, corpApproach.RunState!.Id);
        Assert.Equal(fixture.IceId, corpApproach.RunState.CurrentIceId);
        Assert.Equal(RunTimingStep.ApproachPaidWindow, corpApproach.RunState.Step);
        Assert.Equal(fixture.CorpId, corpApproach.PendingDecision!.Owner);

        // The hidden ICE remains definition-free to the Runner while the Corp
        // receives the rez decision.  The Corp action itself is the only
        // source of the frozen cost quote.
        Observation runnerApproach = ObservationBuilder.Build(
            corpApproach,
            PlayerSide.Runner,
            behaviorCatalog);
        Assert.DoesNotContain(
            IceDefinition.Value,
            EngineJson.Serialize(runnerApproach),
            StringComparison.Ordinal);

        LegalAction rezAction = Assert.Single(
            corpApproach.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.IceId);
        RezCardParameters rezParameters = Assert.IsType<RezCardParameters>(rezAction.Parameters);
        CostQuote rezQuote = Assert.IsType<CostQuote>(rezParameters.CostQuote);
        Assert.Equal(3, rezQuote.BaseCost);
        Assert.Equal(6, rezQuote.EffectiveCost);
        CostAdjustment rezAdjustment = Assert.Single(rezQuote.Adjustments);
        Assert.Equal(BehaviorCostModifierKind.IceRezCreditCost, rezAdjustment.Kind);
        Assert.Equal(3, rezAdjustment.Delta);
        Assert.Equal("tread-lightly-ice-rez-cost", rezAdjustment.ReasonKey);
        Assert.Equal(GatewayBatch3CardCodes.TreadLightly, rezAdjustment.SourceDefinitionId);
        Assert.Equal(fixture.TreadLightlyId, rezAdjustment.SourceCardId);

        ApplyResult rezzed = Apply(
            corpApproach,
            rezAction,
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(rezzed.Accepted, rezzed.Rejection?.Message);
        Assert.Equal(4, rezzed.State.GetPlayer(fixture.CorpId).Credits);
        Assert.True(rezzed.State.GetCard(fixture.IceId).Rezzed);
        Assert.Equal(fixture.IceId, rezzed.State.RunState!.CurrentIceId);
        Assert.Equal(
            1,
            rezzed.Events.Count(value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Target == fixture.CorpId
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == -6));
        Assert.Equal(
            runRezModifier,
            Assert.Single(rezzed.State.RunState.RezCostModifiers!));
        Assert.Empty(StateInvariantValidator.Collect(rezzed.State, behaviorCatalog, runtimeCatalog));

        GameState encounter = ReachEncounter(rezzed.State, fixture, behaviorCatalog, runtimeCatalog);
        Assert.Equal(startedRun.Id, encounter.RunState!.Id);
        Assert.Equal(fixture.IceId, encounter.RunState.Encounter!.IceId);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, encounter.RunState.Step);
        Assert.Equal(
            runRezModifier,
            Assert.Single(encounter.RunState.RezCostModifiers!));

        // A save/load at the exact encounter decision boundary must retain
        // the run-scoped Tread snapshot before the encounter-scoped Leech
        // effect is created.
        string encounterJson = EngineJson.Serialize(encounter);
        GameState restoredEncounter = EngineJson.DeserializeState(
            encounterJson,
            behaviorCatalog,
            runtimeCatalog);
        Assert.Equal(encounterJson, EngineJson.Serialize(restoredEncounter));
        Assert.Equal(DeterministicHash.ForState(encounter), DeterministicHash.ForState(restoredEncounter));
        Assert.Equal(
            runRezModifier,
            Assert.Single(restoredEncounter.RunState!.RezCostModifiers!));
        Assert.Empty(StateInvariantValidator.Collect(restoredEncounter, behaviorCatalog, runtimeCatalog));

        LegalAction leechAction = Assert.Single(
            restoredEncounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == fixture.LeechId);
        AbilityActionParameters leechParameters = Assert.IsType<AbilityActionParameters>(leechAction.Parameters);
        Assert.Equal(GatewayBatch3CardCodes.Leech, leechParameters.DefinitionId);
        Assert.Equal("leech-encounter-strength", leechParameters.AbilityKey);
        Assert.Equal(BehaviorAbilityTiming.EncounterPaidWindow, leechParameters.Timing);
        Assert.Equal(BehaviorAbilityActivation.PaidAbility, leechParameters.Activation);
        Assert.Contains(
            leechParameters.Costs,
            cost => cost.Kind == BehaviorCostKind.Counter
                && cost.Amount == 1
                && cost.CounterName == "virus");

        Observation encounterObservation = ObservationBuilder.Build(
            restoredEncounter,
            PlayerSide.Runner,
            behaviorCatalog);
        Assert.Equal(
            restoredEncounter.PendingDecision.Id.Value,
            encounterObservation.Decision!.Id.Value);
        Assert.Contains(
            encounterObservation.LegalActions,
            action => action.Id == leechAction.Id);

        // FullGameContractAgent v3 deliberately declines unlimited
        // non-click abilities. Leech is such an ability, so this proof uses
        // the exact engine-issued ActionId directly after asserting it crosses
        // the observation boundary; no action payload is synthesized or
        // modified by the proof.
        LegalAction selectedAction = Assert.Single(
            restoredEncounter.PendingDecision.LegalActions,
            action => action.Id == leechAction.Id);
        Assert.Equal(ActionKind.UseAbility, selectedAction.Kind);
        Assert.Equal(fixture.LeechId, Assert.IsType<AbilityActionParameters>(selectedAction.Parameters).SourceCardId);

        ApplyResult used = Apply(
            restoredEncounter,
            selectedAction,
            behaviorCatalog,
            runtimeCatalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(startedRun.Id, used.State.RunState!.Id);
        Assert.Equal(fixture.IceId, used.State.RunState.Encounter!.IceId);
        Assert.Equal(0, CounterCount(used.State, fixture.LeechId, "virus"));
        Assert.Equal(
            runRezModifier,
            Assert.Single(used.State.RunState.RezCostModifiers!));

        RunLingeringEffect leechEffect = Assert.Single(used.State.RunState.LingeringEffects);
        Assert.Equal(
            $"behavior:encounter-strength:{GatewayBatch3CardCodes.Leech.Value}:leech-encounter-strength",
            leechEffect.BehaviorId);
        Assert.Equal(fixture.LeechId, leechEffect.SourceCardId);
        Assert.Equal(RunLingeringEffectExpiry.Encounter, leechEffect.Expiry);
        RunValueAdjustment leechAdjustment = Assert.IsType<RunValueAdjustment>(leechEffect.ValueAdjustment);
        Assert.Equal(BehaviorValueKind.Strength, leechAdjustment.ValueKind);
        Assert.Equal(BehaviorValueSourceKind.EncounterScoped, leechAdjustment.Source);
        Assert.Equal(BehaviorValueOperation.Add, leechAdjustment.Operation);
        Assert.Equal(fixture.LeechId, leechAdjustment.SourceCardId);
        Assert.Equal(fixture.IceId, leechAdjustment.TargetCardId);
        Assert.Equal(-1, leechAdjustment.Delta);
        Assert.Equal("leech-encounter-ice-strength", leechAdjustment.ReasonKey);

        ValueQuote iceQuote = Assert.IsType<ValueQuote>(
            EncounterStrength.ProjectIceQuote(
                used.State,
                behaviorCatalog,
                fixture.IceId));
        Assert.Equal(2, iceQuote.BaseValue);
        Assert.Equal(1, iceQuote.EffectiveValue);
        ValueCalculationTerm leechTerm = Assert.Single(iceQuote.Terms);
        Assert.Equal(BehaviorValueSourceKind.EncounterScoped, leechTerm.Source);
        Assert.Equal(BehaviorValueOperation.Add, leechTerm.Operation);
        Assert.Equal(fixture.LeechId, leechTerm.SourceCardId);
        Assert.Equal(-1, leechTerm.Delta);
        Assert.Equal("leech-encounter-ice-strength", leechTerm.ReasonKey);

        // The post-payment checkpoint retains both independent modifier
        // snapshots and the same authoritative run/ICE identities.
        string usedJson = EngineJson.Serialize(used.State);
        GameState restoredUsed = EngineJson.DeserializeState(
            usedJson,
            behaviorCatalog,
            runtimeCatalog);
        Assert.Equal(usedJson, EngineJson.Serialize(restoredUsed));
        Assert.Equal(DeterministicHash.ForState(used.State), DeterministicHash.ForState(restoredUsed));
        Assert.Equal(
            runRezModifier,
            Assert.Single(restoredUsed.RunState!.RezCostModifiers!));
        RunValueAdjustment restoredAdjustment = Assert.IsType<RunValueAdjustment>(
            Assert.Single(restoredUsed.RunState.LingeringEffects).ValueAdjustment);
        Assert.Equal(fixture.IceId, restoredAdjustment.TargetCardId);
        Assert.Equal(-1, restoredAdjustment.Delta);
        Assert.Equal(
            iceQuote,
            EncounterStrength.ProjectIceQuote(restoredUsed, behaviorCatalog, fixture.IceId));
        Assert.Empty(StateInvariantValidator.Collect(restoredUsed, behaviorCatalog, runtimeCatalog));
    }

    private static ProofFixture BuildFixture(
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        GameState baseline = GameState.CreateNew(
            seed: 444401,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        CardInstanceId treadLightlyId = allocator.NextCardInstanceId();
        CardInstanceId leechId = allocator.NextCardInstanceId();
        CardInstanceId iceId = allocator.NextCardInstanceId();
        ServerState server = baseline.Servers.Single(candidate => candidate.Kind == ServerKind.RAndD);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState iceZone = baseline.GetZone(server.IceZoneId);

        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState[] players =
        [
            new PlayerState(runner.Id, runner.Side, credits: 10, clicks: 4, runner.AgendaPoints),
            new PlayerState(corp.Id, corp.Side, credits: 10, clicks: 3, corp.AgendaPoints),
        ];
        CardInstanceState[] cards =
        [
            new(treadLightlyId, GatewayBatch3CardCodes.TreadLightly, grip.Id, PlayerSide.Runner, faceUp: true),
            new(leechId, GatewayBatch3CardCodes.Leech, rig.Id, PlayerSide.Runner, faceUp: true),
            new(iceId, IceDefinition, iceZone.Id, PlayerSide.Corp, faceUp: false, rezzed: false),
        ];
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [treadLightlyId])
                : zone.Id == rig.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [leechId])
                    : zone.Id == iceZone.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [iceId])
                        : zone)
            .ToArray();
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
            defaults.Players.Select(player => player.PlayerId == runner.Id
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    player.Tags,
                    player.BaseMemory,
                    usedMemory: 1)
                : player).ToArray(),
            [new ProgramMemoryState(leechId, runner.Id, memoryCost: 1)],
            [new CardCounterState(leechId, "virus", 1)],
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
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            resolutionStack: [],
            pendingDecision: null,
            actionSequence: baseline.ActionSequence,
            eventSequence: baseline.EventSequence,
            decisionRevision: baseline.DecisionRevision,
            initialSeed: baseline.InitialSeed,
            runState: null,
            rulesState: rules,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return new ProofFixture(state, runner.Id, corp.Id, server.Id, treadLightlyId, leechId, iceId);
    }

    private static GameState ReachCorpApproach(
        GameState source,
        ProofFixture fixture,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        GameState state = source;
        for (int guard = 0; guard < 64; guard++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            if (state.RunState is
                {
                    Step: RunTimingStep.ApproachPaidWindow,
                    CurrentIceId: CardInstanceId current,
                }
                && current == fixture.IceId
                && pending.Owner == fixture.CorpId)
            {
                return state;
            }

            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, pass, behaviorCatalog, runtimeCatalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        throw new Xunit.Sdk.XunitException("The run did not reach the Corp approach rez window.");
    }

    private static GameState ReachEncounter(
        GameState source,
        ProofFixture fixture,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        GameState state = source;
        for (int guard = 0; guard < 64; guard++)
        {
            if (state.RunState is
                {
                    Step: RunTimingStep.EncounterPaidWindow,
                    Encounter.IceId: CardInstanceId current,
                }
                && current == fixture.IceId)
            {
                return state;
            }

            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(state, pass, behaviorCatalog, runtimeCatalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        throw new Xunit.Sdk.XunitException("The run did not reach the ICE encounter window.");
    }

    private static ApplyResult Apply(
        GameState state,
        LegalAction action,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            behaviorCatalog,
            runtimeCatalog);
    }

    private static int CounterCount(
        GameState state,
        CardInstanceId cardId,
        string counterName) => state.RulesState?.Counters
            .SingleOrDefault(counter => counter.CardId == cardId && counter.CounterName == counterName)
            ?.Count
        ?? 0;

    private static RuntimeCardCatalog CreateRuntimeCatalog()
    {
        NormalizedCardDefinition[] cards =
        [
            Card(
                GatewayBatch3CardCodes.Leech.Value,
                "Leech",
                "runner",
                "anarch",
                "program",
                ["Virus"],
                cost: 1,
                memoryCost: 1),
            Card(
                GatewayBatch3CardCodes.TreadLightly.Value,
                "Tread Lightly",
                "runner",
                "criminal",
                "event",
                ["Run"],
                cost: 1),
            Card(
                IceDefinition.Value,
                "Palisade",
                "corp",
                "neutral-corp",
                "ice",
                ["Barrier"],
                cost: 3,
                strength: 2),
        ];
        NormalizedCardDefinition[] ordered = cards
            .OrderBy(card => card.Title, StringComparer.Ordinal)
            .ThenBy(card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] cardBytes = CardDataJson.SerializeCards(ordered);
        CardDataManifest manifest = new(
            CardDataValidator.CurrentManifestSchemaVersion,
            "https://example.invalid/gateway-slice44-modifier-tests",
            new string('c', 40),
            "2026-09-04",
            "fixture-v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            CardDataJson.ComputeSha256Hex(cardBytes),
            "fixtures/NOTICE.txt",
            "NOASSERTION",
            "fixtures/cards.json",
            "fixture",
            "Gateway Slice 4.4 modifier fixture",
            ordered.Length);
        return GatewayRuntimeCardCatalog.Create(CardDataResource.LoadBytes(
            cardBytes,
            CardDataJson.SerializeManifest(manifest),
            Encoding.UTF8.GetBytes("runtime fixture notice")));
    }

    private static NormalizedCardDefinition Card(
        string code,
        string title,
        string side,
        string faction,
        string type,
        IReadOnlyList<string>? subtypes = null,
        int? cost = null,
        int? strength = null,
        int? memoryCost = null) =>
        new(
            code,
            title,
            side,
            faction,
            type,
            subtypes ?? [],
            string.Empty,
            "fixture",
            quantity: 3,
            deckLimit: 3,
            uniqueness: false,
            cost,
            strength,
            memoryCost,
            trashCost: null,
            agendaPoints: null,
            advancementRequirement: null,
            influenceCost: 0,
            minimumDeckSize: null,
            influenceLimit: null,
            baseLink: null);

    private sealed record ProofFixture(
        GameState State,
        PlayerId RunnerId,
        PlayerId CorpId,
        ServerId ServerId,
        CardInstanceId TreadLightlyId,
        CardInstanceId LeechId,
        CardInstanceId IceId);
}
