using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch5RuntimeTests
{
    private static readonly CardDefinitionId Filler = new("fixture:batch5-grip");
    private static readonly CardDefinitionId CorpFiller = new("fixture:batch5-deck");
    private static readonly CardDefinitionId Agenda = new("30067");
    private static readonly CardDefinitionId Program = new("30025");
    private static readonly CardDefinitionId Hardware = GatewayBatch2CardCodes.Pantograph;
    private static readonly CardDefinitionId Resource = GatewayBatch2CardCodes.TeleworkContract;

    [Theory]
    [InlineData(false, 0, 2)]
    [InlineData(false, 3, 5)]
    [InlineData(true, 2, 4)]
    [Trait("GatewayCard", "30045")]
    public void InstalledUrticaDealsOneNetDamageInstanceEvenUnrezzed(bool rezzed, int advancement, int damage)
    {
        Fixture fixture = Create(PlayerSide.Runner,
            [new Placement(GatewayBatch5CardCodes.UrticaCipher, ZoneKind.ServerRoot, rezzed, advancement)]);
        GameState run = GameEngine.BeginRun(fixture.State, fixture.RemoteId, fixture.Behavior, fixture.Runtime);
        run = PassUntil(fixture, run, state => state.RunState?.Step == RunTimingStep.BreachCandidateSelection);
        LegalAction access = Assert.Single(run.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate);
        ApplyResult result = Apply(fixture, run, access);
        DamageDealtEventPayload dealt = Assert.IsType<DamageDealtEventPayload>(
            Assert.Single(result.Events, entry => entry.Kind == EngineEventKinds.DamageDealt).Payload);
        Assert.Equal(DamageKind.Net, dealt.Kind);
        Assert.Equal(damage, dealt.Amount);
        Assert.Equal(8 - damage, result.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        RoundTrip(fixture, result.State);
    }

    [Fact]
    [Trait("GatewayCard", "30045")]
    public void UrticaInHeadquartersDoesNotDealAccessDamage()
    {
        Fixture fixture = Create(PlayerSide.Runner,
            [new Placement(GatewayBatch5CardCodes.UrticaCipher, ZoneKind.CorpHand)]);
        ServerId hq = fixture.State.Servers.Single(server => server.Kind == ServerKind.Headquarters).Id;
        GameState run = GameEngine.BeginRun(fixture.State, hq, fixture.Behavior, fixture.Runtime);
        run = PassUntil(fixture, run, state => state.RunState?.Step == RunTimingStep.BreachCandidateSelection);
        ApplyResult accessed = Apply(fixture, run, Assert.Single(run.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ChooseBreachCandidate));
        Assert.DoesNotContain(accessed.Events, entry => entry.Kind == EngineEventKinds.DamageDealt);
        Assert.Equal(8, accessed.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("GatewayCard", "30061")]
    public void ClearinghouseTurnBeginCanDeclineOrTrashForSnapshottedMeatDamage(bool use)
    {
        Fixture fixture = Create(PlayerSide.Corp,
            [new Placement(GatewayBatch5CardCodes.Clearinghouse, ZoneKind.ServerRoot, true, 3)]);
        CardInstanceId source = Card(fixture.State, GatewayBatch5CardCodes.Clearinghouse);
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Corp, fixture.Behavior, fixture.Runtime);
        state = PassUntil(fixture, state, value => value.PendingDecision?.Kind == DecisionKind.TriggerChoice);
        state = RoundTrip(fixture, state);
        ApplyResult result = Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions,
            action => action.Kind == (use ? ActionKind.UseTrigger : ActionKind.DeclineTrigger)));
        Assert.Equal(use ? 5 : 8, result.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(use ? 0 : 3, result.State.GetCard(source).AdvancementCounters);
        Assert.Equal(use, result.State.GetZone(ZoneKind.CorpDiscard).CardIds.Contains(source));
        if (use)
        {
            DamageDealtEventPayload damage = Assert.IsType<DamageDealtEventPayload>(
                Assert.Single(result.Events, entry => entry.Kind == EngineEventKinds.DamageDealt).Payload);
            Assert.Equal(DamageKind.Meat, damage.Kind);
            Assert.Equal(3, damage.Amount);
        }
        RoundTrip(fixture, result.State);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    [Trait("GatewayCard", "30049")]
    public void NeurospikeCountsPrintedPointsScoredDuringThisTurn(bool score, int damage)
    {
        Fixture fixture = Create(PlayerSide.Corp,
            [new Placement(GatewayBatch5CardCodes.Neurospike, ZoneKind.CorpHand),
             new Placement(Agenda, ZoneKind.ServerRoot, Advancement: 4)]);
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Corp, fixture.Behavior, fixture.Runtime);
        if (score)
        {
            state = PassUntil(fixture, state, value => value.PendingDecision!.LegalActions.Any(action => action.Kind == ActionKind.ScoreAgenda));
            state = Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.ScoreAgenda)).State;
        }
        state = PassUntil(fixture, state, value => value.PendingDecision?.Kind == DecisionKind.TurnAction);
        ApplyResult played = Play(fixture, state, GatewayBatch5CardCodes.Neurospike);
        Assert.Equal(8 - damage, played.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(score, played.Events.Any(entry => entry.Kind == EngineEventKinds.DamageDealt));
        if (score)
        {
            Assert.Equal(2, Assert.IsType<DamageDealtEventPayload>(
                Assert.Single(played.Events, entry => entry.Kind == EngineEventKinds.DamageDealt).Payload).Amount);
        }
        RoundTrip(fixture, played.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("GatewayCard", "30057")]
    public void PublicTrailUsesLastRunnerTurnAndResumesRunnerTagOrPayChoice(bool pay)
    {
        Fixture fixture = Create(PlayerSide.Runner,
            [new Placement(GatewayBatch5CardCodes.PublicTrail, ZoneKind.CorpHand)]);
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Runner, fixture.Behavior, fixture.Runtime);
        state = PassUntil(fixture, state, value => value.PendingDecision?.Kind == DecisionKind.TurnAction);
        state = Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions,
            action => action.Parameters is RunServerParameters run && run.ServerId == fixture.RemoteId)).State;
        state = PassUntil(fixture, state, value => value.RunState is null);
        Assert.True(state.Turn.History?.RunnerThisTurn?.SuccessfulRun);
        for (int count = 0; state.Turn.ActiveSide == PlayerSide.Runner; count++)
        {
            Assert.True(count < 24, "Runner turn did not finish.");
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction action = decision.Kind switch
            {
                DecisionKind.TurnAction => decision.LegalActions.Single(action => action.Kind == ActionKind.GainCredit),
                DecisionKind.TurnDiscard => decision.LegalActions[0],
                _ => decision.LegalActions.Single(action => action.Kind == ActionKind.PassPriority),
            };
            state = Apply(fixture, state, action).State;
        }
        state = PassUntil(fixture, state, value => value.PendingDecision?.Kind == DecisionKind.TurnAction);
        ApplyResult played = Play(fixture, state, GatewayBatch5CardCodes.PublicTrail);
        state = RoundTrip(fixture, played.State);
        Assert.Equal(PlayerSide.Runner, state.GetPlayer(state.PendingDecision!.Owner).Side);
        int priorCredits = state.GetPlayerBySide(PlayerSide.Runner).Credits;
        ApplyResult chosen = Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters choice
                && choice.OptionKeys.SequenceEqual([pay ? "pay-eight" : "take-tag"])));
        Assert.Equal(priorCredits - (pay ? 8 : 0), chosen.State.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.Equal(pay ? 0 : 1, Tags(chosen.State));
        RoundTrip(fixture, chosen.State);
    }

    [Fact]
    [Trait("GatewayCard", "30057")]
    public void PublicTrailIsNotPlayableWithoutSuccessfulPriorRunnerTurn()
    {
        Fixture fixture = Create(PlayerSide.Corp,
            [new Placement(GatewayBatch5CardCodes.PublicTrail, ZoneKind.CorpHand)]);
        GameState state = OpenAction(fixture);
        Assert.DoesNotContain(state.PendingDecision!.LegalActions,
            action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, GatewayBatch5CardCodes.PublicTrail));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("GatewayCard", "30065")]
    public void RetributionRequiresTagsAndSelectsOnlyInstalledProgramOrHardware(int tags)
    {
        Fixture fixture = Create(PlayerSide.Corp,
            [new Placement(GatewayBatch5CardCodes.Retribution, ZoneKind.CorpHand),
             new Placement(Program, ZoneKind.RunnerRig), new Placement(Hardware, ZoneKind.RunnerRig),
             new Placement(Resource, ZoneKind.RunnerRig)], tags: tags);
        GameState state = OpenAction(fixture);
        if (tags == 0)
        {
            Assert.DoesNotContain(state.PendingDecision!.LegalActions,
                action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, GatewayBatch5CardCodes.Retribution));
            return;
        }
        state = RoundTrip(fixture, Play(fixture, state, GatewayBatch5CardCodes.Retribution).State);
        CardInstanceId hardware = Card(state, Hardware);
        CardInstanceId program = Card(state, Program);
        CardInstanceId resource = Card(state, Resource);
        CardInstanceId[] candidates = state.PendingDecision!.LegalActions.SelectMany(action =>
            Assert.IsType<BehaviorChoiceParameters>(action.Parameters).CardIds).ToArray();
        Assert.Contains(hardware, candidates);
        Assert.Contains(program, candidates);
        Assert.DoesNotContain(resource, candidates);
        ApplyResult trashed = Apply(fixture, state, Assert.Single(state.PendingDecision.LegalActions,
            action => Assert.IsType<BehaviorChoiceParameters>(action.Parameters).CardIds.Contains(hardware)));
        Assert.Contains(hardware, trashed.State.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Contains(resource, trashed.State.GetZone(ZoneKind.RunnerRig).CardIds);
    }

    [Theory]
    [InlineData("30045", false)]
    [InlineData("30045", true)]
    [InlineData("30061", false)]
    [InlineData("30061", true)]
    [InlineData("30063", false)]
    [InlineData("30063", true)]
    [Trait("GatewayCard", "30045")]
    [Trait("GatewayCard", "30061")]
    [Trait("GatewayCard", "30063")]
    public void PrintedAdvanceabilityIssuesRealActionForAssetsAndIce(string code, bool rezzed)
    {
        CardDefinitionId definition = new(code);
        Fixture fixture = Create(PlayerSide.Corp,
            [new Placement(definition, code == "30063" ? ZoneKind.ServerIce : ZoneKind.ServerRoot, rezzed)]);
        GameState state = RoundTrip(fixture, OpenAction(fixture));
        CardInstanceId source = Card(state, definition);
        int credits = state.GetPlayerBySide(PlayerSide.Corp).Credits;
        int clicks = state.GetPlayerBySide(PlayerSide.Corp).Clicks;
        ApplyResult advanced = Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions,
            action => action.Parameters is AdvanceCardParameters advance && advance.CardId == source));
        Assert.Equal(1, advanced.State.GetCard(source).AdvancementCounters);
        Assert.Equal(credits - 1, advanced.State.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.Equal(clicks - 1, advanced.State.GetPlayerBySide(PlayerSide.Corp).Clicks);
        Assert.Contains(advanced.Events, entry => entry.Kind == EngineEventKinds.BehaviorCardAdvanced);
        RoundTrip(fixture, advanced.State);
    }

    private static RuntimeCardCatalog Runtime() => new RuntimeCardCatalogBuilder("runtime:batch5")
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.UrticaCipher, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 0, trashCost: 2))
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.Clearinghouse, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 0, trashCost: 3))
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.Pharos, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 7))
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.Neurospike, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 3))
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.PublicTrail, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 4))
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.Retribution, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 1))
        .Add(new CardRuntimeSpec(Agenda, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 4))
        .Add(new CardRuntimeSpec(Program, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 3, memoryUnits: 1))
        .Add(new CardRuntimeSpec(Hardware, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 2))
        .Add(new CardRuntimeSpec(Resource, PlayerSide.Runner, RuntimeCardClass.Resource, installCost: 1))
        .Add(new CardRuntimeSpec(Filler, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 0))
        .Add(new CardRuntimeSpec(CorpFiller, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 0))
        .Build();

    private static Fixture Create(PlayerSide side, Placement[] placements, int tags = 0)
    {
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        RuntimeCardCatalog runtime = Runtime();
        GameState baseline = GameState.CreateNew(46045, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StableIdAllocator allocator = new(baseline.Allocator);
        ServerId remote = allocator.NextServerId();
        ZoneId ice = allocator.NextZoneId();
        ZoneId root = allocator.NextZoneId();
        ServerState[] servers = [.. baseline.Servers, new ServerState(remote, ServerKind.Remote, ice, root)];
        ZoneState[] zones = [.. baseline.Zones, new ZoneState(ice, ZoneKind.ServerIce, PlayerSide.Corp, remote),
            new ZoneState(root, ZoneKind.ServerRoot, PlayerSide.Corp, remote)];
        List<CardInstanceState> cards = [];
        foreach (Placement placement in placements.Concat(Enumerable.Repeat(new Placement(Filler, ZoneKind.RunnerGrip), 8))
            .Concat(Enumerable.Repeat(new Placement(CorpFiller, ZoneKind.CorpDeck), 2)))
        {
            ZoneState zone = placement.Zone is ZoneKind.ServerRoot or ZoneKind.ServerIce
                ? zones.Single(zone => zone.Id == (placement.Zone == ZoneKind.ServerRoot ? root : ice))
                : zones.Single(zone => zone.Kind == placement.Zone);
            PlayerSide owner = runtime.GetSpec(placement.Definition).Side;
            cards.Add(new CardInstanceState(allocator.NextCardInstanceId(), placement.Definition, zone.Id, owner,
                faceUp: placement.Rezzed || placement.Zone == ZoneKind.RunnerRig, rezzed: placement.Rezzed,
                advancementCounters: placement.Advancement));
        }
        zones = zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToArray();
        PlayerState[] players = baseline.Players.Select(player => new PlayerState(player.Id, player.Side, 20,
            player.Side == PlayerSide.Corp ? 3 : 4, player.AgendaPoints)).ToArray();
        PlayerId runner = players.Single(player => player.Side == PlayerSide.Runner).Id;
        ProgramMemoryState[] programs = cards.Where(card => card.ZoneId == baseline.GetZone(ZoneKind.RunnerRig).Id
                && runtime.GetSpec(card.DefinitionId).CardClass == RuntimeCardClass.Program)
            .Select(card => new ProgramMemoryState(card.Id, runner, runtime.GetSpec(card.DefinitionId).MemoryUnits)).ToArray();
        RulesState rules = new(1, players.Select(player => new PlayerRulesState(player.Id, 5, 0,
            player.Side == PlayerSide.Runner ? tags : 0, 4, player.Side == PlayerSide.Runner ? programs.Sum(program => program.MemoryCost) : 0)).ToArray(),
            programs, [], [], [], []);
        TurnFactsState runnerFacts = new(1, PlayerSide.Runner, 1, 4, 0, true, false, false, false);
        TurnState turn = side == PlayerSide.Runner
            ? new TurnState(side, TurnPhase.Runner, 1, TurnStep.RunnerAction, new TurnHistoryState(1, null, null, runnerFacts, null))
            : new TurnState(side, TurnPhase.Corp, 1);
        GameState state = new(gameId: baseline.GameId, rulesVersion: baseline.RulesVersion,
            cardDataVersion: baseline.CardDataVersion, players: players, cards: cards, zones: zones, servers: servers,
            turn: turn, outcome: baseline.Outcome, randomState: baseline.RandomState, allocator: allocator.State,
            schemaVersion: baseline.SchemaVersion, initialSeed: baseline.InitialSeed, rulesState: rules,
            behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        return new Fixture(state, behavior, runtime, remote);
    }

    private static GameState OpenAction(Fixture fixture) => PassUntil(fixture,
        GameEngine.BeginTurn(fixture.State, PlayerSide.Corp, fixture.Behavior, fixture.Runtime),
        state => state.PendingDecision?.Kind == DecisionKind.TurnAction);

    private static GameState PassUntil(Fixture fixture, GameState state, Func<GameState, bool> stop)
    {
        for (int count = 0; !stop(state); count++)
        {
            Assert.True(count < 20, "Expected timing boundary was not reached.");
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction action = decision.LegalActions.Single(action => action.Kind ==
                (decision.Kind == DecisionKind.TriggerChoice ? ActionKind.DeclineTrigger : ActionKind.PassPriority));
            state = Apply(fixture, state, action).State;
        }
        return state;
    }

    private static ApplyResult Play(Fixture fixture, GameState state, CardDefinitionId definition) =>
        Apply(fixture, state, Assert.Single(state.PendingDecision!.LegalActions,
            action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, definition)));

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        ApplyResult result = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            fixture.Behavior, fixture.Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    private static GameState RoundTrip(Fixture fixture, GameState state)
    {
        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, fixture.Behavior, fixture.Runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Empty(StateInvariantValidator.Collect(restored, fixture.Behavior, fixture.Runtime));
        return restored;
    }

    private static CardInstanceId Card(GameState state, CardDefinitionId definition) =>
        Assert.Single(state.Cards, card => card.DefinitionId == definition).Id;

    private static int Tags(GameState state) => state.RulesState!.Players.Single(player =>
        player.PlayerId == state.GetPlayerBySide(PlayerSide.Runner).Id).Tags;

    private sealed record Placement(CardDefinitionId Definition, ZoneKind Zone, bool Rezzed = false, int Advancement = 0);
    private sealed record Fixture(GameState State, BehaviorCatalog Behavior, RuntimeCardCatalog Runtime, ServerId RemoteId);
}
