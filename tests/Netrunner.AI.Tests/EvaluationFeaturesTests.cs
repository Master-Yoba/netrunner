using Netrunner.Core;
using Xunit;

namespace Netrunner.AI.Tests;

/// <summary>Bot plan S4.1: evaluation feature set 5's terms, each from the observing side's view.</summary>
public sealed class EvaluationFeaturesTests
{
    private static readonly CardDefinitionId EndIce = new("test:end-ice"), TaxIce = new("test:tax-ice"), Agenda = new("test:agenda"),
        Asset = new("test:asset"), Punisher = new("test:punisher"), Rig = new("test:rig");

    private static readonly ScenarioCatalog Catalog = new(
        new(new(EndIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 3), null,
            new(EndIce, 0, IceType.CodeGate, 5, [new EndRunSubroutineSpec()], []), null, null, null, null, [], false),
        new(new(TaxIce, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 1), null,
            new(TaxIce, 0, IceType.CodeGate, 1, [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 1)], []), null, null, null, null, [], false),
        new(new(Agenda, PlayerSide.Corp, RuntimeCardClass.Agenda, agendaPoints: 2, advancementRequirement: 3), null,
            null, null, null, null, new(Agenda, 2), [], false),
        new(new(Asset, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 1, trashCost: 2), null, null, null, null, null, null, [], false),
        new(new(Punisher, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 1),
            new CardBehaviorDefinition(Punisher, PlayerSide.Corp,
                conditions: [new BehaviorCondition(BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn)],
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount: 4, damageKind: DamageKind.Meat)]),
            null, null, null, null, null, [], false),
        new(new(Rig, PlayerSide.Runner, RuntimeCardClass.Resource, installCost: 1),
            new CardBehaviorDefinition(Rig, PlayerSide.Runner, recurringCredits: 2), null, null, null, null, null, [], false));

    [Fact]
    public void SetFiveIsSetFourAndItsStrategicTerms()
    {
        AgentFeatures features = Observe(PlayerSide.Corp, [], []);
        double[] values = EvaluationFeatures.Extract(features, Catalog, 5);
        Assert.Equal(EvaluationFeatures.NamesFor(5).Count, values.Length);
        Assert.Equal(EvaluationFeatures.Extract(features, Catalog, 4), values[..EvaluationFeatures.NamesFor(4).Count]);
        Assert.Equal(EvaluationFeatures.NamesFor(4).Count + 12, EvaluationFeatures.NamesFor(5).Count);
    }

    [Fact]
    public void TheCorpCountsItsOwnAgendasInRemotesTheRunnerCanGetInto()
    {
        AgentCard agenda = Card("card:agenda", Agenda, advancement: 1);
        double Exposed(AgentServer remote) => Value(Observe(PlayerSide.Corp, [remote], []), "exposed-agenda-points");

        Assert.Equal(2, Exposed(new("server:remote", ServerKind.Remote, [], [agenda])));
        // Behind an end-the-run ICE the Runner cannot break, nothing is exposed.
        Assert.Equal(0, Exposed(new("server:remote", ServerKind.Remote, [Card("card:ice", EndIce, rezzed: true)], [agenda])));
        AgentFeatures naked = Observe(PlayerSide.Corp, [new("server:remote", ServerKind.Remote, [], [agenda])], []);
        Assert.Equal(2, Value(naked, "advancements-left"));
        Assert.Equal(1, Value(naked, "scoring-server-open"));
    }

    [Fact]
    public void TheRunnerInfersAProtectedFaceDownCardIsLikelierAnAgenda()
    {
        double Exposed(AgentServer remote) => Value(Observe(PlayerSide.Runner, [remote], []), "exposed-agenda-points");
        double protectedRemote = Exposed(new("server:remote", ServerKind.Remote, [Card("card:ice", TaxIce, rezzed: true)], [null]));
        double nakedRemote = Exposed(new("server:remote", ServerKind.Remote, [], [null]));

        // Half the pool's root cards are agendas of two points: three in four behind ICE, one in eleven in a naked remote.
        Assert.Equal(2 * 0.75, protectedRemote, 3);
        Assert.Equal(2 * (0.1 / 1.1), nakedRemote, 3);
    }

    [Fact]
    public void TheCorpsHqDensityIsItsOwnHand()
    {
        AgentFeatures features = Observe(PlayerSide.Corp, [], [Card("card:a", Agenda), Card("card:b", Asset)]);
        Assert.Equal(0.5, Value(features, "hq-agenda-density"));
    }

    [Fact]
    public void APunisherTheCorpHoldsExposesASmallGrip()
    {
        // The Runner's grip is the fixture's (5 cards dealt): a 4-damage punisher leaves a margin of one.
        AgentFeatures holding = Observe(PlayerSide.Corp, [], [Card("card:p", Punisher)]);
        AgentFeatures without = Observe(PlayerSide.Corp, [], [Card("card:a", Asset)]);
        int grip = holding.Board.HiddenZoneCounts.RunnerGrip;

        Assert.Equal(grip < 4 ? 1 : 0, Value(holding, "punisher-exposure"));
        Assert.Equal(Math.Clamp(grip - 4, -5, 5), Value(holding, "grip-margin"));
        Assert.Equal(0, Value(without, "punisher-exposure"));
        Assert.Equal(Math.Clamp(grip, -5, 5), Value(without, "grip-margin"));
    }

    [Fact]
    public void IncomeCountsTheObserversCardsThatMakeMoney()
    {
        AgentFeatures runner = Observe(PlayerSide.Runner, [], [], rig: [Card("card:rig", Rig)]);
        Assert.Equal(1, Value(runner, "income-cards"));
        Assert.Equal(0, Value(Observe(PlayerSide.Runner, [], []), "income-cards"));
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public void HiddenStateTwinsHaveTheSameSetFiveFeatures(PlayerSide viewer)
    {
        // Two games that differ only in which of two hidden cards is where read the same, from the observer's view.
        GameState baseline = GameState.CreateNew(42);
        PlayerSide owner = viewer == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        ZoneState zone = baseline.Zones.First(entry => entry.Kind == (owner == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack));
        AgentFeatures Twin(bool swapped)
        {
            CardInstanceState[] cards =
            [
                new(new("card:one"), swapped ? Asset : Agenda, zone.Id, owner),
                new(new("card:two"), swapped ? Agenda : Asset, zone.Id, owner),
            ];
            var state = new GameState(baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion, baseline.Players, cards,
                baseline.Zones.Select(entry => entry.Id == zone.Id ? new ZoneState(zone.Id, zone.Kind, owner, zone.ServerId,
                    [new("card:one"), new("card:two")]) : entry), baseline.Servers, baseline.Turn, baseline.Outcome,
                baseline.RandomState, baseline.Allocator, schemaVersion: baseline.SchemaVersion, initialSeed: baseline.InitialSeed);
            return ObservationReader.Read(ObservationBuilder.Build(state, viewer), AgentFeatures.VersionWithHiddenAdvancements);
        }

        Assert.Equal(EvaluationFeatures.Extract(Twin(false), Catalog, 5), EvaluationFeatures.Extract(Twin(true), Catalog, 5));
    }

    private static double Value(AgentFeatures features, string name) =>
        EvaluationFeatures.Extract(features, Catalog, 5)[EvaluationFeatures.NamesFor(5).ToList().IndexOf(name)];

    private static AgentCard Card(string id, CardDefinitionId definition, bool rezzed = false, int advancement = 0) =>
        new() { Id = id, DefinitionId = definition.Value, Rezzed = rezzed, AdvancementCounters = advancement };

    /// <summary>A position seen by <paramref name="viewer"/> with the given remotes, the viewer's hand and the Runner's rig.</summary>
    private static AgentFeatures Observe(PlayerSide viewer, AgentServer[] remotes, AgentCard[] hand, AgentCard[]? rig = null)
    {
        AgentFeatures baseline = ObservationReader.Read(ObservationBuilder.Build(GameState.CreateNew(54), viewer),
            AgentFeatures.VersionWithHiddenAdvancements);
        return baseline with
        {
            PrivateCards = viewer == PlayerSide.Corp ? baseline.PrivateCards with { Hand = [.. hand] } : baseline.PrivateCards with { Grip = [.. hand] },
            Board = baseline.Board with { Servers = [.. baseline.Board.Servers, .. remotes], RunnerRig = [.. rig ?? []] },
        };
    }

    private sealed class ScenarioCatalog(params CardEvaluationDescriptor[] descriptors) : ICardEvaluationCatalog
    {
        private readonly Dictionary<CardDefinitionId, CardEvaluationDescriptor> _definitions = descriptors
            .ToDictionary(descriptor => descriptor.Printed.DefinitionId);
        public string Version => "test-v1";
        public string Fingerprint => "test";
        public IReadOnlyCollection<CardDefinitionId> DefinitionIds => _definitions.Keys;
        public bool TryGet(CardDefinitionId definitionId, out CardEvaluationDescriptor descriptor) =>
            _definitions.TryGetValue(definitionId, out descriptor!);
    }
}
