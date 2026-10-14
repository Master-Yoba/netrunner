using System.Text;

using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests.Elevation;

/// <summary>
/// Shared fixtures for Elevation card and primitive tests: the Gateway +
/// Elevation behavior catalog, a runtime catalog compiled from text-free test
/// metadata (so tests run in the synthetic lane), and a declarative board.
/// </summary>
internal static class ElevationHarness
{
    /// <summary>A Runner event and a Corp operation with no behavior, used as hand and deck filler.</summary>
    internal const string RunnerFiller = "90001";
    internal const string CorpFiller = "90002";
    internal const string CorpFillerAgenda = "90003";
    internal const string CorpFillerIce = "90004";
    internal const string CorpFillerAsset = "90005";
    internal const string RunnerFillerProgram = "90006";
    internal const string RunnerFillerResource = "90007";
    internal const string RunnerFillerHardware = "90008";

    /// <summary>The neutral Gateway identities a match board gets by default.</summary>
    internal const string CorpNeutralIdentity = "30077";

    internal const string RunnerNeutralIdentity = "30076";

    private static readonly Lazy<BehaviorCatalog> LazyBehavior = new(ElevationBehaviorCatalog.Create);

    private static readonly Lazy<RuntimeCardCatalog> LazyRuntime = new(CreateRuntime);

    private static readonly Lazy<BehaviorCatalog> LazyProbe = new(CreateProbe);

    private static readonly Lazy<BehaviorCatalog> LazyVantagePointProbe = new(CreateVantagePointProbe);

    public static BehaviorCatalog Behavior => LazyBehavior.Value;

    /// <summary>
    /// The Elevation catalog plus test-only probe behaviors on the filler
    /// event (<see cref="RunnerFiller"/>: "trash 1 installed ice") and the
    /// filler operation (<see cref="CorpFiller"/>: "forfeit an agenda"), for
    /// primitives no printed card in the pool exercises yet.
    /// </summary>
    public static BehaviorCatalog Probe => LazyProbe.Value;

    /// <summary>
    /// VP.2: the Elevation catalog plus test-only probe behaviors for Vantage Point primitives no registered card uses yet:
    /// the filler operation (<see cref="CorpFiller"/>) takes 2 bad publicity.
    /// </summary>
    public static BehaviorCatalog VantagePointProbe => LazyVantagePointProbe.Value;

    /// <summary>The catalog a state was built with, by fingerprint.</summary>
    public static BehaviorCatalog CatalogFor(GameState state) =>
        string.Equals(state.BehaviorCatalogFingerprint, Probe.Fingerprint, StringComparison.Ordinal) ? Probe
        : string.Equals(state.BehaviorCatalogFingerprint, VantagePointProbe.Fingerprint, StringComparison.Ordinal) ? VantagePointProbe
        : Behavior;

