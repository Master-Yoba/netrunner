using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Real access-window evidence for Carnivore (30003).  These tests keep the
/// card data fixture deliberately small: the behavior catalog supplies the
/// executable declaration, while the runtime catalog test below supplies the
/// independent printed console metadata.
/// </summary>
public sealed class GatewayBatch2CarnivoreBehaviorTests
{
    [Fact]
    [Trait("GatewayCard", "30003")]
    public void CarnivoreDeclaresMemoryAndTypedAccessTrashCostWithConsoleMetadata()
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        CardBehaviorDefinition definition = behaviorCatalog.GenericSpecs.Single(
            value => value.DefinitionId == GatewayBatch2CardCodes.Carnivore);

        Assert.Equal(PlayerSide.Runner, definition.Controller);
        Assert.Equal(
            new BehaviorStaticModifier(
                BehaviorStaticModifierKind.MemoryUnits,
                delta: 1),
            Assert.Single(definition.StaticModifiers));

        BehaviorPaidAbility ability = Assert.Single(definition.PaidAbilities);
        Assert.Equal("carnivore-access", ability.Key);
        Assert.Equal(BehaviorAbilityTiming.BreachAccessPaidWindow, ability.Timing);
        Assert.Equal(BehaviorAbilityActivation.PaidAbility, ability.Activation);
        Assert.Equal(BehaviorAbilityUsageLimit.OncePerTurn, ability.UsageLimit);
        Assert.Equal([new BehaviorTarget(BehaviorTargetKind.AccessedCard)], ability.Targets);
        BehaviorCost cost = Assert.Single(ability.Costs);
        Assert.Equal(BehaviorCostKind.Additional, cost.Kind);
        Assert.Equal(2, cost.Amount);
        Assert.Equal("trash-cards", cost.AdditionalKey);
        Assert.Equal(BehaviorAdditionalCostKind.TrashCards, cost.AdditionalKind);
        BehaviorTarget target = Assert.IsType<BehaviorTarget>(cost.AdditionalTarget);
        Assert.Equal(BehaviorTargetKind.ZoneCards, target.Kind);
        Assert.Equal(BehaviorZone.Grip, target.Zone);
        Assert.Equal(PlayerSide.Runner, target.Filter?.OwnerSide);
        Assert.False(target.Reveal);
        Assert.Equal(BehaviorInstructionKind.TrashCard, Assert.Single(ability.Instructions).Kind);

        RuntimeCardCatalog runtimeCatalog = GatewayRuntimeCardCatalog.CreateEmbedded();
        if (runtimeCatalog.TryGet(GatewayBatch2CardCodes.Carnivore, out CardRuntimeSpec? runtime))
        {
            Assert.Equal(RuntimeCardClass.Hardware, runtime.CardClass);
            Assert.Equal(PlayerSide.Runner, runtime.Side);
            Assert.Equal(4, runtime.InstallCost);
            Assert.Contains("console", runtime.Subtypes);
            Assert.True(runtime.IsConsole);
            Assert.True(runtime.IsUnique);
            Assert.Equal("runner-console", runtime.ConsoleKey);
        }
        else
        {
            // The repository's checked-in CI payload is a seven-card
            // synthetic DATA-400 fixture. The production/official payload
            // carries Carnivore and is asserted above when available.
            Assert.Equal(CardDataResource.SyntheticMode, CardDataResource.EmbeddedMode);
        }

