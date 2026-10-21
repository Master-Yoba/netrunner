using Xunit;

namespace Netrunner.Core.Tests;

public sealed class HostedCreditTakeUpToTests
{
    [Fact]
    public void TakeUpToInstructionHasPositiveAmountNoRestrictionAndStableJson()
    {
        BehaviorInstruction instruction = new(
            BehaviorInstructionKind.TakeUpToHostedCredits,
            amount: 3);

        Assert.Equal(BehaviorInstructionKind.TakeUpToHostedCredits, instruction.Kind);
        Assert.Equal(3, instruction.Amount);
        Assert.Null(instruction.SpendRestriction);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TakeUpToHostedCredits));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.TakeUpToHostedCredits,
            amount: 1,
            spendRestriction: HostedCreditSpendRestriction.AbilityCost));

        string json = EngineJson.Serialize(instruction);
        Assert.Contains("\"kind\":\"takeUpToHostedCredits\"", json, StringComparison.Ordinal);
        BehaviorInstruction restored = EngineJson.Deserialize<BehaviorInstruction>(json);
        Assert.Equal(instruction, restored);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    public void TakeUpToChangesDefinitionFingerprintWithoutChangingExactTake()
    {
        const string definitionValue = "test:take-up-to-fingerprint";
        CardDefinitionId definitionId = new(definitionValue);
        BehaviorCatalog exact = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.TakeHostedCredits,
                amount: 1)]));
        BehaviorCatalog upTo = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.TakeUpToHostedCredits,
                amount: 1)]));

        Assert.NotEqual(exact.Fingerprint, upTo.Fingerprint);
        Assert.NotEqual(
            exact.GetDefinitionFingerprint(definitionId),
            upTo.GetDefinitionFingerprint(definitionId));
    }

    [Theory]
    [InlineData(2, 5, 2, 0, true)]
    [InlineData(5, 2, 2, 3, false)]
    public void TakeUpToReportsActualTransferAndEmptyTransition(
        int initialPool,
        int requested,
        int expectedTaken,
        int expectedAfter,
        bool expectedEmpty)
    {
        CardDefinitionId definitionId = new("test:take-up-to-event");
        CardInstanceId sourceId = new("card:take-up-to-event");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: Gate()),
                new BehaviorInstruction(
                    BehaviorInstructionKind.TakeUpToHostedCredits,
                    amount: requested),
            ]));
        GameState state = CreateState(catalog, definitionId, sourceId, initialPool);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction proceed = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["proceed"]));
        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                proceed.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        HostedCreditPoolChangedEventPayload pool = Assert.IsType<HostedCreditPoolChangedEventPayload>(
            Assert.Single(result.Events, @event => @event.Kind == EngineEventKinds.HostedCreditPoolChanged).Payload);
        Assert.Equal(HostedCreditPoolOperation.TakeUpTo, pool.Operation);
        Assert.Equal(-expectedTaken, pool.Delta);
        Assert.Equal(expectedAfter, pool.CountAfter);
        Assert.Equal(expectedEmpty, pool.Emptied);
        Assert.Equal(runner, pool.Recipient);
        Assert.Equal(
            [EngineEventKinds.HostedCreditPoolChanged, EngineEventKinds.CreditsAdjusted],
            result.Events.Select(@event => @event.Kind));
        Assert.Equal(5 + expectedTaken, result.State.GetPlayer(runner).Credits);
        Assert.Equal(expectedAfter, HostedCount(result.State, sourceId));

        string eventJson = EngineJson.Serialize(pool);
        HostedCreditPoolChangedEventPayload restoredPool =
            EngineJson.Deserialize<HostedCreditPoolChangedEventPayload>(eventJson);
        Assert.Equal(pool, restoredPool);
        Assert.Equal(eventJson, EngineJson.Serialize(restoredPool));
        Assert.Empty(EngineEventValidator.Collect(
            new EngineEvent(1, EngineEventKinds.HostedCreditPoolChanged, pool)));
    }

    [Fact]
    public void TakeUpToWithNoPoolIsSuccessfulNoOpWithoutEventOrEmptyTrigger()
    {
        CardDefinitionId definitionId = new("test:take-up-to-zero");
        CardInstanceId sourceId = new("card:take-up-to-zero");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: Gate()),
                new BehaviorInstruction(
                    BehaviorInstructionKind.TakeUpToHostedCredits,
                    amount: 1),
            ],
            triggers:
            [
                new BehaviorTrigger(
                    "empty",
                    OccurrenceKind.HostedCreditPoolEmptied,
                    TriggerMode.Mandatory,
                    BehaviorTriggerScope.Self,
                    subjectCardFilter: null,
                    instructions: [new BehaviorInstruction(BehaviorInstructionKind.TrashSelf)]),
            ]));
        GameState state = CreateState(catalog, definitionId, sourceId, pool: null);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction proceed = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["proceed"]));

        ApplyResult result = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                proceed.Id),
            catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Empty(result.Events);
        Assert.Equal(5, result.State.GetPlayer(runner).Credits);
        Assert.Empty(result.State.RulesState!.Counters);
        Assert.Contains(sourceId, result.State.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
    }

    [Fact]
    public void TakeUpToEmptyTriggerRunsAfterDebitAndResumesThroughJson()
    {
        CardDefinitionId definitionId = new("test:take-up-to-empty-trigger");
        CardInstanceId sourceId = new("card:take-up-to-empty-trigger");
        BehaviorCatalog catalog = Catalog(new CardBehaviorDefinition(
            definitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: Gate()),
                new BehaviorInstruction(
                    BehaviorInstructionKind.TakeUpToHostedCredits,
                    amount: 5),
            ],
            triggers:
            [
                new BehaviorTrigger(
                    "empty",
                    OccurrenceKind.HostedCreditPoolEmptied,
                    TriggerMode.Optional,
                    BehaviorTriggerScope.Self,
                    subjectCardFilter: null,
                    instructions: [new BehaviorInstruction(BehaviorInstructionKind.TrashSelf)]),
            ]));
        GameState state = CreateState(catalog, definitionId, sourceId, pool: 2);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        LegalAction proceed = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["proceed"]));
        ApplyResult opened = GameEngine.Apply(
            prompt,
            new ActionSubmission(
                prompt.PendingDecision.Owner,
                prompt.PendingDecision.Id,
                prompt.PendingDecision.Revision,
                proceed.Id),
            catalog);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.TriggerChoice, opened.State.PendingDecision?.Kind);
        Assert.Equal(
            [EngineEventKinds.HostedCreditPoolChanged, EngineEventKinds.CreditsAdjusted],
            opened.Events.Select(@event => @event.Kind));
        HostedCreditPoolChangedEventPayload pool = Assert.IsType<HostedCreditPoolChangedEventPayload>(opened.Events[0].Payload);
        Assert.Equal(HostedCreditPoolOperation.TakeUpTo, pool.Operation);
        Assert.Equal(-2, pool.Delta);
        Assert.Equal(0, pool.CountAfter);
        Assert.True(pool.Emptied);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
        PendingDecision triggerDecision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction decline = Assert.Single(
            triggerDecision.LegalActions,
            action => action.Kind == ActionKind.DeclineTrigger);
        ApplyResult declined = GameEngine.Apply(
            restored,
            new ActionSubmission(
                triggerDecision.Owner,
                triggerDecision.Id,
                triggerDecision.Revision,
                decline.Id),
            catalog);

        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Equal(7, declined.State.GetPlayer(runner).Credits);
        Assert.Contains(sourceId, declined.State.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Empty(declined.State.RulesState!.Counters);
        Assert.Empty(declined.Events);
        Assert.Empty(declined.State.ResolutionStack);

        GameState usePrompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, runner),
            catalog);
        ApplyResult useOpened = GameEngine.Apply(
            usePrompt,
            new ActionSubmission(
                usePrompt.PendingDecision!.Owner,
                usePrompt.PendingDecision.Id,
                usePrompt.PendingDecision.Revision,
                usePrompt.PendingDecision.LegalActions.Single(action =>
                    action.Parameters is BehaviorChoiceParameters parameters
                    && parameters.OptionKeys.SequenceEqual(["proceed"])).Id),
            catalog);
        GameState useRestored = EngineJson.DeserializeState(
            EngineJson.Serialize(useOpened.State),
            catalog);
        PendingDecision useTriggerDecision = Assert.IsType<PendingDecision>(useRestored.PendingDecision);
        LegalAction use = Assert.Single(
            useTriggerDecision.LegalActions,
            action => action.Kind == ActionKind.UseTrigger);
        ApplyResult used = GameEngine.Apply(
            useRestored,
            new ActionSubmission(
                useTriggerDecision.Owner,
                useTriggerDecision.Id,
                useTriggerDecision.Revision,
                use.Id),
            catalog);
        Assert.True(used.Accepted, used.Rejection?.Message);
        Assert.Equal(7, used.State.GetPlayer(runner).Credits);
        Assert.Contains(sourceId, used.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Empty(used.State.RulesState!.Counters);
        Assert.Empty(used.State.ResolutionStack);
    }

    [Fact]
    public void ExactTakeStillRejectsUnderflowAndTakeUpToWalletOverflowIsAtomic()
    {
        CardDefinitionId exactDefinitionId = new("test:take-exact-underflow");
        CardInstanceId exactSourceId = new("card:take-exact-underflow");
        BehaviorCatalog exactCatalog = Catalog(new CardBehaviorDefinition(
            exactDefinitionId,
            PlayerSide.Runner,
            instructions: [new BehaviorInstruction(
                BehaviorInstructionKind.TakeHostedCredits,
                amount: 2)]));
        GameState exactState = CreateState(exactCatalog, exactDefinitionId, exactSourceId, pool: 1);
        PlayerId runner = exactState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        string exactJson = EngineJson.Serialize(exactState);
        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            exactState,
            new BehaviorInvocationContext(exactSourceId, runner),
            exactCatalog));
        Assert.Equal(exactJson, EngineJson.Serialize(exactState));

        CardDefinitionId upToDefinitionId = new("test:take-up-to-overflow");
        CardInstanceId upToSourceId = new("card:take-up-to-overflow");
        BehaviorCatalog upToCatalog = Catalog(new CardBehaviorDefinition(
            upToDefinitionId,
            PlayerSide.Runner,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Choose,
                    choice: Gate()),
                new BehaviorInstruction(
                    BehaviorInstructionKind.TakeUpToHostedCredits,
                    amount: 1),
            ]));
        GameState overflowState = CreateState(
            upToCatalog,
            upToDefinitionId,
            upToSourceId,
            pool: 1,
            credits: int.MaxValue);
        GameState overflowPrompt = GameEngine.BeginBehaviorForTesting(
            overflowState,
            new BehaviorInvocationContext(upToSourceId, runner),
            upToCatalog);
        LegalAction proceed = Assert.Single(
            overflowPrompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["proceed"]));
        string overflowJson = EngineJson.Serialize(overflowPrompt);
        ApplyResult overflow = GameEngine.Apply(
            overflowPrompt,
            new ActionSubmission(
                overflowPrompt.PendingDecision.Owner,
                overflowPrompt.PendingDecision.Id,
                overflowPrompt.PendingDecision.Revision,
                proceed.Id),
            upToCatalog);

        Assert.False(overflow.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, overflow.Rejection?.Code);
        Assert.Equal(overflowJson, EngineJson.Serialize(overflow.State));
        Assert.Equal(int.MaxValue, overflow.State.GetPlayer(runner).Credits);
        Assert.Equal(1, HostedCount(overflow.State, upToSourceId));
        Assert.Empty(overflow.Events);
    }

    [Fact]
    public void TakeUpToEventInvariantRejectsZeroDeltaTransfer()
    {
        CardInstanceId sourceId = new("card:take-up-to-invariant");
        HostedCreditPoolChangedEventPayload invalid = new(
            HostedCreditPoolOperation.TakeUpTo,
            sourceId,
            delta: 0,
            countAfter: 1,
            HostedCreditSpendRestriction.CardEffectOnly,
            emptied: false,
            recipient: new PlayerId("player:runner"));
        EngineEvent @event = new(1, EngineEventKinds.HostedCreditPoolChanged, invalid);

        Assert.Contains(
            EngineEventValidator.Collect(@event),
            violation => violation.Code == "event.invalid-hosted-credit-delta");
    }

    private static BehaviorChoiceSpec Gate() => new(
        "gate",
        BehaviorChooser.Controller,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        options:
        [
            new BehaviorChoiceOption("proceed", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            new BehaviorChoiceOption("skip", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
        ]);

    private static BehaviorCatalog Catalog(CardBehaviorDefinition definition) =>
        new BehaviorCatalogBuilder("test:hosted-credit-take-up-to").Add(definition).Build();

    private static int HostedCount(GameState state, CardInstanceId cardId) =>
        state.RulesState!.Counters
            .SingleOrDefault(counter =>
                counter.CardId == cardId
                && counter.Purpose == CardCounterPurpose.HostedCredit)
            ?.Count
        ?? 0;

    private static GameState CreateState(
        BehaviorCatalog catalog,
        CardDefinitionId definitionId,
        CardInstanceId sourceId,
        int? pool,
        int credits = 5)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44043,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        CardInstanceState source = new(
            sourceId,
            definitionId,
            rig.Id,
            PlayerSide.Runner,
            faceUp: true,
            rezzed: false);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [sourceId])
                : zone)
            .ToArray();
        PlayerState[] players = baseline.Players
            .Select(player => player.Side == PlayerSide.Runner
                ? new PlayerState(player.Id, player.Side, credits, player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();
        RulesState defaults = RulesState.CreateDefault(players);
        CardCounterState[] counters = pool is { } count
            ? [CardCounterState.CreateHostedCredit(sourceId, count)]
            : [];
        RulesState rules = new(
            defaults.Version,
            defaults.Players,
            defaults.Programs,
            counters,
            defaults.LingeringEffects,
            defaults.Triggers,
            defaults.Interrupts);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            [source],
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: rules,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
    }
}
