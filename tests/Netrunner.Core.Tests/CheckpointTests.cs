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

    private static PlayerId CorpOf(GameState state) =>
        state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

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
        RulesState? rulesState = null) =>
        new(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
            cards: cards ?? state.Cards,
            zones: zones ?? state.Zones,
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
            runState: state.RunState,
            rulesState: rulesState ?? state.RulesState);
}
