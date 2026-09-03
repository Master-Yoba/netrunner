using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Focused coverage for the fixed runtime-catalog rez action contract.
/// </summary>
public sealed class RuntimeRezActionTests
{
    [Theory]
    [InlineData(PlayerSide.Corp, TurnPriorityWindowKind.PaidRezAndScore)]
    [InlineData(PlayerSide.Runner, TurnPriorityWindowKind.PaidAndRez)]
    public void TurnTimingOffersCorpRezForEitherActiveTurnInPhysicalOrder(
        PlayerSide activeSide,
        TurnPriorityWindowKind expectedWindow)
    {
        Fixture fixture = CreateFixture(activeSide, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);

        TurnFrame frame = Assert.IsType<TurnFrame>(prompt.ResolutionStack[^1]);
        Assert.Equal(activeSide, frame.ActiveSide);
        Assert.Equal(expectedWindow, frame.PriorityWindow);
        Assert.Equal(PlayerSide.Corp, frame.PriorityHolder);
        Assert.Equal(PlayerSide.Corp, prompt.GetPlayer(prompt.PendingDecision!.Owner).Side);

        RezCardParameters[] rezzes = prompt.PendingDecision.LegalActions
            .Where(action => action.Kind == ActionKind.RezCard)
            .Select(action => Assert.IsType<RezCardParameters>(action.Parameters))
            .ToArray();

        Assert.Equal(
            [fixture.AssetZeroId, fixture.UpgradeId, fixture.RemoteAssetOneId, fixture.RemoteAssetTwoId],
            rezzes.Select(rez => rez.CardId));
        Assert.Equal([0, 2, 1, 0], rezzes.Select(rez => rez.CreditCost));
        Assert.DoesNotContain(prompt.PendingDecision.LegalActions, action => action.Kind == ActionKind.RezIce);

        ulong allocatorPosition = prompt.Allocator.NextSequence;
        IReadOnlyList<RezCardParameters> projected =
            GameEngine.MaterializeRezCardParameters(prompt, fixture.RuntimeCatalog);
        Assert.Equal(allocatorPosition, prompt.Allocator.NextSequence);
        Assert.Equal(rezzes, projected);
    }

    [Fact]
    public void CentralAssetIsExcludedFromRuntimeRezProjection()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        ServerState central = fixture.State.Servers.First(server => server.Kind == ServerKind.Headquarters);
        GameState malformedTopology = MoveCardToRoot(
            fixture.State,
            fixture.RemoteAssetOneId,
            central.RootZoneId);

        GameState prompt = OpenTurnRezWindow(fixture with { State = malformedTopology });

