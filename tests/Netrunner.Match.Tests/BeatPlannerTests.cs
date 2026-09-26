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

        Assert.Empty(mismatches);
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
            Assert.InRange(beat.Seconds, 0.7, 1.6);
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
