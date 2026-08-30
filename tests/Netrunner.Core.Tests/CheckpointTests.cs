using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// The first vertical tests for M2 2B's typed imminent-effect checkpoint.
/// These cases intentionally use synthetic registrations; card-specific
/// triggers and prevention remain outside this slice.
/// </summary>
public sealed class CheckpointTests
{
    [Fact]
    public void NoRegistrationsResolveTheOriginalEffectAndLeaveNoCheckpoint()
    {
        GameState state = WithRules(GameState.CreateNew(201));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState result = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 2)));

        Assert.Equal(7, result.GetPlayer(corp).Credits);
        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
        Assert.Equal(state.EventSequence + 1, result.EventSequence);
    }

    [Fact]
    public void MandatoryTriggerResolvesAutomaticallyWithoutAPlayerDecision()
    {
        GameState state = GameState.CreateNew(202);
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        state = WithRules(
            state,
            triggers:
            [
                new TriggerRegistration(
                    new TriggerId("trigger:mandatory"),
                    corp,
                    TriggerMode.Mandatory,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.mandatory-credit",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
            ]);

        GameState result = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));

        Assert.Equal(8, result.GetPlayer(corp).Credits);
        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
    }

    [Fact]
    public void OptionalTriggerCanBeDeclinedAndLeavesOriginalEffectCommitted()
    {
        GameState baseState = GameState.CreateNew(203);
        PlayerId corp = baseState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState state = WithRules(
            baseState,
            triggers:
            [
                new TriggerRegistration(
                    new TriggerId("trigger:optional"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.optional-credit",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, decision.Kind);
        LegalAction decline = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decline.Id));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(6, result.State.GetPlayer(corp).Credits);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void OptionalTriggerCanBeUsedAsAChildResolution()
    {
        GameState baseState = GameState.CreateNew(204);
        PlayerId corp = baseState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState state = WithRules(
            baseState,
            triggers:
            [
                new TriggerRegistration(
                    new TriggerId("trigger:optional"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.optional-credit",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction use = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseTrigger);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, use.Id));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(8, result.State.GetPlayer(corp).Credits);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void PreventionCanPartiallyReduceDamageAndThenCommitTheRemainder()
    {
        GameState baseState = AddRunnerGripCard(GameState.CreateNew(205), "card:damage-partial");
        PlayerId corp = baseState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = baseState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState state = WithRules(
            baseState,
            interrupts:
            [
                new InterruptRegistration(
                    new InterruptId("interrupt:prevent"),
                    corp,
                    InterruptKind.Prevention,
                    false,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.prevent-one",
                    null,
                    new CostBundle(),
                    1,
                    new EffectBundle()),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 2)));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction prevent = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UsePrevention);
        Assert.Equal(1, Assert.IsType<PreventionChoiceParameters>(prevent.Parameters).Amount);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, prevent.Id));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Single(result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void PreventionCanFullyPreventDamageWithoutACommittedDamageOccurrence()
    {
        GameState baseState = GameState.CreateNew(206);
        PlayerId corp = baseState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = baseState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState state = WithRules(
            baseState,
            interrupts:
            [
                new InterruptRegistration(
                    new InterruptId("interrupt:prevent-two"),
                    corp,
                    InterruptKind.Prevention,
                    true,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.prevent-two",
                    null,
                    new CostBundle(),
                    2,
                    new EffectBundle()),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 2)));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction prevent = decision.LegalActions.Single(action =>
            action.Kind == ActionKind.UsePrevention
            && Assert.IsType<PreventionChoiceParameters>(action.Parameters).Amount == 2);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, prevent.Id));

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.DamageDealt);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Null(result.State.PendingDecision);
    }

    [Fact]
    public void SimultaneousSameControllerTriggersYieldExplicitOrdering()
    {
        GameState created = GameState.CreateNew(207);
        PlayerId corp = CorpOf(created);
        TriggerId triggerA = new("trigger:ordering-a");
        TriggerId triggerB = new("trigger:ordering-b");
        GameState state = WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    triggerA,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.ordering-a",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
                new TriggerRegistration(
                    triggerB,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.ordering-b",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 3))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision ordering = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerOrdering, ordering.Kind);
        Assert.Equal(corp, ordering.Owner);
        Assert.Equal(2, ordering.LegalActions.Count(action => action.Kind == ActionKind.OrderTriggers));
        Assert.Equal(6, prompt.GetPlayer(corp).Credits);
        Assert.Equal(1L, prompt.EventSequence);
        LegalAction orderB = ordering.LegalActions
            .Where(action => action.Kind == ActionKind.OrderTriggers)
            .Single(action => ParametersAs<TriggerOrderParameters>(action, ActionKind.OrderTriggers).TriggerId == triggerB);

        ApplyResult afterOrdering = GameEngine.Apply(
            prompt,
            new ActionSubmission(ordering.Owner, ordering.Id, ordering.Revision, orderB.Id));
        Assert.True(afterOrdering.Accepted, afterOrdering.Rejection?.Message);
        Assert.Empty(afterOrdering.Events);

        PendingDecision choiceB = Assert.IsType<PendingDecision>(afterOrdering.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, choiceB.Kind);
        Assert.Equal(corp, choiceB.Owner);
        LegalAction useB = Assert.Single(choiceB.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(triggerB, ParametersAs<TriggerChoiceParameters>(useB, ActionKind.UseTrigger).TriggerId);

        ApplyResult afterUseB = GameEngine.Apply(
            afterOrdering.State,
            new ActionSubmission(choiceB.Owner, choiceB.Id, choiceB.Revision, useB.Id));
        Assert.True(afterUseB.Accepted, afterUseB.Rejection?.Message);

        PendingDecision choiceA = Assert.IsType<PendingDecision>(afterUseB.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, choiceA.Kind);
        Assert.Equal(corp, choiceA.Owner);
        LegalAction declineA = Assert.Single(choiceA.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        Assert.Equal(triggerA, ParametersAs<TriggerChoiceParameters>(declineA, ActionKind.DeclineTrigger).TriggerId);

        EngineEvent usedTriggerCredit = Assert.Single(afterUseB.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        CreditsAdjustedEventPayload usedTriggerPayload = Assert.IsType<CreditsAdjustedEventPayload>(usedTriggerCredit.Payload);
        Assert.Equal(3, usedTriggerPayload.Delta);
        Assert.Equal(corp, usedTriggerPayload.Target);
        Assert.Equal(2L, usedTriggerCredit.Sequence);

        ApplyResult afterDeclineA = GameEngine.Apply(
            afterUseB.State,
            new ActionSubmission(choiceA.Owner, choiceA.Id, choiceA.Revision, declineA.Id));
        Assert.True(afterDeclineA.Accepted, afterDeclineA.Rejection?.Message);
        Assert.Empty(afterDeclineA.Events);

        PendingDecision choiceARefired = Assert.IsType<PendingDecision>(afterDeclineA.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, choiceARefired.Kind);
        Assert.Equal(corp, choiceARefired.Owner);
        LegalAction declineARefired = Assert.Single(choiceARefired.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        Assert.Equal(triggerA, ParametersAs<TriggerChoiceParameters>(declineARefired, ActionKind.DeclineTrigger).TriggerId);

        ApplyResult settled = GameEngine.Apply(
            afterDeclineA.State,
            new ActionSubmission(choiceARefired.Owner, choiceARefired.Id, choiceARefired.Revision, declineARefired.Id));
        Assert.True(settled.Accepted, settled.Rejection?.Message);
        Assert.Empty(settled.Events);
        Assert.Equal(9, settled.State.GetPlayer(corp).Credits);
        Assert.Equal(2L, settled.State.EventSequence);
        Assert.Null(settled.State.PendingDecision);
        Assert.Empty(settled.State.ResolutionStack);
    }

    [Fact]
    public void ActivePlayerPrecedesInactiveControllerInSimultaneousTriggers()
    {
        GameState created = GameState.CreateNew(208);
        PlayerId corp = CorpOf(created);
        PlayerId runner = RunnerOf(created);
        TriggerId corpTrigger = new("trigger:aaa");
        TriggerId runnerTrigger = new("trigger:bbb");
        GameState state = Rebuild(
            WithRules(
                created,
                triggers:
                [
                    new TriggerRegistration(
                        corpTrigger,
                        corp,
                        TriggerMode.Optional,
                        new TriggerCondition(OccurrenceKind.CreditAdjustment),
                        "fixture.active-corp",
                        null,
                        new CostBundle(),
                        new EffectBundle(new CreditAdjustmentEffect(corp, 3))),
                    new TriggerRegistration(
                        runnerTrigger,
                        runner,
                        TriggerMode.Optional,
                        new TriggerCondition(OccurrenceKind.CreditAdjustment),
                        "fixture.active-runner",
                        null,
                        new CostBundle(),
                        new EffectBundle(new CreditAdjustmentEffect(runner, 2))),
                ]),
            turn: new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1));

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision runnerChoice = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, runnerChoice.Kind);
        Assert.Equal(runner, runnerChoice.Owner);
        Assert.Equal(6, prompt.GetPlayer(corp).Credits);
        Assert.Equal(1L, prompt.EventSequence);
        LegalAction declineRunner = Assert.Single(runnerChoice.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        Assert.Equal(runnerTrigger, ParametersAs<TriggerChoiceParameters>(declineRunner, ActionKind.DeclineTrigger).TriggerId);

        ApplyResult afterDecline = GameEngine.Apply(
            prompt,
            new ActionSubmission(runnerChoice.Owner, runnerChoice.Id, runnerChoice.Revision, declineRunner.Id));
        Assert.True(afterDecline.Accepted, afterDecline.Rejection?.Message);
        Assert.Empty(afterDecline.Events);

        PendingDecision corpChoice = Assert.IsType<PendingDecision>(afterDecline.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, corpChoice.Kind);
        Assert.Equal(corp, corpChoice.Owner);
        LegalAction useCorp = Assert.Single(corpChoice.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(corpTrigger, ParametersAs<TriggerChoiceParameters>(useCorp, ActionKind.UseTrigger).TriggerId);

        ApplyResult final = GameEngine.Apply(
            afterDecline.State,
            new ActionSubmission(corpChoice.Owner, corpChoice.Id, corpChoice.Revision, useCorp.Id));
        Assert.True(final.Accepted, final.Rejection?.Message);

        EngineEvent corpTriggerCredit = Assert.Single(final.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        CreditsAdjustedEventPayload corpTriggerPayload = Assert.IsType<CreditsAdjustedEventPayload>(corpTriggerCredit.Payload);
        Assert.Equal(3, corpTriggerPayload.Delta);
        Assert.Equal(corp, corpTriggerPayload.Target);
        Assert.Equal(2L, corpTriggerCredit.Sequence);
        Assert.Equal(9, final.State.GetPlayer(corp).Credits);
        Assert.Equal(5, final.State.GetPlayer(runner).Credits);
        Assert.Equal(2L, final.State.EventSequence);

        PendingDecision runnerRefire = Assert.IsType<PendingDecision>(final.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, runnerRefire.Kind);
        Assert.Equal(runner, runnerRefire.Owner);
        LegalAction declineRunnerRefire = Assert.Single(runnerRefire.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        Assert.Equal(runnerTrigger, ParametersAs<TriggerChoiceParameters>(declineRunnerRefire, ActionKind.DeclineTrigger).TriggerId);

        ApplyResult settled = GameEngine.Apply(
            final.State,
            new ActionSubmission(runnerRefire.Owner, runnerRefire.Id, runnerRefire.Revision, declineRunnerRefire.Id));
        Assert.True(settled.Accepted, settled.Rejection?.Message);
        Assert.Empty(settled.Events);
        Assert.Equal(9, settled.State.GetPlayer(corp).Credits);
        Assert.Equal(5, settled.State.GetPlayer(runner).Credits);
        Assert.Equal(2L, settled.State.EventSequence);
        Assert.Null(settled.State.PendingDecision);
        Assert.Empty(settled.State.ResolutionStack);
    }

    [Fact]
    public void ReplacementResolvesInsteadOfOriginalEffect()
    {
        GameState created = AddRunnerGripCard(GameState.CreateNew(209), "card:replacement-damage");
        PlayerId corp = CorpOf(created);
        PlayerId runner = RunnerOf(created);
        InterruptId interruptId = new("interrupt:replace-damage");
        GameState state = WithRules(
            created,
            interrupts:
            [
                new InterruptRegistration(
                    interruptId,
                    runner,
                    InterruptKind.Replacement,
                    false,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.replace-damage",
                    null,
                    new CostBundle(),
                    0,
                    new EffectBundle(new DamageEffect(runner, DamageKind.Meat, 1))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 1)));
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.InterruptChoice, decision.Kind);
        Assert.Equal(runner, decision.Owner);
        LegalAction useReplacement = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseReplacement);
        LegalAction declineReplacement = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.DeclineReplacement);
        Assert.Equal(interruptId, ParametersAs<ReplacementChoiceParameters>(useReplacement, ActionKind.UseReplacement).InterruptId);
        Assert.Equal(interruptId, ParametersAs<ReplacementChoiceParameters>(declineReplacement, ActionKind.DeclineReplacement).InterruptId);

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, useReplacement.Id));
        Assert.True(result.Accepted, result.Rejection?.Message);

        EngineEvent damage = Assert.Single(result.Events, @event => @event.Kind == EngineEventKinds.DamageDealt);
        DamageDealtEventPayload payload = Assert.IsType<DamageDealtEventPayload>(damage.Payload);
        Assert.Equal(DamageKind.Meat, payload.Kind);
        Assert.Equal(1, payload.Amount);
        Assert.False(payload.Flatlined);
        Assert.DoesNotContain(
            result.Events,
            @event => @event.Kind == EngineEventKinds.DamageDealt
                && (@event.Payload as DamageDealtEventPayload)?.Kind == DamageKind.Net);
        Assert.Single(result.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(result.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void MaterializedTriggerSurvivesItsSourceMovingAfterMaterialization()
    {
        GameState created = AddRunnerGripCard(GameState.CreateNew(210), "card:trigger-source");
        PlayerId corp = CorpOf(created);
        TriggerId triggerId = new("trigger:source-card");
        CardInstanceId cardId = new("card:trigger-source");
        GameState state = WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    triggerId,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.source-card",
                    cardId,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision choice = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, choice.Kind);
        LegalAction use = Assert.Single(choice.LegalActions, action => action.Kind == ActionKind.UseTrigger);

        ZoneState grip = prompt.GetZone(ZoneKind.RunnerGrip);
        ZoneState heap = prompt.GetZone(ZoneKind.RunnerHeap);
        CardInstanceState relocated = new(
            cardId,
            new CardDefinitionId("test:damage-card"),
            heap.Id,
            PlayerSide.Runner,
            faceUp: true);
        CardInstanceState[] cards = prompt.Cards.Select(card => card.Id == cardId ? relocated : card).ToArray();
        ZoneState[] zones = prompt.Zones.Select(zone =>
        {
            if (zone.Id == grip.Id)
            {
                return new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray());
            }

            if (zone.Id == heap.Id)
            {
                return new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray());
            }

            return zone;
        }).ToArray();
        GameState moved = Rebuild(prompt, cards: cards, zones: zones);
        StateInvariantValidator.Validate(moved);

        string movedJson = EngineJson.Serialize(moved);
        string frozenSourceZone = $"\"sourceZoneId\":\"{grip.Id}\"";
        Assert.True(
            SnapshotWindowContains(movedJson, "\"id\":\"trigger:source-card\"", frozenSourceZone),
            "materialized trigger snapshot must still reference the original zone");

        PendingDecision movedChoice = Assert.IsType<PendingDecision>(moved.PendingDecision);
        ApplyResult result = GameEngine.Apply(
            moved,
            new ActionSubmission(movedChoice.Owner, movedChoice.Id, movedChoice.Revision, use.Id));
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(8, result.State.GetPlayer(corp).Credits);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void NestedSaveLoadAtEveryYieldedCheckpointDecision()
    {
        GameState state = BuildDamageCheckpointScenario(211);
        PlayerId corp = CorpOf(state);
        PlayerId runner = RunnerOf(state);

        GameState current = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 2)));
        int yielded = 0;
        while (current.PendingDecision != null)
        {
            yielded++;
            PendingDecision decision = Assert.IsType<PendingDecision>(current.PendingDecision);
            string json = EngineJson.Serialize(current);
            GameState restored = EngineJson.DeserializeState(json);
            Assert.Equal(json, EngineJson.Serialize(restored));
            LegalAction action = PickCheckpointAction(decision);

            ApplyResult original = GameEngine.Apply(
                current,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
            ApplyResult reloaded = GameEngine.Apply(
                restored,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
            Assert.True(original.Accepted, original.Rejection?.Message);
            Assert.True(reloaded.Accepted, reloaded.Rejection?.Message);
            Assert.Equal(EngineJson.Serialize(original.State), EngineJson.Serialize(reloaded.State));
            Assert.Equal(DeterministicHash.ForEvents(original.Events), DeterministicHash.ForEvents(reloaded.Events));
            current = original.State;
        }

        Assert.True(yielded >= 3, $"expected at least 3 yielded decisions but saw {yielded}");
        StateInvariantValidator.Validate(current);
    }

    [Fact]
    public void IdenticalStateEventTraceAndHashesAfterMidScenarioReload()
    {
        (GameState first, List<EngineEvent> firstEvents) = RunDamageCheckpointScenario(212);
        (GameState second, List<EngineEvent> secondEvents) = RunDamageCheckpointScenario(212, reloadAfterFirstDecision: true);

        Assert.Equal(EngineJson.Serialize(first), EngineJson.Serialize(second));
        Assert.Equal(firstEvents.Select(@event => @event.Kind), secondEvents.Select(@event => @event.Kind));
        Assert.Equal(DeterministicHash.ForEvents(firstEvents), DeterministicHash.ForEvents(secondEvents));
        StateInvariantValidator.Validate(first);
        StateInvariantValidator.Validate(second);
    }

    [Fact]
    public void TamperedCheckpointActionsAreRejectedWithByteIdenticalState()
    {
        GameState created = GameState.CreateNew(213);
        PlayerId corp = CorpOf(created);
        PlayerId runner = RunnerOf(created);
        TriggerId triggerA = new("trigger:forge-a");
        TriggerId triggerB = new("trigger:forge-b");
        GameState state = WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    triggerA,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.forge-a",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
                new TriggerRegistration(
                    triggerB,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.forge-b",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 3))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision ordering = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerOrdering, ordering.Kind);
        LegalAction orderB = ordering.LegalActions
            .Where(action => action.Kind == ActionKind.OrderTriggers)
            .Single(action => ParametersAs<TriggerOrderParameters>(action, ActionKind.OrderTriggers).TriggerId == triggerB);

        ApplyResult ordered = GameEngine.Apply(
            prompt,
            new ActionSubmission(ordering.Owner, ordering.Id, ordering.Revision, orderB.Id));
        Assert.True(ordered.Accepted, ordered.Rejection?.Message);

        GameState choicePrompt = ordered.State;
        PendingDecision decision = Assert.IsType<PendingDecision>(choicePrompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, decision.Kind);
        LegalAction use = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        string promptJson = EngineJson.Serialize(choicePrompt);

        ApplyResult staleRevision = GameEngine.Apply(
            choicePrompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision + 1, use.Id));
        Assert.False(staleRevision.Accepted);
        Assert.Equal(ApplyRejectionCode.StaleRevision, staleRevision.RejectionCode);
        Assert.Equal(promptJson, EngineJson.Serialize(staleRevision.State));
        Assert.NotNull(staleRevision.State.PendingDecision);

        ApplyResult wrongOwner = GameEngine.Apply(
            choicePrompt,
            new ActionSubmission(runner, decision.Id, decision.Revision, use.Id));
        Assert.False(wrongOwner.Accepted);
        Assert.Equal(ApplyRejectionCode.WrongOwner, wrongOwner.RejectionCode);
        Assert.Equal(promptJson, EngineJson.Serialize(wrongOwner.State));
        Assert.NotNull(wrongOwner.State.PendingDecision);

        ApplyResult forgedAction = GameEngine.Apply(
            choicePrompt,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, new ActionId("action:forged")));
        Assert.False(forgedAction.Accepted);
        Assert.Equal(ApplyRejectionCode.UnknownAction, forgedAction.RejectionCode);
        Assert.Equal(promptJson, EngineJson.Serialize(forgedAction.State));
        Assert.NotNull(forgedAction.State.PendingDecision);
    }

    [Fact]
    public void SelfTriggeringTriggerResolvesExactlyOncePerOccurrence()
    {
        GameState created = GameState.CreateNew(214);
        PlayerId corp = CorpOf(created);
        GameState state = WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    new TriggerId("trigger:self-credit"),
                    corp,
                    TriggerMode.Mandatory,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.self-credit",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
            ]);

        GameState result = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));

        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
        Assert.Equal(8, result.GetPlayer(corp).Credits);
        Assert.Equal(2L, result.EventSequence);
    }

    [Fact]
    public void UnmatchedRegistrationIsAFastPathWithoutAnyCheckpointFrame()
    {
        GameState created = GameState.CreateNew(215);
        PlayerId corp = CorpOf(created);
        GameState state = WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    new TriggerId("trigger:click-only"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.ClickAdjustment),
                    "fixture.click-only",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
            ]);

        GameState result = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));

        Assert.Null(result.PendingDecision);
        Assert.Empty(result.ResolutionStack);
        Assert.Equal(6, result.GetPlayer(corp).Credits);
        Assert.DoesNotContain(EngineJson.Serialize(result), "effect-checkpoint");
    }

    [Fact]
    public void DeclinedTriggerIsOfferedAgainForNewOccurrence()
    {
        GameState created = GameState.CreateNew(216);
        PlayerId corp = CorpOf(created);
        TriggerId triggerA = new("trigger:decline-refire-a");
        TriggerId triggerB = new("trigger:decline-refire-b");
        GameState state = WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    triggerA,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.decline-refire-a",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
                new TriggerRegistration(
                    triggerB,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.CreditAdjustment),
                    "fixture.decline-refire-b",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 3))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new CreditAdjustmentEffect(corp, 1)));
        PendingDecision ordering = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.TriggerOrdering, ordering.Kind);
        Assert.Equal(corp, ordering.Owner);
        LegalAction orderA = ordering.LegalActions
            .Where(action => action.Kind == ActionKind.OrderTriggers)
            .Single(action => ParametersAs<TriggerOrderParameters>(action, ActionKind.OrderTriggers).TriggerId == triggerA);

        ApplyResult afterOrdering = GameEngine.Apply(
            prompt,
            new ActionSubmission(ordering.Owner, ordering.Id, ordering.Revision, orderA.Id));
        Assert.True(afterOrdering.Accepted, afterOrdering.Rejection?.Message);
        Assert.Empty(afterOrdering.Events);

        PendingDecision choiceA = Assert.IsType<PendingDecision>(afterOrdering.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, choiceA.Kind);
        Assert.Equal(corp, choiceA.Owner);
        LegalAction declineA = Assert.Single(choiceA.LegalActions, action => action.Kind == ActionKind.DeclineTrigger);
        Assert.Equal(triggerA, ParametersAs<TriggerChoiceParameters>(declineA, ActionKind.DeclineTrigger).TriggerId);

        ApplyResult afterDeclineA = GameEngine.Apply(
            afterOrdering.State,
            new ActionSubmission(choiceA.Owner, choiceA.Id, choiceA.Revision, declineA.Id));
        Assert.True(afterDeclineA.Accepted, afterDeclineA.Rejection?.Message);
        Assert.Empty(afterDeclineA.Events);
        Assert.Equal(6, afterDeclineA.State.GetPlayer(corp).Credits);

        PendingDecision choiceB = Assert.IsType<PendingDecision>(afterDeclineA.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, choiceB.Kind);
        Assert.Equal(corp, choiceB.Owner);
        LegalAction useB = Assert.Single(choiceB.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(triggerB, ParametersAs<TriggerChoiceParameters>(useB, ActionKind.UseTrigger).TriggerId);

        ApplyResult afterUseB = GameEngine.Apply(
            afterDeclineA.State,
            new ActionSubmission(choiceB.Owner, choiceB.Id, choiceB.Revision, useB.Id));
        Assert.True(afterUseB.Accepted, afterUseB.Rejection?.Message);
        EngineEvent bCredit = Assert.Single(afterUseB.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        CreditsAdjustedEventPayload bCreditPayload = Assert.IsType<CreditsAdjustedEventPayload>(bCredit.Payload);
        Assert.Equal(3, bCreditPayload.Delta);
        Assert.Equal(corp, bCreditPayload.Target);
        Assert.Equal(2L, bCredit.Sequence);
        Assert.Equal(9, afterUseB.State.GetPlayer(corp).Credits);

        PendingDecision refireA = Assert.IsType<PendingDecision>(afterUseB.State.PendingDecision);
        Assert.Equal(DecisionKind.TriggerChoice, refireA.Kind);
        Assert.Equal(corp, refireA.Owner);
        LegalAction useA = Assert.Single(refireA.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(triggerA, ParametersAs<TriggerChoiceParameters>(useA, ActionKind.UseTrigger).TriggerId);

        ApplyResult settled = GameEngine.Apply(
            afterUseB.State,
            new ActionSubmission(refireA.Owner, refireA.Id, refireA.Revision, useA.Id));
        Assert.True(settled.Accepted, settled.Rejection?.Message);
        EngineEvent aCredit = Assert.Single(settled.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        CreditsAdjustedEventPayload aCreditPayload = Assert.IsType<CreditsAdjustedEventPayload>(aCredit.Payload);
        Assert.Equal(2, aCreditPayload.Delta);
        Assert.Equal(corp, aCreditPayload.Target);
        Assert.Equal(3L, aCredit.Sequence);
        Assert.Equal(11, settled.State.GetPlayer(corp).Credits);
        Assert.Equal(3L, settled.State.EventSequence);
        Assert.Null(settled.State.PendingDecision);
        Assert.Empty(settled.State.ResolutionStack);
        StateInvariantValidator.Validate(settled.State);
    }

    [Fact]
    public void DeclinedPreventionInterruptIsOfferedAgainForReplacementOccurrence()
    {
        GameState created = GameState.CreateNew(217);
        PlayerId corp = CorpOf(created);
        PlayerId runner = RunnerOf(created);
        InterruptId preventionId = new("interrupt:prevent-refire");
        InterruptId replacementId = new("interrupt:replace-refire");
        GameState state = WithRules(
            created,
            interrupts:
            [
                new InterruptRegistration(
                    preventionId,
                    runner,
                    InterruptKind.Prevention,
                    false,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.prevent-refire",
                    null,
                    new CostBundle(),
                    1,
                    new EffectBundle()),
                new InterruptRegistration(
                    replacementId,
                    runner,
                    InterruptKind.Replacement,
                    false,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.replace-refire",
                    null,
                    new CostBundle(),
                    0,
                    new EffectBundle(new DamageEffect(runner, DamageKind.Meat, 1))),
            ]);

        GameState prompt = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Meat, 2)));
        PendingDecision preventionChoice = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        Assert.Equal(DecisionKind.InterruptChoice, preventionChoice.Kind);
        Assert.Equal(runner, preventionChoice.Owner);
        LegalAction declinePrevention = Assert.Single(preventionChoice.LegalActions, action => action.Kind == ActionKind.DeclinePrevention);
        Assert.Equal(preventionId, ParametersAs<PreventionChoiceParameters>(declinePrevention, ActionKind.DeclinePrevention).InterruptId);

        ApplyResult afterDeclinePrevention = GameEngine.Apply(
            prompt,
            new ActionSubmission(preventionChoice.Owner, preventionChoice.Id, preventionChoice.Revision, declinePrevention.Id));
        Assert.True(afterDeclinePrevention.Accepted, afterDeclinePrevention.Rejection?.Message);
        Assert.Empty(afterDeclinePrevention.Events);

        PendingDecision replacementChoice = Assert.IsType<PendingDecision>(afterDeclinePrevention.State.PendingDecision);
        Assert.Equal(DecisionKind.InterruptChoice, replacementChoice.Kind);
        Assert.Equal(runner, replacementChoice.Owner);
        LegalAction useReplacement = Assert.Single(replacementChoice.LegalActions, action => action.Kind == ActionKind.UseReplacement);
        Assert.Equal(replacementId, ParametersAs<ReplacementChoiceParameters>(useReplacement, ActionKind.UseReplacement).InterruptId);

        ApplyResult afterReplacement = GameEngine.Apply(
            afterDeclinePrevention.State,
            new ActionSubmission(replacementChoice.Owner, replacementChoice.Id, replacementChoice.Revision, useReplacement.Id));
        Assert.True(afterReplacement.Accepted, afterReplacement.Rejection?.Message);
        Assert.Empty(afterReplacement.Events);

        PendingDecision refirePrevention = Assert.IsType<PendingDecision>(afterReplacement.State.PendingDecision);
        Assert.Equal(DecisionKind.InterruptChoice, refirePrevention.Kind);
        Assert.Equal(runner, refirePrevention.Owner);
        LegalAction usePrevention = Assert.Single(refirePrevention.LegalActions, action => action.Kind == ActionKind.UsePrevention);
        PreventionChoiceParameters preventionParameters =
            ParametersAs<PreventionChoiceParameters>(usePrevention, ActionKind.UsePrevention);
        Assert.Equal(preventionId, preventionParameters.InterruptId);
        Assert.Equal(1, preventionParameters.Amount);

        ApplyResult settled = GameEngine.Apply(
            afterReplacement.State,
            new ActionSubmission(refirePrevention.Owner, refirePrevention.Id, refirePrevention.Revision, usePrevention.Id));
        Assert.True(settled.Accepted, settled.Rejection?.Message);
        Assert.Empty(settled.Events);
        Assert.Empty(settled.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal(0L, settled.State.EventSequence);
        Assert.Null(settled.State.PendingDecision);
        Assert.Empty(settled.State.ResolutionStack);
        StateInvariantValidator.Validate(settled.State);
    }

    private static PlayerId CorpOf(GameState state) =>
        state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

    private static PlayerId RunnerOf(GameState state) =>
        state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

    private const string SecondDamageTriggerId = "trigger:dmg-second";

    private static readonly TriggerId SecondDamageTrigger = new(SecondDamageTriggerId);

    private static T ParametersAs<T>(LegalAction action, ActionKind kind)
        where T : notnull
    {
        Assert.Equal(kind, action.Kind);
        return Assert.IsType<T>(action.Parameters);
    }

    private static LegalAction PickCheckpointAction(PendingDecision decision)
    {
        if (decision.Kind == DecisionKind.InterruptChoice)
        {
            return decision.LegalActions.Single(action => action.Kind == ActionKind.UsePrevention);
        }

        if (decision.Kind == DecisionKind.TriggerOrdering)
        {
            return decision.LegalActions
                .Where(action => action.Kind == ActionKind.OrderTriggers)
                .Single(action => ParametersAs<TriggerOrderParameters>(action, ActionKind.OrderTriggers).TriggerId == SecondDamageTrigger);
        }

        if (decision.Kind == DecisionKind.TriggerChoice)
        {
            LegalAction use = decision.LegalActions.Single(action => action.Kind == ActionKind.UseTrigger);
            return ParametersAs<TriggerChoiceParameters>(use, ActionKind.UseTrigger).TriggerId == SecondDamageTrigger
                ? use
                : decision.LegalActions.Single(action => action.Kind == ActionKind.DeclineTrigger);
        }

        throw new InvalidOperationException($"unexpected decision kind {decision.Kind}");
    }

    private static GameState BuildDamageCheckpointScenario(ulong seed)
    {
        GameState created = AddRunnerGripCard(AddRunnerGripCard(GameState.CreateNew(seed), "card:dmg-one"), "card:dmg-two");
        PlayerId corp = CorpOf(created);
        return WithRules(
            created,
            triggers:
            [
                new TriggerRegistration(
                    new TriggerId("trigger:dmg-first"),
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.dmg-first",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 2))),
                new TriggerRegistration(
                    SecondDamageTrigger,
                    corp,
                    TriggerMode.Optional,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.dmg-second",
                    null,
                    new CostBundle(),
                    new EffectBundle(new CreditAdjustmentEffect(corp, 3))),
            ],
            interrupts:
            [
                new InterruptRegistration(
                    new InterruptId("interrupt:dmg-prevent"),
                    corp,
                    InterruptKind.Prevention,
                    false,
                    new TriggerCondition(OccurrenceKind.Damage),
                    "fixture.dmg-prevent",
                    null,
                    new CostBundle(),
                    1,
                    new EffectBundle()),
            ]);
    }

    private static (GameState State, List<EngineEvent> Events) RunDamageCheckpointScenario(
        ulong seed,
        bool reloadAfterFirstDecision = false)
    {
        GameState state = BuildDamageCheckpointScenario(seed);
        PlayerId corp = CorpOf(state);
        PlayerId runner = RunnerOf(state);
        GameState current = GameEngine.ResolveEffects(
            state,
            corp,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 2)));
        if (reloadAfterFirstDecision)
        {
            Assert.NotNull(current.PendingDecision);
            current = EngineJson.DeserializeState(EngineJson.Serialize(current));
        }

        List<EngineEvent> events = [];
        while (current.PendingDecision != null)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(current.PendingDecision);
            LegalAction action = PickCheckpointAction(decision);
            ApplyResult result = GameEngine.Apply(
                current,
                new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id));
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            current = result.State;
        }

        return (current, events);
    }

    private static bool SnapshotWindowContains(string json, string anchor, string needle)
    {
        int index = json.IndexOf(anchor, StringComparison.Ordinal);
        while (index >= 0)
        {
            int start = Math.Max(0, index - 400);
            int end = Math.Min(json.Length, index + anchor.Length + 400);
            if (json.Substring(start, end - start).Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }

            index = json.IndexOf(anchor, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static GameState AddRunnerGripCard(GameState state, string id)
    {
        ZoneState grip = state.GetZone(ZoneKind.RunnerGrip);
        CardInstanceId cardId = new(id);
        var card = new CardInstanceState(
            cardId,
            new CardDefinitionId("test:damage-card"),
            grip.Id,
            PlayerSide.Runner);
        ZoneState[] zones = state.Zones.Select(zone => zone.Id == grip.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
            : zone).ToArray();
        return Rebuild(state, cards: state.Cards.Append(card).ToArray(), zones: zones);
    }

    private static GameState WithRules(
        GameState state,
        IReadOnlyList<TriggerRegistration>? triggers = null,
        IReadOnlyList<InterruptRegistration>? interrupts = null)
    {
        RulesState defaults = RulesState.CreateDefault(state.Players);
        return Rebuild(
            state,
            rulesState: new RulesState(
                defaults.Version,
                defaults.Players,
                defaults.Programs,
                defaults.Counters,
                defaults.LingeringEffects,
                triggers,
                interrupts));
    }

    private static GameState Rebuild(
        GameState state,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        RulesState? rulesState = null,
        TurnState? turn = null) =>
        new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: cards ?? state.Cards,
            zones: zones ?? state.Zones,
            servers: state.Servers,
            turn: turn ?? state.Turn,
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
            runState: state.RunState,
            rulesState: rulesState ?? state.RulesState);
}
