using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>VP.5's Corp cards on the Startup catalog.</summary>
public sealed class VantagePointBatch2CorpTests
{
    private const string Ansel20 = "36028";
    private const string Reverb = "36029";
    private const string Sleipnir = "36030";
    private const string Vertigo = "36031";
    private const string PerfectRecall = "36035";
    private const string EzaM = "36039";
    private const string Lionsmane = "36041";
    private const string Vicsek = "36042";
    private const string Unleash = "36044";
    private const string TheRedRoom = "36045";
    private const string Lethe = "36051";
    private const string Paywall = "36052";
    private const string SacrificeZoneExpansion = "36056";
    private const string EventHorizon = "36058";
    private const string Flywheel = "36059";
    private const string ReanimationProtocol = "36062";
    private const string Flagship = "36064";
    private const string ShackletonGrid = "36065";

    private const string Cleaver = "30006";
    private const string Mayfly = "30032";
    private const string HedgeFund = "30075";

    private static GameState Play(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code))).State;

    private static bool Playable(GameState state, string code) =>
        Actions(state).Any(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code));

    private static bool IsChoice(GameState state) => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice;

    /// <summary>Passes to the next behavior choice and takes the option.</summary>
    private static GameState Option(GameState state, string option) => ChooseOption(PassUntil(state, IsChoice), option);

    private static string[] OptionKeys(GameState state) => Actions(state).Select(static action => action.Parameters)
        .OfType<BehaviorChoiceParameters>().SelectMany(static choice => choice.OptionKeys).Distinct().Order(StringComparer.Ordinal).ToArray();

    private static RezCardParameters? RezOffer(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Select(static action => action.Parameters).OfType<RezCardParameters>()
            .FirstOrDefault(rez => rez.CardId == Card(state, code));

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Where(zone => zone.Kind == kind).Sum(static zone => zone.CardIds.Count);

    /// <summary>Runs the remote and passes to the first encounter with the ICE, rezzed.</summary>
    private static GameState Encounter(ElevationBoard board, int server = ElevationBoard.Remote) =>
        RoundTrip(RunTo(board.Begin(catalog: Startup), server, InEncounter));

    /// <summary>Passes the encounter: the Runner breaks nothing, and the subroutines resolve in order.</summary>
    private static GameState Unbroken(GameState state) =>
        Apply(state, static action => action.Kind == ActionKind.PassPriority).State;

    [Fact]
    [Trait("VantagePointCard", Ansel20)]
    public void Ansel20LosesTwoClicksToBreakTwoAndRemovesAHeapCard()
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner)
            .Add(Ansel20, Place.Ice, rezzed: true)
            .Add(Cleaver, Place.Heap));
        Assert.Contains(Actions(state), static action => action.Parameters is RunBioroidAbilityParameters
        { Cost.Amount: 2, SecondSubroutineOrdinal: not null });
        state = RoundTrip(Apply(state, static action => action.Parameters is RunBioroidAbilityParameters
        { SubroutineOrdinal: 0, SecondSubroutineOrdinal: 2 }).State);
        Assert.Equal([true, false, true, false], state.RunState!.Encounter!.Subroutines.Select(static value => value.Broken));

        // The second subroutine removes the heap's card from the game; the run then ends.
        state = PassUntil(Unbroken(state), IsChoice);
        state = ChooseCards(state, Card(state, Cleaver));

        state = RoundTrip(ToAction(state));
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, ZoneOf(state, Cleaver));
        Assert.Null(state.RunState);
    }

    // PR #156's review: a subroutine Stick and Poke gives Ansel 2.0 pairs with each printed one, though it is listed first.
    [Fact]
    [Trait("VantagePointCard", Ansel20)]
    public void Ansel20PairsAGainedSubroutineWithAPrintedOne()
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { StackFiller = 2 }
            .Add("36008", Place.Rig)
            .Add(Ansel20, Place.Ice, rezzed: true));
        Assert.Equal(5, state.RunState!.Encounter!.Subroutines.Count);
        int gained = state.RunState.Encounter.Subroutines[0].Ordinal;
        Assert.Equal(4, gained);
        Assert.Contains(Actions(state), action => action.Parameters is RunBioroidAbilityParameters pair
            && pair.SubroutineOrdinal == 0 && pair.SecondSubroutineOrdinal == gained);
    }

    [Fact]
    [Trait("VantagePointCard", Reverb)]
    public void ReverbCostsLessForEachOtherUnrezzedIce()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 2 }
            .Add(Reverb, Place.Ice)
            .Add(Vertigo, Place.Ice, server: ElevationBoard.Hq)
            .Add(Paywall, Place.Ice, server: ElevationBoard.ArchivesServer)
            .Begin(catalog: Startup);
        state = RunTo(state, ElevationBoard.Remote, value => RezOffer(value, Reverb) is not null);
        Assert.Equal(2, RezOffer(state, Reverb)!.CreditCost);
    }

    [Theory]
    [Trait("VantagePointCard", Sleipnir)]
    [InlineData("headquarters", 0)]
    [InlineData("decline", 1)]
    public void SleipnirShufflesAnHqCardIntoRAndD(string option, int hq)
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(Sleipnir, Place.Ice, rezzed: true));
        int deck = ZoneCount(state, ZoneKind.CorpDeck);
        state = Option(Unbroken(state), "draw");
        state = Option(state, option);
        if (option != "decline")
        {
            state = ChooseCards(state, state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds[0]);
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(hq + 1, ZoneCount(state, ZoneKind.CorpHand));
        Assert.Equal(deck - 1 + (1 - hq), ZoneCount(state, ZoneKind.CorpDeck));
        Assert.Null(state.RunState);
    }

    [Fact]
    [Trait("VantagePointCard", Vertigo)]
    public void VertigoTakesAClickAndThenForbidsStealingWithNoneLeft()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(Vertigo, Place.Ice, rezzed: true)
            .Add("30071", Place.Root)
            .Begin(catalog: Startup);
        // Three clicks spent first: the run's click leaves one; the subroutine takes it.
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);
        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));
        state = PassUntil(Unbroken(state), static value => value.RunState?.Step == RunTimingStep.BreachAccessAbility || value.RunState is null);

        Assert.Equal(0, Player(state, PlayerSide.Runner).Clicks);
        Assert.DoesNotContain(Actions(state), static action => action.Kind == ActionKind.StealAgenda);
    }

    [Fact]
    [Trait("VantagePointCard", PerfectRecall)]
    public void PerfectRecallNamesAnHqCardTheRunnerCannotSteal()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(PerfectRecall, Place.Root, rezzed: true, counters: new Dictionary<string, int> { ["power"] = 1 })
            .Add("30071", Place.Root)
            .Add("30071", Place.Hq)
            .Begin(catalog: Startup);
        state = Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.Remote)).State;
        state = PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(static action =>
            action.Parameters is AbilityActionParameters { AbilityKey: "perfect-recall-name" }) == true);
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "perfect-recall-name" }).State;
        if (IsChoice(state))
        {
            state = ChooseCards(state, state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds[0]);
        }

        state = PassUntil(RoundTrip(state), static value => value.RunState?.Step == RunTimingStep.BreachAccessAbility || value.RunState is null);
        Assert.DoesNotContain(Actions(state), static action => action.Kind == ActionKind.StealAgenda);
    }

    [Theory]
    [Trait("VantagePointCard", EzaM)]
    [InlineData(true)]
    [InlineData(false)]
    public void EzamSendsRAndDsTopToTheBottomAndStrengthensEveryIce(bool bottom)
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 3 }
            .Add(EzaM, Place.Ice, rezzed: true));
        CardInstanceId[] before = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds.ToArray();
        state = PassUntil(Unbroken(state), IsChoice);
        state = bottom ? ChooseCards(state, before[0]) : ChooseCards(state);
        state = RoundTrip(state);

        CardInstanceId[] after = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds.ToArray();
        Assert.Equal(bottom ? [.. before[1..], before[0]] : before, after);
        Assert.Equal(1, state.RunState?.AllIceStrengthModifiers?.Sum(static value => value.Delta) ?? 0);
    }

    [Fact]
    [Trait("VantagePointCard", EzaM)]
    public void EzamSwapsItselfWithAnotherIce()
    {
        GameState state = new ElevationBoard()
            .Add(EzaM, Place.Ice, rezzed: true)
            .Add(Paywall, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: Startup);
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "ezam-swap" }).State;
        if (IsChoice(state))
        {
            state = ChooseCards(state, Card(state, Paywall));
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Hq), ServerOf(state, EzaM));
    }

    [Theory]
    [Trait("VantagePointCard", Lionsmane)]
    [InlineData("pay", "jack-out", 3, 0)]
    [InlineData("decline", "suffer-damage", 0, 4)]
    public void LionsmaneDamagesUnlessPaidOrJackedOut(string pay, string jack, int credits, int damage)
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = credits, GripFiller = 8 }
            .Add(Lionsmane, Place.Ice, rezzed: true));
        state = Option(Unbroken(state), pay);
        state = RoundTrip(ToAction(Option(state, jack)));
        Assert.Equal(8 - 2 - damage, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.Null(state.RunState);
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
    }

    [Theory]
    [Trait("VantagePointCard", Vicsek)]
    [InlineData(Cleaver, false)]
    [InlineData(Mayfly, true)]
    public void VicsekIsATrapOnlyABreakerForAnyIceBreaks(string breaker, bool breaks)
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(breaker, Place.Rig)
            .Add(Vicsek, Place.Ice, rezzed: true));
        // Pumped to Vicsek's strength, only the breaker for any ICE is offered a break.
        CardInstanceId id = Card(state, breaker);
        for (int pumps = 0; pumps < 3 && Actions(state).Any(action => action.Parameters is RunBreakerParameters pump && pump.BreakerId == id); pumps++)
        {
            state = Apply(state, action => action.Parameters is RunBreakerParameters pump && pump.BreakerId == id).State;
        }

        Assert.Equal(breaks, Actions(state).Any(action => action.Parameters is RunBreakParameters use && use.BreakerId == id));
    }

    [Fact]
    [Trait("VantagePointCard", Vicsek)]
    public void VicsekDoublesTheRunnersTagsAndTrashesItself()
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { Tags = 2, GripFiller = 5 }
            .Add(Vicsek, Place.Ice, rezzed: true));
        state = RoundTrip(ToAction(Unbroken(state)));

        Assert.Equal(2 + 2 + 1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(5 - 2, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, Vicsek));
    }

    // The fuzz's find: Vicsek trashes itself mid-encounter; its encounter ends and the Runner moves on to the next ICE.
    [Fact]
    [Trait("VantagePointCard", Vicsek)]
    public void AfterVicsekTrashesItselfTheRunnerMovesToTheNextIce()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 5 }
            .Add(Paywall, Place.Ice, rezzed: true)
            .Add(Vicsek, Place.Ice, rezzed: true)
            .Begin(catalog: Startup);
        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));
        Assert.Equal(Card(state, Vicsek), state.RunState!.Encounter!.IceId);
        state = RoundTrip(PassUntil(Unbroken(state), value => value.RunState?.Encounter?.IceId == Card(value, Paywall)
            || value.RunState is null));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, Vicsek));
        Assert.Equal(Card(state, Paywall), state.RunState!.CurrentIceId);
    }

    [Fact]
    [Trait("VantagePointCard", ReanimationProtocol)]
    public void ReanimationProtocolWithNoIceInArchivesDoesNothing()
    {
        GameState state = new ElevationBoard { CorpCredits = 2 }
            .Add(ReanimationProtocol, Place.Hq)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(Play(state, ReanimationProtocol)));
        Assert.Equal(0, state.RulesState?.BadPublicity ?? 0);
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, ReanimationProtocol));
    }

    [Theory]
    [Trait("VantagePointCard", Unleash)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void UnleashRemovesATagToRezAndResolveASubroutine(int tags, bool playable)
    {
        GameState state = new ElevationBoard { CorpCredits = 0, Tags = tags }
            .Add(Unleash, Place.Hq)
            .Add(Flywheel, Place.Ice)
            .Begin(catalog: Startup);
        Assert.Equal(playable, Playable(state, Unleash));
        if (!playable)
        {
            return;
        }

        state = Play(state, Unleash);
        if (IsChoice(state) && Actions(state).Any(static action => action.Parameters is BehaviorChoiceParameters { CardIds.Count: > 0 }))
        {
            state = ChooseCards(state, Card(state, Flywheel));
        }

        Assert.Equal(["decline", "subroutine-1", "subroutine-2"], OptionKeys(state));
        state = ChooseOption(state, "subroutine-1");
        state = RoundTrip(ToAction(ChooseOption(state, "decline")));

        Assert.True(CardState(state, Flywheel).Rezzed);
        Assert.Equal(0, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(1, Player(state, PlayerSide.Corp).Credits);
    }

    [Theory]
    [Trait("VantagePointCard", TheRedRoom)]
    [InlineData(ElevationBoard.Remote, true)]
    [InlineData(ElevationBoard.Hq, false)]
    public void TheRedRoomEndsARunOnAnotherServer(int server, bool offered)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(TheRedRoom, Place.Root, server: ElevationBoard.Hq, rezzed: true, counters: new Dictionary<string, int> { ["power"] = 1 })
            .Add(HedgeFund, Place.Root)
            .Begin(catalog: Startup);
        state = Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;
        GameState found = PassUntil(state, static value => value.RunState is null || value.PendingDecision?.LegalActions.Any(static action =>
            action.Parameters is AbilityActionParameters { AbilityKey: "red-room-end-run" }) == true || value.RunState.Step == RunTimingStep.BreachAccessAbility);
        bool canEnd = found.PendingDecision?.LegalActions.Any(static action =>
            action.Parameters is AbilityActionParameters { AbilityKey: "red-room-end-run" }) == true;
        Assert.Equal(offered, canEnd);
        if (offered)
        {
            found = RoundTrip(PassUntil(Apply(found, static action => action.Parameters is AbilityActionParameters
            { AbilityKey: "red-room-end-run" }).State, static value => value.RunState is null));
            Assert.Null(found.RunState);
        }
    }

    [Fact]
    [Trait("VantagePointCard", TheRedRoom)]
    public void TheRedRoomIsInstalledOnlyInACentralServer()
    {
        GameState state = new ElevationBoard()
            .Add(TheRedRoom, Place.Hq)
            .Begin(catalog: Startup);
        CardInstanceId room = Card(state, TheRedRoom);
        IEnumerable<InstallCardParameters> installs = Actions(state).Select(static action => action.Parameters).OfType<InstallCardParameters>()
            .Where(install => install.CardId == room);
        Assert.NotEmpty(installs);
        Assert.All(installs, install => Assert.False(install.CreateNewRemote));
    }

    [Fact]
    [Trait("VantagePointCard", Lethe)]
    public void LetheBouncesAnInstalledRunnerCard()
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner)
            .Add(Lethe, Place.Ice, rezzed: true)
            .Add(Cleaver, Place.Rig));
        state = Unbroken(state);
        while (PassUntil(state, static value => IsChoice(value) || value.RunState is null) is var next && IsChoice(next))
        {
            state = Actions(next).Any(action => action.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Contains(Card(next, Cleaver)))
                ? ChooseCards(next, Card(next, Cleaver))
                : ChooseCards(next);
        }

        state = RoundTrip(ToAction(state));
        Assert.True(ZoneOf(state, Cleaver) == ZoneKind.RunnerGrip, $"{ZoneOf(state, Cleaver)} {string.Join(",", state.Zones.Where(z => z.Kind == ZoneKind.CorpDiscard).SelectMany(z => z.CardIds))}");
    }

    [Fact]
    [Trait("VantagePointCard", Lethe)]
    public void LetheTagsTheRunnerWhoFullyBreaksIt()
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add(Lethe, Place.Ice, rezzed: true)
            .Add(Mayfly, Place.Rig));
        CardInstanceId mayfly = Card(state, Mayfly);
        while (Actions(state).FirstOrDefault(action => action.Parameters is RunBreakParameters use && use.BreakerId == mayfly) is not { } breakAction)
        {
            state = Apply(state, action => action.Parameters is RunBreakerParameters pump && pump.BreakerId == mayfly).State;
        }

        while (Actions(state).FirstOrDefault(action => action.Parameters is RunBreakParameters use && use.BreakerId == mayfly) is { } breakAction)
        {
            state = Apply(state, breakAction).State;
        }

        state = RoundTrip(PassUntil(state, static value => value.RunState?.Step is RunTimingStep.BreachAccessAbility or null));
        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
    }

    [Theory]
    [Trait("VantagePointCard", Paywall)]
    [InlineData(2, "pay", true)]
    [InlineData(1, "decline", false)]
    public void PaywallCostsACreditAndEndsTheRunUnlessPaid(int credits, string option, bool continues)
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = credits }
            .Add(Paywall, Place.Ice, rezzed: true));
        Assert.Equal(credits - 1, Player(state, PlayerSide.Runner).Credits);
        state = PassUntil(Unbroken(state), IsChoice);
        Assert.Equal(credits > 1, OptionKeys(state).Contains("pay"));
        state = RoundTrip(ChooseOption(state, option!));
        Assert.Equal(continues, state.RunState is { Unsuccessful: false });
    }

    [Fact]
    [Trait("VantagePointCard", SacrificeZoneExpansion)]
    public void SacrificeZoneExpansionGainsThreeTheFirstTimeItIsAdvanced()
    {
        GameState state = new ElevationBoard { CorpCredits = 5 }
            .Add(SacrificeZoneExpansion, Place.Root, faceUp: true)
            .Begin(catalog: Startup);
        CardInstanceId agenda = Card(state, SacrificeZoneExpansion);
        state = ToAction(Apply(state, action => action.Kind == ActionKind.AdvanceCard
            && action.Parameters is AdvanceCardParameters advance && advance.CardId == agenda).State);
        state = RoundTrip(ToAction(Apply(state, action => action.Kind == ActionKind.AdvanceCard
            && action.Parameters is AdvanceCardParameters advance && advance.CardId == agenda).State));

        Assert.Equal(5 - 1 + 3 - 1, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(2, CardState(state, SacrificeZoneExpansion).AdvancementCounters);
    }

    [Theory]
    [Trait("VantagePointCard", EventHorizon)]
    [InlineData(ElevationBoard.Remote, true)]
    [InlineData(ElevationBoard.Hq, false)]
    public void EventHorizonTrashesItselfToEndARunOnItsServer(int server, bool offered)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(EventHorizon, Place.Ice, rezzed: true)
            .Add(HedgeFund, Place.Root)
            .Begin(catalog: Startup);
        state = Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;
        GameState found = PassUntil(state, static value => value.RunState is null || value.PendingDecision?.LegalActions.Any(static action =>
            action.Parameters is AbilityActionParameters { AbilityKey: "event-horizon-trash" }) == true);
        Assert.Equal(offered, found.RunState is not null);
        if (offered)
        {
            found = RoundTrip(PassUntil(Apply(found, static action => action.Parameters is AbilityActionParameters
            { AbilityKey: "event-horizon-trash" }).State, static value => value.RunState is null));
            Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(found, EventHorizon));
        }
    }

    [Fact]
    [Trait("VantagePointCard", Flywheel)]
    public void FlywheelGainsACreditAndMayDraw()
    {
        GameState state = Encounter(new ElevationBoard(PlayerSide.Runner) { CorpCredits = 0 }
            .Add(Flywheel, Place.Ice, rezzed: true));
        int hq = ZoneCount(state, ZoneKind.CorpHand);
        state = Unbroken(state);
        state = ChooseOption(PassUntil(state, IsChoice), "draw");
        state = ChooseOption(PassUntil(state, IsChoice), "decline");
        state = RoundTrip(state);

        Assert.Equal(2, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(hq + 1, ZoneCount(state, ZoneKind.CorpHand));
    }

    [Fact]
    [Trait("VantagePointCard", ReanimationProtocol)]
    public void ReanimationProtocolInstallsAndRezzesIceFromArchives()
    {
        GameState state = new ElevationBoard { CorpCredits = 2 }
            .Add(ReanimationProtocol, Place.Hq)
            .Add(Lionsmane, Place.Archives)
            .Begin(catalog: Startup);
        state = Play(state, ReanimationProtocol);
        state = PassUntil(state, static value => value.PendingDecision?.Kind != DecisionKind.BehaviorInstallChoice
            || value.PendingDecision.LegalActions.Any(static action => action.Parameters is BehaviorInstallChoiceParameters { Choice: not null }));
        state = Apply(state, Actions(state).First(static action => action.Parameters is BehaviorInstallChoiceParameters { Choice: not null })).State;
        state = RoundTrip(ToAction(state));

        Assert.True(CardState(state, Lionsmane).Rezzed);
        Assert.Equal(1, state.RulesState?.BadPublicity ?? 0);
        Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    [Trait("VantagePointCard", Flagship)]
    public void FlagshipStopsRunsOnItsServerSucceeding()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(Flagship, Place.Root, server: ElevationBoard.Hq, rezzed: true)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.Hq)).State));

        // The run reached its success step and ended, neither successful nor unsuccessful: no breach, no access.
        Assert.Empty(state.Turn.History!.RunnerThisTurn!.SuccessfulRunServerIds);
        Assert.Null(state.RunState);
    }

    [Theory]
    [Trait("VantagePointCard", ShackletonGrid)]
    [InlineData(1, 1)]
    [InlineData(0, 5)]
    public void ShackletonGridDamagesTheRunnerWhoSpendsBadPublicityCredits(int badPublicity, int grip)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { BadPublicity = badPublicity, RunnerCredits = 0, GripFiller = 5 }
            .Add(ShackletonGrid, Place.Root, rezzed: true)
            .Add(Paywall, Place.Ice, rezzed: true)
            .Begin(catalog: Startup);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(Unbroken(state), IsChoice);
        state = ChooseOption(state, OptionKeys(state).Contains("pay") ? "pay" : "decline");

        state = PassUntil(state, static value => HasTrigger(value) || value.RunState?.Step is RunTimingStep.BreachAccessAbility or null);
        if (HasTrigger(state))
        {
            state = UseTrigger(state);
        }

        state = RoundTrip(state);
        Assert.Equal(grip, ZoneCount(state, ZoneKind.RunnerGrip));
    }

    private static ServerId? ServerOf(GameState state, string code) =>
        state.Zones.Single(zone => zone.CardIds.Contains(Card(state, code))).ServerId;
}
