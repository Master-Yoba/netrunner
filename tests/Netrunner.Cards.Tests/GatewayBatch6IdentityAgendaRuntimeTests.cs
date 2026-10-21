using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch6IdentityAgendaRuntimeTests
{
    private static readonly CardDefinitionId Filler = new("fixture:filler");
    private static readonly BehaviorCatalog Catalog = new BehaviorCatalogBuilder("fixture:batch6-identities")
        .Add(GatewayBatch6IdentityAgendaBehaviorSpecs.GenericSpecs)
        .Add(GatewayBatch1BehaviorSpecs.GenericSpecs.Single(definition => definition.DefinitionId == GatewayBatch1CardCodes.SeamlessLaunch))
        .AddAgenda(GatewayBatch6IdentityAgendaBehaviorSpecs.AgendaSpecs)
        .AddAgenda(GatewayAgendaBehaviorSpecs.AgendaSpecs).Build();
    private static readonly RuntimeCardCatalog Runtime = BuildRuntime();

    [Theory]
    [InlineData("30067", PlayerSide.Corp, 7)]
    [InlineData("30067", PlayerSide.Runner, 0)]
    [Trait("GatewayCard", "30067")]
    public void OffworldTriggersOnlyForScoring(string code, PlayerSide scorer, int expectedGain)
    {
        GameState state = Fixture(code, scorer == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore);
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, scorer);
        Assert.Equal(5 + expectedGain, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        AssertValid(state);
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    [Trait("GatewayCard", "30052")]
    public void HeadlineTagsOnScoreAndSteal(PlayerSide scorer)
    {
        GameState state = Fixture("30052", scorer == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore);
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, scorer);
        Assert.Equal(1, state.RulesState!.Players.Single(value => value.PlayerId == state.GetPlayerBySide(PlayerSide.Runner).Id).Tags);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30036")]
    public void LuminalAddsThreeClicksAndPersistsTurnRestriction()
    {
        GameState state = Occur(Fixture("30036", ZoneKind.CorpScore), OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        Assert.Equal(6, state.GetPlayerBySide(PlayerSide.Corp).Clicks);
        Assert.Contains(state.Turn.History!.CorpThisTurn!.UsedAbilityKeys, key => key.AbilityKey == "score-prohibited");
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30059")]
    public void WeylandGainsTwoAtAdvanceFromZeroOccurrence()
    {
        GameState state = Occur(Fixture("30059", ZoneKind.CorpIdentity), OccurrenceKind.CardAdvancedFromZero, PlayerSide.Corp);
        Assert.Equal(7, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30001")]
    public void LoupGainsAndDrawsOnlyForFirstAccessTrashOfTurn()
    {
        GameState state = Occur(Fixture("30001", ZoneKind.RunnerIdentity), OccurrenceKind.AccessedCardTrashed, PlayerSide.Runner);
        Assert.Equal(6, state.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.Single(state.GetZone(ZoneKind.RunnerGrip).CardIds);
        state = Occur(state, OccurrenceKind.AccessedCardTrashed, PlayerSide.Runner);
        Assert.Equal(6, state.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.Single(state.GetZone(ZoneKind.RunnerGrip).CardIds);
        AssertValid(state);
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 6)]
    [Trait("GatewayCard", "30043")]
    public void JintekiChecksFacedownArchivesAtDiscardEnd(bool facedown, int expectedCredits)
    {
        GameState state = Occur(Fixture("30043", ZoneKind.CorpIdentity, facedownArchive: facedown),
            OccurrenceKind.DiscardPhaseEnded, PlayerSide.Corp, subject: false);
        Assert.Equal(expectedCredits, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30051")]
    public void NbnFirstTagChoiceIsCorpOwnedAndDoesNotRepeatAfterRemoval()
    {
        GameState state = Fixture("30051", ZoneKind.CorpIdentity);
        state = Tags(state, 1);
        Assert.Equal(state.GetPlayerBySide(PlayerSide.Corp).Id, state.PendingDecision!.Owner);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.OptionKeys.Contains("gain-two"));
        Assert.Equal(7, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        state = Tags(Tags(state, -1), 1);
        Assert.Null(state.PendingDecision);
        Assert.Equal(7, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30070")]
    public void HubOptionalDrawResumesFromSerializedScoreTrigger()
    {
        GameState state = Occur(Fixture("30070", ZoneKind.CorpScore), OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        Assert.Equal(DecisionKind.TriggerChoice, state.PendingDecision!.Kind);
        state = EngineJson.DeserializeState(EngineJson.Serialize(state), Catalog, Runtime);
        state = Pick(state, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(2, state.GetZone(ZoneKind.CorpHand).CardIds.Count);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30068")]
    public void OrbitalTagsUntaggedRunner()
    {
        GameState state = Occur(Fixture("30068", ZoneKind.CorpScore), OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        Assert.Equal(1, state.RulesState!.Players.Single(value => value.PlayerId == state.GetPlayerBySide(PlayerSide.Runner).Id).Tags);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30035")]
    public void PrecisionDesignAddsPrivateArchivesCardToHq()
    {
        GameState state = Fixture("30035", ZoneKind.CorpIdentity, facedownArchive: true);
        CardInstanceId archived = Assert.Single(state.GetZone(ZoneKind.CorpDiscard).CardIds);
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        state = Pick(state, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(state.GetPlayerBySide(PlayerSide.Corp).Id, state.PendingDecision!.Owner);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(archived));
        Assert.Contains(archived, state.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.False(state.GetCard(archived).FaceUp);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30044")]
    public void LongevityAcceptsZeroHqTrashAndShufflesChosenArchive()
    {
        GameState state = Fixture("30044", ZoneKind.CorpScore, facedownArchive: true);
        CardInstanceId archived = Assert.Single(state.GetZone(ZoneKind.CorpDiscard).CardIds);
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        if (state.PendingDecision!.LegalActions.Any(action => action.Parameters is BehaviorChoiceParameters { ChoiceKey: "longevity-hq" }))
            state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Count == 0);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(archived));
        Assert.Contains(archived, state.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.Empty(state.GetZone(ZoneKind.CorpDiscard).CardIds);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30066")]
    public void MalapertSearchRevealsShufflesAndAddsNonAgendaToHq()
    {
        GameState state = Fixture("30066", ZoneKind.ServerRoot, deckAgenda: true);
        ServerId server = state.GetZone(state.Cards[0].ZoneId).ServerId!.Value;
        PlayerId corp = state.GetPlayerBySide(PlayerSide.Corp).Id;
        state = GameEngine.BeginResolution(state, new EffectResolutionFrame(corp, new CostBundle(),
            new EffectBundle(new GameplayOccurrenceEffect(OccurrenceKind.AgendaScoredOrStolen, corp, state.Cards[0].Id, server))), Catalog, Runtime);
        state = Pick(state, action => action.Kind == ActionKind.UseTrigger);
        CardInstanceId selected = state.GetZone(ZoneKind.CorpDeck).CardIds[0];
        Assert.Equal(corp, state.PendingDecision!.Owner);
        Assert.DoesNotContain(state.PendingDecision.LegalActions, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.Any(id => state.GetCard(id).DefinitionId.Value == "30067"));
        string runnerView = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, Catalog));
        Assert.DoesNotContain(Filler.Value, runnerView, StringComparison.Ordinal);
        List<CardInstanceId> expectedDeck = state.GetZone(ZoneKind.CorpDeck).CardIds.Where(id => id != selected).ToList();
        DeterministicRandom expectedRandom = DeterministicRandom.Restore(state.RandomState);
        expectedRandom.Shuffle(expectedDeck);
        PendingDecision decision = state.PendingDecision;
        LegalAction action = Assert.Single(decision.LegalActions, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(selected));
        ApplyResult result = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id), Catalog, Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        List<string> events = result.Events.Select(value => value.Kind).ToList();
        Assert.True(events.IndexOf(EngineEventKinds.BehaviorCardsShuffled) < events.IndexOf(EngineEventKinds.BehaviorCardRevealed));
        state = result.State;
        Assert.Equal(expectedDeck, state.GetZone(ZoneKind.CorpDeck).CardIds);
        Assert.Equal(expectedRandom.State, state.RandomState);
        Assert.Contains(selected, state.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.False(state.GetCard(selected).FaceUp);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30060")]
    public void AboveTheLawTrashesChosenInstalledResource()
    {
        GameState state = Fixture("30060", ZoneKind.CorpScore, installedTargets: true);
        CardInstanceId resource = Assert.Single(state.GetZone(ZoneKind.RunnerRig).CardIds);
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        state = Pick(state, action => action.Kind == ActionKind.UseTrigger);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(resource));
        Assert.Contains(resource, state.GetZone(ZoneKind.RunnerHeap).CardIds);
        AssertValid(state);
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    [Trait("GatewayCard", "30069")]
    public void SendMessageRezzesInstalledIceWithoutCreditCost(PlayerSide scorer)
    {
        GameState state = Fixture("30069", scorer == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore, installedTargets: true);
        CardInstanceId ice = state.Cards.Single(card => card.DefinitionId.Value == "fixture:ice").Id;
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, scorer);
        state = Pick(state, action => action.Kind == ActionKind.UseTrigger);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(ice));
        Assert.True(state.GetCard(ice).Rezzed);
        Assert.Equal(5, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        AssertValid(state);
    }

    [Theory]
    [InlineData("30035", ZoneKind.CorpIdentity, 6)]
    [InlineData("30070", ZoneKind.CorpScore, 7)]
    [InlineData("30070", ZoneKind.RunnerScore, 5)]
    [Trait("GatewayCard", "30035")]
    [Trait("GatewayCard", "30070")]
    public void HandSizeOnlyIncludesActiveOwnedIdentityOrScoredAgenda(string code, ZoneKind zone, int maximum)
    {
        GameState state = Fixture(code, zone);
        Observation observation = ObservationBuilder.Build(state, PlayerSide.Corp, Catalog);
        var players = Assert.IsType<Dictionary<string, object?>>(observation.PublicState["players"]);
        var corp = Assert.IsType<Dictionary<string, object?>>(players["corp"]);
        Assert.Equal(maximum, corp["maximumHandSize"]);
    }

    [Theory]
    [InlineData("30076", ZoneKind.RunnerIdentity)]
    [InlineData("30077", ZoneKind.CorpIdentity)]
    [Trait("GatewayCard", "30076")]
    [Trait("GatewayCard", "30077")]
    public void TutorialIdentitiesHaveNoGameplayAbility(string code, ZoneKind zone)
    {
        GameState state = Occur(Fixture(code, zone), OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        Assert.Null(state.PendingDecision);
        Assert.Equal(5, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.Equal(5, state.GetPlayerBySide(PlayerSide.Runner).Credits);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30036")]
    public void ActualLuminalScoreRemovesOtherFullyAdvancedAgendaFromScoreWindow()
    {
        GameState initial = Fixture("30036", ZoneKind.CorpScore);
        StableIdAllocator allocator = new(initial.Allocator);
        List<ServerState> servers = initial.Servers.ToList();
        List<ZoneState> zones = initial.Zones.Select(zone => zone.Kind == ZoneKind.CorpScore
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide) : zone).ToList();
        List<CardInstanceState> cards = initial.Cards.Skip(1).ToList();
        for (int i = 0; i < 2; i++)
        {
            ServerId serverId = allocator.NextServerId();
            ZoneId root = allocator.NextZoneId();
            ZoneId ice = allocator.NextZoneId();
            CardInstanceId cardId = i == 0 ? initial.Cards[0].Id : allocator.NextCardInstanceId();
            servers.Add(new ServerState(serverId, ServerKind.Remote, ice, root));
            zones.Add(new ZoneState(root, ZoneKind.ServerRoot, PlayerSide.Corp, serverId, [cardId]));
            zones.Add(new ZoneState(ice, ZoneKind.ServerIce, PlayerSide.Corp, serverId));
            cards.Add(new CardInstanceState(cardId, new(i == 0 ? "30036" : "30067"), root, PlayerSide.Corp, advancementCounters: 3));
        }
        PlayerState[] players = initial.Players.Select(player => new PlayerState(player.Id, player.Side, player.Credits, player.Clicks, 0)).ToArray();
        GameState state = new(initial.SchemaVersion, initial.GameId, initial.RulesVersion, initial.CardDataVersion,
            players, cards, zones, servers, initial.Turn, initial.Outcome, initial.RandomState, allocator.State,
            initialSeed: initial.InitialSeed, rulesState: initial.RulesState, behaviorRegistryVersion: Catalog.Version,
            behaviorCatalogFingerprint: Catalog.Fingerprint, runtimeCardCatalogVersion: Runtime.Version, runtimeCardCatalogFingerprint: Runtime.Fingerprint);
        state = GameEngine.BeginTurn(state, PlayerSide.Corp, Catalog, Runtime);
        state = Pick(state, action => action.Kind == ActionKind.PassPriority);
        state = Pick(state, action => action.Kind == ActionKind.PassPriority);
        Assert.Equal(2, state.PendingDecision!.LegalActions.Count(action => action.Kind == ActionKind.ScoreAgenda));
        state = Pick(state, action => action.Parameters is ScoreAgendaParameters score && score.CardId == initial.Cards[0].Id);
        Assert.Equal(6, state.GetPlayerBySide(PlayerSide.Corp).Clicks);
        Assert.Equal(2, state.GetPlayerBySide(PlayerSide.Corp).AgendaPoints);
        Assert.DoesNotContain(state.PendingDecision!.LegalActions, action => action.Kind == ActionKind.ScoreAgenda);
        AssertValid(state);
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 9)]
    [Trait("GatewayCard", "30043")]
    public void RealDiscardBoundaryChecksArchivesBeforeNextTurn(bool facedownArchive, int expectedCredits)
    {
        GameState state = GameEngine.BeginTurn(Fixture("30043", ZoneKind.CorpIdentity, facedownArchive), PlayerSide.Corp, Catalog, Runtime);
        for (int step = 0; step < 40 && state.Turn.ActiveSide == PlayerSide.Corp; step++)
        {
            bool actionWindow = state.PendingDecision!.Kind == DecisionKind.TurnAction;
            state = Pick(state, action => action.Kind == (actionWindow ? ActionKind.GainCredit : ActionKind.PassPriority));
        }
        Assert.Equal(PlayerSide.Runner, state.Turn.ActiveSide);
        Assert.Equal(expectedCredits, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30059")]
    public void RealAdvanceActionRewardsOnlyItsZeroCounterSnapshot()
    {
        GameState state = WithInstalledAgenda(Fixture("30059", ZoneKind.CorpIdentity));
        CardInstanceId agenda = state.Cards.Single(card => card.DefinitionId.Value == "30067").Id;
        state = GameEngine.BeginTurn(state, PlayerSide.Corp, Catalog, Runtime);
        for (int advance = 0; advance < 2; advance++)
        {
            for (int step = 0; state.PendingDecision!.Kind != DecisionKind.TurnAction && step < 10; step++)
                state = Pick(state, action => action.Kind == ActionKind.PassPriority);
            state = Pick(state, action => action.Parameters is AdvanceCardParameters parameters && parameters.CardId == agenda);
            Assert.Equal(advance == 0 ? 6 : 5, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        }
        Assert.Equal(2, state.GetCard(agenda).AdvancementCounters);
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30059")]
    [Trait("GatewayCard", "30040")]
    public void SeamlessLaunchPlacesCountersWithoutTriggeringBuiltToLast()
    {
        GameState state = WithInstalledAgenda(Fixture("30059", ZoneKind.CorpIdentity, seamlessLaunch: true));
        CardInstanceId agenda = state.Cards.Single(card => card.DefinitionId.Value == "30067").Id;
        CardInstanceId operation = state.Cards.Single(card => card.DefinitionId == GatewayBatch1CardCodes.SeamlessLaunch).Id;
        Assert.Equal(0, state.GetCard(agenda).AdvancementCounters);
        state = GameEngine.BeginTurn(state, PlayerSide.Corp, Catalog, Runtime);
        for (int step = 0; state.PendingDecision!.Kind != DecisionKind.TurnAction && step < 10; step++)
            state = Pick(state, action => action.Kind == ActionKind.PassPriority);
        state = Pick(state, action => action.Parameters is PlayCardParameters play && play.CardId == operation);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(agenda));
        Assert.Equal(2, state.GetCard(agenda).AdvancementCounters);
        // Pay the operation's printed 1 credit; placing counters grants no
        // Built to Last income even though the target started at zero.
        Assert.Equal(4, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.Equal(2, state.GetPlayerBySide(PlayerSide.Corp).Clicks);
        Assert.Contains(operation, state.GetZone(ZoneKind.CorpDiscard).CardIds);
        AssertValid(state);
    }

    private static GameState WithInstalledAgenda(GameState state)
    {
        StableIdAllocator allocator = new(state.Allocator);
        ServerId server = allocator.NextServerId();
        ZoneId ice = allocator.NextZoneId();
        ZoneId root = allocator.NextZoneId();
        CardInstanceId card = allocator.NextCardInstanceId();
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion,
            state.Players, [.. state.Cards, new CardInstanceState(card, new("30067"), root, PlayerSide.Corp)],
            [.. state.Zones, new ZoneState(root, ZoneKind.ServerRoot, PlayerSide.Corp, server, [card]), new ZoneState(ice, ZoneKind.ServerIce, PlayerSide.Corp, server)],
            [.. state.Servers, new ServerState(server, ServerKind.Remote, ice, root)], state.Turn, state.Outcome,
            state.RandomState, allocator.State, initialSeed: state.InitialSeed, rulesState: state.RulesState,
            behaviorRegistryVersion: Catalog.Version, behaviorCatalogFingerprint: Catalog.Fingerprint,
            runtimeCardCatalogVersion: Runtime.Version, runtimeCardCatalogFingerprint: Runtime.Fingerprint);
    }

    [Fact]
    [Trait("GatewayCard", "30044")]
    public void LongevityTrashesSelectedHqSubsetFacedownWithoutRevealingIt()
    {
        GameState state = Fixture("30044", ZoneKind.CorpScore, hqCards: 3);
        CardInstanceId[] selected = state.GetZone(ZoneKind.CorpHand).CardIds.Take(2).ToArray();
        state = Occur(state, OccurrenceKind.AgendaScoredOrStolen, PlayerSide.Corp);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.SequenceEqual(selected));
        Assert.Single(state.GetZone(ZoneKind.CorpHand).CardIds);
        Assert.Equal(2, state.GetZone(ZoneKind.CorpDiscard).CardIds.Count);
        Assert.All(selected, id => Assert.False(state.GetCard(id).FaceUp));
        Assert.DoesNotContain(Filler.Value, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, Catalog)), StringComparison.Ordinal);
        state = EngineJson.DeserializeState(EngineJson.Serialize(state), Catalog, Runtime);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Count == 0);
        Assert.All(selected, id => Assert.False(state.GetCard(id).FaceUp));
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30066")]
    public void RealScoreUsesMalapertServerSnapshotAfterAgendaLeavesRoot()
    {
        GameState fixture = WithInstalledAgenda(Fixture("30066", ZoneKind.ServerRoot));
        CardInstanceState source = fixture.Cards[0];
        CardInstanceState agenda = fixture.Cards.Single(card => card.DefinitionId.Value == "30067");
        CardInstanceState[] cards = fixture.Cards.Select(card => card.Id == source.Id
            ? new CardInstanceState(card.Id, card.DefinitionId, agenda.ZoneId, card.OwnerSide, faceUp: true, rezzed: true)
            : card.Id == agenda.Id ? new CardInstanceState(card.Id, card.DefinitionId, card.ZoneId, card.OwnerSide, advancementCounters: 3) : card).ToArray();
        ZoneState[] zones = fixture.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToArray();
        GameState state = new(fixture.SchemaVersion, fixture.GameId, fixture.RulesVersion, fixture.CardDataVersion,
            fixture.Players, cards, zones, fixture.Servers, fixture.Turn, fixture.Outcome, fixture.RandomState, fixture.Allocator,
            initialSeed: fixture.InitialSeed, rulesState: fixture.RulesState, behaviorRegistryVersion: Catalog.Version,
            behaviorCatalogFingerprint: Catalog.Fingerprint, runtimeCardCatalogVersion: Runtime.Version, runtimeCardCatalogFingerprint: Runtime.Fingerprint);
        state = GameEngine.BeginTurn(state, PlayerSide.Corp, Catalog, Runtime);
        state = Pick(state, action => action.Kind == ActionKind.PassPriority);
        state = Pick(state, action => action.Kind == ActionKind.PassPriority);
        state = Pick(state, action => action.Parameters is ScoreAgendaParameters parameters && parameters.CardId == agenda.Id);
        if (state.PendingDecision!.Kind == DecisionKind.TriggerOrdering)
            state = Pick(state, action => action.Id == state.PendingDecision.LegalActions[0].Id);
        Assert.Equal(DecisionKind.TriggerChoice, state.PendingDecision!.Kind);
        Assert.Equal(state.GetZone(ZoneKind.CorpScore).Id, state.GetCard(agenda.Id).ZoneId);
        state = Pick(state, action => action.Kind == ActionKind.UseTrigger);
        Assert.Equal(DecisionKind.BehaviorChoice, state.PendingDecision!.Kind);
        Assert.All(state.PendingDecision.LegalActions.Where(action => action.Parameters is BehaviorChoiceParameters), action =>
            Assert.DoesNotContain(agenda.Id, Assert.IsType<BehaviorChoiceParameters>(action.Parameters).CardIds));
        AssertValid(state);
    }

    [Fact]
    [Trait("GatewayCard", "30051")]
    public void NbnDrawOptionDecksOutAndPreservesFormalTurnHistory()
    {
        GameState state = Tags(Fixture("30051", ZoneKind.CorpIdentity, corpDeckCards: 1), 1);
        state = Pick(state, action => action.Parameters is BehaviorChoiceParameters choice && choice.OptionKeys.Contains("draw-two"));
        Assert.Equal(PlayerSide.Runner, state.Outcome.Winner);
        Assert.Equal(GameWinReason.DeckOut, state.Outcome.Reason);
        Assert.NotNull(state.Turn.History);
        Assert.Null(state.RunState);
        AssertValid(state);
    }

    private static GameState Tags(GameState state, int delta) => GameEngine.BeginResolution(state,
        new EffectResolutionFrame(state.GetPlayerBySide(PlayerSide.Corp).Id, new CostBundle(),
            new EffectBundle(new TagAdjustmentEffect(state.GetPlayerBySide(PlayerSide.Runner).Id, delta))), Catalog, Runtime);

    private static GameState Occur(GameState state, OccurrenceKind kind, PlayerSide controller, bool subject = true) =>
        GameEngine.BeginResolution(state, new EffectResolutionFrame(state.GetPlayerBySide(controller).Id,
            new CostBundle(), new EffectBundle(new GameplayOccurrenceEffect(kind, state.GetPlayerBySide(controller).Id,
                subject ? state.Cards[0].Id : null))), Catalog, Runtime);

    private static GameState Pick(GameState state, Func<LegalAction, bool> predicate)
    {
        PendingDecision decision = state.PendingDecision!;
        LegalAction action = Assert.Single(decision.LegalActions, action => predicate(action));
        ApplyResult result = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id), Catalog, Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static void AssertValid(GameState state)
    {
        Assert.Empty(StateInvariantValidator.Collect(state, Catalog, Runtime));
        Assert.Equal(EngineJson.Serialize(state), EngineJson.Serialize(EngineJson.DeserializeState(EngineJson.Serialize(state), Catalog, Runtime)));
    }

    private static RuntimeCardCatalog BuildRuntime()
    {
        RuntimeCardCatalogBuilder builder = new("fixture:batch6-runtime");
        foreach (CardBehaviorDefinition definition in Catalog.GenericSpecs)
        {
            bool agenda = Catalog.TryGetAgenda(definition.DefinitionId, out AgendaBehaviorSpec spec);
            if (definition.DefinitionId == GatewayBatch1CardCodes.SeamlessLaunch)
            {
                builder.Add(new CardRuntimeSpec(definition.DefinitionId, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 1));
                continue;
            }
            builder.Add(new CardRuntimeSpec(definition.DefinitionId, definition.Controller,
                agenda ? RuntimeCardClass.Agenda : definition.DefinitionId.Value == "30066" ? RuntimeCardClass.Upgrade : RuntimeCardClass.Identity,
                rezCost: definition.DefinitionId.Value == "30066" ? 2 : null,
                agendaPoints: agenda ? spec.AgendaPoints : 0, advancementRequirement: agenda ? 3 : 0));
        }
        builder.Add(new CardRuntimeSpec(new("fixture:resource"), PlayerSide.Runner, RuntimeCardClass.Resource, installCost: 1));
        builder.Add(new CardRuntimeSpec(new("fixture:ice"), PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 9));
        builder.Add(new CardRuntimeSpec(Filler, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 0));
        builder.Add(new CardRuntimeSpec(new("fixture:runner-filler"), PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0));
        return builder.Build();
    }

    private static GameState Fixture(string code, ZoneKind sourceKind, bool facedownArchive = false, bool installedTargets = false, bool deckAgenda = false, int hqCards = 0, int corpDeckCards = 5, bool seamlessLaunch = false)
    {
        GameState baseline = GameState.CreateNew(47042, behaviorRegistryVersion: Catalog.Version,
            behaviorCatalogFingerprint: Catalog.Fingerprint, runtimeCardCatalogVersion: Runtime.Version,
            runtimeCardCatalogFingerprint: Runtime.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        PlayerSide owner = Runtime.GetSpec(new(code)).Side;
        List<ZoneState> zones = baseline.Zones.ToList();
        ZoneState? sourceZone = zones.FirstOrDefault(zone => zone.Kind == sourceKind);
        if (sourceZone is null)
        {
            sourceZone = new ZoneState(allocator.NextZoneId(), sourceKind, owner);
            zones.Add(sourceZone);
        }
        List<CardInstanceState> cards = [new(allocator.NextCardInstanceId(), new(code), sourceZone.Id, owner, faceUp: true, rezzed: sourceKind == ZoneKind.ServerRoot)];
        for (int i = 0; i < 5; i++)
        {
            if (i < corpDeckCards) cards.Add(new(allocator.NextCardInstanceId(), Filler, zones.Single(zone => zone.Kind == ZoneKind.CorpDeck).Id, PlayerSide.Corp));
            cards.Add(new(allocator.NextCardInstanceId(), new("fixture:runner-filler"), zones.Single(zone => zone.Kind == ZoneKind.RunnerStack).Id, PlayerSide.Runner));
        }
        for (int i = 0; i < hqCards; i++) cards.Add(new(allocator.NextCardInstanceId(), Filler, zones.Single(zone => zone.Kind == ZoneKind.CorpHand).Id, PlayerSide.Corp));
        if (seamlessLaunch) cards.Add(new(allocator.NextCardInstanceId(), GatewayBatch1CardCodes.SeamlessLaunch,
            zones.Single(zone => zone.Kind == ZoneKind.CorpHand).Id, PlayerSide.Corp));
        if (deckAgenda) cards.Add(new(allocator.NextCardInstanceId(), new("30067"), zones.Single(zone => zone.Kind == ZoneKind.CorpDeck).Id, PlayerSide.Corp));
        if (installedTargets)
        {
            cards.Add(new(allocator.NextCardInstanceId(), new("fixture:resource"), zones.Single(zone => zone.Kind == ZoneKind.RunnerRig).Id, PlayerSide.Runner, faceUp: true));
            cards.Add(new(allocator.NextCardInstanceId(), new("fixture:ice"), zones.First(zone => zone.Kind == ZoneKind.ServerIce).Id, PlayerSide.Corp));
        }
        if (sourceKind is ZoneKind.CorpIdentity or ZoneKind.RunnerIdentity)
        {
            PlayerSide opposite = owner == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
            ZoneKind identityKind = opposite == PlayerSide.Corp ? ZoneKind.CorpIdentity : ZoneKind.RunnerIdentity;
            ZoneState identityZone = new(allocator.NextZoneId(), identityKind, opposite);
            zones.Add(identityZone);
            cards.Add(new(allocator.NextCardInstanceId(), new(opposite == PlayerSide.Corp ? "30077" : "30076"), identityZone.Id, opposite, faceUp: true));
        }
        if (facedownArchive) cards.Add(new(allocator.NextCardInstanceId(), Filler, zones.Single(zone => zone.Kind == ZoneKind.CorpDiscard).Id, PlayerSide.Corp));
        zones = zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToList();
        PlayerState[] players = baseline.Players.Select(player => new PlayerState(player.Id, player.Side, 5,
            player.Side == PlayerSide.Corp ? 3 : 4,
            sourceKind == (player.Side == PlayerSide.Corp ? ZoneKind.CorpScore : ZoneKind.RunnerScore)
                ? Runtime.GetSpec(new(code)).AgendaPoints : 0)).ToArray();
        TurnFactsState facts = new(1, PlayerSide.Corp, baseline.Turn.TurnNumber, 3, 0, true, true, false, false);
        return new GameState(baseline.SchemaVersion, baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players, cards, zones, baseline.Servers,
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, baseline.Turn.TurnNumber, TurnStep.CorpAction, new TurnHistoryState(1, facts, null, null, null)),
            baseline.Outcome, baseline.RandomState, allocator.State, initialSeed: baseline.InitialSeed,
            rulesState: RulesState.CreateDefault(players), behaviorRegistryVersion: Catalog.Version,
            behaviorCatalogFingerprint: Catalog.Fingerprint, runtimeCardCatalogVersion: Runtime.Version,
            runtimeCardCatalogFingerprint: Runtime.Fingerprint);
    }
}
