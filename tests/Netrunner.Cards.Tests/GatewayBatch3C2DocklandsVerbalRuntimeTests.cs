using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Production-bound lifecycle evidence for the two C2 first-window cards
/// whose effects are exercised by the real run and turn reducers.  The
/// executable behavior catalog and a runtime catalog compiled through the
/// Gateway composition root are both injected into every reducer call.
/// </summary>
public sealed class GatewayBatch3C2DocklandsVerbalRuntimeTests
{
    private const string DocklandsHiddenDefinition = "fixture:batch3-c2-docklands-hidden";
    private const string VerbalStackDefinition = "fixture:batch3-c2-verbal-stack";

    [Fact]
    [Trait("GatewayCard", "30013")]
    public void DocklandsPassAddsOneActualRandomHqAccessOnlyOnTheFirstBreach()
    {
        RuntimeFixture fixture = BuildDocklandsFixture();
        Assert.Equal(2, fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.DocklandsPass).InstallCost);
        Assert.Equal(
            RuntimeCardClass.Hardware,
            fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.DocklandsPass).CardClass);
        Assert.Equal(
            PlayerSide.Runner,
            fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.DocklandsPass).Side);
        Assert.True(fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.DocklandsPass).IsUnique);

        GameEngine.RunStartResult firstStart = GameEngine.BeginRunWithEvents(
            fixture.State,
            fixture.Headquarters!.Value,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        List<EngineEvent> firstEvents = firstStart.Events.ToList();
        GameState firstBreach = ReachBreach(fixture, firstStart.State, firstEvents);

        BreachStartedEventPayload firstBreachEvent = Assert.IsType<BreachStartedEventPayload>(
            firstEvents.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(fixture.Headquarters!.Value, firstBreachEvent.ServerId);
        Assert.Equal(0, firstBreachEvent.CandidateCount);
        Assert.Equal(2, firstBreachEvent.RandomAccessLimit);
        Assert.Equal(2, firstBreach.RunState!.RandomAccessLimit);
        RunBreachAccessModifier modifier = Assert.Single(firstBreach.RunState.BreachAccessModifiers!);
        Assert.Equal(fixture.DocklandsId!.Value, modifier.SourceCardId);
        Assert.Equal(fixture.Headquarters!.Value, modifier.AttackedServerId);
        Assert.Equal(1, modifier.Delta);
        Assert.Equal("docklands-pass-first-hq-breach", modifier.ReasonKey);
        Assert.Equal(
            RunBreachAccessModifierSourceKind.FirstHeadquartersBreach,
            modifier.SourceKind);

        for (int access = 0; access < 2; access++)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(firstBreach.PendingDecision);
            LegalAction choose = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.ChooseBreachCandidate
                    && Assert.IsType<BreachCandidateParameters>(action.Parameters).Source
                        == BreachHiddenCandidateSource.CorpHand);

            BreachCandidateParameters parameters = Assert.IsType<BreachCandidateParameters>(choose.Parameters);
            Assert.Null(parameters.CardId);
            ApplyResult selected = Apply(fixture, firstBreach, choose);
            Assert.True(selected.Accepted, selected.Rejection?.Message);
            Assert.Contains(selected.Events, value => value.Kind == EngineEventKinds.CardAccessed);
            firstEvents.AddRange(selected.Events);
            firstBreach = selected.State;

            CardInstanceId accessed = firstBreach.RunState!.AccessedCards[^1];
            Assert.Contains(accessed, fixture.HiddenHandIds);
            Assert.False(firstBreach.GetCard(accessed).FaceUp);
            Assert.Equal(access + 1, firstBreach.RunState.AccessedCards.Count);
            Assert.Equal(1 - access, firstBreach.RunState.RandomAccessLimit);

            LegalAction noAccess = Assert.Single(
                Assert.IsType<PendingDecision>(firstBreach.PendingDecision).LegalActions,
                action => action.Kind == ActionKind.NoAccessAction);
            ApplyResult completedAccess = Apply(fixture, firstBreach, noAccess);
            Assert.True(completedAccess.Accepted, completedAccess.Rejection?.Message);
            firstEvents.AddRange(completedAccess.Events);
            firstBreach = completedAccess.State;
        }

        Assert.Null(firstBreach.RunState);
        Assert.Equal(
            [EngineEventKinds.CardAccessed, EngineEventKinds.CardAccessed],
            firstEvents
                .Where(value => value.Kind == EngineEventKinds.CardAccessed)
                .Select(value => value.Kind));
        Assert.Equal(
            [fixture.Headquarters!.Value],
            firstBreach.Turn.ThisTurnFacts!.HqBreachOccurrenceServerIds);
        Assert.Equal(
            1,
            FirstXCount(
                firstBreach.Turn.ThisTurnFacts!,
                fixture.DocklandsId!.Value,
                "docklands-pass-first-hq-breach"));

        GameEngine.RunStartResult secondStart = GameEngine.BeginRunWithEvents(
            firstBreach,
            fixture.Headquarters!.Value,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        List<EngineEvent> secondEvents = secondStart.Events.ToList();
        GameState secondBreach = ReachBreach(fixture, secondStart.State, secondEvents);

        BreachStartedEventPayload secondBreachEvent = Assert.IsType<BreachStartedEventPayload>(
            secondEvents.Single(value => value.Kind == EngineEventKinds.BreachStarted).Payload);
        Assert.Equal(1, secondBreachEvent.RandomAccessLimit);
        Assert.Equal(1, secondBreach.RunState!.RandomAccessLimit);
        Assert.DoesNotContain(
            secondBreach.RunState.BreachAccessModifiers ?? [],
            value => value.SourceKind == RunBreachAccessModifierSourceKind.FirstHeadquartersBreach);
        Assert.Equal(
            [fixture.Headquarters!.Value, fixture.Headquarters.Value],
            secondBreach.Turn.ThisTurnFacts!.HqBreachOccurrenceServerIds);
        Assert.Equal(
            1,
            FirstXCount(
                secondBreach.Turn.ThisTurnFacts!,
                fixture.DocklandsId!.Value,
                "docklands-pass-first-hq-breach"));
        Assert.Empty(StateInvariantValidator.Collect(
            secondBreach,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30034")]
    public void VerbalPlasticityReplacesTheFirstRealBasicDrawAndLeavesTheSecondNormal()
    {
        RuntimeFixture fixture = BuildVerbalFixture();
        Assert.Equal(3, fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.VerbalPlasticity).InstallCost);
        Assert.Equal(
            RuntimeCardClass.Resource,
            fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.VerbalPlasticity).CardClass);
        Assert.True(fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.VerbalPlasticity).IsUnique);
        Assert.Contains("genetics", fixture.RuntimeCatalog.GetSpec(GatewayBatch3CardCodes.VerbalPlasticity).Subtypes);

        GameState actionState = BeginRunnerAction(fixture);
        LegalAction firstDraw = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(fixture, actionState, firstDraw);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Single(opened.State.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds);
        LegalAction replacement = Assert.Single(
            opened.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseReplacement);

        ApplyResult replaced = Apply(fixture, opened.State, replacement);
        Assert.True(replaced.Accepted, replaced.Rejection?.Message);
        Assert.Equal(
            fixture.StackIds.Take(2),
            replaced.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Empty(replaced.State.GetZone(ZoneKind.RunnerStack).CardIds
            .Intersect(fixture.StackIds.Take(2)));
        Assert.Equal(
            1,
            FirstXCount(
                replaced.State.Turn.ThisTurnFacts!,
                fixture.VerbalId!.Value,
                "verbal-plasticity-first-basic-draw"));
        Assert.Empty(StateInvariantValidator.Collect(
            replaced.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));

        GameState afterPriority = Pass(Pass(replaced.State, fixture), fixture);
        LegalAction secondDraw = Assert.Single(
            afterPriority.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.DrawCard);
        ApplyResult second = Apply(fixture, afterPriority, secondDraw);
        Assert.True(second.Accepted, second.Rejection?.Message);
        Assert.Equal(
            fixture.StackIds.Take(3),
            second.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(
            second.State.ResolutionStack,
            frame => frame is EffectCheckpointFrame);
        Assert.Collection(
            second.State.Turn.ThisTurnFacts!.BasicDrawOccurrenceIds,
            _ => { },
            _ => { });
        Assert.Equal(
            1,
            FirstXCount(
                second.State.Turn.ThisTurnFacts!,
                fixture.VerbalId!.Value,
                "verbal-plasticity-first-basic-draw"));
        Assert.Empty(StateInvariantValidator.Collect(
            second.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    [Fact]
    [Trait("GatewayCard", "30034")]
    public void VerbalPlasticityShortStackStopsAfterAvailableCardsWithoutAReplacementDeadlock()
    {
        RuntimeFixture fixture = BuildVerbalFixture(stackCount: 1);
        GameState actionState = BeginRunnerAction(fixture);
        LegalAction draw = Assert.Single(
            actionState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.DrawCard);
        ApplyResult opened = Apply(fixture, actionState, draw);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        LegalAction replacement = Assert.Single(opened.State.PendingDecision!.LegalActions);
        Assert.Equal(ActionKind.UseReplacement, replacement.Kind);

        ApplyResult used = Apply(fixture, opened.State, replacement);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(fixture.StackIds, used.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Empty(used.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.DoesNotContain(used.State.ResolutionStack, frame => frame is EffectCheckpointFrame);
        Assert.Empty(StateInvariantValidator.Collect(
            used.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog));
    }

    private static GameState ReachBreach(
        RuntimeFixture fixture,
        GameState state,
        List<EngineEvent> events)
    {
        for (int guard = 0; guard < 8; guard++)
        {
            if (state.RunState?.Step == RunTimingStep.BreachCandidateSelection)
            {
                return state;
            }

            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, pass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            state = result.State;
        }

        throw new Xunit.Sdk.XunitException("The run did not reach the HQ breach candidate window.");
    }

    private static GameState BeginRunnerAction(RuntimeFixture fixture)
    {
        return GameEngine.BeginResolution(
            fixture.State,
            new TurnFrame(PlayerSide.Runner, fixture.State.Turn.TurnNumber, TurnFrameStage.AwaitingAction),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static ApplyResult Apply(RuntimeFixture fixture, GameState state, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);

    private static GameState Pass(GameState state, RuntimeFixture fixture)
    {
        LegalAction pass = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = Apply(fixture, state, pass);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static RuntimeFixture BuildDocklandsFixture()
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtimeCatalog = CreateRuntimeCatalog();
        GameState baseline = GameState.CreateNew(
            seed: 441301,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState hand = baseline.GetZone(ZoneKind.CorpHand);
        ServerState headquarters = baseline.Servers.Single(server => server.Kind == ServerKind.Headquarters);
        CardInstanceId docklandsId = new("card:batch3-c2-docklands");
        CardInstanceId[] hiddenHandIds =
        [
            new("card:batch3-c2-docklands-hidden-0"),
            new("card:batch3-c2-docklands-hidden-1"),
            new("card:batch3-c2-docklands-hidden-2"),
        ];
        CardInstanceState[] cards =
        [
            new(docklandsId, GatewayBatch3CardCodes.DocklandsPass, rig.Id, PlayerSide.Runner, faceUp: true),
            .. hiddenHandIds.Select(id => new CardInstanceState(
                id,
                new CardDefinitionId(DocklandsHiddenDefinition),
                hand.Id,
                PlayerSide.Corp,
                faceUp: false)),
        ];
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [docklandsId])
                : zone.Id == hand.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, hiddenHandIds)
                    : zone)
            .ToArray();
        GameState state = WithRunnerTurn(
            baseline,
            behaviorCatalog,
            runtimeCatalog,
            cards,
            zones);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return new RuntimeFixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            headquarters.Id,
            docklandsId,
            null,
            hiddenHandIds,
            []);
    }

    private static RuntimeFixture BuildVerbalFixture(int stackCount = 4)
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtimeCatalog = CreateRuntimeCatalog();
        GameState baseline = GameState.CreateNew(
            seed: 441302,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState stack = baseline.GetZone(ZoneKind.RunnerStack);
        CardInstanceId verbalId = new("card:batch3-c2-verbal");
        CardInstanceId[] stackIds = Enumerable.Range(0, stackCount)
            .Select(index => new CardInstanceId($"card:batch3-c2-verbal-stack-{index}"))
            .ToArray();
        CardInstanceState[] cards =
        [
            new(verbalId, GatewayBatch3CardCodes.VerbalPlasticity, rig.Id, PlayerSide.Runner, faceUp: true),
            .. stackIds.Select(id => new CardInstanceState(
                id,
                new CardDefinitionId(VerbalStackDefinition),
                stack.Id,
                PlayerSide.Runner,
                faceUp: false)),
        ];
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [verbalId])
                : zone.Id == stack.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, stackIds)
                    : zone)
            .ToArray();
        GameState state = WithRunnerTurn(
            baseline,
            behaviorCatalog,
            runtimeCatalog,
            cards,
            zones);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return new RuntimeFixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            null,
            null,
            verbalId,
            [],
            stackIds);
    }

    private static GameState WithRunnerTurn(
        GameState baseline,
        BehaviorCatalog behaviorCatalog,
        RuntimeCardCatalog runtimeCatalog,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones)
    {
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(runner.Id, runner.Side, credits: 10, clicks: 4, runner.AgendaPoints)
                : player)
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
        TurnHistoryState history = new(1, null, null, facts, null);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(
                PlayerSide.Runner,
                TurnPhase.Runner,
                baseline.Turn.TurnNumber,
                TurnStep.RunnerAction,
                history),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            resolutionStack: [],
            pendingDecision: null,
            actionSequence: baseline.ActionSequence,
            initialSeed: baseline.InitialSeed,
            runState: null,
            rulesState: baseline.RulesState,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
    }

    private static RuntimeCardCatalog CreateRuntimeCatalog()
    {
        NormalizedCardDefinition[] cards =
        [
            Card(
                GatewayBatch3CardCodes.DocklandsPass.Value,
                "Docklands Pass",
                "runner",
                "criminal",
                "hardware",
                cost: 2,
                uniqueness: true),
            Card(
                GatewayBatch3CardCodes.VerbalPlasticity.Value,
                "Verbal Plasticity",
                "runner",
                "neutral-runner",
                "resource",
                subtypes: ["Genetics"],
                cost: 3,
                uniqueness: true),
            Card(
                DocklandsHiddenDefinition,
                "Docklands Fixture Card",
                "corp",
                "neutral-corp",
                "operation",
                cost: 0),
            Card(
                VerbalStackDefinition,
                "Verbal Fixture Card",
                "runner",
                "neutral-runner",
                "event",
                cost: 0),
        ];
        NormalizedCardDefinition[] ordered = cards
            .OrderBy(card => card.Title, StringComparer.Ordinal)
            .ThenBy(card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] cardBytes = CardDataJson.SerializeCards(ordered);
        CardDataManifest manifest = new(
            CardDataValidator.CurrentManifestSchemaVersion,
            "https://example.invalid/gateway-c2-runtime-tests",
            new string('c', 40),
            "2026-09-04",
            "fixture-v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            CardDataJson.ComputeSha256Hex(cardBytes),
            "fixtures/NOTICE.txt",
            "NOASSERTION",
            "fixtures/cards.json",
            "fixture",
            "Gateway C2 runtime fixture",
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
        bool uniqueness = false,
        int? cost = null) =>
        new(
            code,
            title,
            side,
            faction,
            type,
            subtypes ?? [],
            string.Empty,
            "fixture",
            quantity: type == "identity" ? 1 : 3,
            deckLimit: type == "identity" ? 1 : 3,
            uniqueness,
            cost,
            strength: null,
            memoryCost: null,
            trashCost: null,
            agendaPoints: null,
            advancementRequirement: null,
            influenceCost: type == "identity" ? null : 0,
            minimumDeckSize: type == "identity" ? 40 : null,
            influenceLimit: type == "identity" ? 15 : null,
            baseLink: type == "identity" ? 0 : null);

    private sealed record RuntimeFixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        ServerId? Headquarters,
        CardInstanceId? DocklandsId,
        CardInstanceId? VerbalId,
        IReadOnlyList<CardInstanceId> HiddenHandIds,
        IReadOnlyList<CardInstanceId> StackIds);

    private static int FirstXCount(
        TurnFactsState facts,
        CardInstanceId sourceCardId,
        string reasonKey) => facts.FirstXUseKeys
            .Where(value => value.SourceCardId == sourceCardId
                && string.Equals(value.Key, reasonKey, StringComparison.Ordinal))
            .Select(value => value.Count)
            .SingleOrDefault();
}
