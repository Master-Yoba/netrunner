using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch2RunCostBehaviorSpecTests
{
    [Fact]
    public void RunAndCostDefinitionsAreComposedInPrintedCodeOrder()
    {
        Assert.Equal(
            new[]
            {
                GatewayBatch2CardCodes.Carmen,
                GatewayBatch2CardCodes.Marjanah,
                GatewayBatch2CardCodes.RedTeam,
                GatewayBatch2CardCodes.Conduit,
            },
            GatewayBatch2RunCostBehaviorSpecs.AssignedDefinitionIds);
        Assert.Equal(
            GatewayBatch2RunCostBehaviorSpecs.AssignedDefinitionIds,
            GatewayBatch2RunCostBehaviorSpecs.GenericSpecs.Select(static definition => definition.DefinitionId));

        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        Assert.All(
            GatewayBatch2RunCostBehaviorSpecs.AssignedDefinitionIds,
            id => Assert.Contains(id, catalog.AllDefinitionIds));
    }

    [Fact]
    public void ConduitDeclaresSuccessfulRndCounterAndTypedSnapshotRun()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch2CardCodes.Conduit);
        BehaviorTrigger trigger = Assert.Single(definition.Triggers);
        Assert.Equal("conduit-successful-rnd", trigger.Key);
        Assert.Equal(OccurrenceKind.RunEnded, trigger.Occurrence);
        Assert.Equal(TriggerMode.Optional, trigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Controller, trigger.Scope);
        Assert.Equal(RunOutcomeRequirement.Successful, trigger.RunOutcomeRequirement);
        Assert.Equal(ServerKind.RAndD, trigger.ServerKindRequirement);
        BehaviorInstruction counter = Assert.Single(trigger.Instructions);
        Assert.Equal(BehaviorInstructionKind.AddCounter, counter.Kind);
        Assert.Equal(1, counter.Amount);
        Assert.Equal("virus", counter.Argument);

        BehaviorPaidAbility ability = Assert.Single(definition.PaidAbilities);
        Assert.Equal(BehaviorAbilityActivation.ClickAction, ability.Activation);
        Assert.Equal(BehaviorAbilityTiming.ActionPhase, ability.Timing);
        Assert.Equal((BehaviorCostKind.Clicks, 1), SingleCost(ability));
        BehaviorInstruction start = Assert.Single(ability.Instructions);
        Assert.Equal(BehaviorInstructionKind.StartRun, start.Kind);
        Assert.Equal(
            new BehaviorRunOperand(
                ServerKind.RAndD,
                sourceCounterName: "virus",
                breachAccessDeltaPerCounter: 1,
                breachAccessReasonKey: "conduit-virus-access"),
            start.RunOperand);

        string json = EngineJson.Serialize(definition);
        CardBehaviorDefinition restored = EngineJson.Deserialize<CardBehaviorDefinition>(json);
        Assert.Equal(definition.DefinitionId, restored.DefinitionId);
        Assert.Equal(definition.Triggers, restored.Triggers);
        Assert.Equal(definition.PaidAbilities, restored.PaidAbilities);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    public void RedTeamDeclaresCentralRunsHistoryConstraintAndSelfPayout()
    {
        CardBehaviorDefinition definition = Definition(GatewayBatch2CardCodes.RedTeam);
        Assert.Equal(PlayerSide.Runner, definition.Controller);
        Assert.Equal(
            ["red-team-install", "red-team-empty", "red-team-successful-run"],
            definition.Triggers.Select(static trigger => trigger.Key));
        Assert.Equal(OccurrenceKind.CardInstalled, definition.Triggers[0].Occurrence);
        Assert.Equal(BehaviorInstructionKind.LoadHostedCredits, Assert.Single(definition.Triggers[0].Instructions).Kind);
        Assert.Equal(12, definition.Triggers[0].Instructions[0].Amount);
        Assert.Equal(OccurrenceKind.HostedCreditPoolEmptied, definition.Triggers[1].Occurrence);
        Assert.Equal(BehaviorInstructionKind.TrashSelf, Assert.Single(definition.Triggers[1].Instructions).Kind);

        BehaviorTrigger payout = definition.Triggers[2];
        Assert.Equal(BehaviorTriggerScope.Self, payout.Scope);
        Assert.Equal(RunOutcomeRequirement.Successful, payout.RunOutcomeRequirement);
        Assert.Equal(BehaviorInstructionKind.TakeHostedCredits, Assert.Single(payout.Instructions).Kind);
        Assert.Equal(3, payout.Instructions[0].Amount);

        Assert.Equal(
            [ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives],
            definition.PaidAbilities.Select(static ability => Assert.Single(ability.Instructions).RunOperand!.ServerKind));
        Assert.All(definition.PaidAbilities, ability =>
        {
            Assert.Equal(BehaviorAbilityUsageLimit.Unlimited, ability.UsageLimit);
            Assert.Equal((BehaviorCostKind.Clicks, 1), SingleCost(ability));
            Assert.True(ability.Instructions[^1].RunOperand!.RequireNotRunThisTurn);
        });
    }

    [Fact]
    public void CarmenAndMarjanahBindBreakerFamiliesAndConditionalCostModifiers()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.True(catalog.TryGetIcebreaker(GatewayBatch2CardCodes.Carmen, out IcebreakerBehaviorSpec? carmenBreaker));
        Assert.Equal(2, carmenBreaker.BaseStrength);
        Assert.Equal(IceType.Sentry, carmenBreaker.InterfaceType);
        Assert.Equal(1, carmenBreaker.BreakCost);
        Assert.Equal(1, carmenBreaker.BreakLimit);
        Assert.Equal(2, carmenBreaker.PumpCost);
        Assert.Equal(3, carmenBreaker.PumpStrength);
        BehaviorCostModifier carmenModifier = Assert.Single(Definition(GatewayBatch2CardCodes.Carmen).CostModifiers!);
        Assert.Equal(BehaviorCostModifierKind.RunnerInstallCreditCost, carmenModifier.Kind);
        Assert.Equal(-2, carmenModifier.Delta);
        Assert.Equal("carmen-successful-run-install", carmenModifier.ReasonKey);

        Assert.True(catalog.TryGetIcebreaker(GatewayBatch2CardCodes.Marjanah, out IcebreakerBehaviorSpec? marjanahBreaker));
        Assert.Equal(1, marjanahBreaker.BaseStrength);
        Assert.Equal(IceType.Barrier, marjanahBreaker.InterfaceType);
        Assert.Equal(2, marjanahBreaker.BreakCost);
        Assert.Equal(1, marjanahBreaker.BreakLimit);
        Assert.Equal(1, marjanahBreaker.PumpCost);
        Assert.Equal(1, marjanahBreaker.PumpStrength);
        BehaviorCostModifier marjanahModifier = Assert.Single(Definition(GatewayBatch2CardCodes.Marjanah).CostModifiers!);
        Assert.Equal(BehaviorCostModifierKind.IcebreakerBreakCreditCost, marjanahModifier.Kind);
        Assert.Equal(-1, marjanahModifier.Delta);
        Assert.Equal("marjanah-successful-run-break", marjanahModifier.ReasonKey);
    }

    [Fact]
    public void ConduitRealClickRunSnapshotsVirusAndFiltersAdditionalRndAccesses()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId conduitId = new("card:batch2-conduit-runtime");
        CardInstanceId[] deckCards =
        [
            new("card:batch2-conduit-rd-0"),
            new("card:batch2-conduit-rd-1"),
            new("card:batch2-conduit-rd-2"),
        ];
        GameState state = BuildRunnerState(
            catalog,
            conduitId,
            GatewayBatch2CardCodes.Conduit,
            ZoneKind.RunnerRig,
            counters: [new CardCounterState(conduitId, "virus", 2)],
            corpDeckCards: deckCards,
            runnerCredits: 10);
        GameState actionState = BeginRunnerAction(state, catalog);
        PendingDecision actionDecision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        LegalAction use = Assert.Single(
            actionDecision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == conduitId);

        ApplyResult started = Apply(actionState, use, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        RunBreachAccessModifier modifier = Assert.Single(started.State.RunState!.BreachAccessModifiers!);
        Assert.Equal(conduitId, modifier.SourceCardId);
        Assert.Equal(2, modifier.Delta);
        Assert.Equal("conduit-virus-access", modifier.ReasonKey);

        List<EngineEvent> events = started.Events.ToList();
        GameState cursor = started.State;
        for (int step = 0;
            step < 32
            && cursor.PendingDecision is not null
            && !cursor.PendingDecision.LegalActions.Any(action => action.Kind == ActionKind.ChooseBreachCandidate);
            step++)
        {
            ApplyResult passed = Pass(cursor, catalog);
            Assert.True(passed.Accepted, passed.Rejection?.Message);
            events.AddRange(passed.Events);
            cursor = passed.State;
        }

        Assert.Contains(
            Assert.IsType<PendingDecision>(cursor.PendingDecision).LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate);
        for (int access = 0; access < deckCards.Length; access++)
        {
            PendingDecision breachDecision = Assert.IsType<PendingDecision>(cursor.PendingDecision);
            LegalAction choose = Assert.Single(
                breachDecision.LegalActions,
                action => action.Kind == ActionKind.ChooseBreachCandidate
                    && Assert.IsType<BreachCandidateParameters>(action.Parameters).Source
                        == BreachHiddenCandidateSource.CorpDeck);
            ApplyResult selected = Apply(cursor, choose, catalog);
            Assert.True(selected.Accepted, selected.Rejection?.Message);
            events.AddRange(selected.Events);
            cursor = selected.State;

            LegalAction noAccess = Assert.Single(
                Assert.IsType<PendingDecision>(cursor.PendingDecision).LegalActions,
                action => action.Kind == ActionKind.NoAccessAction);
            ApplyResult completedAccess = Apply(cursor, noAccess, catalog);
            Assert.True(completedAccess.Accepted, completedAccess.Rejection?.Message);
            events.AddRange(completedAccess.Events);
            cursor = completedAccess.State;
        }

        PendingDecision triggerDecision = Assert.IsType<PendingDecision>(cursor.PendingDecision);
        LegalAction useTrigger = Assert.Single(
            triggerDecision.LegalActions,
            action => action.Kind == ActionKind.UseTrigger);
        ApplyResult acceptedTrigger = Apply(cursor, useTrigger, catalog);
        Assert.True(acceptedTrigger.Accepted, acceptedTrigger.Rejection?.Message);
        events.AddRange(acceptedTrigger.Events);
        cursor = acceptedTrigger.State;

        Assert.Null(cursor.RunState);
        Assert.Equal(deckCards.Length, events.Count(@event => @event.Kind == EngineEventKinds.CardAccessed));
        Assert.Equal(3, CounterCount(cursor, conduitId, "virus"));
        Assert.Empty(StateInvariantValidator.Collect(cursor, catalog));

        string json = EngineJson.Serialize(started.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(started.State), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void RedTeamRealCentralRunPaysFromItsPoolAndRemovesUsedServerAbility()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId redTeamId = new("card:batch2-red-team-runtime");
        GameState state = BuildRunnerState(
            catalog,
            redTeamId,
            GatewayBatch2CardCodes.RedTeam,
            ZoneKind.RunnerRig,
            runnerCredits: 5);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        state = ResolveOccurrence(
            state,
            runner,
            new GameplayOccurrenceEffect(OccurrenceKind.CardInstalled, runner, redTeamId),
            catalog);
        Assert.Equal(12, CounterCount(state, redTeamId, "credit"));

        GameState actionState = BeginRunnerAction(state, catalog);
        PendingDecision actionDecision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        LegalAction hqRun = Assert.Single(
            actionDecision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == "red-team-run-hq");
        Assert.Equal(
            ["red-team-run-hq", "red-team-run-rnd", "red-team-run-archives"],
            actionDecision.LegalActions
                .Where(action => action.Kind == ActionKind.UseAbility)
                .Select(action => Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey));

        ApplyResult started = Apply(actionState, hqRun, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        GameState cursor = started.State;
        List<EngineEvent> events = started.Events.ToList();
        for (int step = 0; step < 32 && cursor.RunState is not null; step++)
        {
            ApplyResult passed = Pass(cursor, catalog);
            Assert.True(passed.Accepted, passed.Rejection?.Message);
            events.AddRange(passed.Events);
            cursor = passed.State;
        }

        Assert.Null(cursor.RunState);
        Assert.Equal(8, cursor.GetPlayer(runner).Credits);
        Assert.Equal(9, CounterCount(cursor, redTeamId, "credit"));
        Assert.Contains(
            cursor.Turn.ThisTurnFacts!.AttemptedRunServerIds,
            id => cursor.GetServer(id).Kind == ServerKind.Headquarters);
        Assert.DoesNotContain(
            Assert.IsType<PendingDecision>(cursor.PendingDecision).LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == "red-team-run-hq");
        Assert.Contains(events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.Empty(StateInvariantValidator.Collect(cursor, catalog));

        GameState empty = BuildRunnerState(
            catalog,
            new CardInstanceId("card:batch2-red-team-empty"),
            GatewayBatch2CardCodes.RedTeam,
            ZoneKind.RunnerRig,
            runnerCredits: 5);
        CardInstanceId emptyId = empty.Cards.Single().Id;
        empty = ResolveOccurrence(
            empty,
            empty.Players.Single(player => player.Side == PlayerSide.Runner).Id,
            new GameplayOccurrenceEffect(
                OccurrenceKind.HostedCreditPoolEmptied,
                empty.Players.Single(player => player.Side == PlayerSide.Runner).Id,
                emptyId),
            catalog);
        Assert.Equal(ZoneKind.RunnerHeap, empty.GetZone(empty.GetCard(emptyId).ZoneId).Kind);
    }

    [Fact]
    public void RedTeamRealAbilitiesRespectEveryCentralServerAndExactRunSource()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId redTeamId = new("card:batch2-red-team-central-servers");
        GameState state = BuildRunnerState(
            catalog,
            redTeamId,
            GatewayBatch2CardCodes.RedTeam,
            ZoneKind.RunnerRig,
            runnerCredits: 5,
            counters: [CardCounterState.CreateHostedCredit(redTeamId, 12)]);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        // A basic run has no Red Team source, so the self-scoped payout must
        // not fire even though it is successful.
        ServerId headquarters = state.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        GameState otherSourceRun = DriveRunToEnd(
            GameEngine.BeginRunWithEvents(state, headquarters, catalog).State,
            catalog);
        Assert.Null(otherSourceRun.PendingDecision);
        Assert.Empty(otherSourceRun.ResolutionStack);
        Assert.Equal(12, CounterCount(otherSourceRun, redTeamId, "credit"));
        Assert.Contains(
            otherSourceRun.Turn.ThisTurnFacts!.AttemptedRunServerIds,
            id => otherSourceRun.GetServer(id).Kind == ServerKind.Headquarters);

        GameState actionState = BeginRunnerAction(otherSourceRun, catalog);
        Assert.DoesNotContain(
            actionState.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == "red-team-run-hq");
        Assert.Contains(
            actionState.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == "red-team-run-rnd");

        foreach ((string key, ServerKind kind) in new[]
        {
            ("red-team-run-rnd", ServerKind.RAndD),
            ("red-team-run-archives", ServerKind.Archives),
        })
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
            LegalAction use = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.UseAbility
                    && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == key);
            ApplyResult started = Apply(actionState, use, catalog);
            Assert.True(started.Accepted, started.Rejection?.Message);
            List<EngineEvent> runEvents = [];
            runEvents.AddRange(started.Events);
            actionState = DriveRunToEnd(started.State, catalog, events: runEvents);
            Assert.Null(actionState.RunState);
            Assert.True(
                CounterCount(actionState, redTeamId, "credit") == (kind == ServerKind.RAndD ? 9 : 6),
                $"{key}: {string.Join(',', runEvents.Select(value => value.Kind))}");

            actionState = OpenRunnerAction(actionState, catalog);
            Assert.DoesNotContain(
                actionState.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.UseAbility
                    && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == key);
        }

        Assert.Equal(6, CounterCount(actionState, redTeamId, "credit"));
        Assert.Contains(
            actionState.Turn.ThisTurnFacts!.AttemptedRunServerIds,
            id => actionState.GetServer(id).Kind == ServerKind.Archives);
        Assert.DoesNotContain(
            Assert.IsType<PendingDecision>(actionState.PendingDecision).LegalActions,
            action => action.Kind == ActionKind.UseAbility);
        Assert.Equal(11, actionState.GetPlayer(runner).Credits);
        Assert.Empty(StateInvariantValidator.Collect(actionState, catalog));
    }

    [Fact]
    public void RedTeamSuccessfulRunEmptiesPoolAndTrashesItselfThroughRealTriggers()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId redTeamId = new("card:batch2-red-team-empty-real");
        GameState state = BuildRunnerState(
            catalog,
            redTeamId,
            GatewayBatch2CardCodes.RedTeam,
            ZoneKind.RunnerRig,
            runnerCredits: 5,
            counters: [CardCounterState.CreateHostedCredit(redTeamId, 3)]);
        GameState actionState = BeginRunnerAction(state, catalog);
        PendingDecision decision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        LegalAction run = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).AbilityKey == "red-team-run-hq");

        ApplyResult started = Apply(actionState, run, catalog);
        Assert.True(started.Accepted, started.Rejection?.Message);
        GameState ended = DriveRunToEnd(started.State, catalog);

        Assert.Null(ended.RunState);
        Assert.Equal(ZoneKind.RunnerHeap, ended.GetZone(ended.GetCard(redTeamId).ZoneId).Kind);
        Assert.Equal(0, CounterCount(ended, redTeamId, "credit"));
        Assert.Empty(StateInvariantValidator.Collect(ended, catalog));
    }

    [Fact]
    public void CarmenRealInstallUsesPrintedCostBeforeAndReducedCostAfterSuccessfulRun()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRunCostRuntimeCatalog();

        CardInstanceId beforeId = new("card:batch2-carmen-before");
        GameState before = BuildRunnerState(
            catalog,
            beforeId,
            GatewayBatch2CardCodes.Carmen,
            ZoneKind.RunnerGrip,
            runnerCredits: 5,
            runtimeCardCatalog: runtime);
        GameState beforePrompt = BeginRunnerAction(before, catalog, runtime);
        LegalAction beforeInstall = Assert.Single(
            Assert.IsType<PendingDecision>(beforePrompt.PendingDecision).LegalActions,
            action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters beforeParameters = Assert.IsType<InstallCardParameters>(beforeInstall.Parameters);
        Assert.Equal(5, beforeParameters.CreditCost);
        Assert.Null(beforeParameters.CostQuote);
        Assert.Contains("creditCost", EngineJson.Serialize(ObservationBuilder.Build(
            beforePrompt,
            PlayerSide.Runner,
            catalog)), StringComparison.Ordinal);

        ApplyResult beforeInstalled = Apply(beforePrompt, beforeInstall, catalog, runtime);
        Assert.True(beforeInstalled.Accepted, beforeInstalled.Rejection?.Message);
        Assert.Equal(0, beforeInstalled.State.GetPlayer(beforeInstalled.State.Players.Single(player => player.Side == PlayerSide.Runner).Id).Credits);
        Assert.Equal(ZoneKind.RunnerRig, beforeInstalled.State.GetZone(beforeInstalled.State.GetCard(beforeId).ZoneId).Kind);

        CardInstanceId afterId = new("card:batch2-carmen-after");
        GameState after = BuildRunnerState(
            catalog,
            afterId,
            GatewayBatch2CardCodes.Carmen,
            ZoneKind.RunnerGrip,
            runnerCredits: 3,
            runtimeCardCatalog: runtime);
        ServerId rnd = after.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;
        after = DriveRunToEnd(
            GameEngine.BeginRunWithEvents(after, rnd, catalog, runtime).State,
            catalog,
            runtime);
        Assert.True(after.Turn.ThisTurnFacts!.SuccessfulRun);

        GameState afterPrompt = BeginRunnerAction(after, catalog, runtime);
        LegalAction afterInstall = Assert.Single(
            Assert.IsType<PendingDecision>(afterPrompt.PendingDecision).LegalActions,
            action => action.Kind == ActionKind.InstallCard);
        InstallCardParameters afterParameters = Assert.IsType<InstallCardParameters>(afterInstall.Parameters);
        Assert.Equal(3, afterParameters.CreditCost);
        Assert.Equal(5, afterParameters.CostQuote!.BaseCost);
        Assert.Equal(3, afterParameters.CostQuote.EffectiveCost);
        Assert.Contains(
            "carmen-successful-run-install",
            EngineJson.Serialize(ObservationBuilder.Build(afterPrompt, PlayerSide.Runner, catalog)),
            StringComparison.Ordinal);

        ApplyResult afterInstalled = Apply(afterPrompt, afterInstall, catalog, runtime);
        Assert.True(afterInstalled.Accepted, afterInstalled.Rejection?.Message);
        PlayerId runner = afterInstalled.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        Assert.Equal(0, afterInstalled.State.GetPlayer(runner).Credits);
        Assert.Equal(ZoneKind.RunnerRig, afterInstalled.State.GetZone(afterInstalled.State.GetCard(afterId).ZoneId).Kind);
        Assert.Empty(StateInvariantValidator.Collect(afterInstalled.State, catalog, runtime));
    }

    [Fact]
    public void MarjanahRealEncounterUsesWalletAndHostedSplitsBeforeAndAfterSuccessfulRun()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = CreateRunCostRuntimeCatalog();

        GameState before = BuildMarjanahEncounterState(catalog, runtime, successfulRun: false);
        GameState beforeEncounter = ReachMarjanahEncounter(before, catalog, runtime);
        LegalAction beforePump = Assert.Single(
            beforeEncounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PumpBreaker
                && Assert.IsType<RunBreakerParameters>(action.Parameters).HostedCredits == 1);
        ApplyResult beforePumped = Apply(beforeEncounter, beforePump, catalog, runtime);
        Assert.True(beforePumped.Accepted, beforePumped.Rejection?.Message);
        beforeEncounter = beforePumped.State;
        PlayerId beforePumpedRunner = beforeEncounter.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        Assert.Equal(1, beforeEncounter.GetPlayer(beforePumpedRunner).Credits);
        Assert.Equal(4, beforeEncounter.RunState!.RunCredits);
        PendingDecision beforeDecision = Assert.IsType<PendingDecision>(beforeEncounter.PendingDecision);
        RunBreakParameters[] beforeBreaks = beforeDecision.LegalActions
            .Where(action => action.Kind == ActionKind.BreakSubroutines)
            .Select(action => Assert.IsType<RunBreakParameters>(action.Parameters))
            .ToArray();
        Assert.Contains(beforeBreaks, parameters => parameters.HostedCredits == 1 && parameters.CostQuote is null);
        Assert.Contains(beforeBreaks, parameters => parameters.HostedCredits == 2 && parameters.CostQuote is null);
        LegalAction split = Assert.Single(
            beforeDecision.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && Assert.IsType<RunBreakParameters>(action.Parameters).HostedCredits == 1);
        ApplyResult beforeBroken = Apply(beforeEncounter, split, catalog, runtime);
        Assert.True(beforeBroken.Accepted, beforeBroken.Rejection?.Message);
        PlayerId beforeRunner = beforeBroken.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        Assert.Equal(0, beforeBroken.State.GetPlayer(beforeRunner).Credits);
        // The pre-history break costs two: one wallet credit plus the one
        // hosted credit selected above, leaving three hosted credits.
        Assert.Equal(3, beforeBroken.State.RunState!.RunCredits);

        GameState after = BuildMarjanahEncounterState(catalog, runtime, successfulRun: true);
        GameState afterEncounter = ReachMarjanahEncounter(after, catalog, runtime);
        LegalAction afterPump = Assert.Single(
            afterEncounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PumpBreaker
                && Assert.IsType<RunBreakerParameters>(action.Parameters).HostedCredits == 1);
        ApplyResult afterPumped = Apply(afterEncounter, afterPump, catalog, runtime);
        Assert.True(afterPumped.Accepted, afterPumped.Rejection?.Message);
        afterEncounter = afterPumped.State;
        PlayerId afterPumpedRunner = afterEncounter.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        Assert.Equal(1, afterEncounter.GetPlayer(afterPumpedRunner).Credits);
        Assert.Equal(4, afterEncounter.RunState!.RunCredits);
        PendingDecision afterDecision = Assert.IsType<PendingDecision>(afterEncounter.PendingDecision);
        RunBreakParameters reduced = Assert.IsType<RunBreakParameters>(Assert.Single(
            afterDecision.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && Assert.IsType<RunBreakParameters>(action.Parameters).HostedCredits == 1).Parameters);
        Assert.Equal(2, reduced.CostQuote!.BaseCost);
        Assert.Equal(1, reduced.CostQuote.EffectiveCost);
        Assert.Contains(
            reduced.CostQuote.Adjustments,
            adjustment => adjustment.ReasonKey == "marjanah-successful-run-break");
        LegalAction reducedSplit = Assert.Single(
            afterDecision.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines
                && Assert.IsType<RunBreakParameters>(action.Parameters).HostedCredits == 1);
        ApplyResult afterBroken = Apply(afterEncounter, reducedSplit, catalog, runtime);
        Assert.True(afterBroken.Accepted, afterBroken.Rejection?.Message);
        PlayerId afterRunner = afterBroken.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        Assert.Equal(1, afterBroken.State.GetPlayer(afterRunner).Credits);
        Assert.Equal(3, afterBroken.State.RunState!.RunCredits);
        Assert.Empty(StateInvariantValidator.Collect(afterBroken.State, catalog, runtime));
    }

    private static CardBehaviorDefinition Definition(CardDefinitionId id) =>
        GatewayBehaviorCatalog.Create().GenericSpecs.Single(definition => definition.DefinitionId == id);

    private static RuntimeCardCatalog CreateRunCostRuntimeCatalog() =>
        new RuntimeCardCatalog(
            "gateway-run-cost-test-runtime",
            [
                new CardRuntimeSpec(
                    GatewayBatch2CardCodes.Carmen,
                    PlayerSide.Runner,
                    RuntimeCardClass.Program,
                    installCost: 5,
                    memoryUnits: 1,
                    subtypes: ["icebreaker", "sentry"]),
                new CardRuntimeSpec(
                    GatewayBatch2CardCodes.Marjanah,
                    PlayerSide.Runner,
                    RuntimeCardClass.Program,
                    installCost: 0,
                    memoryUnits: 1,
                    subtypes: ["icebreaker", "barrier"]),
                new CardRuntimeSpec(
                    GatewayCardCodes.Overclock,
                    PlayerSide.Runner,
                    RuntimeCardClass.Event,
                    playCost: 1),
                new CardRuntimeSpec(
                    GatewayCardCodes.Palisade,
                    PlayerSide.Corp,
                    RuntimeCardClass.Ice,
                    rezCost: 3,
                    subtypes: ["barrier"]),
            ]);

    private static (BehaviorCostKind Kind, int Amount) SingleCost(BehaviorPaidAbility ability)
    {
        BehaviorCost cost = Assert.Single(ability.Costs);
        return (cost.Kind, cost.Amount);
    }

    private static GameState BeginRunnerAction(
        GameState state,
        BehaviorCatalog catalog,
        IRuntimeCardCatalog? runtimeCardCatalog = null) =>
        runtimeCardCatalog is null
            ? GameEngine.BeginResolution(
                state,
                new TurnFrame(
                    1,
                    ResolutionBehaviorIds.Turn,
                    PlayerSide.Runner,
                    state.Turn.TurnNumber,
                    TurnFrameStage.AwaitingAction),
                catalog)
            : GameEngine.BeginResolution(
                state,
                new TurnFrame(
                    1,
                    ResolutionBehaviorIds.Turn,
                    PlayerSide.Runner,
                    state.Turn.TurnNumber,
                    TurnFrameStage.AwaitingAction),
                catalog,
                runtimeCardCatalog);

    private static GameState OpenRunnerAction(GameState state, BehaviorCatalog catalog)
    {
        if (state.PendingDecision is null)
        {
            return BeginRunnerAction(state, catalog);
        }

        for (int passCount = 0;
            passCount < 2
            && state.PendingDecision is not null
            && state.PendingDecision.LegalActions.All(action => action.Kind == ActionKind.PassPriority);
            passCount++)
        {
            ApplyResult passed = Apply(state, state.PendingDecision.LegalActions.Single(), catalog);
            Assert.True(passed.Accepted, passed.Rejection?.Message);
            state = passed.State;
        }

        return state;
    }

    private static ApplyResult Apply(
        GameState state,
        LegalAction action,
        BehaviorCatalog catalog,
        RuntimeCardCatalog? runtimeCardCatalog = null)
    {
        ActionSubmission submission = new(
            state.PendingDecision!.Owner,
            state.PendingDecision.Id,
            state.PendingDecision.Revision,
            action.Id);
        return runtimeCardCatalog is null
            ? GameEngine.Apply(state, submission, catalog)
            : GameEngine.Apply(state, submission, catalog, runtimeCardCatalog);
    }

    private static ApplyResult Pass(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return Apply(state, pass, catalog);
    }

    private static GameState DriveRunToEnd(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog? runtimeCardCatalog = null,
        List<EngineEvent>? events = null)
    {
        for (int guard = 0; guard < 128; guard++)
        {
            if (state.RunState is null)
            {
                if (state.PendingDecision?.Kind != DecisionKind.TriggerChoice)
                {
                    return state;
                }

                PendingDecision trigger = state.PendingDecision;
                LegalAction decline = Assert.Single(
                    trigger.LegalActions,
                    action => action.Kind == ActionKind.DeclineTrigger);
                ApplyResult declined = Apply(state, decline, catalog, runtimeCardCatalog);
                Assert.True(declined.Accepted, declined.Rejection?.Message);
                if (events is not null)
                {
                    foreach (EngineEvent eventValue in declined.Events)
                    {
                        events.Add(eventValue);
                    }
                }
                state = declined.State;
                continue;
            }

            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction next = decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun)
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority)
                ?? throw new Xunit.Sdk.XunitException(
                    $"The run yielded no pass/continue action at step '{state.RunState.Step}'.");
            ApplyResult applied = Apply(state, next, catalog, runtimeCardCatalog);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            if (events is not null)
            {
                foreach (EngineEvent eventValue in applied.Events)
                {
                    events.Add(eventValue);
                }
            }
            state = applied.State;
        }

        throw new Xunit.Sdk.XunitException("The run did not reach its end within the test guard.");
    }

    private static GameState ReachMarjanahEncounter(
        GameState state,
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtimeCardCatalog)
    {
        ServerId rnd = state.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;
        CardInstanceId overclockId = state.Cards
            .Single(card => card.DefinitionId == GatewayCardCodes.Overclock)
            .Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            state,
            rnd,
            catalog,
            runtimeCardCatalog,
            runner,
            overclockId);
        state = started.State;

        for (int guard = 0; guard < 64; guard++)
        {
            if (state.RunState is
                {
                    Step: RunTimingStep.EncounterPaidWindow,
                    Encounter: { IceId: CardInstanceId iceId },
                })
            {
                Assert.Equal(
                    GatewayCardCodes.Palisade,
                    state.GetCard(iceId).DefinitionId);
                return state;
            }

            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction next = decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun)
                ?? decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PassPriority)
                ?? throw new Xunit.Sdk.XunitException(
                    $"The run yielded no pass/continue action at step '{state.RunState?.Step}'.");
            ApplyResult applied = Apply(state, next, catalog, runtimeCardCatalog);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        throw new Xunit.Sdk.XunitException("The run did not reach the Marjanah encounter window.");
    }

    private static GameState ResolveOccurrence(
        GameState state,
        PlayerId controller,
        GameplayOccurrenceEffect occurrence,
        BehaviorCatalog catalog) =>
        GameEngine.BeginResolution(
            state,
            new EffectResolutionFrame(controller, new CostBundle(), new EffectBundle(occurrence)),
            catalog);

    private static int CounterCount(GameState state, CardInstanceId cardId, string counterName) =>
        state.RulesState?.Counters
            .SingleOrDefault(counter => counter.CardId == cardId && counter.CounterName == counterName)
            ?.Count
        ?? 0;

    private static GameState BuildRunnerState(
        BehaviorCatalog catalog,
        CardInstanceId sourceId,
        CardDefinitionId sourceDefinitionId,
        ZoneKind sourceZoneKind,
        IReadOnlyList<CardCounterState>? counters = null,
        IReadOnlyList<CardInstanceId>? corpDeckCards = null,
        int runnerCredits = 10,
        RuntimeCardCatalog? runtimeCardCatalog = null)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44321,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCardCatalog?.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog?.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState deck = baseline.GetZone(ZoneKind.CorpDeck);
        ZoneState sourceZone = sourceZoneKind == ZoneKind.RunnerRig ? rig : grip;
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, runnerCredits, 4, player.AgendaPoints)
                : player)
            .ToArray();
        CardInstanceState source = new(
            sourceId,
            sourceDefinitionId,
            sourceZone.Id,
            PlayerSide.Runner,
            faceUp: sourceZoneKind == ZoneKind.RunnerRig);
        CardInstanceId[] deckIds = corpDeckCards?.ToArray() ?? [];
        CardInstanceState[] corpCards = deckIds
            .Select(cardId => new CardInstanceState(
                cardId,
                GatewayBatch1CardCodes.HedgeFund,
                deck.Id,
                PlayerSide.Corp,
                faceUp: false))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == sourceZone.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [sourceId])
                : zone.Id == deck.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, deckIds)
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
            defaults.Players,
            defaults.Programs,
            counters ?? [],
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [source, .. corpCards],
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            resolutionStack: [],
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCardCatalog?.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog?.Fingerprint);
        StateInvariantValidator.Validate(state, catalog, runtimeCardCatalog);
        return state;
    }

    private static GameState BuildMarjanahEncounterState(
        BehaviorCatalog catalog,
        RuntimeCardCatalog runtimeCardCatalog,
        bool successfulRun)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44322,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCardCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog.Fingerprint);
        ServerState rnd = baseline.Servers.Single(server => server.Kind == ServerKind.RAndD);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseline.GetZone(ZoneKind.RunnerGrip);
        ZoneState iceZone = baseline.GetZone(rnd.IceZoneId);
        StableIdAllocator allocator = new(baseline.Allocator);
        CardInstanceId marjanahId = allocator.NextCardInstanceId();
        CardInstanceId overclockId = allocator.NextCardInstanceId();
        CardInstanceId palisadeId = allocator.NextCardInstanceId();
        PlayerState runner = baseline.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState corp = baseline.Players.Single(player => player.Side == PlayerSide.Corp);
        PlayerState[] players =
        [
            corp,
            new PlayerState(runner.Id, runner.Side, credits: 2, clicks: 4, runner.AgendaPoints),
        ];
        CardInstanceState[] cards =
        [
            new(marjanahId, GatewayBatch2CardCodes.Marjanah, rig.Id, PlayerSide.Runner, faceUp: true),
            new(overclockId, GatewayCardCodes.Overclock, grip.Id, PlayerSide.Runner),
            new(palisadeId, GatewayCardCodes.Palisade, iceZone.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
        ];
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [marjanahId])
                : zone.Id == grip.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [overclockId])
                    : zone.Id == iceZone.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [palisadeId])
                        : zone)
            .ToArray();
        ServerId[] attempted = successfulRun ? [rnd.Id] : [];
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            baseline.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false,
            successfulRun: successfulRun,
            attemptedRunServerIds: attempted,
            successfulRunServerIds: attempted);
        TurnState turn = new(
            PlayerSide.Runner,
            TurnPhase.Runner,
            baseline.Turn.TurnNumber,
            TurnStep.RunnerAction,
            new TurnHistoryState(1, null, null, facts, null));
        RulesState defaults = RulesState.CreateDefault(players);
        PlayerRulesState[] rulesPlayers = defaults.Players
            .Select(value => value.PlayerId == runner.Id
                ? new PlayerRulesState(
                    value.PlayerId,
                    value.BaseHandSize,
                    value.CoreDamage,
                    value.Tags,
                    value.BaseMemory,
                    usedMemory: 1)
                : value)
            .ToArray();
        RulesState rules = new(
            defaults.Version,
            rulesPlayers,
            [new ProgramMemoryState(marjanahId, runner.Id, memoryCost: 1)],
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
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCardCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, catalog, runtimeCardCatalog);
        return state;
    }
}
