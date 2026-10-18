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
        // PR #154's fourth review: the Runner's view names the gained subroutine's source, so the bots can read it.
        Assert.Contains($"\"gainedFrom\":\"{ProbeStickAndPokeResource}\"",
            EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, VantagePointProbe)), StringComparison.Ordinal);

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

        // PR #154's review: jacking out mid-encounter still ends the encounter, and its trigger resolves (the probe's
        // EncounterEnded gains the Corp a credit; the encounter ends either way).
        GameState after = RoundTrip(PassUntil(chosen.State, static value => value.RunState is null));
        Assert.Equal(5 + 1, Player(after, PlayerSide.Corp).Credits);
    }

    // PR #154's fifth review: jacking out mid-encounter keeps a valid encounter cursor while an optional EncounterEnded
    // trigger (the probe watcher's credit) asks whether to use it.
    [Fact]
    public void AnOptionalEncounterEndedTriggerCanBeUsedAfterJackingOut()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 3 }
            .Add(ProbeEncounterWatcherResource, Place.Rig)
            .Add(ProbeLionsmaneIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        int credits = Player(state, PlayerSide.Runner).Credits;
        ApplyResult chosen = Apply(state, static action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.SequenceEqual(["jack-out"]));
        Assert.True(chosen.Accepted, chosen.Rejection?.Message);
        state = RoundTrip(PassUntil(chosen.State, static value => value.RunState is null
            || value.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.UseTrigger) == true));
        Assert.Equal(RunEncounterStatus.Complete, state.RunState!.Encounter!.Status);

        state = RoundTrip(PassUntil(Apply(state, static action => action.Kind == ActionKind.UseTrigger).State,
            static value => value.RunState is null));
        Assert.Equal(credits + 1, Player(state, PlayerSide.Runner).Credits);
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

        // PR #154's review: the probe's "when a run ends unsuccessfully" gains the Corp a credit; a run never declared
        // successful isn't unsuccessful, and a successful one isn't either.
        state = RoundTrip(PassUntil(state, static value => value.RunState is null));
        Assert.Equal(5, Player(state, PlayerSide.Corp).Credits);
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
            // PR #154's fourth review: the offered action names the 3 credits it costs, for its label and the bots.
            AbilityActionParameters offered = state.PendingDecision!.LegalActions.Select(static action => action.Parameters)
                .OfType<AbilityActionParameters>().Single(use => use.SourceCardId == Card(state, ProbeLampadesProgram));
            Assert.Equal(3, offered.ResolvedCreditCost);
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

    // PR #154's review: a saved run's restriction and all-ICE change must match what their source declares.
    [Theory]
    [InlineData("\"reasonKey\":\"probe-vertigo-passed\"", "\"reasonKey\":\"probe-forged\"", "run.behavior-restriction-source-invalid")]
    public void AForgedRunRestrictionIsInvalid(string declared, string forged, string violation)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerClicks = 3 }
            .Add(ProbeVertigoIce, Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin(catalog: VantagePointProbe);
        state = Apply(state, static action => action.Kind == ActionKind.GainCredit).State;
        state = RunTo(ToAction(state), ElevationBoard.Remote, static value => value.RunState?.BehaviorRestrictions is { Count: 1 });

        AssertForgeryCaught(state, declared, forged, violation);
    }

    // PR #154's third review: a restriction whose source never resolved it (a copy still in HQ) is refused.
    [Fact]
    public void ARunRestrictionFromAnUninstalledCopyIsInvalid()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerClicks = 3 }
            .Add(ProbeVertigoIce, Place.Ice, rezzed: true)
            .Add(ProbeVertigoIce, Place.Hq)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin(catalog: VantagePointProbe);
        state = Apply(state, static action => action.Kind == ActionKind.GainCredit).State;
        state = RunTo(ToAction(state), ElevationBoard.Remote, static value => value.RunState?.BehaviorRestrictions is { Count: 1 });
        CardInstanceId installed = state.RunState!.BehaviorRestrictions![0].SourceCardId;
        CardInstanceId inHq = state.Cards.Single(card => card.DefinitionId.Value == ProbeVertigoIce && card.Id != installed).Id;

        AssertForgeryCaught(state, $"\"sourceCardId\":\"{installed.Value}\",\"sourceDefinitionId\"",
            $"\"sourceCardId\":\"{inHq.Value}\",\"sourceDefinitionId\"", "run.behavior-restriction-not-activated");
    }

    [Fact]
    public void AForgedAllIceStrengthChangeIsInvalid()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Add(ProbeEzamIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.RunState?.AllIceStrengthModifiers is { Count: 1 });

        AssertForgeryCaught(state, "\"delta\":1,\"reasonKey\":\"probe-ezam-strength\"",
            "\"delta\":5,\"reasonKey\":\"probe-ezam-strength\"", "run.all-ice-strength-undeclared");
    }

    // PR #154's second review: a copy of a valid change is refused too, past what the ICE's subroutine resolved.
    [Fact]
    public void ADuplicatedAllIceStrengthChangeIsInvalid()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Add(ProbeEzamIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.RunState?.AllIceStrengthModifiers is { Count: 1 });
        string json = EngineJson.Serialize(state);
        int start = json.IndexOf("\"allIceStrengthModifiers\":[", StringComparison.Ordinal) + "\"allIceStrengthModifiers\":[".Length;
        string entry = json[start..(json.IndexOf(']', start))];

        AssertForgeryCaught(state, entry, entry + "," + entry, "run.all-ice-strength-undeclared");
    }

    [Fact]
    public void AGainedSubroutineWithAForgedSourceKeyIsInvalid()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 2 }
            .Add(ProbeStickAndPokeResource, Place.Rig)
            .Add(CorpFillerIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);

        AssertForgeryCaught(state, "\"reasonKey\":\"probe-stick-and-poke\"", "\"reasonKey\":\"probe-forged\"",
            "run.gained-subroutine-source-invalid");
    }

    // PR #154's second review: moving the Runner mid-encounter (Proprionegation) ends the encounter, whose trigger
    // resolves before the move (the probe Lionsmane's EncounterEnded gains the Corp a credit).
    [Fact]
    public void MovingTheRunnerEndsTheEncounter()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35048", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add(ProbeLionsmaneIce, Place.Ice, rezzed: true)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.ArchivesServer)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, static value => value.RunState?.Step == RunTimingStep.EncounterPaidWindow
            && value.PendingDecision?.LegalActions.Any(static action => action.Parameters is AbilityActionParameters { AbilityKey: "proprionegation-move" }) == true);
        state = RoundTrip(Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "proprionegation-move" }).State);
        state = RoundTrip(PassUntil(state, static value => value.RunState is null
            || value.RunState.AttackedServer == ElevationBoard.ServerId(value, ElevationBoard.ArchivesServer)));

        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.ArchivesServer), state.RunState!.AttackedServer);
        Assert.Equal(5 + 1, Player(state, PlayerSide.Corp).Credits);
    }

    private static void AssertForgeryCaught(GameState state, string declared, string forged, string violation)
    {
        string json = EngineJson.Serialize(state);
        Assert.Contains(declared, json, StringComparison.Ordinal);
        Assert.Empty(StateInvariantValidator.Collect(state, CatalogFor(state), Runtime));
        // Restoring runs the validator, which refuses the forged state.
        System.Text.Json.JsonException refused = Assert.Throws<System.Text.Json.JsonException>(() =>
            EngineJson.DeserializeState(json.Replace(declared, forged, StringComparison.Ordinal), CatalogFor(state), Runtime));
        Assert.Contains(violation, refused.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    private static GameState RunToNext(GameState state, Func<GameState, bool> stop) =>
        PassUntil(state, value => stop(value) || value.RunState is null);
}
