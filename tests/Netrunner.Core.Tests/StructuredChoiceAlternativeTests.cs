using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused ENG-420 coverage for choice-owned typed costs and semantic
/// alternatives. These tests intentionally use synthetic IDs so the tests
/// exercise the declarative interpreter rather than card-title dispatch.
/// </summary>
public sealed class StructuredChoiceAlternativeTests
{
    [Fact]
    public void CreditCostOptionIsMaterializedAgainstFallbackAndOmittedWhenUnaffordable()
    {
        CardDefinitionId definitionId = new("test:structured-credit-choice");
        CardInstanceId sourceId = new("card:structured-credit-choice");
        BehaviorCatalog catalog = ChoiceCatalog(
            definitionId,
            new BehaviorChoiceOption(
                "pay",
                [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 4)],
                costs: [new BehaviorCost(BehaviorCostKind.Credits, 2)],
                semantic: BehaviorChoiceOptionSemantic.Normal),
            new BehaviorChoiceOption(
                "fallback",
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                semantic: BehaviorChoiceOptionSemantic.Fallback));
        GameState state = WithRules(CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        GameState prompt = BeginChoice(state, catalog, sourceId, corp);
        Assert.Contains(
            prompt.PendingDecision!.LegalActions,
            action => IsOption(action, "pay")
                && action.Parameters is BehaviorChoiceParameters { CardIds.Count: 0 });
        Assert.Contains(prompt.PendingDecision.LegalActions, action => IsOption(action, "fallback"));

        LegalAction pay = Assert.Single(prompt.PendingDecision.LegalActions, action => IsOption(action, "pay"));
        ApplyResult paid = ApplyAction(prompt, pay, catalog);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        Assert.Equal(7, paid.State.GetPlayer(corp).Credits);
        Assert.Empty(paid.State.ResolutionStack);

        GameState poor = ReplacePlayer(state, corp, credits: 1);
        GameState poorPrompt = BeginChoice(poor, catalog, sourceId, corp);
        Assert.DoesNotContain(poorPrompt.PendingDecision!.LegalActions, action => IsOption(action, "pay"));
        LegalAction fallback = Assert.Single(
            poorPrompt.PendingDecision.LegalActions,
            action => IsOption(action, "fallback"));
        ApplyResult declined = ApplyAction(poorPrompt, fallback, catalog);
        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Equal(1, declined.State.GetPlayer(corp).Credits);
    }

