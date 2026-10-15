using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP.4, the run and ICE primitives, on the probe catalog's test cards (see <see cref="ElevationHarness.VantagePointProbe"/>):
/// VP-ICE-PASSED, VP-SUBROUTINE-BROKEN, VP-ALL-ICE-STRENGTH, VP-ICE-GAINS-SUBROUTINE, VP-CANNOT-STEAL-OR-TRASH,
/// VP-UNLESS-JACK-OUT, VP-NO-SUCCESSFUL-RUN, VP-ACCESS-PAID-ABILITY, and the ones VP.5's binding found (the Runner's tags as
/// an amount, an install limited to server kinds, a run against the source's server, spending advancement counters and
/// returning an installed Runner card to the grip).
/// </summary>
public sealed class VantagePointRunIceTests
{
    private const string Cleaver = "30006";

    private static bool CanUse(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Any(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code)) == true;

    private static GameState Use(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code))).State;

    private static GameState BreakAll(GameState state, string breaker) =>
        Apply(state, Actions(state).Where(action => action.Kind == ActionKind.BreakSubroutines
                && action.Parameters is RunBreakParameters breaking && breaking.BreakerId == Card(state, breaker))
            .MaxBy(static action => ((RunBreakParameters)action.Parameters!).Quantity)!).State;

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Where(zone => zone.Kind == kind).Sum(static zone => zone.CardIds.Count);

    private static int IceStrength(GameState state, string code) =>
        EncounterStrength.ProjectIceQuote(state, CatalogFor(state), Card(state, code))!.EffectiveValue;

    private static bool StealOffered(GameState state) =>
        state.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.StealAgenda) == true;

    private static GameState AccessAgenda(GameState state) =>
        PassUntil(state, static value => value.RunState is { Step: RunTimingStep.BreachAccessAbility } run
            && value.GetCard(run.ChosenAccessCards[^1]).DefinitionId.Value == CorpFillerAgenda);

    // CR 26.03 6.9.4a: passing a piece of ICE; Vertigo: with (here) at most 2 clicks left, the run can't steal or trash.
    // The probe agenda costs a click to steal, so the steal is an action the restriction removes.
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void PassingIceCanForbidStealingForTheRun(int clicksSpent, bool restricted)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(ProbeVertigoIce, Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin(catalog: VantagePointProbe);
        for (int click = 0; click < clicksSpent; click++)
        {
            state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);
        }

        state = AccessAgenda(RunTo(state, ElevationBoard.Remote, static value => value.RunState?.Step == RunTimingStep.BreachCandidateSelection));
        Assert.Equal(restricted, RoundTrip(state).RunState!.BehaviorRestrictions is { Count: 1 });
        Assert.Equal(!restricted, StealOffered(state));
    }

    // Sipa: passing the outermost ICE after fully breaking it.
    [Fact]
    public void PassingTheOutermostIceAfterFullyBreakingItIsAnOccurrence()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add(ProbeSipaProgram, Place.Rig)
            .Add(Cleaver, Place.Rig)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = BreakAll(RunTo(state, ElevationBoard.Remote, InEncounter), Cleaver);
        int credits = Player(state, PlayerSide.Runner).Credits;

        state = PassUntil(state, static value => value.RunState?.Step is RunTimingStep.JackOutDecision or null);
        Assert.Equal(credits + 1, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void PassingInnerIceOrIceWithoutSubroutinesIsNotOutermostFullyBroken()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10, RunnerClicks = 5 }
            .Add(ProbeSipaProgram, Place.Rig)
            .Add(Cleaver, Place.Rig)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Add(ProbeVertigoIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, value => InEncounter(value) && value.RunState!.CurrentIceId == Card(value, CorpFillerIce));
        state = BreakAll(state, Cleaver);
        int credits = Player(state, PlayerSide.Runner).Credits;

        state = PassUntil(state, static value => value.RunState is null);
        Assert.Equal(credits, Player(state, PlayerSide.Runner).Credits);
    }

    // The Tungsten Tailor: every ICE gets -1 strength, and breaking a subroutine on ICE at 0 or less pays once a turn.
    [Fact]
    public void EveryIceCanLoseStrengthAndBreakingAtZeroIsAnOccurrence()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add(ProbeTailorHardware, Place.Rig)
            .Add(Cleaver, Place.Rig)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        Assert.Equal(1 - 1, IceStrength(state, CorpFillerIce));
        int credits = Player(state, PlayerSide.Runner).Credits;

        state = RoundTrip(BreakAll(state, Cleaver));
        Assert.Equal(credits - 1 + 1, Player(state, PlayerSide.Runner).Credits);
    }

    // ezaM: every ICE gets +1 strength for the rest of the run.
    [Fact]
    public void ASubroutineCanStrengthenEveryIceForTheRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Add(ProbeEzamIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        Assert.Equal(1, IceStrength(state, ProbeEzamIce));

        state = RunToNext(state, value => InEncounter(value) && value.RunState!.CurrentIceId == Card(value, CorpFillerIce));
        Assert.Equal(1 + 1, IceStrength(RoundTrip(state), CorpFillerIce));
    }

    // Stick and Poke: the first ICE encountered each turn gains a subroutine before its others.
    [Fact]
    public void EncounteredIceCanGainASubroutineFirst()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 2 }
            .Add(ProbeStickAndPokeResource, Place.Rig)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));
        RunSubroutineState[] subroutines = state.RunState!.Encounter!.Subroutines.ToArray();
        Assert.Equal([1, 0], subroutines.Select(static subroutine => subroutine.Ordinal));

        // The gained one resolves first: 1 net damage and a draw, then the printed one ends the run.
        state = PassUntil(state, static value => value.RunState is null);
        Assert.Equal(2 - 1 + 1, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerHeap));
    }

    // Lionsmane: "unless the Runner jacks out".
    [Theory]
    [InlineData("jack-out", 3, true)]
    [InlineData("suffer-damage", 1, false)]
    public void ASubroutineCanBePreventedByJackingOut(string option, int grip, bool jackedOut)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 3 }
            .Add(ProbeLionsmaneIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        ApplyResult chosen = Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual([option]));

        Assert.Equal(jackedOut, chosen.Events.Select(static value => value.Payload).OfType<RunEndedEventPayload>()
            .Any(static ended => ended.Reason == RunEndReason.JackedOut));
        Assert.Equal(grip, ZoneCount(chosen.State, ZoneKind.RunnerGrip));
    }

    // Flagship: runs against its server can't be declared successful, and don't become unsuccessful.
    [Theory]
    [InlineData(ElevationBoard.Hq, false)]
    [InlineData(ElevationBoard.Rd, true)]
    public void ARunCanBeKeptFromSucceeding(int server, bool successful)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(ProbeFlagshipUpgrade, Place.Root, server: ElevationBoard.Hq, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        ServerId target = ElevationBoard.ServerId(state, server);
        state = Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
        state = PassUntil(state, static value => value.RunState is null or { Step: RunTimingStep.RunEnd });
        Assert.Equal(successful, state.Turn.History!.RunnerThisTurn!.SuccessfulRun);
        if (!successful)
        {
            Assert.False(state.RunState is { Unsuccessful: true });
        }
    }

    // Lampades: an access ability paying the accessed card's printed rez cost.
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void AnAccessAbilityCanCostTheAccessedCardsPrintedCost(int credits, bool usable)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = credits }
            .Add(ProbeLampadesProgram, Place.Rig)
            .Add(ProbeHypeUpgrade, Place.Root)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.RunState?.Step == RunTimingStep.BreachAccessAbility);
        Assert.Equal(usable, CanUse(state, ProbeLampadesProgram));
        if (usable)
        {
            state = RoundTrip(PassUntil(Use(state, ProbeLampadesProgram), static value => value.RunState is null));
            Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, ProbeHypeUpgrade));
            Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
        }
    }

    // Perfect Recall: the Runner can't steal or trash copies of a named card for the run.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARunCanForbidStealingCopiesOfANamedCard(bool name)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(ProbePerfectRecallUpgrade, Place.Root, server: ElevationBoard.Hq, rezzed: true)
            .Add(CorpFillerAgenda, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        ServerId hq = ElevationBoard.ServerId(state, ElevationBoard.Hq);
        state = Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == hq).State;
        if (name)
        {
            state = Use(PassUntil(state, value => CanUse(value, ProbePerfectRecallUpgrade)), ProbePerfectRecallUpgrade);
            if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice)
            {
                state = ChooseCards(state, Card(state, CorpFillerAgenda));
            }

            RunBehaviorRestriction restriction = Assert.Single(RoundTrip(state).RunState!.BehaviorRestrictions!);
            Assert.Equal(CorpFillerAgenda, restriction.NamedDefinitionId?.Value);
        }

        Assert.Equal(!name, StealOffered(AccessAgenda(state)));
    }

    // The Red Room, Flagship: "central server only".
    [Fact]
    public void ACardCanBeInstalledOnlyInSomeServerKinds()
    {
        GameState state = new ElevationBoard()
            .Add(ProbeCentralUpgrade, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        InstallCardParameters[] installs = Actions(state).Select(static action => action.Parameters).OfType<InstallCardParameters>()
            .Where(install => install.CardId == Card(state, ProbeCentralUpgrade)).ToArray();

        Assert.NotEmpty(installs);
        Assert.All(installs, install =>
        {
            Assert.False(install.CreateNewRemote);
            Assert.Contains(state.Servers.Single(server => server.Id == install.ServerId).Kind,
                new[] { ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives });
        });
    }

    [Fact]
    public void DamageCanCountTheRunnersTags()
    {
        GameState state = new ElevationBoard { Tags = 2, GripFiller = 3 }
            .Add(ProbeTagDamageOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        state = RoundTrip(ToAction(PlayFirst(state, ProbeTagDamageOperation)));

        Assert.Equal(3 - 2, ZoneCount(state, ZoneKind.RunnerGrip));
    }

    // Event Horizon: an ability usable only during a run against its own server.
    [Theory]
    [InlineData(ElevationBoard.Remote, true)]
    [InlineData(ElevationBoard.Hq, false)]
    public void AnAbilityCanNeedARunAgainstItsServer(int server, bool usable)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(ProbeSourceServerUpgrade, Place.Root, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        ServerId target = ElevationBoard.ServerId(state, server);
        state = Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
        state = PassUntil(state, static value => value.PendingDecision?.Owner == value.GetPlayerBySide(PlayerSide.Corp).Id
            || value.RunState is null);

        Assert.Equal(usable, CanUse(state, ProbeSourceServerUpgrade));
    }

    // Sacrifice Zone Expansion: hosted advancement counters as a resource.
    [Fact]
    public void AdvancementCountersCanBeRemoved()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeSpendAdvancementAsset, Place.Root, rezzed: true, advancement: 2)
            .Begin(catalog: VantagePointProbe);
        state = RoundTrip(ToAction(Use(state, ProbeSpendAdvancementAsset)));

        Assert.Equal(1, CardState(state, ProbeSpendAdvancementAsset).AdvancementCounters);
        Assert.Equal(2, Player(state, PlayerSide.Corp).Credits);
    }

    // Lethe: an installed Runner card returned to the grip.
    [Fact]
    public void AnInstalledRunnerCardCanReturnToTheGrip()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(RunnerFillerProgram, Place.Rig)
            .Add(ProbeBounceIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice
            || value.RunState?.Step == RunTimingStep.JackOutDecision);
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice)
        {
            state = ChooseCards(state, Card(state, RunnerFillerProgram));
        }

        Assert.Equal(ZoneKind.RunnerGrip, ZoneOf(RoundTrip(state), RunnerFillerProgram));
    }

    // Knowledge Seeker: virus counters on Corp ICE, purged by its own encounter-ended ability, which also derezzes it.
    [Fact]
    public void AnEncounterEndingIsAnOccurrenceAndIceCanHostVirusCounters()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(ProbeSeekerIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.RunState?.Step is RunTimingStep.JackOutDecision or null);

        Assert.Equal(0, Counter(RoundTrip(state), ProbeSeekerIce, "virus"));
        Assert.False(CardState(state, ProbeSeekerIce).Rezzed);
    }

    // Lethe: fully breaking the ICE is an occurrence.
    [Fact]
    public void FullyBreakingIceIsAnOccurrence()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add(Cleaver, Place.Rig)
            .Add(ProbeLetheIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RoundTrip(BreakAll(RunTo(state, ElevationBoard.Remote, InEncounter), Cleaver));

        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
    }

    private static GameState RunToNext(GameState state, Func<GameState, bool> stop) =>
        PassUntil(state, value => stop(value) || value.RunState is null);
}
