using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Production-path evidence for the C2 run-event and installed hardware
/// cards. The fixture uses the Gateway behavior composition and a normalized
/// runtime-data snapshot containing the exact printed metadata required by
/// these real reducers; no card behavior is reconstructed in the test.
/// </summary>
public sealed class GatewayBatch3C2TreadDzmzRuntimeTests
{
    private static readonly CardDefinitionId IceDefinition = GatewayCardCodes.Palisade;

    [Fact]
    [Trait("GatewayCard", "30012")]
    public void TreadLightlyPaysOneSelectsTheRequestedServerRaisesRezCostAndExpiresAtRunEnd()
    {
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = GatewayBatch3C2BehaviorSpecTests.CreateRuntimeCatalog();
        TreadFixture fixture = CreateTreadFixture(behavior, runtime, runnerCredits: 10, corpCredits: 10);
        PlayerId runner = fixture.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerId corp = fixture.State.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        ServerId rnd = fixture.State.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;

        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            fixture.State,
            rnd,
            behavior,
            runtime,
            runner,
            fixture.EventId);

        Assert.Equal(9, started.State.GetPlayer(runner).Credits);
        Assert.Equal(rnd, started.State.RunState!.AttackedServer);
        Assert.Equal(fixture.EventId, started.State.RunState.ActiveRunEventId);
        Assert.Equal(
            ZoneKind.RunnerPlayArea,
            started.State.GetZone(started.State.GetCard(fixture.EventId).ZoneId).Kind);
        RunEventPlayedEventPayload played = Assert.IsType<RunEventPlayedEventPayload>(
            started.Events.Single(value => value.Kind == EngineEventKinds.RunEventPlayed).Payload);
        Assert.Equal(1, played.Cost);
        Assert.Equal(9, played.RunnerCreditsAfter);

        GameState corpApproach = ReachCorpApproach(started.State, behavior, runtime, fixture.IceId);
        LegalAction rez = Assert.Single(
            corpApproach.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.IceId);
        RezCardParameters rezParameters = Assert.IsType<RezCardParameters>(rez.Parameters);
        Assert.Equal(3, rezParameters.CostQuote!.BaseCost);
        Assert.Equal(6, rezParameters.CostQuote.EffectiveCost);
        CostAdjustment adjustment = Assert.Single(rezParameters.CostQuote.Adjustments);
        Assert.Equal(3, adjustment.Delta);
        Assert.Equal("tread-lightly-ice-rez-cost", adjustment.ReasonKey);
        Assert.Equal(GatewayBatch3CardCodes.TreadLightly, adjustment.SourceDefinitionId);
        Assert.Equal(fixture.EventId, adjustment.SourceCardId);

        ApplyResult rezzed = Apply(corpApproach, rez, behavior, runtime);
        Assert.True(rezzed.Accepted, rezzed.Rejection?.Message);
        Assert.Equal(4, rezzed.State.GetPlayer(corp).Credits);
        Assert.True(rezzed.State.GetCard(fixture.IceId).Rezzed);
        Assert.Contains(
            rezzed.Events,
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Target == corp
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == -6);

