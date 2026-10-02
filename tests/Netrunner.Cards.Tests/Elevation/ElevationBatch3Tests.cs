using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch3Tests
{
    private const string Hantu = "35008";
    private const string Cleaver = "30006";
    private const string HedgeFund = "30075";
    private const string SmartwareDistributor = "30033";
    private const string T400 = "30031";

    private static bool CanRez(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Any(action => action.Kind == ActionKind.RezCard
            && action.Parameters is RezCardParameters rez && rez.CardId == Card(state, code)) == true;

    private static GameState Rez(GameState state, string code) =>
        Apply(state, action => action.Kind == ActionKind.RezCard
            && action.Parameters is RezCardParameters rez && rez.CardId == Card(state, code)).State;

    /// <summary>Starts a run on the remote and has the Corp rez the approached ICE.</summary>
    private static GameState RunAndRez(GameState state, string ice) =>
        Rez(RunTo(state, ElevationBoard.Remote, value => CanRez(value, ice)), ice);

    [Fact]
    [Trait("ElevationCard", "35079")]
    public void FlyswatterPurgesVirusCountersWhenRezzedDuringARunOnItsServer()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 5 }
            .Add("35079", Place.Ice)
            .Add(Hantu, Place.Rig, counters: new Dictionary<string, int> { ["virus"] = 2 })
            .Begin();
        state = RoundTrip(PassUntil(RunAndRez(state, "35079"), static value => !HasTrigger(value)));

        Assert.True(CardState(state, "35079").Rezzed);
        Assert.Equal(0, Counter(state, Hantu, "virus"));
    }

    [Fact]
    [Trait("ElevationCard", "35079")]
    public void FlyswatterEndsTheRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35079", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = RoundTrip(PassUntil(state, static value => value.RunState is null));

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, CorpFillerAgenda));
    }

    [Fact]
    [Trait("ElevationCard", "35041")]
    public void BumiMayTrashATrojanWhenRezzedDuringARunOnItsServer()
    {
        const string Chromatophores = "35030";
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 5, RunnerCredits = 5 }
            .Add("35041", Place.Ice)
            .Add(Chromatophores, Place.Grip)
            .Begin();
        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, Chromatophores)).State);
        state = RunAndRez(state, "35041");
        Assert.True(HasTrigger(state));
        state = UseTrigger(state);
        state = RoundTrip(ChooseCards(state, Card(state, Chromatophores)));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, Chromatophores));
    }

    [Fact]
    [Trait("ElevationCard", "35041")]
    public void BumiCanBeBrokenByLosingAClick()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35041", Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);

        Assert.Contains(Actions(state), static action => action.Kind == ActionKind.UseBioroidAbility);
    }

    [Fact]
    [Trait("ElevationCard", "35052")]
    public void EmpiricistDrawsTopsHqThenDamagesAndTags()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 5, CorpDeckFiller = 3 }
            .Add("35052", Place.Ice, rezzed: true)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = RoundTrip(ChooseCards(state, Card(state, HedgeFund)));
        state = PassUntil(state, static value => value.RunState is null);

        Assert.Equal(Card(state, HedgeFund), state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds[0]);
        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(5 - 1 - 2, state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    [Trait("ElevationCard", "35074")]
    public void BiawakEndsTheRunWithItsLastSubroutine()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35074", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        // With no program or resource installed, the Corp chooses "end the run".
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
        state = RoundTrip(Apply(state, static action => action.Parameters is IceSubroutineChoiceParameters
        {
            Choice: IceSubroutineChoiceKind.EndRun,
        }).State);
        state = PassUntil(state, static value => value.RunState is null);

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, CorpFillerAgenda));
    }

    [Fact]
    [Trait("ElevationCard", "35002")]
    public void TopanInstallsTwoCheaperAndDealsOneMeatDamage()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, GripFiller = 2 }
            .Add("35002", Place.RunnerIdentity)
            .Add(Cleaver, Place.Grip)
            .Begin();
        state = Apply(state, action => action.Parameters is AbilityActionParameters { AbilityKey: "topan-install" }).State;
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Cleaver)).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));
        Assert.Equal(5 - (3 - 2), Player(state, PlayerSide.Runner).Credits);
        Assert.Single(state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(Actions(state), static action => action.Parameters is AbilityActionParameters { AbilityKey: "topan-install" });
    }

    [Fact]
    [Trait("ElevationCard", "35001")]
    public void RyoGainsAndTheCorpTrashesFromHqAfterASubroutineResolved()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, GripFiller = 5 }
            .Add("35001", Place.RunnerIdentity)
            .Add("35052", Place.Ice, rezzed: true)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        // Empiricist's subroutines resolve, none ending the run.
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = ChooseCards(state);
        // Ryō's trigger: the Corp chooses the HQ card to trash.
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice || value.RunState is null);
        Assert.NotNull(state.RunState);
        state = RoundTrip(ChooseCards(state, Card(state, HedgeFund)));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, HedgeFund));
        Assert.Equal(5 + 1, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35001")]
    public void RyoDoesNothingWithoutAResolvedSubroutine()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add("35001", Place.RunnerIdentity)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        state = RoundTrip(RunTo(state, 0, static value => value.RunState?.Step == RunTimingStep.BreachCandidateSelection));

        Assert.Equal(5, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, HedgeFund));
    }

    [Fact]
    [Trait("ElevationCard", "35012")]
    public void BarryInstallsAResourceWhenTheCorpRezzesIce()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 5, RunnerCredits = 5 }
            .Add("35012", Place.RunnerIdentity)
            .Add(CorpFillerIce, Place.Ice)
            .Add(SmartwareDistributor, Place.Grip)
            .Add(Cleaver, Place.Grip)
            .Begin();
        state = PassToTrigger(RunAndRez(state, CorpFillerIce));
        state = UseTrigger(state);
        Assert.DoesNotContain(Actions(state), action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Cleaver));
        state = RoundTrip(Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, SmartwareDistributor)).State);

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, SmartwareDistributor));
    }

    private static GameState UseIceBreak(GameState state, int ordinal) =>
        Apply(state, action => action.Parameters is RunBioroidAbilityParameters breaking && breaking.SubroutineOrdinal == ordinal).State;

    [Theory]
    [Trait("ElevationCard", "35064")]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void NPotCanBeBrokenForThreeCreditsAndEndsTheRunAtThreatTwo(int corpPoints, bool ends)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, CorpPoints = corpPoints }
            .Add("35064", Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = RoundTrip(UseIceBreak(state, 0));
        Assert.Equal(5 - 3, Player(state, PlayerSide.Runner).Credits);
        Assert.DoesNotContain(Actions(state), static action => action.Kind == ActionKind.UseBioroidAbility);

        state = PassUntil(state, static value => value.RunState is null);
        Assert.Equal(!ends, state.Turn.History!.RunnerThisTurn!.SuccessfulRun);
    }

    [Fact]
    [Trait("ElevationCard", "35054")]
    public void SemakSamunIsBrokenOnlyByAFracter()
    {
        const string Mayfly = "30032";
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35054", Place.Ice, rezzed: true)
            .Add(Cleaver, Place.Rig)
            .Add(Mayfly, Place.Rig)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);

        Assert.True(CanBreak(state, Cleaver));
        Assert.False(CanBreak(state, Mayfly));
    }

    [Theory]
    [Trait("ElevationCard", "35054")]
    [InlineData("suffer-damage")]
    [InlineData("end-run")]
    public void SemakSamunEndsTheRunUnlessTheRunnerSuffersThreeNetDamage(string option)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 5 }
            .Add("35054", Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.Equal(PlayerSide.Runner, state.Players.Single(player => player.Id == state.PendingDecision!.Owner).Side);
        state = RoundTrip(ChooseOption(state, option));
        state = PassUntil(state, static value => value.RunState is null);

        Assert.Equal(option != "end-run", state.Turn.History!.RunnerThisTurn!.SuccessfulRun);
        Assert.Equal(option == "end-run" ? 5 : 2, state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Theory]
    [Trait("ElevationCard", "35042")]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ScatterFieldIsStrongerWhileItIsTheOnlyIceOnItsServer(bool secondIce, bool breakableAtOnce)
    {
        const string Buzzsaw = "30005";
        var board = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35042", Place.Ice, rezzed: true)
            .Add(Buzzsaw, Place.Rig);
        if (secondIce)
        {
            board = board.Add(CorpFillerIce, Place.Ice);
        }

        GameState state = board.Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);

        Assert.Equal(Card(state, "35042"), state.RunState!.Encounter!.IceId);
        Assert.Equal(breakableAtOnce, CanBreak(state, Buzzsaw));
    }

    [Fact]
    [Trait("ElevationCard", "35030")]
    public void ChromatophoresGivesItsHostIceEverySubtype()
    {
        const string Buzzsaw = "30005";
        const string Kessleroid = "35075";
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add(Kessleroid, Place.Ice, rezzed: true)
            .Add("35030", Place.Grip)
            .Add(Buzzsaw, Place.Rig)
            .Begin();
        state = Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, "35030") && install.HostCardId == Card(state, Kessleroid)).State;
        state = RoundTrip(ToAction(state));
        Assert.Equal(Card(state, Kessleroid), CardState(state, "35030").HostCardId);

        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        // A decoder breaks the barrier, now also a code gate.
        Assert.True(CanBreak(state, Buzzsaw));
    }

    private static bool CanUse(GameState state, string abilityKey) =>
        state.PendingDecision?.LegalActions.Any(action => action.Parameters is AbilityActionParameters use
            && use.AbilityKey == abilityKey) == true;

    [Fact]
    [Trait("ElevationCard", "35048")]
    public void ProprionegationMovesTheRunnerToTheOutermostPositionOfArchives()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35048", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add(CorpFillerIce, Place.Ice)
            .Add(CorpFillerIce, Place.Ice, server: 2)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => CanUse(value, "proprionegation-move"));
        Assert.NotNull(state.RunState);
        state = RoundTrip(Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "proprionegation-move" }).State);
        state = PassUntil(state, static value => value.RunState is null
            || value.RunState.AttackedServer == ElevationBoard.ServerId(value, 2) && value.RunState.Step == RunTimingStep.ApproachPaidWindow);

        Assert.Equal(ElevationBoard.ServerId(state, 2), state.RunState!.AttackedServer);
        Assert.Equal(0, Counter(state, "35048", "agenda"));
        Assert.False(CanUse(state, "proprionegation-move"));
    }

    [Fact]
    [Trait("ElevationCard", "35048")]
    public void ProprionegationCannotBeUsedOutsideARun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35048", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Begin();
        state = PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Corp);

        Assert.False(CanUse(state, "proprionegation-move"));
    }

    [Fact]
    [Trait("ElevationCard", "35019")]
    public void MaglectricRapidTrashesItselfToDerezACorpCardAfterAnHqRun()
    {
        const string Regolith = "30071";
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35019", Place.Rig)
            .Add(Regolith, Place.Root, rezzed: true)
            .Begin();
        state = RunTo(state, 0, HasTrigger);
        state = RoundTrip(ChooseCards(UseTrigger(state), Card(state, Regolith)));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35019"));
        Assert.False(CardState(state, Regolith).Rezzed);
        Assert.False(CardState(state, Regolith).FaceUp);
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, Regolith));
    }

    private static GameState PlayRunEvent(GameState state, string code, int server) =>
        Apply(state, action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code)
            && play.TargetServerId == ElevationBoard.ServerId(state, server)).State;

    [Fact]
    [Trait("ElevationCard", "35016")]
    public void MaintenanceAccessRunsArchivesButApproachesAndBreachesHq()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35016", Place.Grip)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        Assert.DoesNotContain(Actions(state), action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, "35016")
            && play.TargetServerId != ElevationBoard.ServerId(state, 2));
        state = PlayRunEvent(state, "35016", 2);
        Assert.Equal(4 - 2, Player(state, PlayerSide.Runner).Clicks);
        state = RoundTrip(PassUntil(state, static value => value.RunState is null
            || value.RunState.Step is RunTimingStep.BreachCandidateSelection or RunTimingStep.BreachAccessAbility));

        Assert.Equal(ElevationBoard.ServerId(state, 0), state.RunState!.AttackedServer);
    }

    [Fact]
    [Trait("ElevationCard", "35003")]
    public void CharmOffensiveTrashesARezzedCopyOfACardAccessedInArchives()
    {
        const string Regolith = "30071";
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35003", Place.Grip)
            .Add(Regolith, Place.ArchivesFaceUp)
            .Add(Regolith, Place.Root, rezzed: true)
            .Begin();
        CardInstanceId installed = state.Cards.Single(card => card.DefinitionId.Value == Regolith
            && state.Zones.Single(zone => zone.Id == card.ZoneId).Kind == ZoneKind.ServerRoot).Id;
        state = PlayRunEvent(state, "35003", 2);
        state = PassToTrigger(state);
        state = RoundTrip(ChooseCards(UseTrigger(state), installed));

        Assert.Equal(ZoneKind.CorpDiscard, state.Zones.Single(zone => zone.CardIds.Contains(installed)).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35003"));
    }

    [Fact]
    [Trait("ElevationCard", "35021")]
    public void FransofiaWardRaisesEveryIceRezCost()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 10 }
            .Add("35021", Place.Rig)
            .Add("35079", Place.Ice)
            .Begin();
        state = RoundTrip(RunAndRez(state, "35079"));

        Assert.Equal(10 - (2 + 1), Player(state, PlayerSide.Corp).Credits);
    }

    [Theory]
    [Trait("ElevationCard", "35021")]
    [InlineData(15, true)]
    [InlineData(14, false)]
    public void FransofiaWardTrashesItselfToBypassIceWhenTheCorpIsRich(int corpCredits, bool offered)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = corpCredits }
            .Add("35021", Place.Rig)
            .Add("35075", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => HasTrigger(value) || InEncounter(value));
        Assert.Equal(offered, HasTrigger(state));
        if (!offered)
        {
            return;
        }

        state = RoundTrip(UseTrigger(state));
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35021"));
        state = PassUntil(state, static value => value.RunState is null
            || value.RunState.Step is RunTimingStep.BreachCandidateSelection or RunTimingStep.BreachAccessAbility);
        // Kessleroid's end-the-run subroutines never resolved: the Runner reached the root.
        Assert.NotNull(state.RunState);
    }

    [Fact]
    [Trait("ElevationCard", "35053")]
    public void MycowebInstallsFromArchivesRezzesCheaperAndResolvesASentrySubroutine()
    {
        const string Doomscroll = "35063";
        const string Kessleroid = "35075";
        const string Flyswatter = "35079";
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 0, GripFiller = 5 }
            .Add("35053", Place.Ice, rezzed: true)
            .Add(Doomscroll, Place.Ice, server: 0, rezzed: true)
            .Add(Kessleroid, Place.Ice, server: 1)
            .Add(Flyswatter, Place.ArchivesFaceUp)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        state = RoundTrip(Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { CreateNewRemote: true } choice }
            && choice.CardId == Card(state, Flyswatter)).State);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = RoundTrip(ChooseCards(state, Card(state, Kessleroid)));
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.DoesNotContain(Actions(state), static action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("resolve-35079-0"));
        state = RoundTrip(ChooseOption(state, "resolve-35063-0"));

        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, Flyswatter));
        Assert.True(CardState(state, Kessleroid).Rezzed);
        Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
    }

    [Fact]
    [Trait("ElevationCard", "35045")]
    public void MerciaInstallsIceFromHqAndMovesToTheServerItProtects()
    {
        const string Kessleroid = "35075";
        GameState state = new ElevationBoard { CorpCredits = 5, CorpDeckFiller = 3 }
            .Add("35045", Place.Root, rezzed: true)
            .Add(Kessleroid, Place.Hq)
            .Begin();
        state = PassToTrigger(SpendRemainingClicks(state));
        state = UseTrigger(state);
        state = RoundTrip(Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Kessleroid) && choice.ServerId == ElevationBoard.ServerId(state, 0)).State);

        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, Kessleroid));
        ZoneState merciaZone = state.Zones.Single(zone => zone.CardIds.Contains(Card(state, "35045")));
        Assert.Equal(ZoneKind.ServerRoot, merciaZone.Kind);
        Assert.Equal(ElevationBoard.ServerId(state, 0), merciaZone.ServerId);
        Assert.True(CardState(state, "35045").Rezzed);
    }

    [Fact]
    [Trait("ElevationCard", "35035")]
    public void LeoConstructionTrashesABioroidOnTheAttackedServerToEndTheRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35035", Place.CorpIdentity)
            .Add("35041", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin();
        Assert.False(CanUse(state, "leo-end-run"));
        state = RunTo(state, ElevationBoard.Remote, static value => CanUse(value, "leo-end-run"));
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "leo-end-run" }).State;
        state = RoundTrip(ChooseCards(state, Card(state, "35041")));
        state = PassUntil(state, static value => value.RunState is null);

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35041"));
        Assert.False(state.Turn.History!.RunnerThisTurn!.SuccessfulRun);
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, CorpFillerAgenda));
    }

    [Fact]
    [Trait("ElevationCard", "35074")]
    public void BiawakCanBeRezzedByForfeitingAnAgendaForTenOfItsCost()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 4, CorpPoints = 2 }
            .Add("35074", Place.Ice)
            .Add(CorpFillerAgenda, Place.CorpScore)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, value => CanRez(value, "35074"));
        RezCardParameters rez = Assert.Single(Actions(state).Select(static action => action.Parameters).OfType<RezCardParameters>());
        Assert.Equal(Card(state, CorpFillerAgenda), rez.ForfeitAgendaId);
        Assert.Equal(14 - 10, rez.CreditCost);
        state = RoundTrip(Rez(state, "35074"));

        Assert.True(CardState(state, "35074").Rezzed);
        Assert.Equal(ZoneKind.CorpRemovedFromGame, ZoneOf(state, CorpFillerAgenda));
        Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(0, Player(state, PlayerSide.Corp).AgendaPoints);
    }

    [Theory]
    [Trait("ElevationCard", "35005")]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ShredPreventsTheFirstEndOfTheRunUnlessTheCorpTrashesRandomHqCards(int hqCards, bool firstPrevented)
    {
        var board = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add("35005", Place.Grip)
            .Add("35075", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root);
        for (int index = 0; index < hqCards; index++)
        {
            board = board.Add(HedgeFund, Place.Hq);
        }

        GameState state = board.Begin();
        state = PlayRunEvent(state, "35005", ElevationBoard.Remote);
        state = RoundTrip(PassUntil(state, static value => value.RunState is null));

        // Kessleroid's second end-the-run ends the run even after Shred stopped the first.
        Assert.False(state.Turn.History!.RunnerThisTurn!.SuccessfulRun);
        Assert.Empty(state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds);
        Assert.Equal(firstPrevented ? 0 : 1, state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDiscard).CardIds.Count);
    }

    [Fact]
    [Trait("ElevationCard", "35005")]
    public void ShredLetsTheRunReachTheServerPastOneEndTheRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add("35005", Place.Grip)
            .Add("35079", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin();
        state = PlayRunEvent(state, "35005", ElevationBoard.Remote);
        state = PassUntil(state, static value => value.RunState is null
            || value.RunState.Step is RunTimingStep.BreachCandidateSelection or RunTimingStep.BreachAccessAbility);

        Assert.NotNull(RoundTrip(state).RunState);
    }

    [Fact]
    [Trait("ElevationCard", "35056")]
    public void MitraAmanGainsThreeAndSwapsTheApproachedIceWithIceFromHq()
    {
        const string Kessleroid = "35075";
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 0 }
            .Add("35056", Place.Root, rezzed: true)
            .Add(CorpFillerIce, Place.Ice)
            .Add(Kessleroid, Place.Hq)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, HasTrigger);
        state = UseTrigger(state);
        state = ChooseOption(state, "headquarters");
        state = RoundTrip(ChooseCards(state, Card(state, Kessleroid)));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35056"));
        Assert.Equal(3, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, Kessleroid));
        Assert.False(CardState(state, Kessleroid).Rezzed);
        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, CorpFillerIce));
        Assert.Equal(Card(state, Kessleroid), state.RunState!.CurrentIceId);
        // The Corp may rez the ICE now being approached.
        state = PassUntil(state, value => CanRez(value, Kessleroid) || value.RunState is null);
        Assert.True(CanRez(state, Kessleroid));
    }

    [Fact]
    [Trait("ElevationCard", "35027")]
    public void GamedragonProHostsOnABreakerForStrengthThatLastsTheRun()
    {
        const string Palisade = "30072";
        const string Mayfly = "30032";
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 20 }
            .Add("35027", Place.Grip)
            .Add(Cleaver, Place.Rig)
            .Add(Mayfly, Place.Rig)
            .Add(Palisade, Place.Ice, rezzed: true)
            .Add(Palisade, Place.Ice, rezzed: true)
            .Begin();
        state = Apply(state, action => action.Parameters is InstallCardParameters install && install.CardId == Card(state, "35027")).State;
        state = UseTrigger(PassToTrigger(state));
        Assert.DoesNotContain(Actions(state), action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.Contains(Card(state, Mayfly)));
        state = RoundTrip(ChooseCards(state, Card(state, Cleaver)));
        Assert.Equal(Card(state, Cleaver), CardState(state, "35027").HostCardId);

        // Palisade is strength 4 on a remote: Cleaver (3) breaks it with the mod's +1.
        state = RunTo(ToAction(state), ElevationBoard.Remote, InEncounter);
        Assert.True(CanBreak(state, Cleaver));
        state = Apply(state, action => action.Kind == ActionKind.PumpBreaker
            && action.Parameters is RunBreakerParameters pump && pump.BreakerId == Card(state, Cleaver)).State;
        state = Apply(state, action => action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters breaking && breaking.BreakerId == Card(state, Cleaver)).State;
        state = PassUntil(state, static value => InEncounter(value) && value.RunState!.CurrentIceIndex == 0 || value.RunState is null);

        // The pump from the first encounter still counts in the second.
        Assert.Contains(state.RunState!.LingeringEffects, static effect => effect.Expiry == RunLingeringEffectExpiry.Run
            && effect.BehaviorId == "icebreaker-pump");
        RoundTrip(state);
    }

    /// <summary>A successful HQ run that uses Detente's host trigger, then the turn's remaining clicks.</summary>
    private static GameState HostWithDetente(GameState state)
    {
        state = RunTo(state, ElevationBoard.Hq, HasTrigger);
        Assert.True(HasTrigger(state));
        state = UseTrigger(state);
        return PassUntil(state, static value => value.RunState is null);
    }

    [Fact]
    [Trait("ElevationCard", "35018")]
    public void DetenteHostsARandomHqCardFaceupAfterASuccessfulHqRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35018", Place.Rig)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        state = RoundTrip(HostWithDetente(state));

        Assert.Equal(ZoneKind.CorpHostedFaceUp, ZoneOf(state, HedgeFund));
        Assert.Equal(Card(state, "35018"), CardState(state, HedgeFund).HostCardId);
        Assert.True(CardState(state, HedgeFund).FaceUp);
    }

    [Fact]
    [Trait("ElevationCard", "35018")]
    public void DetenteReturnsTwoHostedCardsToGiveTheRunnerARandomHqAccess()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 6, RunnerCredits = 10 }
            .Add("35018", Place.Rig)
            .Add(HedgeFund, Place.Hq)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        state = HostWithDetente(state);
        // The Corp's turn, then a second HQ run hosts the second card.
        state = PassUntil(SpendRemainingClicks(ToAction(state)), static value => value.Turn.ActiveSide == PlayerSide.Corp);
        state = PassUntil(SpendRemainingClicks(ToAction(state)), static value => value.Turn.ActiveSide == PlayerSide.Runner
            && value.PendingDecision?.Kind == DecisionKind.TurnAction);
        state = HostWithDetente(ToAction(state));
        state = ToAction(state);
        Assert.Equal(2, state.Cards.Count(card => card.HostCardId == Card(state, "35018")));

        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "detente-access" }).State;
        CardInstanceId[] hosted = state.Cards.Where(card => card.HostCardId == Card(state, "35018")).Select(static card => card.Id).ToArray();
        state = RoundTrip(Apply(state, static action => action.Parameters is BehaviorChoiceParameters { ChoiceKey: "detente-return" }).State);
        // The access: an operation has no trash cost, so it stays in HQ.
        state = ToAction(state);

        Assert.All(hosted, cardId => Assert.Equal(ZoneKind.CorpHand, state.Zones.Single(zone => zone.CardIds.Contains(cardId)).Kind));
        Assert.DoesNotContain(state.Cards, card => card.HostCardId == Card(state, "35018"));
    }

    [Fact]
    [Trait("ElevationCard", "35018")]
    public void DetenteIsUnusableWithFewerThanTwoHostedCards()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35018", Place.Rig)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        state = ToAction(HostWithDetente(state));

        Assert.False(CanUse(state, "detente-access"));
    }

    /// <summary>Two turns of HQ runs host both HQ cards on Detente; then its access ability returns them.</summary>
    private static GameState DetenteAccess(string hqCard) => DetenteAccess(hqCard, out _);

    private static GameState DetenteAccess(string hqCard, out int runnerPointsBefore)
    {
        // Every Corp card is a copy, including the ones the Corp draws, so the random access finds one.
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35018", Place.Rig)
            .Add(hqCard, Place.Hq)
            .Add(hqCard, Place.Hq)
            .Add(hqCard, Place.CorpDeck)
            .Add(hqCard, Place.CorpDeck)
            .Add(hqCard, Place.CorpDeck)
            .Begin();
        state = HostWithDetente(state);
        state = PassUntil(SpendRemainingClicks(ToAction(state)), static value => value.Turn.ActiveSide == PlayerSide.Corp);
        state = PassUntil(SpendRemainingClicks(ToAction(state)), static value => value.Turn.ActiveSide == PlayerSide.Runner
            && value.PendingDecision?.Kind == DecisionKind.TurnAction);
        state = ToAction(HostWithDetente(ToAction(state)));
        runnerPointsBefore = Player(state, PlayerSide.Runner).AgendaPoints;
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "detente-access" }).State;
        return Apply(state, static action => action.Parameters is BehaviorChoiceParameters { ChoiceKey: "detente-return" }).State;
    }

    [Fact]
    [Trait("ElevationCard", "35018")]
    public void DetentesAccessStealsAnAgenda()
    {
        const string OffworldOffice = "30067";
        GameState state = RoundTrip(ToAction(DetenteAccess(OffworldOffice, out int before)));

        Assert.Equal(before + 2, Player(state, PlayerSide.Runner).AgendaPoints);
    }

    [Theory]
    [Trait("ElevationCard", "35018")]
    [InlineData("trash", true)]
    [InlineData("leave", false)]
    public void DetentesAccessOffersTheTrashAtItsCost(string option, bool trashed)
    {
        const string Regolith = "30071";
        GameState state = DetenteAccess(Regolith);
        Assert.Equal(PlayerSide.Runner, state.Players.Single(player => player.Id == state.PendingDecision!.Owner).Side);
        int credits = Player(state, PlayerSide.Runner).Credits;
        state = RoundTrip(ChooseOption(state, option));

        Assert.Equal(trashed ? 1 : 0, state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDiscard).CardIds
            .Count(cardId => state.Cards.Single(card => card.Id == cardId).DefinitionId.Value == Regolith));
        Assert.Equal(credits - (trashed ? 3 : 0), Player(state, PlayerSide.Runner).Credits);
    }
}
