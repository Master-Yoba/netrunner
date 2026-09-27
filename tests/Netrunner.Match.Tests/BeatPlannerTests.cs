using System.Collections.Concurrent;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BeatPlannerTests
{
    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public async Task SliceEventsExplainEveryVisibleChangeWithoutSnapMismatches(PlayerSide human)
    {
        var mismatches = new List<string>();
        var kinds = new HashSet<PresentationEventKind>();
        foreach (ulong seed in Enumerable.Range(100, 12).Select(value => (ulong)value))
        {
            IReadOnlyList<MatchSnapshot> snapshots = await PlayAsync(seed, human);
            for (int index = 1; index < snapshots.Count; index++)
            {
                MatchSnapshot previous = snapshots[index - 1], current = snapshots[index];
                if (current.PresentationEvents.Count == 0) continue;
                BoardDisplayState shown = current.PresentationEvents.Aggregate(BoardDisplayState.From(previous.View),
                    (state, value) => state.Apply(value));
                foreach (string difference in shown.Differences(BoardDisplayState.From(current.View)))
                    mismatches.Add($"seed {seed} action {current.ActionCount}: {difference} "
                        + $"(events {string.Join(",", current.PresentationEvents.Select(value => value.Kind))})");
                kinds.UnionWith(current.PresentationEvents.Select(value => value.Kind));
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(40)));
        Assert.Superset(new HashSet<PresentationEventKind>
        {
            PresentationEventKind.RunStarted, PresentationEventKind.IceApproached, PresentationEventKind.EncounterStarted,
            PresentationEventKind.SubroutineFired, PresentationEventKind.RunEnded,
        }, kinds);
    }

    [Fact]
    public async Task EveryPublicationPlansReadableBeatsForTheSlice()
    {
        var beats = new List<Beat>();
        foreach (ulong seed in new ulong[] { 51, 52, 53, 54 })
        {
            IReadOnlyList<MatchSnapshot> snapshots = await PlayAsync(seed, PlayerSide.Runner);
            for (int index = 1; index < snapshots.Count; index++)
                beats.AddRange(BeatPlanner.Plan(snapshots[index].PresentationEvents, snapshots[index - 1].View, snapshots[index].View,
                    id => id));
        }

        Assert.All(beats, beat =>
        {
            Assert.False(string.IsNullOrWhiteSpace(beat.Caption));
            Assert.DoesNotContain("card:", beat.Caption, StringComparison.Ordinal);
            Assert.DoesNotContain(CardKeyTracker.TrackedPrefix, beat.Caption, StringComparison.Ordinal);
            Assert.InRange(beat.Seconds, BeatPlanner.Brief, BeatPlanner.Result);
        });
        Assert.Contains(beats, beat => beat is { Kind: PresentationEventKind.RunEnded, Hold: true });
        Assert.Contains(beats, beat => beat.Kind == PresentationEventKind.SubroutineFired && beat.Emphasis == BeatEmphasis.High);
    }

    [Fact]
    public void AFaceDownAdvanceIsNamedAsSuchAndTheTotalCarries()
    {
        MatchViewModel view = ObservationViewMapper.FromObservation(ObservationBuilder.Build(GameState.CreateNew(3), PlayerSide.Runner));
        var advanced = new PresentationEvent(PresentationEventKind.CardAdvanced, PlayerSide.Corp)
        {
            CardKey = "tracked:4",
            ServerId = "server:9",
            Amount = 1,
            Total = 2,
        };

        Beat beat = Assert.Single(BeatPlanner.Plan([advanced], view, view, _ => "Remote 1"));
        BoardDisplayState state = BoardDisplayState.From(view).Apply(advanced);

        Assert.Equal("Corp advances a face-down card · 2 counters", beat.Caption);
        Assert.Equal(2, state.Advancement["tracked:4"]);
    }

    [Fact]
    public void OneActionShowsOnlyTheStepThatMatters()
    {
        MatchViewModel view = ObservationViewMapper.FromObservation(ObservationBuilder.Build(GameState.CreateNew(3), PlayerSide.Runner));
        PresentationEvent[] events =
        [
            new(PresentationEventKind.IceApproached, PlayerSide.Runner) { CardKey = "tracked:1" },
            new(PresentationEventKind.EncounterStarted, PlayerSide.Runner) { CardKey = "tracked:1", Total = 2 },
            new(PresentationEventKind.ServerApproached, PlayerSide.Runner) { ServerId = "server:1" },
            new(PresentationEventKind.RunSuccessful, PlayerSide.Runner) { ServerId = "server:1" },
        ];

        Assert.Equal([PresentationEventKind.EncounterStarted, PresentationEventKind.RunSuccessful],
            BeatPlanner.Plan(events, view, view, _ => "R&D").Select(beat => beat.Kind));
    }

    [Fact]
    public void PaymentsAreShownOnTheCardThatCausedThem()
    {
        MatchViewModel view = EmptyView();
        PresentationEvent[] events =
        [
            new(PresentationEventKind.CreditsChanged, PlayerSide.Runner) { Amount = -2, Total = 3, SourceCardKey = "breaker" },
            new(PresentationEventKind.SubroutinesBroken, PlayerSide.Runner) { CardKey = "tracked:1", Amount = 2, SourceCardKey = "breaker" },
            new(PresentationEventKind.CreditsChanged, PlayerSide.Runner) { Amount = -3, Total = 0, SourceCardKey = "program" },
            new(PresentationEventKind.CardInstalled, PlayerSide.Runner) { CardKey = "program", Amount = 3 },
        ];

        IReadOnlyList<Beat> beats = BeatPlanner.Plan(events, view, view, _ => "HQ");

        Assert.Equal(["Runner breaks 2 subroutines on a face-down card with a face-down card · 2 credits",
            "Runner installs a face-down card · 3 credits"], beats.Select(beat => beat.Caption));
    }

    [Fact]
    public void DamageNamesTheCardsItTrashesInOneBeat()
    {
        MatchViewModel view = EmptyView();
        PresentationEvent[] events =
        [
            new(PresentationEventKind.DamageDealt, PlayerSide.Runner) { Damage = DamageKind.Net, Amount = 2, Total = 2 },
            new(PresentationEventKind.CardMoved, PlayerSide.Runner) { CardKey = "a", FromZone = ZoneKind.RunnerGrip, ToZone = ZoneKind.RunnerHeap },
            new(PresentationEventKind.CardMoved, PlayerSide.Runner) { CardKey = "b", FromZone = ZoneKind.RunnerGrip, ToZone = ZoneKind.RunnerHeap },
        ];

        Beat beat = Assert.Single(BeatPlanner.Plan(events, view, view, _ => "HQ"));

        Assert.Equal(PresentationEventKind.DamageDealt, beat.Kind);
        Assert.Equal(["a", "b"], beat.RelatedCardKeys);
        Assert.StartsWith("Runner takes 2 net damage · loses", beat.Caption, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedDrawsAndMovesReadAsOneBeat()
    {
        MatchViewModel view = EmptyView();
        PresentationEvent[] events =
        [
            new(PresentationEventKind.CardMoved, PlayerSide.Corp) { FromZone = ZoneKind.CorpHand, ToZone = ZoneKind.CorpDeck },
            new(PresentationEventKind.CardMoved, PlayerSide.Corp) { FromZone = ZoneKind.CorpHand, ToZone = ZoneKind.CorpDeck },
            new(PresentationEventKind.CardMoved, PlayerSide.Corp) { FromZone = ZoneKind.CorpDeck, ToZone = ZoneKind.CorpHand },
            new(PresentationEventKind.CardDrawn, PlayerSide.Corp),
            new(PresentationEventKind.CardMoved, PlayerSide.Corp) { FromZone = ZoneKind.CorpDeck, ToZone = ZoneKind.CorpHand },
            new(PresentationEventKind.CardDrawn, PlayerSide.Corp),
        ];

        Assert.Equal(["2 cards go into R&D", "Corp draws 2 cards"],
            BeatPlanner.Plan(events, view, view, _ => "HQ").Select(beat => beat.Caption));
    }

    [Fact]
    public void PacingShortensOnlyLowBeatsThePlayerAlreadyKnowsAbout()
    {
        var draw = new Beat(PresentationEventKind.CardDrawn, "Corp draws a card", BeatPlanner.Brief, BeatEmphasis.Low) { Side = PlayerSide.Corp };
        var score = new Beat(PresentationEventKind.AgendaScored, "Corp scores", BeatPlanner.Result, BeatEmphasis.High)
        { Side = PlayerSide.Corp, Hold = true };

        Assert.Equal(BeatPacing.Compressed, new BeatPacing().Seconds(draw, PlayerSide.Corp));
        Assert.Equal(BeatPlanner.Brief, new BeatPacing().Seconds(draw, PlayerSide.Runner));
        Assert.Equal(BeatPacing.Compressed, new BeatPacing(FastOpponent: true).Seconds(draw, PlayerSide.Runner));
        Assert.Equal(BeatPlanner.Result + BeatPacing.HoldSeconds, new BeatPacing(FastOpponent: true).Seconds(score, PlayerSide.Runner), 6);
        Assert.Equal(BeatPlanner.Result, new BeatPacing(ReducedMotion: true).Seconds(score, PlayerSide.Runner), 6);
        Assert.Equal(BeatPlanner.Result / 2, new BeatPacing(Speed: 2, ReducedMotion: true).Seconds(score, PlayerSide.Runner), 6);
    }

    private static MatchViewModel EmptyView() =>
        ObservationViewMapper.FromObservation(ObservationBuilder.Build(GameState.CreateNew(3), PlayerSide.Runner));

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static async Task<IReadOnlyList<MatchSnapshot>> PlayAsync(ulong seed, PlayerSide human)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
            Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, seed);
        await using var coordinator = new MatchCoordinator(definition, new RandomLegalAgent(seed),
            new MatchCoordinatorOptions { HumanSide = human });
        var snapshots = new ConcurrentQueue<MatchSnapshot>();
        coordinator.SnapshotPublished += snapshots.Enqueue;
        await coordinator.StartAsync();
        var agent = new RandomLegalAgent(seed + 500);
        for (int count = 0; count < 300; count++)
        {
            Observation observation = await coordinator.GetReadyObservationAsync(human).WaitAsync(TimeSpan.FromSeconds(10));
            if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
            Assert.True((await coordinator.SubmitHumanActionAsync(await agent.ChooseActionAsync(observation))).Accepted);
        }
        return [.. snapshots.OrderBy(snapshot => snapshot.Sequence)];
    }
}
