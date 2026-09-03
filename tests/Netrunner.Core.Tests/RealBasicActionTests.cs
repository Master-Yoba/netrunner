using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused coverage for the real-turn tag, resource, and purge actions.</summary>
public sealed class RealBasicActionTests
{
    [Fact]
    public void RunnerRemovesExactlyOneOfMultipleTagsWithTypedEvidenceInOrder()
    {
        Fixture fixture = CreateFixture(PlayerSide.Runner, tags: 2);
        GameState state = OpenTurnAction(fixture);
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RemoveTag);
        ulong allocatorPosition = state.Allocator.NextSequence;
        Assert.Single(GameEngine.MaterializeRemoveTagParameters(state));
        Assert.Equal(allocatorPosition, state.Allocator.NextSequence);
        int removeIndex = state.PendingDecision.LegalActions.ToList().IndexOf(action);
        Assert.Equal(
            removeIndex - 1,
            state.PendingDecision.LegalActions
                .Select((candidate, index) => (candidate.Kind, index))
                .Where(value => value.Kind is not (ActionKind.RemoveTag or ActionKind.TrashTaggedResource or ActionKind.PurgeVirusCounters))
                .Max(value => value.index));
        PlayerState before = state.GetPlayer(fixture.RunnerId);

        ApplyResult result = Apply(fixture, state, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.TagsAdjusted,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(@event => @event.Kind));
        PlayerState after = result.State.GetPlayer(fixture.RunnerId);
        Assert.Equal(before.Clicks - 1, after.Clicks);
        Assert.Equal(before.Credits - 2, after.Credits);
        Assert.Equal(1, result.State.RulesState!.Players.Single(value => value.PlayerId == fixture.RunnerId).Tags);
        Assert.Equal(
            [new TagTurnChange(fixture.RunnerId, -1)],
            result.State.Turn.ThisTurnFacts!.TagTurnChanges);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack.Single()).Stage);
    }

    [Fact]
    public void CorpTrashesTaggedResourceInPhysicalRigOrderWithTypedEvidenceInOrder()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, tags: 1, resourceCount: 2);
        GameState state = OpenTurnAction(fixture);
        TrashTaggedResourceParameters[] actions = state.PendingDecision!
            .LegalActions
            .Where(candidate => candidate.Kind == ActionKind.TrashTaggedResource)
            .Select(candidate => Assert.IsType<TrashTaggedResourceParameters>(candidate.Parameters))
            .ToArray();
        Assert.Equal(fixture.ResourceIds, actions.Select(value => value.CardId));
        ulong allocatorPosition = state.Allocator.NextSequence;
        Assert.Equal(
            fixture.ResourceIds,
            GameEngine.MaterializeTrashTaggedResourceParameters(state, fixture.RuntimeCatalog)
                .Select(value => value.CardId));
        Assert.Equal(allocatorPosition, state.Allocator.NextSequence);
        int firstNewIndex = state.PendingDecision.LegalActions
            .Select((candidate, index) => (candidate.Kind, index))
            .Where(value => value.Kind is ActionKind.TrashTaggedResource or ActionKind.PurgeVirusCounters)
            .Min(value => value.index);
        Assert.All(
            state.PendingDecision.LegalActions
                .Take(firstNewIndex),
            candidate => Assert.DoesNotContain(
                candidate.Kind,
                new[] { ActionKind.RemoveTag, ActionKind.TrashTaggedResource, ActionKind.PurgeVirusCounters }));
        int purgeIndex = state.PendingDecision.LegalActions
            .ToList()
            .FindIndex(candidate => candidate.Kind == ActionKind.PurgeVirusCounters);
        Assert.All(
            state.PendingDecision.LegalActions
                .Where(candidate => candidate.Kind == ActionKind.TrashTaggedResource),
            candidate => Assert.True(state.PendingDecision.LegalActions.ToList().IndexOf(candidate) < purgeIndex));

        LegalAction action = Assert.Single(
            state.PendingDecision.LegalActions,
            candidate => candidate.Kind == ActionKind.TrashTaggedResource
                && Assert.IsType<TrashTaggedResourceParameters>(candidate.Parameters).CardId == fixture.ResourceIds[0]);
        PlayerState before = state.GetPlayer(fixture.CorpId);

        ApplyResult result = Apply(fixture, state, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.ClickSpent,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.CardMoved,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(@event => @event.Kind));
        PlayerState after = result.State.GetPlayer(fixture.CorpId);
        Assert.Equal(before.Clicks - 1, after.Clicks);
        Assert.Equal(before.Credits - 2, after.Credits);
        CardInstanceState trashed = result.State.GetCard(fixture.ResourceIds[0]);
        Assert.Equal(ZoneKind.RunnerHeap, result.State.GetZone(trashed.ZoneId).Kind);
        Assert.True(trashed.FaceUp);
        Assert.Contains(fixture.ResourceIds[1], result.State.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision!.Kind);
    }

    [Fact]
    public void CorpPurgeEmitsCheckedTokenTotalAndSupportsZeroMultipleAndNullRulesState()
    {
        AssertPurgeCase(initializeRules: true, counterSpecs: [], expectedRemoved: 0);
        AssertPurgeCase(
            initializeRules: true,
            counterSpecs: [(2, "virus"), (7, "power"), (3, "virus")],
            expectedRemoved: 5);
        AssertPurgeCase(initializeRules: false, counterSpecs: [], expectedRemoved: 0);
    }

    [Fact]
    public void BasicActionProjectionsCloseAtEveryOneBelowThreshold()
    {
        Fixture runnerFixture = CreateFixture(PlayerSide.Runner, tags: 1);
        GameState runnerAction = OpenTurnAction(runnerFixture);
        Assert.Single(GameEngine.MaterializeRemoveTagParameters(runnerAction));
        Assert.Empty(GameEngine.MaterializeRemoveTagParameters(
            ReplacePlayer(runnerAction, PlayerSide.Runner, clicks: 0)));
        Assert.Empty(GameEngine.MaterializeRemoveTagParameters(
            ReplacePlayer(runnerAction, PlayerSide.Runner, credits: 1)));
        Assert.Empty(GameEngine.MaterializeRemoveTagParameters(
            OpenTurnAction(CreateFixture(PlayerSide.Runner, tags: 0))));

        Fixture resourceFixture = CreateFixture(PlayerSide.Corp, tags: 1, resourceCount: 1);
        GameState resourceAction = OpenTurnAction(resourceFixture);
        Assert.Single(GameEngine.MaterializeTrashTaggedResourceParameters(
            resourceAction,
            resourceFixture.RuntimeCatalog));
        Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(
            ReplacePlayer(resourceAction, PlayerSide.Corp, clicks: 0),
            resourceFixture.RuntimeCatalog));
        Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(
            ReplacePlayer(resourceAction, PlayerSide.Corp, credits: 1),
            resourceFixture.RuntimeCatalog));
        Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(
            OpenTurnAction(CreateFixture(PlayerSide.Corp, tags: 0, resourceCount: 1)),
            resourceFixture.RuntimeCatalog));

        Fixture purgeFixture = CreateFixture(PlayerSide.Corp, initializeRules: false);
        GameState purgeAction = OpenTurnAction(purgeFixture);
        Assert.Single(GameEngine.MaterializePurgeVirusCountersParameters(purgeAction));
        Assert.Empty(GameEngine.MaterializePurgeVirusCountersParameters(
            ReplacePlayer(purgeAction, PlayerSide.Corp, clicks: 2)));
    }

    [Fact]
    public void ForgedBasicActionFamiliesAreDiagnosedAndRejectedAtomically()
    {
        Fixture removeFixture = CreateFixture(PlayerSide.Runner, tags: 1);
        GameState removeState = OpenTurnAction(removeFixture);
        LegalAction remove = Assert.Single(
            removeState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RemoveTag);
        AssertRejectsForgery(
            removeFixture,
            removeState,
            removeState.PendingDecision.LegalActions.Where(candidate => candidate.Id != remove.Id).ToArray(),
            remove.Id,
            "decision.missing-remove-tag-action");
        AssertRejectsForgery(
            removeFixture,
            removeState,
            removeState.PendingDecision.LegalActions.Append(new LegalAction(
                new ActionId("action:real-basic-remove-extra"),
                ActionKind.RemoveTag,
                "Remove extra tag",
                new RemoveTagParameters(1, 2))).ToArray(),
            new ActionId("action:real-basic-remove-extra"),
            "decision.extra-remove-tag-action");
        AssertRejectsForgery(
            removeFixture,
            removeState,
            removeState.PendingDecision.LegalActions.Append(new LegalAction(
                new ActionId("action:real-basic-remove-duplicate"),
                ActionKind.RemoveTag,
                "Remove duplicate tag",
                new RemoveTagParameters(1, 2))).ToArray(),
            new ActionId("action:real-basic-remove-duplicate"),
            "decision.duplicate-remove-tag-action");
        AssertRejectsForgery(
            removeFixture,
            removeState,
            ReplaceActionParameters(
                removeState.PendingDecision.LegalActions,
                remove.Id,
                new RemoveTagParameters(0, 2)),
            remove.Id,
            "decision.remove-tag-click-cost-mismatch");
        AssertRejectsForgery(
            removeFixture,
            removeState,
            ReplaceActionParameters(
                removeState.PendingDecision.LegalActions,
                remove.Id,
                new RemoveTagParameters(1, 1)),
            remove.Id,
            "decision.remove-tag-credit-cost-mismatch");
        AssertRejectsForgery(
            removeFixture,
            removeState,
            ReplaceActionParameters(
                removeState.PendingDecision.LegalActions,
                remove.Id,
                new NoActionParameters()),
            remove.Id,
            "decision.action-parameters-mismatch");

        Fixture resourceFixture = CreateFixture(PlayerSide.Corp, tags: 1, resourceCount: 2);
        GameState resourceState = OpenTurnAction(resourceFixture);
        LegalAction resource = Assert.Single(
            resourceState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.TrashTaggedResource
                && Assert.IsType<TrashTaggedResourceParameters>(candidate.Parameters).CardId == resourceFixture.ResourceIds[0]);
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            resourceState.PendingDecision.LegalActions.Where(candidate => candidate.Id != resource.Id).ToArray(),
            resource.Id,
            "decision.missing-trash-tagged-resource-action");
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            resourceState.PendingDecision.LegalActions.Append(new LegalAction(
                new ActionId("action:real-basic-resource-extra"),
                ActionKind.TrashTaggedResource,
                "Trash extra resource",
                Assert.IsType<TrashTaggedResourceParameters>(resource.Parameters))).ToArray(),
            new ActionId("action:real-basic-resource-extra"),
            "decision.extra-trash-tagged-resource-action");
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            resourceState.PendingDecision.LegalActions.Append(new LegalAction(
                new ActionId("action:real-basic-resource-duplicate"),
                ActionKind.TrashTaggedResource,
                "Trash duplicate resource",
                Assert.IsType<TrashTaggedResourceParameters>(resource.Parameters))).ToArray(),
            new ActionId("action:real-basic-resource-duplicate"),
            "decision.duplicate-trash-tagged-resource-action");
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            ReplaceActionParameters(
                resourceState.PendingDecision.LegalActions,
                resource.Id,
                new TrashTaggedResourceParameters(new CardInstanceId("card:real-basic-unknown"), 1, 2)),
            resource.Id,
            "decision.unknown-tagged-resource-target");
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            ReplaceActionParameters(
                resourceState.PendingDecision.LegalActions,
                resource.Id,
                new TrashTaggedResourceParameters(
                    Assert.IsType<TrashTaggedResourceParameters>(resource.Parameters).CardId,
                    0,
                    2)),
            resource.Id,
            "decision.trash-tagged-resource-click-cost-mismatch");
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            ReplaceActionParameters(
                resourceState.PendingDecision.LegalActions,
                resource.Id,
                new TrashTaggedResourceParameters(
                    Assert.IsType<TrashTaggedResourceParameters>(resource.Parameters).CardId,
                    1,
                    1)),
            resource.Id,
            "decision.trash-tagged-resource-credit-cost-mismatch");
        AssertRejectsForgery(
            resourceFixture,
            resourceState,
            ReplaceActionParameters(
                resourceState.PendingDecision.LegalActions,
                resource.Id,
                new NoActionParameters()),
            resource.Id,
            "decision.action-parameters-mismatch");

        Fixture purgeFixture = CreateFixture(PlayerSide.Corp, initializeRules: false);
        GameState purgeState = OpenTurnAction(purgeFixture);
        LegalAction purge = Assert.Single(
            purgeState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.PurgeVirusCounters);
        AssertRejectsForgery(
            purgeFixture,
            purgeState,
            purgeState.PendingDecision.LegalActions.Where(candidate => candidate.Id != purge.Id).ToArray(),
            purge.Id,
            "decision.missing-purge-virus-counters-action");
        AssertRejectsForgery(
            purgeFixture,
            purgeState,
            purgeState.PendingDecision.LegalActions.Append(new LegalAction(
                new ActionId("action:real-basic-purge-extra"),
                ActionKind.PurgeVirusCounters,
                "Purge extra",
                new PurgeVirusCountersParameters(3))).ToArray(),
            new ActionId("action:real-basic-purge-extra"),
            "decision.extra-purge-virus-counters-action");
        AssertRejectsForgery(
            purgeFixture,
            purgeState,
            purgeState.PendingDecision.LegalActions.Append(new LegalAction(
                new ActionId("action:real-basic-purge-duplicate"),
                ActionKind.PurgeVirusCounters,
                "Purge duplicate",
                new PurgeVirusCountersParameters(3))).ToArray(),
            new ActionId("action:real-basic-purge-duplicate"),
            "decision.duplicate-purge-virus-counters-action");
        AssertRejectsForgery(
            purgeFixture,
            purgeState,
            ReplaceActionParameters(
                purgeState.PendingDecision.LegalActions,
                purge.Id,
                new PurgeVirusCountersParameters(2)),
            purge.Id,
            "decision.purge-virus-counters-click-cost-mismatch");
        AssertRejectsForgery(
            purgeFixture,
            purgeState,
            ReplaceActionParameters(
                purgeState.PendingDecision.LegalActions,
                purge.Id,
                new NoActionParameters()),
            purge.Id,
            "decision.action-parameters-mismatch");
    }

    [Fact]
    public void TaggedResourceProjectionRequiresEveryPhysicalRuntimeFactAndDoesNotAllocate()
    {
        Fixture fixture = CreateFixture(PlayerSide.Corp, tags: 1, resourceCount: 1);
        GameState state = OpenTurnAction(fixture);
        CardInstanceId cardId = fixture.ResourceIds.Single();
        CardInstanceState card = state.GetCard(cardId);
        ulong allocatorPosition = state.Allocator.NextSequence;

        Assert.Single(GameEngine.MaterializeTrashTaggedResourceParameters(state, fixture.RuntimeCatalog));
        Assert.Equal(allocatorPosition, state.Allocator.NextSequence);

        ZoneId heapId = state.GetZone(ZoneKind.RunnerHeap).Id;
        var cardVariants = new[]
        {
            new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, PlayerSide.Corp, faceUp: true),
            new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide, faceUp: false),
            new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide, faceUp: true, rezzed: true),
            new CardInstanceState(card.Id, card.DefinitionId, heapId, card.OwnerSide, faceUp: true),
        };
        foreach (CardInstanceState variant in cardVariants)
        {
            GameState forged = Rebuild(
                state,
                cards: state.Cards.Select(candidate => candidate.Id == cardId ? variant : candidate).ToArray());
            Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(forged, fixture.RuntimeCatalog));
        }

        GameState absentFromRig = Rebuild(
            state,
            zones: state.Zones
                .Select(zone => zone.Id == state.GetZone(ZoneKind.RunnerRig).Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId)
                    : zone)
                .ToArray());
        Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(absentFromRig, fixture.RuntimeCatalog));

        CardDefinitionId definitionId = card.DefinitionId;
        RuntimeCardCatalog wrongClassCatalog = new RuntimeCardCatalogBuilder("runtime:real-basic-wrong-class")
            .Add(new CardRuntimeSpec(
                definitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 2))
            .Build();
        Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(state, wrongClassCatalog));
        Assert.Empty(GameEngine.MaterializeTrashTaggedResourceParameters(
            state,
            RuntimeCardCatalog.Empty("runtime:real-basic-missing")));
    }

    [Fact]
    public void PendingBasicActionJsonHashAndPublicTagObservationRoundTrip()
    {
        Fixture[] fixtures =
        [
            CreateFixture(PlayerSide.Runner, tags: 2),
            CreateFixture(PlayerSide.Corp, tags: 1, resourceCount: 1),
            CreateFixture(PlayerSide.Corp, initializeRules: false),
        ];
        foreach (Fixture fixture in fixtures)
        {
            GameState state = OpenTurnAction(fixture);
            string json = EngineJson.Serialize(state);
            GameState restored = EngineJson.DeserializeState(json, fixture.BehaviorCatalog, fixture.RuntimeCatalog);

            Assert.Equal(json, EngineJson.Serialize(restored));
            Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(restored));
            Assert.Equal(
                state.PendingDecision!.LegalActions.Select(action => action.Kind),
                restored.PendingDecision!.LegalActions.Select(action => action.Kind));
        }

        Fixture taggedFixture = fixtures[0];
        GameState taggedState = OpenTurnAction(taggedFixture);
        foreach (PlayerSide viewer in new[] { PlayerSide.Runner, PlayerSide.Corp })
        {
            Observation observation = ObservationBuilder.Build(taggedState, viewer, taggedFixture.BehaviorCatalog);
            IReadOnlyDictionary<string, object?> players =
                Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(observation.PublicState["players"]);
            IReadOnlyDictionary<string, object?> runner =
                Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(players["runner"]);
            Assert.Equal(2, runner["tags"]);
        }

        Fixture legacyFixture = CreateFixture(PlayerSide.Corp, initializeRules: false);
        IReadOnlyDictionary<string, object?> legacyPlayers =
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
                ObservationBuilder.Build(legacyFixture.State, PlayerSide.Runner, legacyFixture.BehaviorCatalog).PublicState["players"]);
        IReadOnlyDictionary<string, object?> legacyRunner =
            Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(legacyPlayers["runner"]);
        Assert.False(legacyRunner.ContainsKey("tags"));
    }

    [Fact]
    public void StaleBasicActionWalletTagsCardsAndCatalogAreRejectedAtomically()
    {
        Fixture removeFixture = CreateFixture(PlayerSide.Runner, tags: 1);
        GameState removeState = OpenTurnAction(removeFixture);
        ActionId removeId = Assert.Single(
            removeState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.RemoveTag).Id;
        AssertStaleRejected(
            removeFixture,
            ReplacePlayer(removeState, PlayerSide.Runner, clicks: 0),
            removeId);
        AssertStaleRejected(
            removeFixture,
            ReplacePlayer(removeState, PlayerSide.Runner, credits: 1),
            removeId);
        AssertStaleRejected(
            removeFixture,
            ReplaceRules(removeState, tags: 0),
            removeId);
        AssertCatalogRejected(removeFixture, removeState, removeId);

        Fixture resourceFixture = CreateFixture(PlayerSide.Corp, tags: 1, resourceCount: 1);
        GameState resourceState = OpenTurnAction(resourceFixture);
        ActionId resourceId = Assert.Single(
            resourceState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.TrashTaggedResource).Id;
        AssertStaleRejected(
            resourceFixture,
            ReplacePlayer(resourceState, PlayerSide.Corp, clicks: 0),
            resourceId);
        AssertStaleRejected(
            resourceFixture,
            ReplacePlayer(resourceState, PlayerSide.Corp, credits: 1),
            resourceId);
        AssertStaleRejected(
            resourceFixture,
            ReplaceRules(resourceState, tags: 0),
            resourceId);
        CardInstanceId resourceCardId = resourceFixture.ResourceIds.Single();
        ZoneId heapId = resourceState.GetZone(ZoneKind.RunnerHeap).Id;
        GameState movedResource = Rebuild(
            resourceState,
            cards: resourceState.Cards.Select(card => card.Id == resourceCardId
                ? new CardInstanceState(card.Id, card.DefinitionId, heapId, card.OwnerSide, faceUp: true)
                : card).ToArray());
        AssertStaleRejected(resourceFixture, movedResource, resourceId);
        GameState flippedResource = Rebuild(
            resourceState,
            cards: resourceState.Cards.Select(card => card.Id == resourceCardId
                ? new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide, faceUp: false)
                : card).ToArray());
        AssertStaleRejected(resourceFixture, flippedResource, resourceId);
        GameState rezzedResource = Rebuild(
            resourceState,
            cards: resourceState.Cards.Select(card => card.Id == resourceCardId
                ? new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide, faceUp: true, rezzed: true)
                : card).ToArray());
        AssertStaleRejected(resourceFixture, rezzedResource, resourceId);

        RuntimeCardCatalog wrongClassCatalog = new RuntimeCardCatalogBuilder("runtime:real-basic-stale-class")
            .Add(new CardRuntimeSpec(
                resourceState.GetCard(resourceCardId).DefinitionId,
                PlayerSide.Runner,
                RuntimeCardClass.Hardware,
                installCost: 2))
            .Build();
        AssertCompatibleCatalogRejected(
            resourceFixture,
            resourceState,
            resourceId,
            wrongClassCatalog,
            "decision.extra-trash-tagged-resource-action");
        RuntimeCardCatalog missingDefinitionCatalog = RuntimeCardCatalog.Empty("runtime:real-basic-stale-missing");
        AssertCompatibleCatalogRejected(
            resourceFixture,
            resourceState,
            resourceId,
            missingDefinitionCatalog,
            "decision.extra-trash-tagged-resource-action");
        AssertCatalogRejected(resourceFixture, resourceState, resourceId);

        Fixture purgeFixture = CreateFixture(PlayerSide.Corp, initializeRules: false);
        GameState purgeState = OpenTurnAction(purgeFixture);
        ActionId purgeId = Assert.Single(
            purgeState.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.PurgeVirusCounters).Id;
        AssertStaleRejected(
            purgeFixture,
            ReplacePlayer(purgeState, PlayerSide.Corp, clicks: 2),
            purgeId);
        AssertCatalogRejected(purgeFixture, purgeState, purgeId);
    }

    [Fact]
    public void PurgeCounterTotalOverflowRejectsAtomicallyAfterMaterialization()
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            counterSpecs: [(int.MaxValue, "virus"), (1, "virus")]);
        GameState state = OpenTurnAction(fixture);
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.PurgeVirusCounters);
        string before = EngineJson.Serialize(state);

        ApplyResult rejected = Apply(fixture, state, action);

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, rejected.Rejection?.Code);
        Assert.Same(state, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    private static void AssertRejectsForgery(
        Fixture fixture,
        GameState source,
        IReadOnlyList<LegalAction> actions,
        ActionId submittedActionId,
        string diagnosticCode)
    {
        GameState forged = ReplacePending(source, actions);
        IReadOnlyList<StateInvariantViolation>? violations = null;
        Exception? collectException = Record.Exception(() =>
            violations = StateInvariantValidator.Collect(
                forged,
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
        Assert.Null(collectException);
        Assert.Contains(violations!, violation => violation.Code == diagnosticCode);

        string before = EngineJson.Serialize(forged);
        PendingDecision pending = forged.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            forged,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, submittedActionId),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(forged, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    private static LegalAction[] ReplaceActionParameters(
        IReadOnlyList<LegalAction> actions,
        ActionId actionId,
        ActionParameters parameters) => actions
            .Select(action => action.Id == actionId
                ? new LegalAction(action.Id, action.Kind, action.Description, parameters)
                : action)
            .ToArray();

    private static GameState ReplacePending(GameState source, IReadOnlyList<LegalAction> actions) => Rebuild(
        source,
        pendingDecision: new PendingDecision(
            source.PendingDecision!.Id,
            source.PendingDecision.Revision,
            source.PendingDecision.Owner,
            source.PendingDecision.Kind,
            source.PendingDecision.Prompt,
            actions));

    private static GameState ReplacePlayer(
        GameState source,
        PlayerSide side,
        int? credits = null,
        int? clicks = null)
    {
        PlayerState target = source.Players.Single(player => player.Side == side);
        return Rebuild(
            source,
            players: source.Players
                .Select(player => player.Id == target.Id
                    ? new PlayerState(
                        player.Id,
                        player.Side,
                        credits ?? player.Credits,
                        clicks ?? player.Clicks,
                        player.AgendaPoints)
                    : player)
                .ToArray());
    }

    private static GameState ReplaceRules(GameState source, int tags)
    {
        RulesState rules = Assert.IsType<RulesState>(source.RulesState);
        PlayerId runnerId = source.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        RulesState replacement = new(
            rules.Version,
            rules.Players.Select(player => player.PlayerId == runnerId
                ? new PlayerRulesState(
                    player.PlayerId,
                    player.BaseHandSize,
                    player.CoreDamage,
                    tags,
                    player.BaseMemory,
                    player.UsedMemory)
                : player).ToArray(),
            rules.Programs,
            rules.Counters,
            rules.LingeringEffects,
            rules.Triggers,
            rules.Interrupts);
        return Rebuild(source, rulesState: replacement);
    }

    private static void AssertStaleRejected(Fixture fixture, GameState stale, ActionId actionId)
    {
        IReadOnlyList<StateInvariantViolation>? violations = null;
        Exception? exception = Record.Exception(() =>
            violations = StateInvariantValidator.Collect(
                stale,
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog));
        Assert.Null(exception);
        Assert.NotEmpty(violations!);

        string before = EngineJson.Serialize(stale);
        PendingDecision pending = stale.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            stale,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, actionId),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(stale, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    private static void AssertCatalogRejected(
        Fixture fixture,
        GameState state,
        ActionId actionId,
        RuntimeCardCatalog? mismatchedCatalog = null)
    {
        mismatchedCatalog ??= RuntimeCardCatalog.Empty("runtime:real-basic-mismatched");
        string before = EngineJson.Serialize(state);
        PendingDecision pending = state.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, actionId),
            fixture.BehaviorCatalog,
            mismatchedCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(state, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    private static void AssertCompatibleCatalogRejected(
        Fixture fixture,
        GameState source,
        ActionId actionId,
        RuntimeCardCatalog forgedCatalog,
        string diagnosticCode)
    {
        GameState compatible = RebuildRuntimeCatalogPin(source, forgedCatalog);
        IReadOnlyList<StateInvariantViolation>? violations = null;
        Exception? collectException = Record.Exception(() =>
            violations = StateInvariantValidator.Collect(
                compatible,
                fixture.BehaviorCatalog,
                forgedCatalog));
        Assert.Null(collectException);
        Assert.Contains(violations!, violation => violation.Code == diagnosticCode);

        string before = EngineJson.Serialize(compatible);
        PendingDecision pending = compatible.PendingDecision!;
        ApplyResult rejected = GameEngine.Apply(
            compatible,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, actionId),
            fixture.BehaviorCatalog,
            forgedCatalog);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(compatible, rejected.State);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    private static GameState RebuildRuntimeCatalogPin(
        GameState source,
        RuntimeCardCatalog runtimeCardCatalog) => new(
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
        source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        runtimeCardCatalog.Version,
        runtimeCardCatalog.Fingerprint);

    private static GameState Rebuild(
        GameState source,
        IReadOnlyList<PlayerState>? players = null,
        IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null,
        PendingDecision? pendingDecision = null,
        RulesState? rulesState = null) => new(
        source.SchemaVersion,
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        players ?? source.Players,
        cards ?? source.Cards,
        zones ?? source.Zones,
        source.Servers,
        source.Turn,
        source.Outcome,
        source.RandomState,
        source.Allocator,
        source.DecisionRevision,
        source.EventSequence,
        source.ResolutionStack,
        pendingDecision ?? source.PendingDecision,
        source.ActionSequence,
        source.InitialSeed,
        source.RunState,
        rulesState ?? source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private static void AssertPurgeCase(
        bool initializeRules,
        IReadOnlyList<(int Count, string Name)> counterSpecs,
        int expectedRemoved)
    {
        Fixture fixture = CreateFixture(
            PlayerSide.Corp,
            initializeRules: initializeRules,
            counterSpecs: counterSpecs);
        GameState state = OpenTurnAction(fixture);
        LegalAction action = Assert.Single(
            state.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.PurgeVirusCounters);

        ApplyResult result = Apply(fixture, state, action);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.ClickSpent,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.ClickSpent,
                EngineEventKinds.VirusPurged,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(@event => @event.Kind));
        Assert.Equal(
            [2, 1, 0],
            result.Events
                .Where(@event => @event.Kind == EngineEventKinds.ClickSpent)
                .Select(@event => Assert.IsType<ClickSpentEventPayload>(@event.Payload).ClicksRemaining));
        VirusPurgedEventPayload purge = Assert.IsType<VirusPurgedEventPayload>(
            result.Events.Single(@event => @event.Kind == EngineEventKinds.VirusPurged).Payload);
        Assert.Equal(expectedRemoved, purge.CountersRemoved);
        Assert.NotNull(result.State.RulesState);
        Assert.DoesNotContain(result.State.RulesState!.Counters, value => value.CounterName == "virus");
        if (expectedRemoved == 5)
        {
            CardCounterState power = Assert.Single(result.State.RulesState.Counters);
            Assert.Equal("power", power.CounterName);
            Assert.Equal(7, power.Count);
        }
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
    }

    private static GameState OpenTurnAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(
            fixture.State,
            fixture.Side,
            fixture.BehaviorCatalog,
            fixture.RuntimeCatalog);
        while (state.PendingDecision?.Kind != DecisionKind.TurnAction)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction pass = Assert.Single(pending.LegalActions, candidate => candidate.Kind == ActionKind.PassPriority);
            ApplyResult result = GameEngine.Apply(
                state,
                new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pass.Id),
                fixture.BehaviorCatalog,
                fixture.RuntimeCatalog);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        return state;
    }

    private static Fixture CreateFixture(
        PlayerSide side,
        int tags = 0,
        int resourceCount = 0,
        bool initializeRules = true,
        IReadOnlyList<(int Count, string Name)>? counterSpecs = null)
    {
        CardDefinitionId resourceDefinition = new("test:real-basic-resource");
        RuntimeCardCatalog runtimeCatalog = new RuntimeCardCatalogBuilder(
                $"runtime:real-basic-{side.ToString().ToLowerInvariant()}-{resourceCount}")
            .Add(new CardRuntimeSpec(
                resourceDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 2))
            .Build();
        BehaviorCatalog behaviorCatalog = CardBehaviorSpecCatalog.Default;
        GameState baseline = GameState.CreateNew(
            seed: 612,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);

        ZoneState corpDeck = baseline.GetZone(ZoneKind.CorpDeck);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        ZoneState heap = baseline.GetZone(ZoneKind.RunnerHeap);
        var cards = new List<CardInstanceState>();
        List<CardInstanceId> deckCards = [new("card:real-basic-corp-deck")];
        cards.Add(new CardInstanceState(deckCards[0], new CardDefinitionId("test:real-basic-corp-card"), corpDeck.Id, PlayerSide.Corp));

        List<CardInstanceId> resourceIds = [];
        for (int index = 0; index < resourceCount; index++)
        {
            string physicalName = index == 0 ? "z" : $"{index + 1}";
            CardInstanceId cardId = new($"card:real-basic-resource-{physicalName}");
            resourceIds.Add(cardId);
            cards.Add(new CardInstanceState(cardId, resourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true));
        }

        (int Count, string Name)[] requestedCounters = (counterSpecs ?? []).ToArray();
        List<CardInstanceId> counterCardIds = [];
        for (int index = 0; index < requestedCounters.Length; index++)
        {
            CardInstanceId cardId = new($"card:real-basic-counter-{index}");
            counterCardIds.Add(cardId);
            cards.Add(new CardInstanceState(
                cardId,
                new CardDefinitionId($"test:real-basic-counter-card-{index}"),
                heap.Id,
                PlayerSide.Runner,
                faceUp: true));
        }

        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == corpDeck.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, deckCards)
                : zone.Id == rig.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, resourceIds)
                    : zone.Id == heap.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, counterCardIds)
                        : zone)
            .ToArray();
        PlayerState[] players = baseline.Players
            .Select(player => new PlayerState(player.Id, player.Side, credits: 5, clicks: player.Side == PlayerSide.Corp ? 3 : 4))
            .ToArray();
        PlayerId corpId = players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerId runnerId = players.Single(player => player.Side == PlayerSide.Runner).Id;

        RulesState? rules = null;
        if (initializeRules)
        {
            PlayerRulesState[] rulePlayers = players
                .Select(player => new PlayerRulesState(
                    player.Id,
                    baseHandSize: 5,
                    coreDamage: 0,
                    tags: player.Side == PlayerSide.Runner ? tags : 0,
                    baseMemory: player.Side == PlayerSide.Runner ? 4 : 0,
                    usedMemory: 0))
                .ToArray();
            CardCounterState[] counters = requestedCounters
                .Select((value, index) => new CardCounterState(counterCardIds[index], value.Name, value.Count))
                .ToArray();
            rules = new RulesState(rulePlayers, counters: counters);
        }

        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(side, side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner, baseline.Turn.TurnNumber),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            initialSeed: baseline.InitialSeed,
            runState: null,
            rulesState: rules,
            behaviorRegistryVersion: baseline.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: baseline.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: runtimeCatalog.Version,
            runtimeCardCatalogFingerprint: runtimeCatalog.Fingerprint);
        return new Fixture(state, behaviorCatalog, runtimeCatalog, side, corpId, runnerId, resourceIds);
    }

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        RuntimeCardCatalog RuntimeCatalog,
        PlayerSide Side,
        PlayerId CorpId,
        PlayerId RunnerId,
        IReadOnlyList<CardInstanceId> ResourceIds);
}