        GameState finished = EndRunByJackOut(rezzed.State, behavior, runtime);
        Assert.Null(finished.RunState);
        Assert.Equal(ZoneKind.RunnerHeap, finished.GetZone(finished.GetCard(fixture.EventId).ZoneId).Kind);
        Assert.Empty(finished.ResolutionStack);
        Assert.Null(finished.PendingDecision);
        Assert.Empty(StateInvariantValidator.Collect(finished, behavior, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30022")]
    public void DzmzAddsMemoryAndDiscountsOnlyTheFirstProgramInstallThenChargesTheSecondInFull()
    {
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = GatewayBatch3C2BehaviorSpecTests.CreateRuntimeCatalog();
        DzmzFixture fixture = CreateDzmzFixture(behavior, runtime);
        PlayerId runner = fixture.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState actionState = GameEngine.BeginResolution(
            fixture.State,
            new TurnFrame(PlayerSide.Runner, 1, TurnFrameStage.AwaitingAction),
            behavior,
            runtime);
        LegalAction firstInstall = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == fixture.FirstProgramId);
        InstallCardParameters firstParameters = Assert.IsType<InstallCardParameters>(firstInstall.Parameters);
        Assert.Equal(3, firstParameters.CreditCost);
        Assert.Equal(4, firstParameters.CostQuote!.BaseCost);
        Assert.Equal(-1, firstParameters.CostQuote.Adjustments.Single().Delta);
        Assert.Equal("dzmz-first-program-install", firstParameters.CostQuote.Adjustments.Single().ReasonKey);

        string pendingJson = EngineJson.Serialize(actionState);
        GameState restored = EngineJson.DeserializeState(pendingJson, behavior, runtime);
        Assert.Equal(pendingJson, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(actionState), DeterministicHash.ForState(restored));

        LegalAction restoredFirstInstall = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Id == firstInstall.Id);
        ApplyResult firstChoice = Apply(restored, restoredFirstInstall, behavior, runtime);
        Assert.True(firstChoice.Accepted, firstChoice.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, firstChoice.State.PendingDecision?.Kind);
        LegalAction firstFinalize = FinalizeInstallChoice(firstChoice.State, fixture.FirstProgramId);
        ApplyResult first = Apply(firstChoice.State, firstFinalize, behavior, runtime);
        Assert.True(first.Accepted, first.Rejection?.Message);
        Assert.Equal(7, first.State.GetPlayer(runner).Credits);
        Assert.Equal(4, first.State.RulesState!.Players.Single(value => value.PlayerId == runner).UsedMemory);
        FirstXUseKey firstUse = Assert.Single(first.State.Turn.ThisTurnFacts!.FirstXUseKeys);
        Assert.Equal(fixture.DzmzId, firstUse.SourceCardId);
        Assert.Equal("dzmz-first-program-install", firstUse.Key);
        Assert.Equal(1, firstUse.Count);
        Assert.Single(first.State.Turn.ThisTurnFacts.ProgramInstallOccurrenceIds);

        GameState secondAction = ContinueToTurnAction(first.State, behavior, runtime);
        LegalAction secondInstall = Assert.Single(
            secondAction.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.InstallCard
                && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == fixture.SecondProgramId);
        InstallCardParameters secondParameters = Assert.IsType<InstallCardParameters>(secondInstall.Parameters);
        Assert.Equal(4, secondParameters.CreditCost);
        Assert.Null(secondParameters.CostQuote);

        ApplyResult secondChoice = Apply(secondAction, secondInstall, behavior, runtime);
        Assert.True(secondChoice.Accepted, secondChoice.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, secondChoice.State.PendingDecision?.Kind);
        LegalAction secondFinalize = FinalizeInstallChoice(secondChoice.State, fixture.SecondProgramId);
        ApplyResult second = Apply(secondChoice.State, secondFinalize, behavior, runtime);
        Assert.True(second.Accepted, second.Rejection?.Message);
        Assert.Equal(3, second.State.GetPlayer(runner).Credits);
        Assert.Equal(5, second.State.RulesState!.Players.Single(value => value.PlayerId == runner).UsedMemory);
        Assert.Equal(2, second.State.Turn.ThisTurnFacts!.ProgramInstallOccurrenceIds.Count);
        FirstXUseKey secondUse = Assert.Single(second.State.Turn.ThisTurnFacts.FirstXUseKeys);
        Assert.Equal(fixture.DzmzId, secondUse.SourceCardId);
        Assert.Equal("dzmz-first-program-install", secondUse.Key);
        Assert.Equal(1, secondUse.Count);
        Assert.Equal(
            [fixture.FirstProgramId, fixture.SecondProgramId],
            second.State.Turn.ThisTurnFacts.ProgramInstallOccurrenceIds);
        Assert.Empty(StateInvariantValidator.Collect(second.State, behavior, runtime));
    }