        Assert.DoesNotContain(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.RemoteAssetOneId);
    }

    [Fact]
    public void RunnerPriorityAndNonPriorityTurnStagesOfferNoCorpRezActions()
    {
        Fixture runnerFixture = CreateFixture(PlayerSide.Runner, corpCredits: 3);
        GameState runnerPriority = GameEngine.BeginTurn(
            runnerFixture.State,
            PlayerSide.Runner,
            runnerFixture.BehaviorCatalog,
            runnerFixture.RuntimeCatalog);
        Assert.Equal(PlayerSide.Runner, runnerPriority.GetPlayer(runnerPriority.PendingDecision!.Owner).Side);
        Assert.DoesNotContain(runnerPriority.PendingDecision.LegalActions, action => action.Kind == ActionKind.RezCard);

        GameState corpPriority = ApplyPass(runnerFixture, runnerPriority).State;
        GameState runnerActionPriority = ApplyPass(runnerFixture, corpPriority).State;
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(runnerActionPriority.ResolutionStack[^1]).Stage);
        Assert.Equal(PlayerSide.Runner, runnerActionPriority.GetPlayer(runnerActionPriority.PendingDecision!.Owner).Side);
        Assert.DoesNotContain(runnerActionPriority.PendingDecision.LegalActions, action => action.Kind == ActionKind.RezCard);
    }

    [Fact]
    public void RuntimeRezProjectionExcludesWrongOwnerZoneAndMissingMetadata()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);
        CardInstanceState card = prompt.GetCard(fixture.RemoteAssetOneId);

        GameState wrongOwner = ReplaceCardWithPending(
            prompt,
            new CardInstanceState(
                card.Id,
                card.DefinitionId,
                card.ZoneId,
                PlayerSide.Runner,
                card.FaceUp,
                card.Rezzed,
                card.AdvancementCounters));
        Assert.DoesNotContain(
            GameEngine.MaterializeRezCardParameters(wrongOwner, fixture.RuntimeCatalog),
            rez => rez.CardId == fixture.RemoteAssetOneId);

        GameState wrongZone = ReplaceCardWithPending(
            prompt,
            new CardInstanceState(
                card.Id,
                card.DefinitionId,
                prompt.GetZone(ZoneKind.CorpDeck).Id,
                card.OwnerSide,
                card.FaceUp,
                card.Rezzed,
                card.AdvancementCounters));
        Assert.DoesNotContain(
            GameEngine.MaterializeRezCardParameters(wrongZone, fixture.RuntimeCatalog),
            rez => rez.CardId == fixture.RemoteAssetOneId);

        RuntimeCardCatalog missingMetadata = new RuntimeCardCatalogBuilder("runtime:rez-missing-target")
            .Add(fixture.RuntimeCatalog.GetSpec(prompt.GetCard(fixture.AssetZeroId).DefinitionId))
            .Build();
        Assert.DoesNotContain(
            GameEngine.MaterializeRezCardParameters(prompt, missingMetadata),
            rez => rez.CardId == fixture.RemoteAssetOneId);
    }

    [Fact]
    public void TurnRezPaysFrozenNonzeroCostAndKeepsCorpPriorityInSameWindow()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);
        LegalAction action = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.UpgradeId);

        int beforeCredits = prompt.GetPlayer(fixture.CorpId).Credits;
        ApplyResult result = Apply(fixture, prompt, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(beforeCredits - 2, result.State.GetPlayer(fixture.CorpId).Credits);
        CardInstanceState card = result.State.GetCard(fixture.UpgradeId);
        Assert.True(card.FaceUp);
        Assert.True(card.Rezzed);
        Assert.Equal(7, card.AdvancementCounters);
        Assert.Equal(
            [EngineEventKinds.CreditsAdjusted, EngineEventKinds.CardRezzed, EngineEventKinds.TurnTimingWindowOpened],
            result.Events.Select(@event => @event.Kind));

        CardRezzedEventPayload payload = Assert.IsType<CardRezzedEventPayload>(result.Events[1].Payload);
        Assert.Equal(fixture.UpgradeId, payload.CardId);
        Assert.Equal(2, payload.CreditCost);

        TurnFrame frame = Assert.IsType<TurnFrame>(result.State.ResolutionStack[^1]);
        Assert.Equal(TurnPriorityWindowKind.PaidRezAndScore, frame.PriorityWindow);
        Assert.Equal(PlayerSide.Corp, frame.PriorityHolder);
        Assert.Equal(0, frame.ConsecutivePasses);
        Assert.Contains(
            result.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.AssetZeroId);
        Assert.DoesNotContain(
            result.State.PendingDecision.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.UpgradeId);
    }

    [Fact]
    public void TurnRezSuppressesZeroCreditAdjustmentAndAcceptsFaceupDerezzedCard()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);
        LegalAction action = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.AssetZeroId);

        ApplyResult result = Apply(fixture, prompt, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);
        Assert.Equal(
            [EngineEventKinds.CardRezzed, EngineEventKinds.TurnTimingWindowOpened],
            result.Events.Select(@event => @event.Kind));

        GameState derezzed = ReplaceCard(
            result.State,
            new CardInstanceState(
                fixture.UpgradeId,
                result.State.GetCard(fixture.UpgradeId).DefinitionId,
                result.State.GetCard(fixture.UpgradeId).ZoneId,
                PlayerSide.Corp,
                faceUp: true,
                rezzed: false,
                advancementCounters: 7));
        GameState reopened = ReopenTurnTiming(derezzed, fixture);
        Assert.Contains(
            reopened.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.UpgradeId);
    }

    [Fact]
    public void CorpUniqueRezTrashesOlderCopiesAcrossServersAfterCardRezzed()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3, uniqueAsset: true);
        CardDefinitionId definitionId = fixture.State.GetCard(fixture.AssetZeroId).DefinitionId;
        CardInstanceId firstOldCopyId = new("card:rez-unique-old-a");
        CardInstanceId secondOldCopyId = new("card:rez-unique-old-z");
        ServerId[] remotes = fixture.State.Servers
            .Where(server => server.Kind == ServerKind.Remote)
            .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
            .Select(server => server.Id)
            .ToArray();
        GameState withCopies = AddCardsToRoots(
            fixture.State,
            definitionId,
            (firstOldCopyId, fixture.State.GetServer(remotes[0]).RootZoneId),
            (secondOldCopyId, fixture.State.GetServer(remotes[1]).RootZoneId));
        withCopies = AddCounters(
            withCopies,
            new CardCounterState(firstOldCopyId, "charge", 1),
            new CardCounterState(secondOldCopyId, "charge", 1));
        fixture = fixture with { State = withCopies };

        GameState prompt = OpenTurnRezWindow(fixture);
        LegalAction rez = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.AssetZeroId);
        Assert.False(prompt.GetCard(fixture.AssetZeroId).FaceUp);
        Assert.False(prompt.GetCard(fixture.AssetZeroId).Rezzed);

        ApplyResult result = Apply(fixture, prompt, rez);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.CardRezzed,
                EngineEventKinds.CardMoved,
                EngineEventKinds.CardMoved,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(@event => @event.Kind));
        Assert.Equal(
            [firstOldCopyId, secondOldCopyId],
            result.Events
                .Where(@event => @event.Kind == EngineEventKinds.CardMoved)
                .Select(@event => Assert.IsType<CardMovedEventPayload>(@event.Payload).CardId));
        Assert.Equal(
            [firstOldCopyId, secondOldCopyId],
            result.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        Assert.DoesNotContain(
            result.State.RulesState?.Counters ?? [],
            counter => counter.CardId == firstOldCopyId
                || counter.CardId == secondOldCopyId);
        Assert.True(result.State.GetCard(fixture.AssetZeroId).FaceUp);
        Assert.True(result.State.GetCard(fixture.AssetZeroId).Rezzed);

        string json = EngineJson.Serialize(result.State);
        GameState restored = EngineJson.DeserializeState(
            json,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(result.State), DeterministicHash.ForState(restored));
    }

    [Fact]
    public void RuntimeRunOffersApproachedIceBeforeAttackedRootAndNeverLegacyRezIce()
    {
        Fixture fixture = CreateFixture(PlayerSide.Runner, corpCredits: 3);
        GameState initiation = GameEngine.BeginRun(
            fixture.State,
            fixture.AttackedServerId,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);

        GameState corpInitiation = ApplyPass(fixture, initiation).State;
        Assert.Equal(RunTimingStep.InitiationPaidWindow, corpInitiation.RunState!.Step);
        Assert.DoesNotContain(corpInitiation.PendingDecision!.LegalActions, action => action.Kind == ActionKind.RezIce);
        Assert.Equal(
            [fixture.AssetZeroId, fixture.UpgradeId, fixture.RemoteAssetOneId, fixture.RemoteAssetTwoId],
            corpInitiation.PendingDecision.LegalActions
                .Where(action => action.Kind == ActionKind.RezCard)
                .Select(action => Assert.IsType<RezCardParameters>(action.Parameters).CardId));

        GameState approachRunner = ApplyPass(fixture, corpInitiation).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, approachRunner.RunState!.Step);
        GameState approachCorp = ApplyPass(fixture, approachRunner).State;
        Assert.Equal(PlayerSide.Corp, approachCorp.GetPlayer(approachCorp.PendingDecision!.Owner).Side);

        RezCardParameters[] rezzes = approachCorp.PendingDecision.LegalActions
            .Where(action => action.Kind == ActionKind.RezCard)
            .Select(action => Assert.IsType<RezCardParameters>(action.Parameters))
            .ToArray();
        Assert.Equal(
            [
                fixture.AssetZeroId,
                fixture.UpgradeId,
                fixture.CurrentIceId,
                fixture.RemoteAssetOneId,
                fixture.RemoteAssetTwoId,
            ],
            rezzes.Select(rez => rez.CardId));
        Assert.DoesNotContain(rezzes, rez => rez.CardId == fixture.OuterIceId);
        Assert.DoesNotContain(approachCorp.PendingDecision.LegalActions, action => action.Kind == ActionKind.RezIce);
    }

    [Fact]
    public void RuntimeRunRezResetsPassesAndDisablesOnlyConsumedApproachCapability()
    {
        Fixture fixture = CreateFixture(PlayerSide.Runner, corpCredits: 3);
        GameState initiation = GameEngine.BeginRun(
            fixture.State,
            fixture.AttackedServerId,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        GameState corpInitiation = ApplyPass(fixture, initiation).State;
        GameState approachRunner = ApplyPass(fixture, corpInitiation).State;
        GameState approachCorp = ApplyPass(fixture, approachRunner).State;
        LegalAction action = Assert.Single(
            approachCorp.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.CurrentIceId);

        ApplyResult result = Apply(fixture, approachCorp, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [EngineEventKinds.CreditsAdjusted, EngineEventKinds.CardRezzed, EngineEventKinds.RunTimingWindowOpened],
            result.Events.Select(@event => @event.Kind));
        Assert.True(result.State.GetCard(fixture.CurrentIceId).Rezzed);
        Assert.True(result.State.GetCard(fixture.CurrentIceId).FaceUp);
        Assert.Equal(PlayerSide.Corp, result.State.GetPlayer(result.State.PendingDecision!.Owner).Side);
        Assert.Equal(0, result.State.RunState!.Priority!.ConsecutivePasses);
        Assert.False(result.State.RunState.Priority.CorpMayRezApproachedIce);
        Assert.True(result.State.RunState.Priority.CorpMayRezNonIce);
        Assert.DoesNotContain(
            result.State.PendingDecision.LegalActions,
            candidate => candidate.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(candidate.Parameters).CardId == fixture.CurrentIceId);
    }

    [Fact]
    public void RuntimeRunNonIceCapabilityCanBeDisabledWithoutRemovingApproachedIceOffer()
    {
        Fixture fixture = CreateFixture(PlayerSide.Runner, corpCredits: 3);
        GameState initiation = GameEngine.BeginRun(
            fixture.State,
            fixture.AttackedServerId,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        GameState corpInitiation = ApplyPass(fixture, initiation).State;
        GameState approachRunner = ApplyPass(fixture, corpInitiation).State;
        GameState approachCorp = ApplyPass(fixture, approachRunner).State;
        RunState run = approachCorp.RunState!;
        RunPriorityState priority = run.Priority!;
        RunState noNonIce = run.WithTiming(
            run.Step,
            run.CurrentIceIndex,
            run.CurrentIceId,
            run.Successful,
            run.Unsuccessful,
            run.EndReason,
            new RunPriorityState(
                priority.Version,
                priority.Window,
                priority.PriorityHolder,
                priority.ConsecutivePasses,
                priority.CorpMayRezApproachedIce,
                corpMayRezNonIce: false),
            run.Encounter,
            run.AccessState);
        GameState projected = ReplaceRunState(approachCorp, noNonIce);

        Assert.Equal(
            [fixture.CurrentIceId],
            GameEngine.MaterializeRezCardParameters(projected, fixture.RuntimeCatalog)
                .Select(rez => rez.CardId));
    }

    [Fact]
    public void RunnerObservationHidesCorpRezDecisionAndDefinitionUntilAcceptedRez()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);

        string runnerBefore = EngineJson.Serialize(
            ObservationBuilder.Build(prompt, PlayerSide.Runner, fixture.BehaviorCatalog));
        string corpBefore = EngineJson.Serialize(
            ObservationBuilder.Build(prompt, PlayerSide.Corp, fixture.BehaviorCatalog));
        Assert.DoesNotContain(fixture.RuntimeCatalog.GetSpec(prompt.GetCard(fixture.AssetZeroId).DefinitionId).DefinitionId.Value, runnerBefore);
        Assert.DoesNotContain("rez-card", runnerBefore, StringComparison.Ordinal);
        Assert.Contains(fixture.RuntimeCatalog.GetSpec(prompt.GetCard(fixture.AssetZeroId).DefinitionId).DefinitionId.Value, corpBefore);

        LegalAction rez = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.AssetZeroId);
        ApplyResult result = Apply(fixture, prompt, rez);
        Assert.True(result.Accepted, result.Rejection?.Message);

        string runnerAfter = EngineJson.Serialize(
            ObservationBuilder.Build(result.State, PlayerSide.Runner, fixture.BehaviorCatalog));
        Assert.Contains(fixture.RuntimeCatalog.GetSpec(prompt.GetCard(fixture.AssetZeroId).DefinitionId).DefinitionId.Value, runnerAfter);
    }

    [Fact]
    public void RuntimeRezDecisionAndTypedEventRoundTripWithStableHash()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, fixture.BehaviorCatalog, fixture.RuntimeCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        LegalAction rez = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.AssetZeroId);
        ApplyResult result = Apply(fixture, prompt, rez);
        Assert.True(result.Accepted, result.Rejection?.Message);
        EngineEvent eventValue = Assert.Single(result.Events, value => value.Kind == EngineEventKinds.CardRezzed);
        string eventJson = EngineJson.Serialize(eventValue);
        EngineEvent restoredEvent = EngineJson.Deserialize<EngineEvent>(eventJson);
        CardRezzedEventPayload payload = Assert.IsType<CardRezzedEventPayload>(restoredEvent.Payload);
        Assert.Equal(fixture.AssetZeroId, payload.CardId);
        Assert.Equal(0, payload.CreditCost);
        Assert.Equal(DeterministicHash.ForEvents([eventValue]), DeterministicHash.ForEvents([restoredEvent]));
    }

    [Fact]
    public void ForgedRezCostIsDiagnosedAndRejectedAtomicallyWithoutEvents()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);
        LegalAction pass = Assert.Single(prompt.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PassPriority);
        LegalAction forgedAction = new(
            new ActionId("action:rez-forged-cost"),
            ActionKind.RezCard,
            "Forged rez",
            new RezCardParameters(fixture.UpgradeId, creditCost: 1));
        GameState forged = ReplacePending(prompt, [pass, forgedAction]);

        Assert.Contains(
            StateInvariantValidator.Collect(forged, fixture.BehaviorCatalog, fixture.RuntimeCatalog),
            violation => violation.Code == "decision.rez-cost-mismatch");
        string before = EngineJson.Serialize(forged);
        Exception? exception = Record.Exception(() =>
            GameEngine.Apply(
                forged,
                new ActionSubmission(forged.PendingDecision!.Owner, forged.PendingDecision.Id, forged.PendingDecision.Revision, forgedAction.Id),
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
        Assert.Null(exception);

        ApplyResult result = GameEngine.Apply(
            forged,
            new ActionSubmission(forged.PendingDecision!.Owner, forged.PendingDecision.Id, forged.PendingDecision.Revision, forgedAction.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, result.Rejection?.Code);
        Assert.Same(forged, result.State);
        Assert.Empty(result.Events);
        Assert.Equal(before, EngineJson.Serialize(result.State));
    }

    [Fact]
    public void ForgedDuplicateAndStaleRezActionsProduceDiagnosticsAndPreserveState()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);
        LegalAction rez = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.AssetZeroId);
        LegalAction duplicate = new(
            new ActionId("action:rez-duplicate"),
            rez.Kind,
            rez.Description,
            new RezCardParameters(fixture.AssetZeroId, 0));
        GameState duplicateState = ReplacePending(
            prompt,
            prompt.PendingDecision.LegalActions.Append(duplicate).ToArray());
        Assert.Contains(
            StateInvariantValidator.Collect(duplicateState, fixture.BehaviorCatalog, fixture.RuntimeCatalog),
            violation => violation.Code == "decision.duplicate-rez-action");
        string duplicateBefore = EngineJson.Serialize(duplicateState);
        ApplyResult duplicateResult = GameEngine.Apply(
            duplicateState,
            new ActionSubmission(duplicateState.PendingDecision!.Owner, duplicateState.PendingDecision.Id, duplicateState.PendingDecision.Revision, duplicate.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(duplicateResult.Accepted);
        Assert.Same(duplicateState, duplicateResult.State);
        Assert.Empty(duplicateResult.Events);
        Assert.Equal(duplicateBefore, EngineJson.Serialize(duplicateResult.State));

        CardInstanceState staleCard = prompt.GetCard(fixture.AssetZeroId);
        GameState staleState = ReplaceCardWithPending(
            prompt,
            new CardInstanceState(
                staleCard.Id,
                staleCard.DefinitionId,
                staleCard.ZoneId,
                staleCard.OwnerSide,
                faceUp: true,
                rezzed: true,
                staleCard.AdvancementCounters));
        Assert.Contains(
            StateInvariantValidator.Collect(staleState, fixture.BehaviorCatalog, fixture.RuntimeCatalog),
            violation => violation.Code == "decision.extra-rez-action");
        string staleBefore = EngineJson.Serialize(staleState);
        Exception? exception = Record.Exception(() =>
            GameEngine.Apply(
                staleState,
                new ActionSubmission(staleState.PendingDecision!.Owner, staleState.PendingDecision.Id, staleState.PendingDecision.Revision, rez.Id),
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
        Assert.Null(exception);
        ApplyResult staleResult = GameEngine.Apply(
            staleState,
            new ActionSubmission(staleState.PendingDecision!.Owner, staleState.PendingDecision.Id, staleState.PendingDecision.Revision, rez.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(staleResult.Accepted);
        Assert.Same(staleState, staleResult.State);
        Assert.Empty(staleResult.Events);
        Assert.Equal(staleBefore, EngineJson.Serialize(staleResult.State));
    }

    [Fact]
    public void StaleRezTopologyAndCatalogRejectAtomicallyWithoutThrowing()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, corpCredits: 3);
        GameState prompt = OpenTurnRezWindow(fixture);
        LegalAction rez = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.RemoteAssetOneId);

        // Keep the issued decision while moving its target out of the frozen
        // server root. The fresh projection must no longer contain that card.
        GameState moved = MoveCardToRoot(
            prompt,
            fixture.RemoteAssetOneId,
            prompt.GetZone(ZoneKind.CorpDeck).Id);
        Exception? topologyCollectException = null;
        IReadOnlyList<StateInvariantViolation>? topologyViolations = null;
        topologyCollectException = Record.Exception(() =>
            topologyViolations = StateInvariantValidator.Collect(
                moved,
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
        Assert.Null(topologyCollectException);
        Assert.Contains(topologyViolations!, violation => violation.Code == "decision.extra-rez-action");

        string movedBefore = EngineJson.Serialize(moved);
        ApplyResult movedResult = GameEngine.Apply(
            moved,
            new ActionSubmission(moved.PendingDecision!.Owner, moved.PendingDecision.Id, moved.PendingDecision.Revision, rez.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(movedResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, movedResult.Rejection?.Code);
        Assert.Same(moved, movedResult.State);
        Assert.Empty(movedResult.Events);
        Assert.Equal(movedBefore, EngineJson.Serialize(movedResult.State));

        // A different injected catalog is also an invalid compatibility
        // boundary, and must be diagnosed without entering reduction.
        RuntimeCardCatalog mismatchedCatalog = RuntimeCardCatalog.Empty("runtime:rez-other");
        Exception? catalogCollectException = null;
        IReadOnlyList<StateInvariantViolation>? catalogViolations = null;
        catalogCollectException = Record.Exception(() =>
            catalogViolations = StateInvariantValidator.Collect(
                prompt,
                fixture.BehaviorCatalog,
                mismatchedCatalog));
        Assert.Null(catalogCollectException);
        Assert.Contains(catalogViolations!, violation => violation.Code == "metadata.runtime-card-version-mismatch");

        string promptBefore = EngineJson.Serialize(prompt);
        ApplyResult catalogResult = GameEngine.Apply(
            prompt,
            new ActionSubmission(prompt.PendingDecision!.Owner, prompt.PendingDecision.Id, prompt.PendingDecision.Revision, rez.Id),
            fixture.BehaviorCatalog,
            mismatchedCatalog);
        Assert.False(catalogResult.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, catalogResult.Rejection?.Code);
        Assert.Same(prompt, catalogResult.State);
        Assert.Empty(catalogResult.Events);
        Assert.Equal(promptBefore, EngineJson.Serialize(catalogResult.State));
    }

    [Fact]
    public void ForgedRunRezSetReportsMissingExtraUnknownWrongPayloadAndStaleCredits()
    {
        Fixture fixture = CreateFixture(PlayerSide.Runner, corpCredits: 3);
        GameState initiation = GameEngine.BeginRun(
            fixture.State,
            fixture.AttackedServerId,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        GameState corpInitiation = ApplyPass(fixture, initiation).State;
        GameState approachRunner = ApplyPass(fixture, corpInitiation).State;
        GameState approachCorp = ApplyPass(fixture, approachRunner).State;
        PendingDecision pending = approachCorp.PendingDecision!;
        LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
        LegalAction currentRez = Assert.Single(
            pending.LegalActions,
            action => action.Kind == ActionKind.RezCard
                && Assert.IsType<RezCardParameters>(action.Parameters).CardId == fixture.CurrentIceId);

        GameState missing = ReplacePending(approachCorp, [pass]);
        Assert.Contains(
            StateInvariantValidator.Collect(missing, fixture.BehaviorCatalog, fixture.RuntimeCatalog),
            violation => violation.Code == "run.missing-rez-action");

        LegalAction unknown = new(
            new ActionId("action:run-rez-unknown"),
            ActionKind.RezCard,
            "Unknown rez",
            new RezCardParameters(new CardInstanceId("card:run-rez-unknown"), 0));
        GameState extra = ReplacePending(approachCorp, [pass, unknown]);
        IReadOnlyList<StateInvariantViolation> extraViolations =
            StateInvariantValidator.Collect(extra, fixture.BehaviorCatalog, fixture.RuntimeCatalog);
        Assert.Contains(extraViolations, violation => violation.Code == "decision.unknown-rez-target");
        Assert.Contains(extraViolations, violation => violation.Code == "run.extra-rez-action");

        LegalAction wrongPayload = new(
            new ActionId("action:run-rez-wrong-payload"),
            ActionKind.RezCard,
            "Wrong payload",
            new NoActionParameters());
        GameState wrong = ReplacePending(approachCorp, [pass, wrongPayload]);
        Assert.Contains(
            StateInvariantValidator.Collect(wrong, fixture.BehaviorCatalog, fixture.RuntimeCatalog),
            violation => violation.Code == "decision.action-parameters-mismatch");

        Fixture zeroCreditsFixture = fixture with { State = ReplaceCorpCredits(approachCorp, 0) };
        IReadOnlyList<StateInvariantViolation> staleCredits = StateInvariantValidator.Collect(
            zeroCreditsFixture.State,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.Contains(staleCredits, violation => violation.Code == "run.extra-rez-action");

        string before = EngineJson.Serialize(extra);
        Exception? exception = Record.Exception(() =>
            GameEngine.Apply(
                extra,
                new ActionSubmission(extra.PendingDecision!.Owner, extra.PendingDecision.Id, extra.PendingDecision.Revision, unknown.Id),
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
        Assert.Null(exception);
        ApplyResult rejected = GameEngine.Apply(
            extra,
            new ActionSubmission(extra.PendingDecision!.Owner, extra.PendingDecision.Id, extra.PendingDecision.Revision, unknown.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(extra, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
        Assert.NotNull(currentRez);
    }

    private static GameState OpenTurnRezWindow(Fixture fixture)
    {
        GameState prompt = GameEngine.BeginTurn(
            fixture.State,
            fixture.ActiveSide,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        if (fixture.ActiveSide == PlayerSide.Runner)
        {
            prompt = ApplyPass(fixture, prompt).State;
        }

        return prompt;
    }

    private static GameState ReopenTurnTiming(GameState state, Fixture fixture)
    {
        GameState detached = Rebuild(
            state,
            resolutionStack: [],
            pendingDecision: null);
        return GameEngine.BeginResolution(
            detached,
            new TurnFrame(
                1,
                ResolutionBehaviorIds.Turn,
                fixture.ActiveSide,
                detached.Turn.TurnNumber,
                TurnFrameStage.BeforeTurnPriority,
                fixture.ActiveSide == PlayerSide.Corp
                    ? TurnPriorityWindowKind.PaidRezAndScore
                    : TurnPriorityWindowKind.PaidAndRez,
                PlayerSide.Corp),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static ApplyResult ApplyPass(Fixture fixture, GameState state)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        return Apply(fixture, state, pass);
    }

    private static Fixture CreateFixture(PlayerSide activeSide, int corpCredits, bool uniqueAsset = false)
    {
        CardRuntimeSpec assetZero = new(
            new CardDefinitionId("test:rez-asset-zero"),
            PlayerSide.Corp,
            RuntimeCardClass.Upgrade,
            rezCost: 0,
            unique: uniqueAsset,
            uniquenessKey: uniqueAsset ? "test:rez-asset-zero" : null);
        CardRuntimeSpec upgrade = new(
            new CardDefinitionId("test:rez-upgrade"),
            PlayerSide.Corp,
            RuntimeCardClass.Upgrade,
            rezCost: 2);
        CardRuntimeSpec remoteAssetOne = new(
            new CardDefinitionId("test:rez-remote-asset-one"),
            PlayerSide.Corp,
            RuntimeCardClass.Asset,
            rezCost: 1);
        CardRuntimeSpec remoteAssetTwo = new(
            new CardDefinitionId("test:rez-remote-asset-two"),
            PlayerSide.Corp,
            RuntimeCardClass.Asset,
            rezCost: 0);
        CardRuntimeSpec agenda = new(
            new CardDefinitionId("test:rez-agenda"),
            PlayerSide.Corp,
            RuntimeCardClass.Agenda,
            agendaPoints: 2,
            advancementRequirement: 3);
        CardRuntimeSpec rezzedAsset = new(
            new CardDefinitionId("test:rez-already-rezzed"),
            PlayerSide.Corp,
            RuntimeCardClass.Upgrade,
            rezCost: 0);
        CardRuntimeSpec expensiveAsset = new(
            new CardDefinitionId("test:rez-expensive"),
            PlayerSide.Corp,
            RuntimeCardClass.Upgrade,
            rezCost: 4);
        CardRuntimeSpec outerIce = new(
            new CardDefinitionId("test:rez-outer-ice"),
            PlayerSide.Corp,
            RuntimeCardClass.Ice,
            rezCost: 1);
        CardRuntimeSpec currentIce = new(
            new CardDefinitionId("test:rez-current-ice"),
            PlayerSide.Corp,
            RuntimeCardClass.Ice,
            rezCost: 2);

        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder("runtime:rez-actions")
            .Add(assetZero)
            .Add(upgrade)
            .Add(remoteAssetOne)
            .Add(remoteAssetTwo)
            .Add(agenda)
            .Add(rezzedAsset)
            .Add(expensiveAsset)
            .Add(outerIce)
            .Add(currentIce)
            .Build();
        BehaviorCatalog behaviorCatalog = new BehaviorCatalogBuilder(EngineCompatibility.BehaviorRegistryVersion).Build();
        GameState baseline = GameState.CreateNew(
            seed: 802,
            behaviorRegistryVersion: behaviorCatalog.Version,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerId attackedServerId = allocator.NextServerId();
        ServerId secondRemoteId = allocator.NextServerId();
        ZoneId attackedIceZoneId = allocator.NextZoneId();
        ZoneId attackedRootZoneId = allocator.NextZoneId();
        ZoneId secondIceZoneId = allocator.NextZoneId();
        ZoneId secondRootZoneId = allocator.NextZoneId();

        CardInstanceId assetZeroId = new("card:rez-asset-zero");
        CardInstanceId upgradeId = new("card:rez-upgrade");
        CardInstanceId remoteAssetOneId = new("card:rez-remote-asset-one");
        CardInstanceId remoteAssetTwoId = new("card:rez-remote-asset-two");
        CardInstanceId agendaId = new("card:rez-agenda");
        CardInstanceId rezzedAssetId = new("card:rez-already-rezzed");
        CardInstanceId expensiveAssetId = new("card:rez-expensive");
        CardInstanceId outerIceId = new("card:rez-outer-ice");
        CardInstanceId currentIceId = new("card:rez-current-ice");

        ServerState attackedServer = new(attackedServerId, ServerKind.Remote, attackedIceZoneId, attackedRootZoneId);
        ServerState secondRemote = new(secondRemoteId, ServerKind.Remote, secondIceZoneId, secondRootZoneId);
        var servers = baseline.Servers.Append(attackedServer).Append(secondRemote).ToArray();
        Dictionary<ZoneId, IReadOnlyList<CardInstanceId>> roots = new()
        {
            [baseline.Servers[0].RootZoneId] = [assetZeroId, upgradeId, rezzedAssetId, expensiveAssetId],
            [attackedRootZoneId] = [remoteAssetOneId, agendaId],
            [secondRootZoneId] = [remoteAssetTwoId],
        };
        var zones = baseline.Zones
            .Select(zone => roots.TryGetValue(zone.Id, out IReadOnlyList<CardInstanceId>? ids)
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, ids)
                : zone)
            .Append(new ZoneState(attackedIceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, attackedServerId, [outerIceId, currentIceId]))
            .Append(new ZoneState(attackedRootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, attackedServerId, [remoteAssetOneId, agendaId]))
            .Append(new ZoneState(secondIceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, secondRemoteId))
            .Append(new ZoneState(secondRootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, secondRemoteId, [remoteAssetTwoId]))
            .ToArray();
        var cards = new[]
        {
            new CardInstanceState(assetZeroId, assetZero.DefinitionId, baseline.Servers[0].RootZoneId, PlayerSide.Corp),
            new CardInstanceState(upgradeId, upgrade.DefinitionId, baseline.Servers[0].RootZoneId, PlayerSide.Corp, faceUp: true, rezzed: false, advancementCounters: 7),
            new CardInstanceState(remoteAssetOneId, remoteAssetOne.DefinitionId, attackedRootZoneId, PlayerSide.Corp),
            new CardInstanceState(remoteAssetTwoId, remoteAssetTwo.DefinitionId, secondRootZoneId, PlayerSide.Corp),
            new CardInstanceState(agendaId, agenda.DefinitionId, attackedRootZoneId, PlayerSide.Corp),
            new CardInstanceState(rezzedAssetId, rezzedAsset.DefinitionId, baseline.Servers[0].RootZoneId, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(expensiveAssetId, expensiveAsset.DefinitionId, baseline.Servers[0].RootZoneId, PlayerSide.Corp),
            new CardInstanceState(outerIceId, outerIce.DefinitionId, attackedIceZoneId, PlayerSide.Corp),
            new CardInstanceState(currentIceId, currentIce.DefinitionId, attackedIceZoneId, PlayerSide.Corp),
        };
        PlayerId corpId = baseline.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runnerId = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        var players = baseline.Players.Select(player => new PlayerState(
            player.Id,
            player.Side,
            player.Side == PlayerSide.Corp ? corpCredits : player.Credits,
            player.Side == PlayerSide.Corp ? 3 : 4,
            player.AgendaPoints)).ToArray();
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            new TurnState(
                activeSide,
                activeSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
                baseline.Turn.TurnNumber),
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            baseline.DecisionRevision,
            baseline.EventSequence,
            [],
            null,
            baseline.ActionSequence,
            baseline.InitialSeed,
            null,
            null,
            behaviorCatalog.Version,
            null,
            runtimeCatalog.Version,
            runtimeCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCatalog);
        return new Fixture(
            state,
            behaviorCatalog,
            runtimeCatalog,
            activeSide,
            corpId,
            runnerId,
            attackedServerId,
            assetZeroId,
            upgradeId,
            remoteAssetOneId,
            remoteAssetTwoId,
            outerIceId,
            currentIceId);
    }

    private static GameState ReplaceCard(GameState source, CardInstanceState replacement)
    {
        CardInstanceState[] cards = source.Cards
            .Select(card => card.Id == replacement.Id ? replacement : card)
            .ToArray();
        return Rebuild(source, cards: cards);
    }

    private static GameState AddCardsToRoots(
        GameState source,
        CardDefinitionId definitionId,
        params (CardInstanceId CardId, ZoneId RootZoneId)[] copies)
    {
        ZoneState[] zones = source.Zones
            .Select(zone =>
            {
                CardInstanceId[] additions = copies
                    .Where(copy => copy.RootZoneId == zone.Id)
                    .Select(copy => copy.CardId)
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
        CardInstanceState[] cards = source.Cards
            .Concat(copies.Select(copy => new CardInstanceState(
                copy.CardId,
                definitionId,
                copy.RootZoneId,
                PlayerSide.Corp,
                faceUp: true,
                rezzed: true)))
            .ToArray();
        return RebuildExact(source, cards, source.ResolutionStack, source.PendingDecision, zones);
    }

    private static GameState AddCounters(GameState source, params CardCounterState[] additions)
    {
        RulesState baseline = source.RulesState ?? RulesState.CreateDefault(source.Players);
        RulesState rules = new(
            baseline.Version,
            baseline.Players,
            baseline.Programs,
            baseline.Counters.Concat(additions).ToArray(),
            baseline.LingeringEffects,
            baseline.Triggers,
            baseline.Interrupts);
        return RebuildExact(
            source,
            source.Cards,
            source.ResolutionStack,
            source.PendingDecision,
            rulesState: rules);
    }

    private static GameState MoveCardToRoot(
        GameState source,
        CardInstanceId cardId,
        ZoneId destinationId)
    {
        CardInstanceState card = source.GetCard(cardId);
        ZoneState sourceZone = source.GetZone(card.ZoneId);
        ZoneState destinationZone = source.GetZone(destinationId);
        ZoneState[] zones = source.Zones
            .Select(zone => zone.Id == sourceZone.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == destinationZone.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        CardInstanceState[] cards = source.Cards
            .Select(candidate => candidate.Id == cardId
                ? new CardInstanceState(
                    candidate.Id,
                    candidate.DefinitionId,
                    destinationId,
                    candidate.OwnerSide,
                    candidate.FaceUp,
                    candidate.Rezzed,
                    candidate.AdvancementCounters)
                : candidate)
            .ToArray();
        return RebuildExact(source, cards, source.ResolutionStack, source.PendingDecision, zones);
    }

    private static GameState ReplaceCardWithPending(GameState source, CardInstanceState replacement)
    {
        CardInstanceState[] cards = source.Cards
            .Select(card => card.Id == replacement.Id ? replacement : card)
            .ToArray();
        return RebuildExact(source, cards, source.ResolutionStack, source.PendingDecision);
    }

    private static GameState ReplacePending(GameState source, IReadOnlyList<LegalAction> actions)
    {
        PendingDecision current = Assert.IsType<PendingDecision>(source.PendingDecision);
        return RebuildExact(
            source,
            source.Cards,
            source.ResolutionStack,
            new PendingDecision(current.Id, current.Revision, current.Owner, current.Kind, current.Prompt, actions));
    }

    private static GameState ReplaceRunState(GameState source, RunState runState) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        source.Players,
        source.Cards,
        source.Zones,
        source.Servers,
        source.Turn,
        source.Outcome,
        source.RandomState,
        source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        source.ResolutionStack,
        source.PendingDecision,
        source.ActionSequence,
        source.InitialSeed,
        runState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static GameState ReplaceCorpCredits(GameState source, int credits)
    {
        PlayerState[] players = source.Players
            .Select(player => player.Side == PlayerSide.Corp
                ? new PlayerState(player.Id, player.Side, credits, player.Clicks, player.AgendaPoints)
                : player)
            .ToArray();
        return new GameState(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            players,
            source.Cards,
            source.Zones,
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);
    }

    private static GameState Rebuild(
        GameState source,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ResolutionFrame>? resolutionStack = null,
        PendingDecision? pendingDecision = null) => new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            cards ?? source.Cards,
            source.Zones,
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            resolutionStack ?? source.ResolutionStack,
            pendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private static GameState RebuildExact(
        GameState source,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ResolutionFrame> resolutionStack,
        PendingDecision? pendingDecision,
        IReadOnlyList<ZoneState>? zones = null,
        RulesState? rulesState = null) => new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            cards,
            zones ?? source.Zones,
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.DecisionRevision,
            source.EventSequence,
            resolutionStack,
            pendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            rulesState ?? source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerSide ActiveSide,
        PlayerId CorpId,
        PlayerId RunnerId,
        ServerId AttackedServerId,
        CardInstanceId AssetZeroId,
        CardInstanceId UpgradeId,
        CardInstanceId RemoteAssetOneId,
        CardInstanceId RemoteAssetTwoId,
        CardInstanceId OuterIceId,
        CardInstanceId CurrentIceId);
}
