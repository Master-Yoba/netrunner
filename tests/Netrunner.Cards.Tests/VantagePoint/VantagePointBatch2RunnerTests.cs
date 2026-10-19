using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>VP.5's Runner cards on the Startup catalog.</summary>
public sealed class VantagePointBatch2RunnerTests
{
    private const string ChainReaction = "36001";
    private const string TheTungstenTailor = "36003";
    private const string Lampades = "36005";
    private const string StickAndPoke = "36008";
    private const string Kompromat = "36010";
    private const string Rotary = "36014";
    private const string Baker = "36015";
    private const string Aircheck = "36018";
    private const string BetaBuild = "36019";
    private const string Sipa = "36023";

    private const string Touchstone = "36021";
    private const string Paywall = "36052";
    private const string Vertigo = "36031";
    private const string Cleaver = "30006";
    private const string HedgeFund = "30075";
    private const string NicoCampaign = "30037";

    private static GameState Play(GameState state, string code, int? server = null) =>
        Apply(state, Actions(state).First(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code)
            && (server is null || play.TargetServerId == ElevationBoard.ServerId(state, server.Value)))).State;

    private static bool Playable(GameState state, string code) =>
        Actions(state).Any(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code));

    private static bool IsChoice(GameState state) => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice;

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Where(zone => zone.Kind == kind).Sum(static zone => zone.CardIds.Count);

    private static GameState RunServer(GameState state, int server) =>
        Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;

    [Theory]
    [Trait("VantagePointCard", ChainReaction)]
    [InlineData(true)]
    [InlineData(false)]
    public void ChainReactionNeedsEveryCentralAndTrashesTwoForOne(bool runAll)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1, RunnerCredits = 5 }
            .Add(ChainReaction, Place.Grip)
            .Add(HedgeFund, Place.Root)
            .Add(Paywall, Place.Ice, server: ElevationBoard.Hq)
            .Add(Cleaver, Place.Rig)
            .Begin(catalog: Startup);
        int[] centrals = runAll ? [ElevationBoard.Hq, ElevationBoard.Rd, ElevationBoard.ArchivesServer] : [ElevationBoard.Hq];
        foreach (int central in centrals)
        {
            state = ToAction(RunServer(state, central));
        }

        Assert.Equal(runAll, Playable(state, ChainReaction));
        if (!runAll)
        {
            return;
        }

        state = Play(state, ChainReaction);
        state = PassUntil(state, IsChoice);
        state = ChooseCards(state, Card(state, HedgeFund), Card(state, Paywall));
        state = RoundTrip(ToAction(PassUntil(state, IsChoice) is var corp && IsChoice(corp) ? ChooseCards(corp, Card(corp, Cleaver)) : state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, HedgeFund));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, Paywall));
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, Cleaver));
    }

    [Theory]
    [Trait("VantagePointCard", TheTungstenTailor)]
    [InlineData(true)]
    [InlineData(false)]
    public void TheTungstenTailorWeakensEveryIceAndPaysForBreakingAtZero(bool tailor)
    {
        ElevationBoard board = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(Cleaver, Place.Rig)
            .Add(Paywall, Place.Ice, rezzed: true);
        GameState state = (tailor ? board.Add(TheTungstenTailor, Place.Rig) : board).Begin(catalog: Startup);
        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));
        int credits = Player(state, PlayerSide.Runner).Credits;
        LegalAction breakAction = Actions(state).First(action => action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters use && use.BreakerId == Card(state, Cleaver));
        state = RoundTrip(PassUntil(Apply(state, breakAction).State, static value => !HasTrigger(value)
            && value.PendingDecision?.Kind == DecisionKind.RunTiming));

        // Paywall's printed strength 1 is 0 with the Tailor; breaking its subroutine then gains a credit.
        Assert.Equal(tailor ? 1 : 0, Player(state, PlayerSide.Runner).Credits - credits + 1);
    }

    [Fact]
    [Trait("VantagePointCard", Lampades)]
    public void LampadesTrashesAnAccessedCardWithStealthCredits()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(Lampades, Place.Rig, counters: new Dictionary<string, int> { ["power"] = 3 })
            .Add(Touchstone, Place.Rig, counters: new Dictionary<string, int> { ["run-credit"] = 2 })
            .Add(NicoCampaign, Place.Root)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, ElevationBoard.Remote), static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Parameters is AbilityActionParameters { AbilityKey: "lampades-trash" }) == true);
        state = RoundTrip(ToAction(Apply(state, static action => action.Parameters is AbilityActionParameters
        { AbilityKey: "lampades-trash" }).State));

        // Nico Campaign's printed rez cost (2), paid with Touchstone's stealth credits; the Runner's 5 are untouched.
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, NicoCampaign));
        Assert.Equal(5, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(2, CounterOn(state, Lampades, "power"));
    }

    [Fact]
    [Trait("VantagePointCard", StickAndPoke)]
    public void StickAndPokeGivesTheFirstEncounteredIceASubroutine()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 1, StackFiller = 2 }
            .Add(StickAndPoke, Place.Rig)
            .Add(Vertigo, Place.Ice, rezzed: true)
            .Begin(catalog: Startup);
        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));

        Assert.Equal(2, state.RunState!.Encounter!.Subroutines.Count);
        string runnerView = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, CatalogFor(state)));
        Assert.Contains("\"gainedFrom\":\"36008\"", runnerView, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("VantagePointCard", Kompromat)]
    [InlineData("derez", false, 0)]
    [InlineData("bad-publicity", true, 1)]
    public void KompromatCostsTheCorpAPieceOfIceOrBadPublicity(string option, bool iceRezzed, int badPublicity)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(Kompromat, Place.Grip)
            .Add(Vertigo, Place.Ice, server: ElevationBoard.Hq, rezzed: true)
            .Begin(catalog: Startup);
        state = Play(state, Kompromat, ElevationBoard.Hq);
        state = PassUntil(state, static value => value.RunState is null && IsChoice(value));
        state = ChooseOption(state, option);
        if (IsChoice(state))
        {
            state = ChooseCards(state, Card(state, Vertigo));
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(iceRezzed, CardState(state, Vertigo).Rezzed);
        Assert.Equal(badPublicity, state.RulesState?.BadPublicity ?? 0);
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, ZoneOf(state, Kompromat));
    }

    [Theory]
    [Trait("VantagePointCard", Rotary)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void TheCorpTrashesRotaryWhileTheRunnerIsTagged(int tags, bool offered)
    {
        GameState state = new ElevationBoard { CorpCredits = 2, Tags = tags }
            .Add(Rotary, Place.Rig)
            .Begin(catalog: Startup);
        Assert.Equal(offered, Actions(state).Any(static action => action.Parameters is AbilityActionParameters { AbilityKey: "rotary-trash" }));
        if (offered)
        {
            state = RoundTrip(ToAction(Apply(state, static action => action.Parameters is AbilityActionParameters
            { AbilityKey: "rotary-trash" }).State));
            Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, Rotary));
            Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
        }
    }

    [Fact]
    [Trait("VantagePointCard", Rotary)]
    public void RotaryTakesATagForAnExtraAccessInHq()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 2 }
            .Add(Rotary, Place.Rig)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, ElevationBoard.Hq), HasTrigger);
        state = RoundTrip(UseTrigger(state));

        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
    }

    [Theory]
    [Trait("VantagePointCard", Baker)]
    [InlineData("hq", ElevationBoard.Hq)]
    [InlineData("decline", ElevationBoard.ArchivesServer)]
    public void BakerRunsArchivesAndMayPayStealthCreditsForHq(string option, int successful)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, HqFiller = 1 }
            .Add(Baker, Place.Rig)
            .Add(Touchstone, Place.Rig, counters: new Dictionary<string, int> { ["run-credit"] = 1 })
            .Begin(catalog: Startup);
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "baker-run" }).State;
        state = RoundTrip(PassUntil(state, IsChoice));
        state = RoundTrip(ToAction(ChooseOption(state, option)));

        Assert.Equal([ElevationBoard.ServerId(state, successful)], state.Turn.History!.RunnerThisTurn!.SuccessfulRunServerIds);
    }

    [Fact]
    [Trait("VantagePointCard", Aircheck)]
    public void AircheckRunsHqOnHostedCreditsAndThenARemote()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(Aircheck, Place.Grip)
            .Add(HedgeFund, Place.Root)
            .Begin(catalog: Startup);
        state = Play(state, Aircheck, ElevationBoard.Hq);
        state = PassUntil(state, static value => value.RunState is null && HasTrigger(value));
        state = UseTrigger(state);
        if (IsChoice(state))
        {
            state = ChooseCards(state, Card(state, HedgeFund));
        }

        state = RoundTrip(state);
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Remote), state.RunState?.AttackedServer);
    }

    [Fact]
    [Trait("VantagePointCard", BetaBuild)]
    public void BetaBuildInstallsAProgramForTheRunAndReturnsIt()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 3, StackFiller = 2 }
            .Add(BetaBuild, Place.Grip)
            .Add(Cleaver, Place.Stack)
            .Begin(catalog: Startup);
        state = Play(state, BetaBuild, ElevationBoard.Remote);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        state = RoundTrip(Apply(state, Actions(state).First(action => action.Parameters is BehaviorInstallChoiceParameters
        { Choice: not null } choice && choice.Choice!.CardId == Card(state, Cleaver))).State);
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));

        state = RoundTrip(ToAction(state));
        Assert.Equal(Card(state, Cleaver), state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerStack).CardIds[0]);
    }

    [Fact]
    [Trait("VantagePointCard", Sipa)]
    public void SipaSwapsTheFullyBrokenOutermostIce()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(Sipa, Place.Rig)
            .Add(Cleaver, Place.Rig)
            .Add(Paywall, Place.Ice, rezzed: true)
            .Add(Vertigo, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: Startup);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = Apply(state, action => action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters use && use.BreakerId == Card(state, Cleaver)).State;
        state = PassUntil(state, HasTrigger);
        state = UseTrigger(state);
        if (IsChoice(state))
        {
            state = ChooseCards(state, Card(state, Vertigo));
        }

        state = RoundTrip(state);
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Hq), state.Zones.Single(zone => zone.CardIds.Contains(Card(state, Paywall))).ServerId);
    }

    private static int CounterOn(GameState state, string code, string counter) =>
        state.RulesState?.Counters.Where(value => value.CardId == Card(state, code) && value.CounterName == counter).Sum(static value => value.Count) ?? 0;
}
