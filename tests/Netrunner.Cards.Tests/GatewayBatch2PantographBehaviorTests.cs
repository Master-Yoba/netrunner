using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Real-turn lifecycle evidence for Pantograph (30023).  The fixture is
/// composed from the production Gateway behavior declarations and a
/// self-contained runtime catalog containing the authenticated card codes and
/// printed values needed by these scenarios. It never dispatches by title or
/// by rules text, and it does not depend on an external content snapshot.
/// </summary>
public sealed class GatewayBatch2PantographBehaviorTests
{
    private static readonly CardDefinitionId Mayfly = new("30032");
    private static readonly CardDefinitionId DzmzOptimizer = new("30022");

    [Fact]
    [Trait("GatewayCard", "30023")]
    public void RealCorpScoreGainsOneThenInstallsAProgramWithPantographMemory()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            runnerCredits: 0,
            gripDefinitions: [GatewayBatch2CardCodes.Fermenter],
            installedProgramDefinitions:
            [
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
            ]);

        ApplyResult scoredResult = OpenCorpPantographTrigger(fixture);
        GameState scored = scoredResult.State;
        Assert.Equal(2, scored.GetPlayer(fixture.CorpId).AgendaPoints);
        Assert.Equal(1, scored.GetPlayer(fixture.RunnerId).Credits);
        Assert.Contains(scoredResult.Events, value => value.Kind == EngineEventKinds.AgendaScored);
        Assert.Single(
            scoredResult.Events,
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Target == fixture.RunnerId
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == 1);

        CardInstanceId fermenterId = fixture.GripCardIds.Single();
        GameState restored = AssertPrivateChoiceRoundTrip(fixture, scored, [fermenterId]);
        ApplyResult selected = Apply(fixture, restored, Choice(restored, fermenterId));
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, selected.State.PendingDecision?.Kind);
        Assert.Contains(fermenterId, selected.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(
            selected.State.Zones,
            zone => zone.Kind == ZoneKind.BehaviorSetAside && zone.CardIds.Contains(fermenterId));

        LegalAction finalize = Assert.Single(
            selected.State.PendingDecision!.LegalActions,
            action => action.Parameters is InstallCardParameters parameters
                && parameters.FinalizeSelection
                && parameters.SelectedReplacementCardIds.Count == 0);
        InstallCardParameters install = Assert.IsType<InstallCardParameters>(finalize.Parameters);
        Assert.Equal(1, install.CreditCost);

        ApplyResult completed = Apply(fixture, selected.State, finalize);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(0, completed.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(4, completed.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(ZoneKind.RunnerRig, completed.State.GetZone(completed.State.GetCard(fermenterId).ZoneId).Kind);
        TurnHistoryState history = Assert.IsType<TurnHistoryState>(completed.State.Turn.History);
        Assert.Contains(fermenterId, history.CorpThisTurn!.InstalledCardIds);
        Assert.DoesNotContain(fermenterId, history.RunnerThisTurn?.InstalledCardIds ?? []);
        Assert.DoesNotContain(fermenterId, history.RunnerLastTurn?.InstalledCardIds ?? []);
        Assert.Equal(5, completed.State.RulesState!.Players
            .Single(player => player.PlayerId == fixture.RunnerId).UsedMemory);
        Assert.Contains(
            completed.State.RulesState.Counters,
            counter => counter.CardId == fermenterId
                && counter.CounterName == "virus"
                && counter.Count == 1);
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            completed.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(1, installed.Cost);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        Assert.Empty(StateInvariantValidator.Collect(completed.State, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30023")]
    public void RealRunnerStealGainsOneThenInstallsAResourceThroughOrdinaryPayment()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Runner,
            runnerCredits: 0,
            gripDefinitions: [GatewayBatch2CardCodes.Cookbook]);

        ApplyResult stolenResult = OpenRunnerPantographTrigger(fixture);
        GameState stolen = stolenResult.State;
        Assert.Equal(2, stolen.GetPlayer(fixture.RunnerId).AgendaPoints);
        Assert.Equal(1, stolen.GetPlayer(fixture.RunnerId).Credits);
        Assert.Contains(stolenResult.Events, value => value.Kind == EngineEventKinds.AgendaStolen);
        Assert.Single(
            stolenResult.Events,
            value => value.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Target == fixture.RunnerId
                && Assert.IsType<CreditsAdjustedEventPayload>(value.Payload).Delta == 1);

        CardInstanceId cookbookId = fixture.GripCardIds.Single();
        GameState restored = AssertPrivateChoiceRoundTrip(fixture, stolen, [cookbookId]);
        ApplyResult completed = Apply(fixture, restored, Choice(restored, cookbookId));

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(0, completed.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(3, completed.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(ZoneKind.RunnerRig, completed.State.GetZone(completed.State.GetCard(cookbookId).ZoneId).Kind);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            completed.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(1, installed.Cost);
        Assert.Null(completed.State.RunState);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision?.Kind);
        Assert.Empty(StateInvariantValidator.Collect(completed.State, fixture.Behavior, fixture.Runtime));
    }

    [Theory]
    [InlineData("30007", 1)]
    [InlineData("30022", 2)]
    [InlineData("30009", 1)]
    [Trait("GatewayCard", "30023")]
    public void ChosenProgramHardwareOrResourceUsesItsPrintedInstallCost(
        string definitionCode,
        int expectedCost)
    {
        CardDefinitionId definitionId = new(definitionCode);
        int initialCredits = Math.Max(0, expectedCost - 1);
        IReadOnlyList<CardDefinitionId> installedPrograms = definitionId == GatewayBatch2CardCodes.Fermenter
            ? [
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
            ]
            : [];
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            runnerCredits: initialCredits,
            gripDefinitions: [definitionId],
            installedProgramDefinitions: installedPrograms);

        ApplyResult triggerResult = OpenCorpPantographTrigger(fixture);
        GameState trigger = triggerResult.State;
        Assert.Equal(initialCredits + 1, trigger.GetPlayer(fixture.RunnerId).Credits);
        CardInstanceId chosenId = fixture.GripCardIds.Single();
        ApplyResult completed = Apply(fixture, trigger, Choice(trigger, chosenId));
        GameState completedState = completed.State;
        if (completedState.PendingDecision?.Kind == DecisionKind.InstallChoice)
        {
            LegalAction finalize = Assert.Single(
                completedState.PendingDecision.LegalActions,
                action => action.Parameters is InstallCardParameters parameters
                    && parameters.FinalizeSelection
                    && parameters.SelectedReplacementCardIds.Count == 0);
            Assert.Equal(expectedCost, Assert.IsType<InstallCardParameters>(finalize.Parameters).CreditCost);
            completed = Apply(fixture, completedState, finalize);
            Assert.True(completed.Accepted, completed.Rejection?.Message);
            completedState = completed.State;
        }

        Assert.Equal(0, completedState.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(4, completedState.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(ZoneKind.RunnerRig, completedState.GetZone(completedState.GetCard(chosenId).ZoneId).Kind);
        CardInstalledEventPayload installed = Assert.IsType<CardInstalledEventPayload>(
            completed.Events.Single(value => value.Kind == EngineEventKinds.CardInstalled).Payload);
        Assert.Equal(expectedCost, installed.Cost);
        Assert.Empty(StateInvariantValidator.Collect(completedState, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30023")]
    public void ProgramReplacementUsesTheSharedIncrementalMemoryPipeline()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            runnerCredits: 0,
            gripDefinitions: [Mayfly],
            installedProgramDefinitions:
            [
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Buzzsaw,
            ]);
        ApplyResult triggerResult = OpenCorpPantographTrigger(fixture);
        GameState trigger = triggerResult.State;
        CardInstanceId mayflyId = fixture.GripCardIds.Single();
        ApplyResult selected = Apply(fixture, trigger, Choice(trigger, mayflyId));
        Assert.True(selected.Accepted, selected.Rejection?.Message);
        Assert.Equal(DecisionKind.InstallChoice, selected.State.PendingDecision?.Kind);

        LegalAction replace = selected.State.PendingDecision!.LegalActions
            .First(action => action.Parameters is InstallCardParameters parameters
                && !parameters.FinalizeSelection
                && parameters.SelectedReplacementCardIds.Count == 1);
        CardInstanceId replacementId = Assert.IsType<InstallCardParameters>(replace.Parameters)
            .SelectedReplacementCardIds.Single();
        ApplyResult replacementSelected = Apply(fixture, selected.State, replace);
        Assert.True(replacementSelected.Accepted, replacementSelected.Rejection?.Message);
        LegalAction finalize = Assert.Single(
            replacementSelected.State.PendingDecision!.LegalActions,
            action => action.Parameters is InstallCardParameters parameters
                && parameters.FinalizeSelection
                && parameters.SelectedReplacementCardIds.SequenceEqual([replacementId]));
        Assert.Equal(1, Assert.IsType<InstallCardParameters>(finalize.Parameters).CreditCost);

        ApplyResult completed = Apply(fixture, replacementSelected.State, finalize);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(0, completed.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(4, completed.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Equal(ZoneKind.RunnerRig, completed.State.GetZone(completed.State.GetCard(mayflyId).ZoneId).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, completed.State.GetZone(completed.State.GetCard(replacementId).ZoneId).Kind);
        Assert.Equal(5, completed.State.RulesState!.Players
            .Single(player => player.PlayerId == fixture.RunnerId).UsedMemory);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.ClickSpent);
        Assert.Empty(StateInvariantValidator.Collect(completed.State, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30023")]
    public void EmptyPantographSelectionDeclinesInstallButKeepsTheCreditGain()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            runnerCredits: 0,
            gripDefinitions: [GatewayBatch2CardCodes.Cookbook]);
        ApplyResult triggerResult = OpenCorpPantographTrigger(fixture);
        GameState trigger = triggerResult.State;
        CardInstanceId cookbookId = fixture.GripCardIds.Single();
        LegalAction skip = Assert.Single(
            trigger.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.ChoiceKey == "pantograph-install"
                && choice.CardIds.Count == 0);

        ApplyResult completed = Apply(fixture, trigger, skip);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(1, completed.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(4, completed.State.GetPlayer(fixture.RunnerId).Clicks);
        Assert.Contains(cookbookId, completed.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(completed.Events, value => value.Kind == EngineEventKinds.CardInstalled);
        Assert.Equal(DecisionKind.TurnTiming, completed.State.PendingDecision?.Kind);
        Assert.Empty(StateInvariantValidator.Collect(completed.State, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30023")]
    public void UnaffordableOrImpossibleGripCardsAreExcludedAfterTheCreditGain()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            runnerCredits: 0,
            gripDefinitions:
            [
                GatewayBatch2CardCodes.Fermenter,
                GatewayBatch2CardCodes.RedTeam,
                GatewayBatch1CardCodes.SureGamble,
            ]);
        ApplyResult triggerResult = OpenCorpPantographTrigger(fixture);
        GameState trigger = triggerResult.State;
        Assert.Equal(1, trigger.GetPlayer(fixture.RunnerId).Credits);

        CardInstanceId legal = fixture.GripCardIds[0];
        CardInstanceId unaffordable = fixture.GripCardIds[1];
        CardInstanceId impossible = fixture.GripCardIds[2];
        PendingDecision pending = Assert.IsType<PendingDecision>(trigger.PendingDecision);
        BehaviorChoiceParameters[] choices = pending.LegalActions
            .Select(action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters))
            .ToArray();
        Assert.Equal(2, choices.Length);
        Assert.Contains(choices, choice => choice.CardIds.SequenceEqual([legal]));
        Assert.Contains(choices, choice => choice.CardIds.Count == 0);
        Assert.DoesNotContain(choices, choice => choice.CardIds.Contains(unaffordable));
        Assert.DoesNotContain(choices, choice => choice.CardIds.Contains(impossible));
        Assert.DoesNotContain(
            pending.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.CardIds.Contains(unaffordable));

        Observation corpObservation = ObservationBuilder.Build(
            trigger,
            PlayerSide.Corp,
            fixture.Behavior);
        Assert.Empty(corpObservation.LegalActions);
        string corpJson = EngineJson.Serialize(corpObservation);
        Assert.DoesNotContain(legal.Value, corpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(unaffordable.Value, corpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(impossible.Value, corpJson, StringComparison.Ordinal);
        Assert.Empty(StateInvariantValidator.Collect(trigger, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30023")]
    public void NoInstallableGripCardsCompleteTheOptionalBranchWithoutAPrompt()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            runnerCredits: 0,
            gripDefinitions:
            [
                GatewayBatch2CardCodes.RedTeam,
                GatewayBatch1CardCodes.SureGamble,
            ]);
        ApplyResult scored = ScorePantograph(fixture);

        Assert.True(scored.Accepted, scored.Rejection?.Message);
        Assert.Equal(1, scored.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.NotEqual(DecisionKind.BehaviorChoice, scored.State.PendingDecision?.Kind);
        Assert.Equal(DecisionKind.TurnTiming, scored.State.PendingDecision?.Kind);
        Assert.DoesNotContain(scored.Events, value => value.Kind == EngineEventKinds.CardInstalled);
        Assert.Empty(StateInvariantValidator.Collect(scored.State, fixture.Behavior, fixture.Runtime));
    }

    private static ApplyResult OpenCorpPantographTrigger(Fixture fixture)
    {
        ApplyResult scored = ScorePantograph(fixture);
        Assert.True(scored.Accepted, scored.Rejection?.Message);
        Assert.Equal(DecisionKind.BehaviorChoice, scored.State.PendingDecision?.Kind);
        Assert.Equal(fixture.RunnerId, scored.State.PendingDecision?.Owner);
        return scored;
    }

    private static ApplyResult ScorePantograph(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            PlayerSide.Corp,
            fixture.Behavior,
            fixture.Runtime);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision?.Kind);
        state = ApplyPass(fixture, state).State;
        state = ApplyPass(fixture, state).State;
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(state.ResolutionStack.Single()).Stage);
        LegalAction score = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ScoreAgenda);
        return Apply(fixture, state, score);
    }

    private static ApplyResult OpenRunnerPantographTrigger(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            PlayerSide.Runner,
            fixture.Behavior,
            fixture.Runtime);
        for (int step = 0; state.PendingDecision?.Kind != DecisionKind.TurnAction; step++)
        {
            Assert.True(step < 8, "Runner turn did not reach its action window.");
            state = ApplyPass(fixture, state).State;
        }

        LegalAction startRun = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == fixture.ServerId);
        ApplyResult started = Apply(fixture, state, startRun);
        Assert.True(started.Accepted, started.Rejection?.Message);
        state = started.State;
        for (int step = 0; state.RunState?.Step != RunTimingStep.BreachCandidateSelection; step++)
        {
            Assert.True(step < 16, "Run did not reach breach candidate selection.");
            state = ApplyPass(fixture, state).State;
        }

        LegalAction access = Assert.Single(
            state.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(action.Parameters).CardId == fixture.AgendaId);
        ApplyResult accessed = Apply(fixture, state, access);
        Assert.True(accessed.Accepted, accessed.Rejection?.Message);
        LegalAction noAction = Assert.Single(
            accessed.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.NoAccessAction);
        ApplyResult stolen = Apply(fixture, accessed.State, noAction);
        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        Assert.Equal(DecisionKind.BehaviorChoice, stolen.State.PendingDecision?.Kind);
        Assert.Equal(fixture.RunnerId, stolen.State.PendingDecision?.Owner);
        return stolen;
    }

    private static GameState AssertPrivateChoiceRoundTrip(
        Fixture fixture,
        GameState state,
        IReadOnlyList<CardInstanceId> expectedCandidate)
    {
        Assert.Equal(DecisionKind.BehaviorChoice, state.PendingDecision?.Kind);
        Assert.Equal(
            expectedCandidate,
            state.PendingDecision!.LegalActions
                .SelectMany(action => action.Parameters is BehaviorChoiceParameters choice
                    ? choice.CardIds
                    : [])
                .Distinct()
                .ToArray());
        Observation corpObservation = ObservationBuilder.Build(state, PlayerSide.Corp, fixture.Behavior);
        Assert.Empty(corpObservation.LegalActions);
        string corpJson = EngineJson.Serialize(corpObservation);
        foreach (CardInstanceId cardId in expectedCandidate)
        {
            Assert.DoesNotContain(cardId.Value, corpJson, StringComparison.Ordinal);
        }

        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, fixture.Behavior, fixture.Runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
        return restored;
    }

    private static LegalAction Choice(GameState state, CardInstanceId cardId) => Assert.Single(
        state.PendingDecision!.LegalActions,
        action => action.Parameters is BehaviorChoiceParameters choice
            && choice.ChoiceKey == "pantograph-install"
            && choice.CardIds.SequenceEqual([cardId]));

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            fixture.Behavior,
            fixture.Runtime);
    }

    private static ApplyResult ApplyPass(Fixture fixture, GameState state)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return Apply(fixture, state, pass);
    }

    private static Fixture CreateFixture(
        PlayerSide activeSide,
        int runnerCredits,
        IReadOnlyList<CardDefinitionId> gripDefinitions,
        IReadOnlyList<CardDefinitionId>? installedProgramDefinitions = null)
    {
        RuntimeCardCatalog runtime = CreateRuntimeCatalog();
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        GameState baseline = GameState.CreateNew(
            seed: 94423,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);

        var allocator = new StableIdAllocator(baseline.Allocator);
        ServerId remoteId = allocator.NextServerId();
        ZoneId remoteIceId = allocator.NextZoneId();
        ZoneId remoteRootId = allocator.NextZoneId();
        ServerState remote = new(remoteId, ServerKind.Remote, remoteIceId, remoteRootId);
        ZoneState runnerRig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState runnerGrip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState corpDeck = baseline.GetZone(ZoneKind.CorpDeck);
        Dictionary<ZoneId, List<CardInstanceId>> zoneCards = baseline.Zones
            .ToDictionary(zone => zone.Id, zone => zone.CardIds.ToList());
        zoneCards.Add(remoteIceId, []);
        zoneCards.Add(remoteRootId, []);
        var cards = new List<CardInstanceState>();
        var gripCardIds = new List<CardInstanceId>();
        var installedProgramIds = new List<CardInstanceId>();

        CardInstanceId agendaId = new("card:pantograph-agenda");
        cards.Add(new CardInstanceState(
            agendaId,
            GatewayCardCodes.OffworldOffice,
            remoteRootId,
            PlayerSide.Corp,
            faceUp: false,
            advancementCounters: 4));
        zoneCards[remoteRootId].Add(agendaId);

        CardInstanceId pantographId = new("card:pantograph-source");
        cards.Add(new CardInstanceState(
            pantographId,
            GatewayBatch2CardCodes.Pantograph,
            runnerRig.Id,
            PlayerSide.Runner,
            faceUp: true));
        zoneCards[runnerRig.Id].Add(pantographId);

        for (int index = 0; index < gripDefinitions.Count; index++)
        {
            CardInstanceId cardId = new($"card:pantograph-grip-{index}");
            cards.Add(new CardInstanceState(
                cardId,
                gripDefinitions[index],
                runnerGrip.Id,
                PlayerSide.Runner));
            zoneCards[runnerGrip.Id].Add(cardId);
            gripCardIds.Add(cardId);
        }

        for (int index = 0; index < (installedProgramDefinitions ?? []).Count; index++)
        {
            CardInstanceId cardId = new($"card:pantograph-installed-{index}");
            CardDefinitionId definitionId = installedProgramDefinitions![index];
            cards.Add(new CardInstanceState(
                cardId,
                definitionId,
                runnerRig.Id,
                PlayerSide.Runner,
                faceUp: true));
            zoneCards[runnerRig.Id].Add(cardId);
            installedProgramIds.Add(cardId);
        }

        CardInstanceId corpDeckCardId = new("card:pantograph-corp-deck-filler");
        cards.Add(new CardInstanceState(
            corpDeckCardId,
            GatewayBatch1CardCodes.HedgeFund,
            corpDeck.Id,
            PlayerSide.Corp));
        zoneCards[corpDeck.Id].Add(corpDeckCardId);

        ZoneState[] zones = baseline.Zones
            .Select(zone => zoneCards.TryGetValue(zone.Id, out List<CardInstanceId>? ids)
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids)
                : zone)
            .Append(new ZoneState(
                remoteIceId,
                ZoneKind.ServerIce,
                PlayerSide.Corp,
                remoteId,
                zoneCards[remoteIceId]))
            .Append(new ZoneState(
                remoteRootId,
                ZoneKind.ServerRoot,
                PlayerSide.Corp,
                remoteId,
                zoneCards[remoteRootId]))
            .ToArray();
        PlayerId corpId = baseline.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runnerId = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Corp
                ? new PlayerState(player.Id, player.Side, credits: 5, clicks: 3)
                : new PlayerState(player.Id, player.Side, credits: runnerCredits, clicks: 4))
            .ToArray();

        RulesState defaults = RulesState.CreateDefault(players);
        ProgramMemoryState[] programs = installedProgramIds
            .Select(cardId => new ProgramMemoryState(
                cardId,
                runnerId,
                runtime.GetSpec(cards.Single(card => card.Id == cardId).DefinitionId).MemoryUnits))
            .ToArray();
        PlayerRulesState[] rulePlayers = defaults.Players
            .Select(player => new PlayerRulesState(
                player.PlayerId,
                player.BaseHandSize,
                player.CoreDamage,
                player.Tags,
                player.BaseMemory,
                player.PlayerId == runnerId ? programs.Sum(program => program.MemoryCost) : player.UsedMemory))
            .ToArray();
        RulesState rules = new(
            defaults.Version,
            rulePlayers,
            programs,
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
            baseline.Servers.Append(remote).ToArray(),
            new TurnState(activeSide, activeSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner, 1),
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint,
            rulesState: rules);
        StateInvariantValidator.Validate(state, behavior, runtime);
        return new Fixture(state, behavior, runtime, remoteId, agendaId, pantographId, corpId, runnerId, gripCardIds);
    }

    private static RuntimeCardCatalog CreateRuntimeCatalog() =>
        new RuntimeCardCatalogBuilder("test:gateway-pantograph-runtime")
            // Authenticated System Gateway printed values, keyed only by code.
            .Add(new CardRuntimeSpec(
                GatewayCardCodes.OffworldOffice,
                PlayerSide.Corp,
                RuntimeCardClass.Agenda,
                agendaPoints: 2,
                advancementRequirement: 4,
                subtypes: ["expansion"]))
            .Add(new CardRuntimeSpec(
                GatewayBatch1CardCodes.HedgeFund,
                PlayerSide.Corp,
                RuntimeCardClass.Operation,
                playCost: 5,
                subtypes: ["transaction"]))
            .Add(new CardRuntimeSpec(
                GatewayCardCodes.Buzzsaw,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 4,
                memoryUnits: 1,
                subtypes: ["icebreaker", "decoder"]))
            .Add(new CardRuntimeSpec(
                GatewayBatch2CardCodes.Fermenter,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 1,
                subtypes: ["virus"]))
            .Add(new CardRuntimeSpec(
                DzmzOptimizer,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 2))
            .Add(new CardRuntimeSpec(
                Mayfly,
                PlayerSide.Runner,
                RuntimeCardClass.Program,
                installCost: 1,
                memoryUnits: 2,
                subtypes: ["icebreaker", "ai"]))
            .Add(new CardRuntimeSpec(
                GatewayBatch2CardCodes.Pantograph,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 2,
                subtypes: ["console"],
                unique: true,
                console: true,
                uniquenessKey: "pantograph",
                consoleKey: "runner-console"))
            .Add(new CardRuntimeSpec(
                GatewayBatch2CardCodes.Cookbook,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 1,
                subtypes: ["virtual"],
                unique: true,
                uniquenessKey: "cookbook"))
            .Add(new CardRuntimeSpec(
                GatewayBatch2CardCodes.RedTeam,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 5,
                subtypes: ["job"]))
            .Add(new CardRuntimeSpec(
                GatewayBatch1CardCodes.SureGamble,
                PlayerSide.Runner,
                RuntimeCardClass.Event,
                playCost: 5))
            .Build();

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog Behavior,
        RuntimeCardCatalog Runtime,
        ServerId ServerId,
        CardInstanceId AgendaId,
        CardInstanceId PantographId,
        PlayerId CorpId,
        PlayerId RunnerId,
        IReadOnlyList<CardInstanceId> GripCardIds);
}
