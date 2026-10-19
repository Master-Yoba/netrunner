using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP.5a, the primitives binding VP.5's cards found missing, on the probe catalog's test cards (see
/// <see cref="ElevationHarness.VantagePointProbe"/>).
/// </summary>
public sealed class VantagePointRunGapTests
{
    // Kompromat: a run-end ability names the ICE protecting the server of the run that has ended (CR 26.03 6.9.6: the
    // run's end abilities resolve after its state is gone).
    [Theory]
    [InlineData("derez", false, 0)]
    [InlineData("bad-publicity", true, 1)]
    public void ARunEndAbilityNamesTheEndedRunsServer(string option, bool iceRezzed, int badPublicity)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(ProbeKompromatEvent, Place.Grip)
            .Add(ProbeVertigoIce, Place.Ice, server: ElevationBoard.Hq, rezzed: true)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Rd, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        ServerId hq = ElevationBoard.ServerId(state, ElevationBoard.Hq);
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.TargetServerId == hq).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.Null(state.RunState);

        state = ChooseOption(state, option);
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice)
        {
            CardInstanceId[] offered = Actions(state).Select(static action => action.Parameters).OfType<BehaviorChoiceParameters>()
                .SelectMany(static choice => choice.CardIds).Distinct().ToArray();
            Assert.Equal([Card(state, ProbeVertigoIce)], offered);
            state = ChooseCards(state, Card(state, ProbeVertigoIce));
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(iceRezzed, CardState(state, ProbeVertigoIce).Rezzed);
        Assert.True(CardState(state, CorpFillerIce).Rezzed);
        Assert.Equal(badPublicity, state.RulesState?.BadPublicity ?? 0);
    }

    // Reanimation Protocol: install and rez ICE from Archives paying 10 less in all; bad publicity for non-liability ICE.
    // The remote already has the given ICE, so the install tax eats into the shared discount first.
    [Theory]
    [InlineData(ProbeCostlyIce, 2, true, 1)]
    [InlineData(ProbeLiabilityIce, 2, true, 0)]
    [InlineData(ProbeCostlyIce, 3, false, 0)]
    public void AnInstallAndARezCanShareOneDiscount(string ice, int iceBefore, bool rezzed, int badPublicity)
    {
        ElevationBoard board = new() { CorpCredits = 0 };
        for (int index = 0; index < iceBefore; index++)
        {
            board.Add(index % 2 == 0 ? ProbeVertigoIce : ProbeLetheIce, Place.Ice);
        }

        GameState state = board
            .Add(ProbeReanimationOperation, Place.Hq)
            .Add(ice, Place.Archives)
            .Begin(catalog: VantagePointProbe);
        ServerId remote = ElevationBoard.ServerId(state, ElevationBoard.Remote);
        state = PlayFirst(state, ProbeReanimationOperation);
        state = Apply(state, Actions(state).First(action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, ice) && choice.ServerId == remote)).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, ice));
        Assert.Equal(rezzed, CardState(state, ice).Rezzed);
        Assert.Equal(badPublicity, state.RulesState?.BadPublicity ?? 0);
        Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
    }

    // Beta Build: a non-virus program installed from the stack as the run begins returns to the stack's top when it ends.
    [Fact]
    public void ARunEventCanReturnTheProgramItInstalledWhenItsRunEnds()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, StackFiller = 2 }
            .Add(ProbeBetaBuildEvent, Place.Grip)
            .Add(RunnerFillerProgram, Place.Stack)
            .Add(ProbeVirusProgram, Place.Stack)
            .Begin(catalog: VantagePointProbe);
        ServerId remote = ElevationBoard.ServerId(state, ElevationBoard.Remote);
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.TargetServerId == remote).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);

        CardInstanceId[] offered = Actions(state).Select(static action => action.Parameters).OfType<BehaviorInstallChoiceParameters>()
            .Where(static choice => choice.Choice is not null).Select(static choice => choice.Choice!.CardId).Distinct().ToArray();
        Assert.Equal([Card(state, RunnerFillerProgram)], offered);
        state = Apply(state, Actions(state).First(action => action.Parameters is BehaviorInstallChoiceParameters { Choice: not null })).State;
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(RoundTrip(state), RunnerFillerProgram));

        state = RoundTrip(ToAction(state));
        ZoneState stack = state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerStack);
        Assert.Equal(Card(state, RunnerFillerProgram), stack.CardIds[0]);
    }

    // PR #155's review: the remembered program follows the run when the Corp moves it (Proprionegation), and still
    // returns when the moved run ends.
    [Fact]
    public void ARememberedCardFollowsTheRunToAnotherServer()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, StackFiller = 2 }
            .Add("35048", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.ArchivesServer)
            .Add(ProbeBetaBuildEvent, Place.Grip)
            .Add(RunnerFillerProgram, Place.Stack)
            .Begin(catalog: VantagePointProbe);
        state = InstallBetaBuildProgram(state);
        state = PassUntil(state, static value => value.RunState is null || value.PendingDecision?.LegalActions.Any(static action =>
            action.Parameters is AbilityActionParameters { AbilityKey: "proprionegation-move" }) == true);
        ApplyResult moved = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "proprionegation-move" });
        Assert.True(moved.Accepted, moved.Rejection?.Message);
        state = RoundTrip(moved.State);
        ServerId archives = ElevationBoard.ServerId(state, ElevationBoard.ArchivesServer);
        Assert.Equal(archives, state.RunState!.PersistentContext!.ServerId);

        state = RoundTrip(ToAction(state));
        Assert.Equal(Card(state, RunnerFillerProgram), state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerStack).CardIds[0]);
    }

    // PR #155's review: a remembered program that leaves play is forgotten (a copy installed again is a new object), so the
    // run's end leaves it where it is; a live run remembering an uninstalled card is a forgery.
    [Fact]
    public void ARememberedCardIsForgottenWhenItLeavesPlay()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, StackFiller = 2 }
            .Add(ProbeBounceIce, Place.Ice, rezzed: true)
            .Add(ProbeBetaBuildEvent, Place.Grip)
            .Add(RunnerFillerProgram, Place.Stack)
            .Begin(catalog: VantagePointProbe);
        state = InstallBetaBuildProgram(state);
        CardInstanceId program = Card(state, RunnerFillerProgram);
        string remembered = $"\"key\":\"probe-beta-program\",\"cardId\":\"{program.Value}\"";
        string json = EngineJson.Serialize(state);
        Assert.Contains(remembered, json, StringComparison.Ordinal);
        CardInstanceId stacked = state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerStack).CardIds[0];
        System.Text.Json.JsonException refused = Assert.Throws<System.Text.Json.JsonException>(() => EngineJson.DeserializeState(
            json.Replace(remembered, $"\"key\":\"probe-beta-program\",\"cardId\":\"{stacked.Value}\"", StringComparison.Ordinal),
            CatalogFor(state), Runtime));
        Assert.Contains("run.remembered-card-uninstalled", refused.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);

        // The bounce subroutine returns the program to the grip mid-run.
        state = PassUntil(state, static value => value.RunState is null || value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = RoundTrip(ChooseCards(state, program));
        Assert.Equal(ZoneKind.RunnerGrip, ZoneOf(state, RunnerFillerProgram));
        Assert.Null(state.RunState!.PersistentContext?.RememberedCards);

        state = RoundTrip(ToAction(state));
        Assert.Equal(ZoneKind.RunnerGrip, ZoneOf(state, RunnerFillerProgram));
    }

    private static GameState InstallBetaBuildProgram(GameState state)
    {
        ServerId remote = ElevationBoard.ServerId(state, ElevationBoard.Remote);
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.TargetServerId == remote).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        state = RoundTrip(Apply(state, Actions(state).First(action => action.Parameters is BehaviorInstallChoiceParameters { Choice: not null })).State);
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, RunnerFillerProgram));
        return state;
    }

    // Aircheck: when its successful run ends, the Runner may run a remote server, as part of the same action.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARunEndAbilityCanMakeAnotherRun(bool runAgain)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(ProbeAircheckEvent, Place.Grip)
            .Add(CorpFillerAsset, Place.Root)
            .Begin(catalog: VantagePointProbe);
        ServerId hq = ElevationBoard.ServerId(state, ElevationBoard.Hq);
        ServerId remote = ElevationBoard.ServerId(state, ElevationBoard.Remote);
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.TargetServerId == hq).State;
        state = PassToTrigger(state);
        Assert.Null(state.RunState);

        if (!runAgain)
        {
            state = RoundTrip(ToAction(Pass(state)));
            Assert.Null(state.RunState);
            return;
        }

        state = UseTrigger(state);
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice)
        {
            state = ChooseCards(state, Card(state, CorpFillerAsset));
        }

        state = RoundTrip(state);
        Assert.Equal(remote, state.RunState?.AttackedServer);
        Assert.Equal(3, Player(state, PlayerSide.Runner).Clicks);
    }

    // Baker: when the Runner would approach Archives on its run, paying changes the attacked server (CR 26.03 6.9.4g).
    [Theory]
    [InlineData("hq", ElevationBoard.Hq, 0)]
    [InlineData("decline", ElevationBoard.ArchivesServer, 1)]
    public void APaymentCanChangeTheServerTheRunnerWouldApproach(string option, int successful, int credits)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1, HqFiller = 1 }
            .Add(ProbeBakerProgram, Place.Rig)
            .Begin(catalog: VantagePointProbe);
        state = Apply(state, Actions(state).First(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, ProbeBakerProgram))).State;
        state = RoundTrip(PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice));
        Assert.True(state.RunState!.ServerApproachRaised);

        state = RoundTrip(ToAction(ChooseOption(state, option)));
        Assert.Equal([ElevationBoard.ServerId(state, successful)], state.Turn.History!.RunnerThisTurn!.SuccessfulRunServerIds);
        Assert.Equal(credits, Player(state, PlayerSide.Runner).Credits);
    }

    // Unleash: rez a piece of ICE ignoring all costs, then resolve one of its subroutines outside an encounter. A
    // subroutine that needs an encounter's choice (here, damage offering a jack-out) isn't offered.
    [Theory]
    [InlineData("sub-0", 1, 0)]
    [InlineData("sub-1", 0, 2)]
    [InlineData("decline", 0, 0)]
    public void ASubroutineCanResolveOutsideAnEncounter(string option, int tags, int credits)
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeUnleashOperation, Place.Hq)
            .Add(ProbeUnleashIce, Place.Ice)
            .Begin(catalog: VantagePointProbe);
        state = PlayFirst(state, ProbeUnleashOperation);
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice
            && Actions(state).Any(static action => action.Parameters is BehaviorChoiceParameters { CardIds.Count: > 0 }))
        {
            state = ChooseCards(state, Card(state, ProbeUnleashIce));
        }

        Assert.True(CardState(RoundTrip(state), ProbeUnleashIce).Rezzed);
        Assert.Equal(["decline", "sub-0", "sub-1"], Actions(state).Select(static action => action.Parameters).OfType<BehaviorChoiceParameters>()
            .SelectMany(static choice => choice.OptionKeys).Distinct().Order(StringComparer.Ordinal));

        state = ChooseOption(state, option);
        if (option == "sub-1")
        {
            // The subroutine's own choice, saved and restored while it waits.
            state = ChooseOption(RoundTrip(state), "gain-2");
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(tags, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(credits, Player(state, PlayerSide.Corp).Credits);
    }

    // Sacrifice Zone Expansion: once a turn, on a successful run on another server, the Corp may spend an advancement
    // counter for 1 meat damage.
    [Theory]
    [InlineData(ElevationBoard.Hq, 1, true)]
    [InlineData(ElevationBoard.Remote, 1, false)]
    [InlineData(ElevationBoard.Hq, 0, false)]
    public void AnAdvancementCounterCanPayForAReaction(int server, int advancement, bool offered)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1, GripFiller = 2 }
            .Add(ProbeSacrificeZoneAgenda, Place.Root, advancement: advancement, faceUp: true)
            .Begin(catalog: VantagePointProbe);
        ServerId target = ElevationBoard.ServerId(state, server);
        state = Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
        state = PassUntil(state, static value => HasTrigger(value) || value.RunState?.Step is RunTimingStep.BreachCandidateSelection or null);
        Assert.Equal(offered, HasTrigger(state));
        if (offered)
        {
            state = RoundTrip(ToAction(UseTrigger(state)));
            Assert.Equal(0, CardState(state, ProbeSacrificeZoneAgenda).AdvancementCounters);
            Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerGrip));
        }
    }

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Where(zone => zone.Kind == kind).Sum(static zone => zone.CardIds.Count);
}
