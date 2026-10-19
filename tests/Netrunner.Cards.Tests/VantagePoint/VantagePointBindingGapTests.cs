using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP.5b, the primitives binding VP.5's cards found missing after VP.5a, on the probe catalog's test cards (see
/// <see cref="ElevationHarness.VantagePointProbe"/>).
/// </summary>
public sealed class VantagePointBindingGapTests
{
    private const string Cleaver = "30006";
    private const string Mayfly = "30032";

    // Vicsek: a trap has no barrier, code gate or sentry subtype, so only a breaker that interfaces with every piece of ICE
    // can break it.
    [Theory]
    [InlineData(Cleaver, false)]
    [InlineData(Mayfly, true)]
    public void OnlyABreakerForEveryPieceOfIceBreaksATrap(string breaker, bool breaks)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(breaker, Place.Rig)
            .Add(ProbeTrapIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));

        Assert.Equal(breaks, Actions(state).Any(action => action.Kind == ActionKind.BreakSubroutines
            && action.Parameters is RunBreakParameters use && use.BreakerId == Card(state, breaker)));
    }

    // Ansel 2.0: "Lose [click][click]: Break up to 2 subroutines". Each use names one subroutine or two in printed order,
    // and needs two clicks.
    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public void ABioroidAbilityCanBreakTwoSubroutines(int clicksSpentFirst, bool offered)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(ProbeDoubleBreakIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        for (int click = 0; click < clicksSpentFirst; click++)
        {
            state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);
        }

        state = RoundTrip(RunTo(state, ElevationBoard.Remote, InEncounter));
        RunBioroidAbilityParameters[] uses = Actions(state).Select(static action => action.Parameters)
            .OfType<RunBioroidAbilityParameters>().ToArray();
        if (!offered)
        {
            Assert.Equal(1, Player(state, PlayerSide.Runner).Clicks);
            Assert.Empty(uses);
            return;
        }

        Assert.Equal(
            ["0", "0+1", "0+2", "1", "1+2", "2"],
            uses.Select(static use => use.SecondSubroutineOrdinal is { } second ? $"{use.SubroutineOrdinal}+{second}" : $"{use.SubroutineOrdinal}")
                .Order(StringComparer.Ordinal));
        int before = Player(state, PlayerSide.Runner).Clicks;
        ApplyResult used = Apply(state, static action => action.Parameters is RunBioroidAbilityParameters
        { SubroutineOrdinal: 0, SecondSubroutineOrdinal: 2 });
        BioroidAbilityUsedEventPayload payload = Assert.Single(used.Events.Select(static value => value.Payload).OfType<BioroidAbilityUsedEventPayload>());
        Assert.Equal(2, payload.ClickCost);
        Assert.NotNull(payload.SecondSubroutineId);
        state = RoundTrip(used.State);

        Assert.Equal(before - 2, Player(state, PlayerSide.Runner).Clicks);
        Assert.Equal([true, false, true], state.RunState!.Encounter!.Subroutines.Select(static value => value.Broken));
    }

    // A forged second subroutine, one already broken or out of order, is refused as the action is checked.
    [Fact]
    public void ASecondSubroutineMustFollowTheFirst()
    {
        Assert.Throws<ArgumentException>(() => new RunBioroidAbilityParameters(
            new CardInstanceId("ice"), new SubroutineId("sub-b"), "probe-double-break", new BehaviorCost(BehaviorCostKind.LoseClick, 2),
            subroutineOrdinal: 1, secondSubroutineId: new SubroutineId("sub-a"), secondSubroutineOrdinal: 0));
    }

    // Rotary: "[click], 2[credit]: Trash this hardware. Only the Corp can use this ability, and only if the Runner is
    // tagged." The Corp uses and pays for an ability of a Runner card.
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void TheOpponentCanUseAnAbilityOnlyTheyMayUse(int tags, bool offered)
    {
        GameState state = new ElevationBoard { CorpCredits = 5, Tags = tags }
            .Add(ProbeOpponentAbilityHardware, Place.Rig)
            .Begin(catalog: VantagePointProbe);
        bool corpOffered = Actions(state).Any(static action => action.Parameters is AbilityActionParameters
        { AbilityKey: "probe-opponent-trash" });
        Assert.Equal(offered, corpOffered);
        if (!offered)
        {
            return;
        }

        int clicks = Player(state, PlayerSide.Corp).Clicks;
        state = RoundTrip(PassUntil(Apply(state, static action => action.Parameters is AbilityActionParameters
        { AbilityKey: "probe-opponent-trash" }).State, static value => value.PendingDecision?.Kind == DecisionKind.TurnAction));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, ProbeOpponentAbilityHardware));
        Assert.Equal(clicks - 1, Player(state, PlayerSide.Corp).Clicks);
        Assert.Equal(3, Player(state, PlayerSide.Corp).Credits);
    }

    // The owner can't use an ability only its opponent may use.
    [Fact]
    public void TheOwnerCannotUseAnAbilityOnlyTheOpponentMayUse()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { Tags = 1 }
            .Add(ProbeOpponentAbilityHardware, Place.Rig)
            .Begin(catalog: VantagePointProbe);

        Assert.DoesNotContain(Actions(state), static action => action.Parameters is AbilityActionParameters
        { AbilityKey: "probe-opponent-trash" });
    }

    // ezaM: "[click]: Swap this ice with another installed piece of ice". The swap names its source and takes the chosen ICE.
    [Fact]
    public void AnIceCanSwapItselfWithAnother()
    {
        GameState state = new ElevationBoard()
            .Add(ProbeSwapSelfIce, Place.Ice, rezzed: true)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: VantagePointProbe);
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "probe-swap-self" }).State;
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice)
        {
            state = ChooseCards(state, Card(state, CorpFillerIce));
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Hq), ServerOf(state, ProbeSwapSelfIce));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Remote), ServerOf(state, CorpFillerIce));
    }

    // Sipa: "you may swap it with another installed piece of ice". The swap names the ICE just passed (the trigger's
    // subject) and takes the chosen ICE.
    [Fact]
    public void ThePassedIceCanBeSwappedWithAnother()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(ProbeSwapPassedProgram, Place.Rig)
            .Add(ProbeTrapIce, Place.Ice)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, HasTrigger);
        state = UseTrigger(state);
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorChoice)
        {
            state = ChooseCards(state, Card(state, CorpFillerIce));
        }

        state = RoundTrip(state);
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Hq), ServerOf(state, ProbeTrapIce));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Remote), ServerOf(state, CorpFillerIce));
    }

    // Unleash: "As an additional cost to play this operation, remove 1 tag". The Corp's play cost removes the Runner's tag,
    // and the card can't be played while the Runner has none.
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ACorpPlayCostCanRemoveTheRunnersTag(int tags, bool playable)
    {
        GameState state = new ElevationBoard { CorpCredits = 0, Tags = tags }
            .Add(ProbeTagCostOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        CardInstanceId operation = Card(state, ProbeTagCostOperation);
        Assert.Equal(playable, Actions(state).Any(action => action.Parameters is PlayCardParameters play && play.CardId == operation));
        if (!playable)
        {
            return;
        }

        state = RoundTrip(ToAction(PlayFirst(state, ProbeTagCostOperation)));
        Assert.Equal(0, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(3, Player(state, PlayerSide.Corp).Credits);
    }

    // ezaM: "Look at the top card of R&D. You may add that card to the bottom of R&D." A card moved into its own deck goes
    // to the bottom.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADecksTopCardCanGoToItsBottom(bool move)
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 3 }
            .Add(ProbeDeckBottomOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        CardInstanceId[] before = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds.ToArray();
        state = PlayFirst(state, ProbeDeckBottomOperation);
        state = move ? ChooseCards(state, before[0]) : ChooseCards(state);
        state = RoundTrip(ToAction(state));

        CardInstanceId[] after = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds.ToArray();
        Assert.Equal(move ? [.. before[1..], before[0]] : before, after);
    }

    private static ServerId? ServerOf(GameState state, string code) =>
        state.Zones.Single(zone => zone.CardIds.Contains(Card(state, code))).ServerId;
}