    [Fact]
    public void TypedTwoClickCostIsPaidByChoiceOwner()
    {
        CardDefinitionId definitionId = new("test:structured-click-choice");
        CardInstanceId sourceId = new("card:structured-click-choice");
        BehaviorCatalog catalog = ChoiceCatalog(
            definitionId,
            new BehaviorChoiceOption(
                "clicks",
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                costs: [new BehaviorCost(BehaviorCostKind.Clicks, 2)]),
            new BehaviorChoiceOption("fallback", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]));
        GameState state = WithRules(CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState prompt = BeginChoice(state, catalog, sourceId, corp);
        LegalAction clicks = Assert.Single(prompt.PendingDecision!.LegalActions, action => IsOption(action, "clicks"));

        ApplyResult result = ApplyAction(prompt, clicks, catalog);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, result.State.GetPlayer(corp).Clicks);
        Assert.Equal(5, result.State.GetPlayer(corp).Credits);
        Assert.Contains(
            result.Events,
            @event => @event.Kind == EngineEventKinds.ClicksAdjusted
                && @event.Payload is ClicksAdjustedEventPayload payload
                && payload.Target == corp
                && payload.Delta == -2);
    }

    [Fact]
    public void AddTagsRecipientCanTargetActingRunnerInsteadOfCorpController()
    {
        CardDefinitionId definitionId = new("test:structured-recipient");
        CardInstanceId sourceId = new("card:structured-recipient");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:structured-recipient-catalog")
            .Add(new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.AddTags,
                        amount: 1,
                        recipient: BehaviorPlayerSelector.ActingPlayer),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.AddTags,
                        amount: 1,
                        recipient: BehaviorPlayerSelector.Runner),
                ]))
            .Build();
        GameState state = WithRules(CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp)));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState result = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp, runner),
            catalog);

        Assert.Equal(0, Tags(result, corp));
        Assert.Equal(2, Tags(result, runner));
    }

    [Fact]
    public void ExactPrivateTwoCardCostStaysHiddenUntilTrashAndRoundTrips()
    {
        (GameState prompt, BehaviorCatalog catalog, PlayerId corp, CardInstanceId first, CardInstanceId second) =
            BeginTwoCardChoice();
        string firstObservation = EngineJson.Serialize(
            ObservationBuilder.Build(prompt, PlayerSide.Runner, catalog));
        Assert.DoesNotContain(first.Value, firstObservation, StringComparison.Ordinal);
        Assert.DoesNotContain(second.Value, firstObservation, StringComparison.Ordinal);
        Assert.Empty(ObservationBuilder.Build(prompt, PlayerSide.Runner, catalog).LegalActions);

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        LegalAction pay = Assert.Single(restored.PendingDecision!.LegalActions, action => IsOption(action, "pay"));
        BehaviorChoiceParameters payload = Assert.IsType<BehaviorChoiceParameters>(pay.Parameters);
        Assert.Equal([first, second], payload.CardIds);
        ApplyResult applied = ApplyAction(restored, pay, catalog);

        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.All(
            [first, second],
            cardId => Assert.Contains(cardId, applied.State.GetZone(ZoneKind.CorpDiscard).CardIds));
        Assert.All(
            [first, second],
            cardId => Assert.True(applied.State.GetCard(cardId).FaceUp));
        string publicObservation = EngineJson.Serialize(
            ObservationBuilder.Build(applied.State, PlayerSide.Runner, catalog));
        Assert.Contains(first.Value, publicObservation, StringComparison.Ordinal);
        Assert.Contains(second.Value, publicObservation, StringComparison.Ordinal);
        Assert.Equal(5, applied.State.GetPlayer(corp).Credits);
    }

    [Fact]
    public void ForgedOrStaleOptionCardPayloadRejectsWithoutChangingBytes()
    {
        (GameState prompt, BehaviorCatalog catalog, PlayerId corp, CardInstanceId first, CardInstanceId second) =
            BeginTwoCardChoice();
        LegalAction issued = Assert.Single(prompt.PendingDecision!.LegalActions, action => IsOption(action, "pay"));
        BehaviorChoiceParameters forgedParameters = new(
            "two-card-choice",
            ["pay"],
            [new CardInstanceId("card:forged-choice-card")]);
        LegalAction forgedAction = new(issued.Id, issued.Kind, issued.Description, forgedParameters);
        GameState forged = ReplacePending(
            prompt,
            prompt.PendingDecision.LegalActions
                .Select(action => action.Id == issued.Id ? forgedAction : action)
                .ToArray());
        string forgedBytes = EngineJson.Serialize(forged);
        ApplyResult forgedResult = GameEngine.Apply(
            forged,
            new ActionSubmission(corp, forged.PendingDecision!.Id, forged.PendingDecision.Revision, issued.Id),
            catalog);
        Assert.False(forgedResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, forgedResult.Rejection?.Code);
        Assert.Same(forged, forgedResult.State);
        Assert.Equal(forgedBytes, EngineJson.Serialize(forgedResult.State));

        GameState stale = MoveCard(prompt, first, ZoneKind.CorpDiscard);
        string staleBytes = EngineJson.Serialize(stale);
        ApplyResult staleResult = GameEngine.Apply(
            stale,
            new ActionSubmission(corp, stale.PendingDecision!.Id, stale.PendingDecision.Revision, issued.Id),
            catalog);
        Assert.False(staleResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, staleResult.Rejection?.Code);
        Assert.Same(stale, staleResult.State);
        Assert.Equal(staleBytes, EngineJson.Serialize(staleResult.State));

        _ = second;
    }

    [Theory]
    [InlineData(OccurrenceKind.ServerApproached)]
    [InlineData(OccurrenceKind.IceEncountered)]
    public void EndRunCatalogReactionClosesItsLiveRunExactlyOnce(OccurrenceKind occurrenceKind)
    {
        CardDefinitionId sourceDefinition = new($"test:structured-end-{occurrenceKind.ToString().ToLowerInvariant()}");
        BehaviorTrigger trigger = new(
            "end-run",
            occurrenceKind,
            TriggerMode.Optional,
            occurrenceKind == OccurrenceKind.IceEncountered
                ? BehaviorTriggerScope.Self
                : BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            [new BehaviorInstruction(BehaviorInstructionKind.EndRun)],
            requireSourceOnAttackedServer: true);
        BehaviorCatalog catalog;
        GameState state;
        CardInstanceId sourceId;
        if (occurrenceKind == OccurrenceKind.ServerApproached)
        {
            sourceId = new("card:structured-end-server");
            catalog = new BehaviorCatalogBuilder(
                    $"test:structured-end-{occurrenceKind.ToString().ToLowerInvariant()}",
                    FixtureBehaviorCatalog.Catalog)
                .Add(new CardBehaviorDefinition(sourceDefinition, PlayerSide.Corp, triggers: [trigger]))
                .Build();
            GameState baseline = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
            state = AddInstalledSource(baseline, catalog, sourceId, sourceDefinition, RunTimingTests.RemoteId(baseline));
        }
        else
        {
            CardDefinitionId iceDefinition = sourceDefinition;
            catalog = new BehaviorCatalogBuilder(
                    $"test:structured-end-{occurrenceKind.ToString().ToLowerInvariant()}",
                    FixtureBehaviorCatalog.Catalog)
                .AddIce(new IceBehaviorSpec(
                    iceDefinition,
                    strength: 0,
                    iceType: IceType.Barrier,
                    rezCost: 0,
                    subroutines: [],
                    strengthModifiers: []))
                .Add(new CardBehaviorDefinition(iceDefinition, PlayerSide.Corp, triggers: [trigger]))
                .Build();
            state = WithCatalog(
                RunTimingTests.CreateRunState(iceCount: 1, rezzed: true, iceDefinition: iceDefinition),
                catalog);
            sourceId = state.GetZone(state.GetServer(RunTimingTests.RemoteId(state)).IceZoneId).CardIds.Single();
        }

        ServerId server = RunTimingTests.RemoteId(state);
        GameState started = GameEngine.BeginRun(state, server, catalog);
        GameState afterInitiation = PassTwice(started, catalog).State;
        GameState reaction = occurrenceKind == OccurrenceKind.ServerApproached
            ? afterInitiation
            : PassTwice(afterInitiation, catalog).State;
        Assert.Equal(
            occurrenceKind == OccurrenceKind.ServerApproached
                ? RunTimingStep.Success
                : RunTimingStep.EncounterPaidWindow,
            reaction.RunState?.Step);
        PendingDecision decision = Assert.IsType<PendingDecision>(reaction.PendingDecision);
        LegalAction use = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.UseTrigger);
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(reaction.ResolutionStack[^1]);
        Assert.Equal(sourceId, checkpoint.TriggerQueue[0].SourceCardId);

        ApplyResult ended = GameEngine.Apply(
            reaction,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, use.Id),
            catalog);

        Assert.True(ended.Accepted, ended.Rejection?.Message);
        Assert.Null(ended.State.RunState);
        Assert.Null(ended.State.PendingDecision);
        Assert.Empty(ended.State.ResolutionStack);
        Assert.Single(ended.Events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.DoesNotContain(ended.Events, @event => @event.Kind == EngineEventKinds.RunSuccessful);
    }

    [Fact]
    public void EndRunStandaloneBehaviorIsRejected()
    {
        CardDefinitionId definitionId = new("test:structured-end-standalone");
        CardInstanceId sourceId = new("card:structured-end-standalone");
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:structured-end-standalone-catalog")
            .Add(new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.EndRun)]))
            .Build();
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;

        Assert.Throws<ResolutionFailureException>(() => GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, corp),
            catalog));
    }

    private static bool IsOption(LegalAction action, string key) =>
        action.Parameters is BehaviorChoiceParameters parameters
        && parameters.OptionKeys.SequenceEqual([key]);

    private static BehaviorCatalog ChoiceCatalog(
        CardDefinitionId definitionId,
        params BehaviorChoiceOption[] options)
    {
        BehaviorChoiceSpec choice = new(
            "structured-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            options);
        return new BehaviorCatalogBuilder("test:structured-choice-catalog")
            .Add(new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]))
            .Build();
    }

    private static GameState BeginChoice(
        GameState state,
        BehaviorCatalog catalog,
        CardInstanceId sourceId,
        PlayerId controller) => GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceId, controller),
            catalog);

    private static ApplyResult ApplyAction(
        GameState state,
        LegalAction action,
        BehaviorCatalog catalog) => GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            catalog);

    private static (GameState State, BehaviorCatalog Catalog, PlayerId Corp, CardInstanceId First, CardInstanceId Second)
        BeginTwoCardChoice()
    {
        CardDefinitionId definitionId = new("test:structured-two-card-choice");
        CardDefinitionId costDefinitionId = new("test:structured-two-card-cost-card");
        CardInstanceId sourceId = new("card:structured-two-card-source");
        CardInstanceId first = new("card:structured-two-card-first");
        CardInstanceId second = new("card:structured-two-card-second");
        BehaviorTarget target = BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            new BehaviorCardFilter(
                definitionId: costDefinitionId,
                ownerSide: PlayerSide.Corp));
        BehaviorChoiceSpec choice = new(
            "two-card-choice",
            BehaviorChooser.Controller,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [
                new BehaviorChoiceOption(
                    "pay",
                    [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                    costs:
                    [
                        new BehaviorCost(
                            BehaviorCostKind.Additional,
                            amount: 2,
                            additionalKey: "trash-two",
                            additionalTarget: target,
                            additionalKind: BehaviorAdditionalCostKind.TrashCards),
                    ]),
                new BehaviorChoiceOption("fallback", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]),
            ]);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:structured-two-card-catalog")
            .Add(new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice)]))
            .Build();
        GameState state = CreateState(
            catalog,
            (sourceId, definitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (first, costDefinitionId, ZoneKind.CorpHand, PlayerSide.Corp),
            (second, costDefinitionId, ZoneKind.CorpHand, PlayerSide.Corp));
        PlayerId corp = state.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        return (BeginChoice(state, catalog, sourceId, corp), catalog, corp, first, second);
    }

    private static int Tags(GameState state, PlayerId playerId) =>
        state.RulesState!.Players.Single(player => player.PlayerId == playerId).Tags;

    private static GameState ReplacePlayer(
        GameState state,
        PlayerId playerId,
        int? credits = null,
        int? clicks = null) => CopyState(
        state,
        players: state.Players
            .Select(player => player.Id == playerId
                ? new PlayerState(
                    player.Id,
                    player.Side,
                    credits ?? player.Credits,
                    clicks ?? player.Clicks,
                    player.AgendaPoints)
                : player)
            .ToArray());

    private static GameState WithRules(GameState state)
    {
        RulesState rules = state.RulesState ?? RulesState.CreateDefault(state.Players);
        return new GameState(
            state.SchemaVersion,
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState,
            rules,
            state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion,
            state.RuntimeCardCatalogFingerprint);
    }

    private static GameState ReplacePending(GameState state, IReadOnlyList<LegalAction> actions) => CopyState(
        state,
        pendingDecision: new PendingDecision(
            state.PendingDecision!.Id,
            state.PendingDecision.Revision,
            state.PendingDecision.Owner,
            state.PendingDecision.Kind,
            state.PendingDecision.Prompt,
            actions));

    private static GameState MoveCard(GameState state, CardInstanceId cardId, ZoneKind destinationKind)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.GetZone(card.ZoneId);
        ZoneState destination = state.GetZone(destinationKind);
        CardInstanceState moved = new(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            faceUp: destination.Kind is ZoneKind.CorpDiscard or ZoneKind.RunnerHeap || card.FaceUp,
            rezzed: false,
            card.AdvancementCounters);
        ZoneState[] zones = state.Zones
            .Select(zone => zone.Id == source.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destination.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        return CopyState(
            state,
            cards: state.Cards.Select(candidate => candidate.Id == cardId ? moved : candidate).ToArray(),
            zones: zones);
    }

    private static GameState CreateState(
        BehaviorCatalog catalog,
        params (CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)[] placements)
    {
        GameState baseline = GameState.CreateNew(
            seed: 41020,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        CardInstanceState[] cards = placements
            .Select(placement => new CardInstanceState(
                placement.Id,
                placement.DefinitionId,
                baseline.GetZone(placement.Zone).Id,
                placement.Owner,
                faceUp: placement.Zone is ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone =>
            {
                CardInstanceId[] additions = placements
                    .Where(placement => placement.Zone == zone.Kind)
                    .Select(placement => placement.Id)
                    .ToArray();
                return additions.Length == 0
                    ? zone
                    : new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        zone.CardIds.Concat(additions).ToArray());
            })
            .ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers,
            baseline.Turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            baseline.RulesState,
            catalog.Version,
            catalog.Fingerprint,
            baseline.RuntimeCardCatalogVersion,
            baseline.RuntimeCardCatalogFingerprint);
    }

    private static GameState CopyState(
        GameState state,
        IReadOnlyList<PlayerState>? players = null,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        PendingDecision? pendingDecision = null) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        players ?? state.Players,
        cards ?? state.Cards,
        zones ?? state.Zones,
        state.Servers,
        state.Turn,
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        pendingDecision ?? state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private static GameState WithCatalog(GameState state, BehaviorCatalog catalog) => CopyStateWithCatalog(
        state,
        catalog.Version,
        catalog.Fingerprint);

    private static GameState CopyStateWithCatalog(
        GameState state,
        string behaviorVersion,
        string behaviorFingerprint,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        cards ?? state.Cards,
        zones ?? state.Zones,
        state.Servers,
        state.Turn,
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        behaviorVersion,
        behaviorFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private static GameState AddInstalledSource(
        GameState baseline,
        BehaviorCatalog catalog,
        CardInstanceId sourceId,
        CardDefinitionId sourceDefinition,
        ServerId serverId)
    {
        ServerState server = baseline.GetServer(serverId);
        ZoneState root = baseline.GetZone(server.RootZoneId);
        CardInstanceState source = new(
            sourceId,
            sourceDefinition,
            root.Id,
            PlayerSide.Corp,
            faceUp: true,
            rezzed: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == root.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(sourceId).ToArray())
                : zone)
            .ToArray();
        return CopyStateWithCatalog(
            baseline,
            catalog.Version,
            catalog.Fingerprint,
            baseline.Cards.Append(source).ToArray(),
            zones);
    }

    private static ApplyResult Pass(GameState state, BehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    private static ApplyResult PassTwice(GameState state, BehaviorCatalog catalog)
    {
        ApplyResult first = Pass(state, catalog);
        return Pass(first.State, catalog);
    }
}