        string json = EngineJson.Serialize(cost);
        BehaviorCost restored = EngineJson.Deserialize<BehaviorCost>(json);
        Assert.Equal(cost, restored);
        Assert.Equal(json, EngineJson.Serialize(restored));
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void NonAgendaAccessOffersDeterministicExactGripPairsOnlyToRunner()
    {
        Fixture fixture = CreateFixture(gripCount: 3);
        GameState prompt = OpenAccess(fixture);
        PendingDecision decision = Assert.IsType<PendingDecision>(prompt.PendingDecision);

        LegalAction[] abilities = decision.LegalActions
            .Where(action => action.Kind == ActionKind.UseAbility)
            .ToArray();
        Assert.Equal(3, abilities.Length);
        Assert.Equal(ActionKind.NoAccessAction, decision.LegalActions[^1].Kind);

        CardInstanceId[][] expectedPairs =
        [
            [fixture.GripIds[0], fixture.GripIds[1]],
            [fixture.GripIds[0], fixture.GripIds[2]],
            [fixture.GripIds[1], fixture.GripIds[2]],
        ];
        for (int index = 0; index < abilities.Length; index++)
        {
            AbilityActionParameters parameters = Assert.IsType<AbilityActionParameters>(abilities[index].Parameters);
            Assert.Equal(fixture.CarnivoreId, parameters.SourceCardId);
            Assert.Equal([fixture.AccessedId], parameters.TargetCardIds);
            AbilityAdditionalCostSelection selection = Assert.Single(parameters.AdditionalCostSelectionsOrEmpty);
            Assert.Equal(0, selection.CostIndex);
            Assert.Equal("trash-cards", selection.CostKey);
            Assert.Equal(expectedPairs[index], selection.CardIds);
            Assert.Contains(expectedPairs[index][0].Value, abilities[index].Description, StringComparison.Ordinal);
            Assert.Contains(expectedPairs[index][1].Value, abilities[index].Description, StringComparison.Ordinal);
        }

        Observation corp = ObservationBuilder.Build(prompt, PlayerSide.Corp, fixture.BehaviorCatalog);
        Assert.Empty(corp.LegalActions);
        string corpJson = EngineJson.Serialize(corp);
        Assert.DoesNotContain(fixture.GripIds[0].Value, corpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.GripIds[1].Value, corpJson, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.GripIds[2].Value, corpJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void FewerThanTwoGripCardsOffersNoCarnivoreAction()
    {
        Fixture fixture = CreateFixture(gripCount: 1);
        GameState prompt = OpenAccess(fixture);
        Assert.DoesNotContain(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility);
        Assert.Contains(
            prompt.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.NoAccessAction);
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void GripCombinationSearchFailsClosedWhenResultSpaceExceedsBound()
    {
        // C(24, 2) is already larger than the materialization result cap.
        // The explicit exploration budget also protects overlapping future
        // structured costs whose rejected branches would not fill that cap.
        Fixture fixture = CreateFixture(gripCount: 24);
        GameState prompt = OpenAccess(fixture);

        Assert.DoesNotContain(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility);
        Assert.Contains(
            prompt.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.NoAccessAction);
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void CarnivorePaymentTrashesPairAndAccessedCardThenReyieldsNoAction()
    {
        Fixture fixture = CreateFixture(gripCount: 2);
        GameState prompt = OpenAccess(fixture);
        LegalAction use = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility);

        ApplyResult applied = Apply(fixture, prompt, use);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachAccessAbility, applied.State.RunState!.Step);
        Assert.Equal(ActionKind.NoAccessAction, Assert.Single(applied.State.PendingDecision!.LegalActions).Kind);

        ZoneState heap = applied.State.GetZone(ZoneKind.RunnerHeap);
        Assert.Equal(fixture.GripIds, heap.CardIds);
        Assert.All(fixture.GripIds, cardId =>
        {
            CardInstanceState card = applied.State.GetCard(cardId);
            Assert.True(card.FaceUp);
            Assert.False(card.Rezzed);
        });

        ZoneState discard = applied.State.GetZone(ZoneKind.CorpDiscard);
        Assert.Contains(fixture.AccessedId, discard.CardIds);
        CardInstanceState accessed = applied.State.GetCard(fixture.AccessedId);
        Assert.True(accessed.FaceUp);
        Assert.False(accessed.Rezzed);
        Assert.Equal(3, applied.Events.Count(value => value.Kind == EngineEventKinds.CardMoved));
        AccessedCardTrashedEventPayload accessTrash = Assert.Single(
            applied.Events,
            value => value.Kind == EngineEventKinds.AccessedCardTrashed).Payload as AccessedCardTrashedEventPayload
            ?? throw new Xunit.Sdk.XunitException("Expected AccessedCardTrashed payload.");
        Assert.Equal(fixture.AccessedId, accessTrash.CardId);
        Assert.Equal(0, accessTrash.TrashCost);
        Assert.Equal(
            fixture.GripIds.Append(fixture.AccessedId),
            applied.State.Turn.ThisTurnFacts!.TrashedCardOccurrenceIds);

        ApplyResult noAction = Apply(
            fixture,
            applied.State,
            Assert.Single(applied.State.PendingDecision.LegalActions));
        Assert.True(noAction.Accepted, noAction.Rejection?.Message);
        Assert.Null(noAction.State.RunState);
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void AgendaAccessOffersCarnivoreAndTrashingPreventsAutomaticSteal()
    {
        Fixture fixture = CreateFixture(gripCount: 2, agenda: true);
        GameState prompt = OpenAccess(fixture);
        Assert.Contains(prompt.PendingDecision!.LegalActions, action => action.Kind == ActionKind.UseAbility);
        Assert.Contains(prompt.PendingDecision.LegalActions, action => action.Kind == ActionKind.NoAccessAction);

        LegalAction use = Assert.Single(
            prompt.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.UseAbility);
        ApplyResult trashed = Apply(fixture, prompt, use);
        Assert.True(trashed.Accepted, trashed.Rejection?.Message);
        Assert.Contains(fixture.AccessedId, trashed.State.GetZone(ZoneKind.CorpDiscard).CardIds);
        Assert.DoesNotContain(trashed.Events, value => value.Kind == EngineEventKinds.AgendaStolen);

        ApplyResult passed = Apply(
            fixture,
            trashed.State,
            Assert.Single(trashed.State.PendingDecision!.LegalActions));
        Assert.True(passed.Accepted, passed.Rejection?.Message);
        Assert.DoesNotContain(passed.Events, value => value.Kind == EngineEventKinds.AgendaStolen);
        Assert.DoesNotContain(fixture.AccessedId, passed.State.GetZone(ZoneKind.RunnerScore).CardIds);
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void OncePerTurnBlocksSecondAccessWhileGripCardsRemain()
    {
        Fixture fixture = CreateFixture(gripCount: 4, rootCount: 2);
        GameState first = OpenAccess(fixture);
        LegalAction use = first.PendingDecision!.LegalActions.First(
            action => action.Kind == ActionKind.UseAbility);
        ApplyResult paid = Apply(fixture, first, use);
        Assert.True(paid.Accepted, paid.Rejection?.Message);
        ApplyResult firstDone = Apply(
            fixture,
            paid.State,
            Assert.Single(paid.State.PendingDecision!.LegalActions));
        Assert.True(firstDone.Accepted, firstDone.Rejection?.Message);

        PendingDecision nextSelection = Assert.IsType<PendingDecision>(firstDone.State.PendingDecision);
        LegalAction secondCandidate = Assert.Single(
            nextSelection.LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate);
        ApplyResult secondChosen = Apply(fixture, firstDone.State, secondCandidate);
        Assert.True(secondChosen.Accepted, secondChosen.Rejection?.Message);
        Assert.DoesNotContain(
            secondChosen.State.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseAbility);
        Assert.Contains(
            secondChosen.State.PendingDecision.LegalActions,
            action => action.Kind == ActionKind.NoAccessAction);
        Assert.Equal(2, secondChosen.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void OfferedAbilityRoundTripsWithSelectionsAndResumesAtAccessWindow()
    {
        Fixture fixture = CreateFixture(gripCount: 3);
        GameState prompt = OpenAccess(fixture);
        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, fixture.BehaviorCatalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));

        LegalAction original = prompt.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.UseAbility);
        LegalAction resumed = restored.PendingDecision!.LegalActions.First(action => action.Id == original.Id);
        Assert.Equal(
            EngineJson.Serialize(original.Parameters),
            EngineJson.Serialize(resumed.Parameters));
        ApplyResult left = Apply(fixture, prompt, original);
        ApplyResult right = Apply(fixture, restored, resumed);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.True(right.Accepted, right.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));
    }

    [Fact]
    [Trait("GatewayCard", "30003")]
    public void StaleCostCardAndForgedTargetAreRejectedAtomically()
    {
        Fixture fixture = CreateFixture(gripCount: 3);
        GameState prompt = OpenAccess(fixture);
        LegalAction issued = prompt.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.UseAbility);
        AbilityActionParameters parameters = Assert.IsType<AbilityActionParameters>(issued.Parameters);
        AbilityAdditionalCostSelection selection = Assert.Single(parameters.AdditionalCostSelectionsOrEmpty);

        // Replacing the pending action with an otherwise identical payload
        // whose target points at a different live card is a forged state. The
        // decision boundary must reject it before any payment mutation.
        CardInstanceId replacementTarget = fixture.GripIds[2];
        AbilityActionParameters forgedParameters = new(
            parameters.SourceCardId,
            parameters.DefinitionId,
            parameters.AbilityKey,
            parameters.Timing,
            parameters.Activation,
            parameters.Costs,
            [replacementTarget],
            parameters.Usage,
            parameters.CatalogFingerprint,
            parameters.DefinitionFingerprint,
            parameters.SourceZoneId,
            parameters.SourceZoneKind,
            parameters.AdditionalCostSelections);
        GameState forged = ReplacePendingAction(prompt, issued, forgedParameters);
        string forgedHash = DeterministicHash.ForState(forged);
        ApplyResult forgedResult = Apply(
            fixture,
            forged,
            forged.PendingDecision!.LegalActions.Single(action => action.Id == issued.Id));
        Assert.False(forgedResult.Accepted);
        Assert.Equal(forgedHash, DeterministicHash.ForState(forgedResult.State));

        // Move one selected card out of Grip while retaining the issued action
        // payload. This is rejected atomically by the live source preflight.
        GameState stale = MoveCardToRunnerHeap(prompt, selection.CardIds[0]);
        string staleHash = DeterministicHash.ForState(stale);
        ApplyResult staleResult = Apply(fixture, stale, stale.PendingDecision!.LegalActions.Single(action => action.Id == issued.Id));
        Assert.False(staleResult.Accepted);
        Assert.Equal(staleHash, DeterministicHash.ForState(staleResult.State));
    }

    private static Fixture CreateFixture(int gripCount, bool agenda = false, int rootCount = 1)
    {
        BehaviorCatalog behaviorCatalog = GatewayBehaviorCatalog.Create();
        GameState baseline = GameState.CreateNew(
            seed: (ulong)(30003 + gripCount + rootCount + (agenda ? 100 : 0)),
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        PlayerId runnerId = baseline.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerState[] players = baseline.Players
            .Select(player => player.Id == runnerId
                ? new PlayerState(player.Id, player.Side, credits: 5, clicks: 4, player.AgendaPoints)
                : player)
            .ToArray();

        ServerId remoteId = allocator.NextServerId();
        ZoneId iceId = allocator.NextZoneId();
        ZoneId rootId = allocator.NextZoneId();
        List<ServerState> servers = baseline.Servers
            .Append(new ServerState(remoteId, ServerKind.Remote, iceId, rootId))
            .ToList();
        List<ZoneState> zones = baseline.Zones
            .Append(new ZoneState(iceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId))
            .Append(new ZoneState(rootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId))
            .ToList();

        ZoneState rig = zones.Single(zone => zone.Kind == ZoneKind.RunnerRig);
        ZoneState grip = zones.Single(zone => zone.Kind == ZoneKind.RunnerGrip);
        ZoneState root = zones.Single(zone => zone.Id == rootId);
        CardInstanceId carnivoreId = allocator.NextCardInstanceId();
        List<CardInstanceState> cards =
        [
            new(carnivoreId, GatewayBatch2CardCodes.Carnivore, rig.Id, PlayerSide.Runner, faceUp: true),
        ];
        List<CardInstanceId> gripIds = [];
        for (int index = 0; index < gripCount; index++)
        {
            CardInstanceId id = allocator.NextCardInstanceId();
            gripIds.Add(id);
            cards.Add(new CardInstanceState(
                id,
                new CardDefinitionId($"fixture-grip-{index}"),
                grip.Id,
                PlayerSide.Runner,
                faceUp: false));
        }

        List<CardInstanceId> rootIds = [];
        CardDefinitionId accessDefinition = agenda
            ? GatewayCardCodes.OffworldOffice
            : GatewayBatch2CardCodes.SpinDoctor;
        for (int index = 0; index < rootCount; index++)
        {
            CardInstanceId id = allocator.NextCardInstanceId();
            rootIds.Add(id);
            cards.Add(new CardInstanceState(
                id,
                accessDefinition,
                root.Id,
                PlayerSide.Corp,
                faceUp: false));
        }

        ReplaceZone(zones, rig, rig.CardIds.Append(carnivoreId).ToArray());
        ReplaceZone(zones, grip, gripIds);
        ReplaceZone(zones, root, rootIds);

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
        RulesState rules = RulesState.CreateDefault(players);
        GameState state = new(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            players,
            cards,
            zones,
            servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            allocator.State,
            rulesState: rules,
            behaviorRegistryVersion: behaviorCatalog.Version,
            behaviorCatalogFingerprint: behaviorCatalog.Fingerprint);
        StateInvariantValidator.Validate(state, behaviorCatalog);
        return new Fixture(
            state,
            behaviorCatalog,
            runnerId,
            carnivoreId,
            remoteId,
            rootIds[0],
            gripIds,
            accessDefinition);

        static void ReplaceZone(List<ZoneState> zones, ZoneState source, IReadOnlyList<CardInstanceId> cardIds)
        {
            zones[zones.IndexOf(source)] = new ZoneState(
                source.Id,
                source.Kind,
                source.OwnerSide,
                source.ServerId,
                cardIds);
        }
    }

    private static GameState OpenAccess(Fixture fixture)
    {
        GameState started = GameEngine.BeginRun(fixture.State, fixture.RemoteId, fixture.BehaviorCatalog);
        GameState state = started;
        for (int pass = 0; pass < 2; pass++)
        {
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction priorityPass = Assert.Single(
                decision.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = Apply(fixture, state, priorityPass);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }

        PendingDecision selection = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction candidate = selection.LegalActions.First(
            action => action.Kind == ActionKind.ChooseBreachCandidate);
        ApplyResult chosen = Apply(fixture, state, candidate);
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        return chosen.State;
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            fixture.BehaviorCatalog);

    private static GameState ReplacePendingAction(
        GameState source,
        LegalAction original,
        AbilityActionParameters parameters)
    {
        PendingDecision pending = source.PendingDecision!;
        LegalAction forged = new(original.Id, original.Kind, original.Description, parameters);
        PendingDecision replacement = new(
            pending.Id,
            pending.Revision,
            pending.Owner,
            pending.Kind,
            pending.Prompt,
            pending.LegalActions.Select(action => action.Id == original.Id ? forged : action).ToArray());
        return ReplacePending(source, replacement);
    }

    private static GameState MoveCardToRunnerHeap(GameState source, CardInstanceId cardId)
    {
        CardInstanceState card = source.GetCard(cardId);
        ZoneState grip = source.GetZone(ZoneKind.RunnerGrip);
        ZoneState heap = source.GetZone(ZoneKind.RunnerHeap);
        CardInstanceState moved = new(card.Id, card.DefinitionId, heap.Id, card.OwnerSide, faceUp: true);
        ZoneState[] zones = source.Zones
            .Select(zone => zone.Id == grip.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == heap.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cardId).ToArray())
                    : zone)
            .ToArray();
        CardInstanceState[] cards = source.Cards
            .Select(value => value.Id == cardId ? moved : value)
            .ToArray();
        return new GameState(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            cards,
            zones,
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

    private static GameState ReplacePending(GameState source, PendingDecision pending) => new(
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
        pending,
        source.ActionSequence,
        source.InitialSeed,
        source.RunState,
        source.RulesState,
        source.BehaviorRegistryVersion,
        source.BehaviorCatalogFingerprint,
        source.RuntimeCardCatalogVersion,
        source.RuntimeCardCatalogFingerprint);

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog BehaviorCatalog,
        PlayerId RunnerId,
        CardInstanceId CarnivoreId,
        ServerId RemoteId,
        CardInstanceId AccessedId,
        IReadOnlyList<CardInstanceId> GripIds,
        CardDefinitionId AccessDefinition);
}