    private static BehaviorCatalog CreateVantagePointProbe() =>
        new BehaviorCatalogBuilder("vantage-point-probe", Behavior)
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpFiller),
                PlayerSide.Corp,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.TakeBadPublicity, amount: 2)]))
            .Build();

    private static BehaviorCatalog CreateProbe() =>
        new BehaviorCatalogBuilder("elevation-probe", Behavior)
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(RunnerFiller),
                PlayerSide.Runner,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                        "probe-ice",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: new BehaviorTarget(
                            BehaviorTargetKind.InstalledCard,
                            filter: new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp)))),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                ]))
            .Add(new CardBehaviorDefinition(
                new CardDefinitionId(CorpFiller),
                PlayerSide.Corp,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: new BehaviorChoiceSpec(
                        "probe-forfeit",
                        BehaviorChooser.Controller,
                        BehaviorChoiceMode.Mandatory,
                        BehaviorChoiceCardinality.Exactly(1),
                        target: BehaviorTarget.CardInZone(BehaviorZone.ScoreArea,
                            new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp)))),
                    new BehaviorInstruction(BehaviorInstructionKind.ForfeitAgenda, target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                ]))
            .Build();

    public static RuntimeCardCatalog Runtime => LazyRuntime.Value;

    private static RuntimeCardCatalog CreateRuntime()
    {
        CardDataSet pool = CardDataSet.Combine(
        [
            Pack("sg-test", ElevationTestMetadata.Gateway),
            Pack("elev-test", ElevationTestMetadata.Elevation),
            Pack("fixture-test",
            [
                new(RunnerFiller, "Filler Event", "runner", "neutral-runner", "event", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(CorpFiller, "Filler Operation", "corp", "neutral-corp", "operation", [], 3, 3, false, 0, null, null, null, null, null, 0, null, null, null),
                new(CorpFillerAgenda, "Filler Agenda", "corp", "neutral-corp", "agenda", [], 3, 3, false, null, null, null, null, 2, 3, null, null, null, null),
                new(CorpFillerIce, "Filler Ice", "corp", "neutral-corp", "ice", ["barrier"], 3, 3, false, 1, 1, null, null, null, null, 0, null, null, null),
                new(CorpFillerAsset, "Filler Asset", "corp", "neutral-corp", "asset", [], 3, 3, false, 0, null, null, 1, null, null, 0, null, null, null),
                new(RunnerFillerProgram, "Filler Program", "runner", "neutral-runner", "program", [], 3, 3, false, 1, null, 1, null, null, null, 0, null, null, null),
                new(RunnerFillerResource, "Filler Resource", "runner", "neutral-runner", "resource", [], 3, 3, false, 1, null, null, null, null, null, 0, null, null, null),
                new(RunnerFillerHardware, "Filler Hardware", "runner", "neutral-runner", "hardware", [], 3, 3, false, 1, null, null, null, null, null, 0, null, null, null),
            ]),
        ]);
        return GatewayRuntimeCardCatalog.Create(pool);
    }

    private static CardDataSet Pack(string packCode, IReadOnlyList<MetadataRow> rows)
    {
        NormalizedCardDefinition[] cards = rows
            .Select(row => new NormalizedCardDefinition(
                row.Code, row.Title, row.Side, row.Faction, row.Type, row.Subtypes, string.Empty, packCode,
                row.Quantity, row.DeckLimit, row.Unique, row.Cost, row.Strength, row.MemoryCost, row.TrashCost,
                row.AgendaPoints, row.AdvancementRequirement, row.InfluenceCost, row.MinimumDeckSize,
                row.InfluenceLimit, row.BaseLink))
            .OrderBy(static card => card.Title, StringComparer.Ordinal)
            .ThenBy(static card => card.Code, StringComparer.Ordinal)
            .ToArray();
        byte[] bytes = CardDataJson.SerializeCards(cards);
        var manifest = new CardDataManifest(
            CardDataValidator.CurrentManifestSchemaVersion,
            "https://example.invalid/elevation-test",
            CardDataValidator.OfficialCommitSha,
            "2026-10-02",
            "test-v1",
            CardDataValidator.CurrentNormalizedSchemaVersion,
            CardDataJson.ComputeSha256Hex(bytes),
            "TEST.md",
            "NOASSERTION",
            $"test/{packCode}",
            packCode,
            packCode,
            cards.Length);
        return CardDataResource.LoadBytes(bytes, CardDataJson.SerializeManifest(manifest), Encoding.UTF8.GetBytes("test"));
    }

    public static ApplyResult Apply(GameState state, LegalAction action)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            CatalogFor(state),
            Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    public static ApplyResult Apply(GameState state, Func<LegalAction, bool> select) =>
        Apply(state, Assert.Single(Assert.IsType<PendingDecision>(state.PendingDecision).LegalActions, action => select(action)));

    /// <summary>
    /// Takes the "do nothing" step of the pending decision: pass priority,
    /// decline an optional trigger, continue the run, take the first breach
    /// candidate, or finish an access without acting.
    /// </summary>
    public static GameState Pass(GameState state)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        ActionKind[] order =
        [
            ActionKind.PassPriority,
            ActionKind.DeclineTrigger,
            ActionKind.DeclinePrevention,
            ActionKind.DeclineReplacement,
            ActionKind.ContinueRun,
            ActionKind.NoAccessAction,
            ActionKind.ChooseBreachCandidate,
            ActionKind.OrderTriggers,
        ];
        LegalAction? action = order
            .Select(kind => decision.LegalActions.FirstOrDefault(candidate => candidate.Kind == kind))
            .FirstOrDefault(static candidate => candidate is not null);
        if (action is null && decision.Kind == DecisionKind.InstallChoice)
        {
            // Install without trashing anything: finalize the empty selection.
            action = decision.LegalActions.FirstOrDefault(static candidate =>
                candidate.Parameters is InstallCardParameters { FinalizeSelection: true, SelectedReplacementCardIds.Count: 0 });
        }
        Assert.True(action is not null,
            $"No pass action for {decision.Kind} '{decision.Prompt}': "
            + string.Join(", ", decision.LegalActions.Select(static candidate => candidate.Kind)));
        return Apply(state, action!).State;
    }

    /// <summary>Passes priority and declines optional triggers until the stop condition holds.</summary>
    public static GameState PassUntil(GameState state, Func<GameState, bool> stop, int limit = 40)
    {
        for (int count = 0; !stop(state); count++)
        {
            Assert.True(count < limit, $"Expected boundary was not reached; pending {state.PendingDecision?.Kind}: {state.PendingDecision?.Prompt}");
            state = Pass(state);
        }

        return state;
    }

    public static GameState ToAction(GameState state) =>
        PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.TurnAction);

    public static GameState RoundTrip(GameState state)
    {
        string json = EngineJson.Serialize(state);
        BehaviorCatalog behavior = CatalogFor(state);
        GameState restored = EngineJson.DeserializeState(json, behavior, Runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Empty(StateInvariantValidator.Collect(restored, behavior, Runtime));
        return restored;
    }

    public static CardInstanceId Card(GameState state, string code) =>
        Assert.Single(state.Cards, card => card.DefinitionId.Value == code).Id;

    public static CardInstanceState CardState(GameState state, string code) =>
        Assert.Single(state.Cards, card => card.DefinitionId.Value == code);

    public static ZoneKind ZoneOf(GameState state, string code) =>
        state.Zones.Single(zone => zone.Id == CardState(state, code).ZoneId).Kind;

    public static PlayerState Player(GameState state, PlayerSide side) => state.GetPlayerBySide(side);

    public static PlayerRulesState Rules(GameState state, PlayerSide side) =>
        state.RulesState!.Players.Single(player => player.PlayerId == state.GetPlayerBySide(side).Id);

    public static int Counter(GameState state, string code, string counter) =>
        state.RulesState!.Counters
            .Where(value => value.CardId == Card(state, code) && value.CounterName == counter)
            .Sum(static value => value.Count);

    /// <summary>Starts a run on a board server and passes until the stop condition or the run ends.</summary>
    public static GameState RunTo(GameState state, int server, Func<GameState, bool> stop)
    {
        ServerId target = ElevationBoard.ServerId(state, server);
        state = Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
        return PassUntil(state, value => stop(value) || value.RunState is null);
    }

    public static bool InEncounter(GameState state) =>
        state.RunState is { Step: RunTimingStep.EncounterPaidWindow, Priority.PriorityHolder: PlayerSide.Runner };

    public static bool CanBreak(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Any(action => action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters breaking && breaking.BreakerId == Card(state, code)) == true;

    public static GameState ChooseCards(GameState state, params CardInstanceId[] cards) =>
        Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.SequenceEqual(cards)).State;

    public static GameState ChooseOption(GameState state, string option) =>
        Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual([option])).State;

    public static bool HasTrigger(GameState state) =>
        state.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.UseTrigger) == true;

    public static GameState UseTrigger(GameState state) =>
        Apply(state, static action => action.Kind == ActionKind.UseTrigger).State;

    /// <summary>Passes until a trigger is offered, without declining any.</summary>
    public static GameState PassToTrigger(GameState state) =>
        PassUntil(state, HasTrigger);

    /// <summary>
    /// Takes the basic gain-a-credit action until the active player is out of
    /// clicks, then passes until a trigger or choice waits or the turn moves on.
    /// </summary>
    public static GameState SpendRemainingClicks(GameState state)
    {
        PlayerSide active = state.Turn.ActiveSide;
        for (int step = 0; step < 60; step++)
        {
            if (HasTrigger(state)
                || state.PendingDecision?.Kind is DecisionKind.BehaviorChoice or DecisionKind.BehaviorInstallChoice
                || state.Turn.ActiveSide != active)
            {
                return state;
            }

            state = state.PendingDecision?.Kind == DecisionKind.TurnAction && Player(state, active).Clicks > 0
                ? Apply(state, static action => action.Kind == ActionKind.GainCredit).State
                : Pass(state);
        }

        throw new InvalidOperationException("The turn did not move on.");
    }

    public static GameState PlayFirst(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Kind == ActionKind.PlayCard
            && action.Parameters is PlayCardParameters play && state.Cards.Single(card => card.Id == play.CardId).DefinitionId.Value == code)).State;

    public static IReadOnlyList<LegalAction> Actions(GameState state) =>
        Assert.IsType<PendingDecision>(state.PendingDecision).LegalActions;
}