    private static TreadFixture CreateTreadFixture(
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime,
        int runnerCredits,
        int corpCredits)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44012,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        CardInstanceId eventId = allocator.NextCardInstanceId();
        CardInstanceId iceId = allocator.NextCardInstanceId();
        ServerState rnd = baseline.Servers.Single(server => server.Kind == ServerKind.RAndD);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState iceZone = baseline.GetZone(rnd.IceZoneId);
        CardInstanceState eventCard = new(
            eventId,
            GatewayBatch3CardCodes.TreadLightly,
            grip.Id,
            PlayerSide.Runner,
            faceUp: true);
        CardInstanceState ice = new(
            iceId,
            IceDefinition,
            iceZone.Id,
            PlayerSide.Corp,
            faceUp: false,
            rezzed: false);
        PlayerState[] players = baseline.Players
            .Select(player => player.Side switch
            {
                PlayerSide.Runner => new PlayerState(player.Id, player.Side, runnerCredits, 4, player.AgendaPoints),
                PlayerSide.Corp => new PlayerState(player.Id, player.Side, corpCredits, 3, player.AgendaPoints),
                _ => player,
            })
            .ToArray();
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            1,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            1,
            TurnStep.RunnerAction,
            new TurnHistoryState(1, null, null, facts, null));
        RulesState rules = RulesState.CreateDefault(players);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [eventId])
                : zone.Id == iceZone.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [iceId])
                    : zone)
            .ToArray();
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [eventCard, ice],
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            resolutionStack: [],
            rulesState: rules,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StateInvariantValidator.Validate(state, behavior, runtime);
        return new TreadFixture(state, eventId, iceId);
    }

    private static DzmzFixture CreateDzmzFixture(
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44022,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        CardInstanceId dzmzId = allocator.NextCardInstanceId();
        CardInstanceId[] existingProgramIds = [
            allocator.NextCardInstanceId(),
            allocator.NextCardInstanceId(),
            allocator.NextCardInstanceId(),
        ];
        CardInstanceId firstProgramId = allocator.NextCardInstanceId();
        CardInstanceId secondProgramId = allocator.NextCardInstanceId();
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, credits: 10, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();
        CardInstanceState[] cards = [
            new(dzmzId, GatewayBatch3CardCodes.DzmzOptimizer, rig.Id, PlayerSide.Runner, faceUp: true),
            ..existingProgramIds.Select(id => new CardInstanceState(
                id,
                GatewayCardCodes.Buzzsaw,
                rig.Id,
                PlayerSide.Runner,
                faceUp: true)),
            new(firstProgramId, GatewayCardCodes.Buzzsaw, grip.Id, PlayerSide.Runner, faceUp: false),
            new(secondProgramId, GatewayCardCodes.Buzzsaw, grip.Id, PlayerSide.Runner, faceUp: false),
        ];
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [dzmzId, .. existingProgramIds])
                : zone.Id == grip.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [firstProgramId, secondProgramId])
                    : zone)
            .ToArray();
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            1,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            1,
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
                    usedMemory: existingProgramIds.Length)
                : player).ToArray(),
            existingProgramIds
                .Select(id => new ProgramMemoryState(id, runner.Id, memoryCost: 1))
                .ToArray(),
            defaults.Counters,
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
            rulesState: rules,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StateInvariantValidator.Validate(state, behavior, runtime);
        return new DzmzFixture(state, dzmzId, firstProgramId, secondProgramId);
    }

    private static GameState ReachCorpApproach(
        GameState started,
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime,
        CardInstanceId iceId)
    {
        GameState state = started;
        for (int guard = 0; guard < 64; guard++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            if (state.RunState is
                {
                    Step: RunTimingStep.ApproachPaidWindow,
                    CurrentIceId: CardInstanceId current,
                }
                && current == iceId
                && state.GetPlayer(pending.Owner).Side == PlayerSide.Corp)
            {
                return state;
            }

            if (state.RunState?.Step == RunTimingStep.JackOutDecision)
            {
                throw new Xunit.Sdk.XunitException("The Tread Lightly run reached jack-out before the rez window.");
            }

            LegalAction pass = Assert.Single(
                pending.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult applied = Apply(state, pass, behavior, runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        throw new Xunit.Sdk.XunitException("The Tread Lightly run did not reach the Corp approach window.");
    }

    private static GameState EndRunByJackOut(
        GameState source,
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime)
    {
        GameState state = source;
        for (int guard = 0; guard < 64 && state.RunState is not null; guard++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction action = state.RunState.Step == RunTimingStep.JackOutDecision
                ? Assert.Single(pending.LegalActions, value => value.Kind == ActionKind.JackOut)
                : Assert.Single(pending.LegalActions, value => value.Kind == ActionKind.PassPriority);
            ApplyResult applied = Apply(state, action, behavior, runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        Assert.Null(state.RunState);
        return state;
    }

    private static GameState ContinueToTurnAction(
        GameState source,
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime)
    {
        GameState state = source;
        for (int guard = 0; guard < 32 && state.PendingDecision?.Kind != DecisionKind.TurnAction; guard++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
            ApplyResult applied = Apply(state, pass, behavior, runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        Assert.Equal(DecisionKind.TurnAction, state.PendingDecision?.Kind);
        return state;
    }

    private static ApplyResult Apply(
        GameState state,
        LegalAction action,
        BehaviorCatalog behavior,
        RuntimeCardCatalog runtime)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            behavior,
            runtime);
    }

    private static LegalAction FinalizeInstallChoice(GameState state, CardInstanceId cardId) => Assert.Single(
        state.PendingDecision!.LegalActions,
        action => action.Kind == ActionKind.InstallCard
            && Assert.IsType<InstallCardParameters>(action.Parameters).CardId == cardId
            && Assert.IsType<InstallCardParameters>(action.Parameters).FinalizeSelection
            && Assert.IsType<InstallCardParameters>(action.Parameters).SelectedReplacementCardIds.Count == 0);

    private sealed record TreadFixture(
        GameState State,
        CardInstanceId EventId,
        CardInstanceId IceId);

    private sealed record DzmzFixture(
        GameState State,
        CardInstanceId DzmzId,
        CardInstanceId FirstProgramId,
        CardInstanceId SecondProgramId);
}