/// <summary>Where a card starts on a test board.</summary>
internal enum Place
{
    CorpDeck,
    Hq,
    Archives,
    ArchivesFaceUp,
    CorpScore,
    CorpIdentity,
    Stack,
    Grip,
    Heap,
    Rig,
    RunnerScore,
    RunnerIdentity,
    Root,
    Ice,
}

/// <summary>One card on a test board. Server 0..2 are HQ, R&amp;D and Archives; 3 and up are remotes.</summary>
internal sealed record Placement(
    string Code,
    Place Where,
    int Server = 3,
    bool Rezzed = false,
    int Advancement = 0,
    IReadOnlyDictionary<string, int>? Counters = null,
    bool FaceUp = false);

/// <summary>
/// A declarative test board. Unless stated, both identities are absent, each
/// player has 5 credits, and the active player is at the start of their
/// action phase with full clicks.
/// </summary>
internal sealed class ElevationBoard
{
    public const int Hq = 0;
    public const int Rd = 1;
    public const int ArchivesServer = 2;
    public const int Remote = 3;

    private readonly List<Placement> _placements = [];

    public ElevationBoard(PlayerSide active = PlayerSide.Corp, int turnNumber = 2)
    {
        Active = active;
        TurnNumber = turnNumber;
    }

    public PlayerSide Active { get; }

    public int TurnNumber { get; }

    public int CorpCredits { get; set; } = 5;

    public int RunnerCredits { get; set; } = 5;

    public int? CorpClicks { get; set; }

    public int? RunnerClicks { get; set; }

    public int Tags { get; set; }

    /// <summary>VP-BAD-PUBLICITY: the Corp's bad publicity at the start.</summary>
    public int BadPublicity { get; set; }

    public int CorpPoints { get; set; }

    public int RunnerPoints { get; set; }

    public int CorpDeckFiller { get; set; } = 5;

    public int StackFiller { get; set; } = 5;

    public int GripFiller { get; set; }

    public int HqFiller { get; set; }

    public int RemoteServers { get; set; } = 1;

    public ElevationBoard With(params Placement[] placements)
    {
        _placements.AddRange(placements);
        return this;
    }

    public ElevationBoard Add(string code, Place where, int server = Remote, bool rezzed = false, int advancement = 0,
        IReadOnlyDictionary<string, int>? counters = null, bool faceUp = false)
    {
        _placements.Add(new Placement(code, where, server, rezzed, advancement, counters, faceUp));
        return this;
    }

    /// <summary>Builds the state, ready for <see cref="GameEngine.BeginTurn"/> or a run.</summary>
    public GameState Build(ulong seed = 35000, BehaviorCatalog? catalog = null)
    {
        BehaviorCatalog behavior = catalog ?? ElevationHarness.Behavior;
        RuntimeCardCatalog runtime = ElevationHarness.Runtime;
        GameState baseline = GameState.CreateNew(seed, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        var allocator = new StableIdAllocator(baseline.Allocator);
        List<ServerState> servers = baseline.Servers.ToList();
        List<ZoneState> zones = baseline.Zones.ToList();
        // A board with an identity is a setup-created match: both sides need an
        // identity (the neutral Gateway ones by default) and no remote may be empty.
        bool corpIdentity = _placements.Any(static p => p.Where == Place.CorpIdentity);
        bool runnerIdentity = _placements.Any(static p => p.Where == Place.RunnerIdentity);
        bool match = corpIdentity || runnerIdentity;
        List<Placement> placements = _placements.ToList();
        if (match && !corpIdentity)
        {
            placements.Add(new Placement(ElevationHarness.CorpNeutralIdentity, Place.CorpIdentity));
        }

        if (match && !runnerIdentity)
        {
            placements.Add(new Placement(ElevationHarness.RunnerNeutralIdentity, Place.RunnerIdentity));
        }

        int neededRemotes = placements.Where(static p => p.Where is Place.Root or Place.Ice)
            .Select(static p => p.Server - 2).DefaultIfEmpty(0).Max();
        int remoteCount = match ? neededRemotes : Math.Max(RemoteServers, neededRemotes);
        for (int index = 0; index < remoteCount; index++)
        {
            ServerId remote = allocator.NextServerId();
            ZoneId ice = allocator.NextZoneId();
            ZoneId root = allocator.NextZoneId();
            servers.Add(new ServerState(remote, ServerKind.Remote, ice, root));
            zones.Add(new ZoneState(ice, ZoneKind.ServerIce, PlayerSide.Corp, remote));
            zones.Add(new ZoneState(root, ZoneKind.ServerRoot, PlayerSide.Corp, remote));
        }

        var cards = new List<CardInstanceState>();
        var counters = new List<CardCounterState>();
        IEnumerable<Placement> all = placements
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.CorpFiller, Place.CorpDeck), CorpDeckFiller))
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.RunnerFiller, Place.Stack), StackFiller))
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.RunnerFiller, Place.Grip), GripFiller))
            .Concat(Enumerable.Repeat(new Placement(ElevationHarness.CorpFiller, Place.Hq), HqFiller));
        foreach (Placement placement in all)
        {
            CardRuntimeSpec spec = runtime.GetSpec(new CardDefinitionId(placement.Code));
            ZoneState zone = ZoneFor(placement, zones, servers, allocator);
            bool faceUp = placement.Where switch
            {
                Place.Rig or Place.CorpScore or Place.RunnerScore or Place.CorpIdentity or Place.RunnerIdentity
                    or Place.Heap or Place.ArchivesFaceUp => true,
                Place.Root or Place.Ice => placement.Rezzed || placement.FaceUp,
                _ => false,
            };
            var card = new CardInstanceState(allocator.NextCardInstanceId(), new CardDefinitionId(placement.Code), zone.Id,
                spec.Side, faceUp: faceUp, rezzed: placement.Rezzed, advancementCounters: placement.Advancement);
            cards.Add(card);
            foreach ((string name, int count) in placement.Counters ?? new Dictionary<string, int>())
            {
                // "declared-credit" seeds a declared-use hosted pool (recurring credits, Open Market).
                counters.Add(name switch
                {
                    "credit" => CardCounterState.CreateHostedCredit(card.Id, count),
                    "declared-credit" => CardCounterState.CreateHostedCredit(card.Id, count, HostedCreditSpendRestriction.DeclaredUseOnly),
                    _ => new CardCounterState(card.Id, name, count),
                });
            }
        }

        zones = zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
            cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToList();
        PlayerState[] players = baseline.Players.Select(player => new PlayerState(
            player.Id,
            player.Side,
            player.Side == PlayerSide.Corp ? CorpCredits : RunnerCredits,
            player.Side == PlayerSide.Corp ? CorpClicks ?? 3 : RunnerClicks ?? 4,
            player.Side == PlayerSide.Corp ? CorpPoints : RunnerPoints)).ToArray();
        PlayerId runnerId = players.Single(static player => player.Side == PlayerSide.Runner).Id;
        ZoneId rig = zones.Single(static zone => zone.Kind == ZoneKind.RunnerRig).Id;
        ProgramMemoryState[] programs = cards
            .Where(card => card.ZoneId == rig && runtime.GetSpec(card.DefinitionId).CardClass == RuntimeCardClass.Program)
            .Select(card => new ProgramMemoryState(card.Id, runnerId, runtime.GetSpec(card.DefinitionId).MemoryUnits))
            .ToArray();
        RulesState rules = new(1, players.Select(player => new PlayerRulesState(player.Id, 5, 0,
            player.Side == PlayerSide.Runner ? Tags : 0, 4,
            player.Side == PlayerSide.Runner ? programs.Sum(static program => program.MemoryCost) : 0)).ToArray(),
            programs, counters, [], badPublicity: BadPublicity);
        int allotted = Active == PlayerSide.Corp ? 3 : 4;
        TurnFactsState facts = new(1, Active, TurnNumber, allotted, 0, true, Active == PlayerSide.Corp, false, false);
        // During the Runner's turn the Corp's facts are those of its finished turn.
        TurnHistoryState history = Active == PlayerSide.Corp
            ? new TurnHistoryState(1, facts, null, null, null)
            : new TurnHistoryState(1, new TurnFactsState(1, PlayerSide.Corp, TurnNumber, 3, 0, true, true, true, true),
                null, facts, null);
        var turn = new TurnState(
            Active,
            Active == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
            TurnNumber,
            Active == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction,
            history);
        return new GameState(baseline.SchemaVersion, baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players, cards, zones, servers, turn, baseline.Outcome, baseline.RandomState, allocator.State,
            initialSeed: baseline.InitialSeed, rulesState: rules, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
    }

    /// <summary>Builds the board and opens the active player's action phase.</summary>
    public GameState Begin(ulong seed = 35000, BehaviorCatalog? catalog = null)
    {
        GameState initial = Build(seed, catalog);
        GameState state = GameEngine.BeginTurn(initial, Active, ElevationHarness.CatalogFor(initial), ElevationHarness.Runtime);
        return ElevationHarness.ToAction(state);
    }

    public static ServerId ServerId(GameState state, int server) => server switch
    {
        Hq => state.Servers.Single(static value => value.Kind == ServerKind.Headquarters).Id,
        Rd => state.Servers.Single(static value => value.Kind == ServerKind.RAndD).Id,
        ArchivesServer => state.Servers.Single(static value => value.Kind == ServerKind.Archives).Id,
        _ => state.Servers.Where(static value => value.Kind == ServerKind.Remote).ElementAt(server - Remote).Id,
    };

    private static ZoneState ZoneFor(Placement placement, List<ZoneState> zones, List<ServerState> servers,
        StableIdAllocator allocator)
    {
        ZoneKind? kind = placement.Where switch
        {
            Place.CorpDeck => ZoneKind.CorpDeck,
            Place.Hq => ZoneKind.CorpHand,
            Place.Archives or Place.ArchivesFaceUp => ZoneKind.CorpDiscard,
            Place.CorpScore => ZoneKind.CorpScore,
            Place.Stack => ZoneKind.RunnerStack,
            Place.Grip => ZoneKind.RunnerGrip,
            Place.Heap => ZoneKind.RunnerHeap,
            Place.Rig => ZoneKind.RunnerRig,
            Place.RunnerScore => ZoneKind.RunnerScore,
            _ => null,
        };
        if (kind is { } fixedKind)
        {
            return zones.Single(zone => zone.Kind == fixedKind);
        }

        if (placement.Where is Place.CorpIdentity or Place.RunnerIdentity)
        {
            ZoneKind identity = placement.Where == Place.CorpIdentity ? ZoneKind.CorpIdentity : ZoneKind.RunnerIdentity;
            ZoneState? existing = zones.FirstOrDefault(zone => zone.Kind == identity);
            if (existing is null)
            {
                existing = new ZoneState(allocator.NextZoneId(), identity,
                    identity == ZoneKind.CorpIdentity ? PlayerSide.Corp : PlayerSide.Runner);
                zones.Add(existing);
            }

            return existing;
        }

        ServerState server = placement.Server switch
        {
            Hq => servers.Single(static value => value.Kind == ServerKind.Headquarters),
            Rd => servers.Single(static value => value.Kind == ServerKind.RAndD),
            ArchivesServer => servers.Single(static value => value.Kind == ServerKind.Archives),
            _ => servers.Where(static value => value.Kind == ServerKind.Remote).ElementAt(placement.Server - Remote),
        };
        ZoneId zoneId = placement.Where == Place.Root ? server.RootZoneId : server.IceZoneId;
        return zones.Single(zone => zone.Id == zoneId);
    }
}
