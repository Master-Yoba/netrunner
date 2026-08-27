using Xunit;

namespace Netrunner.Core.Tests;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void SameSeedProducesSameSequence()
    {
        DeterministicRandom first = new(42);
        DeterministicRandom second = new(42);

        ulong[] firstSequence = Enumerable.Range(0, 100).Select(_ => first.NextUInt64()).ToArray();
        ulong[] secondSequence = Enumerable.Range(0, 100).Select(_ => second.NextUInt64()).ToArray();

        Assert.Equal(firstSequence, secondSequence);
    }

    [Fact]
    public void StateCanBeRestoredExactly()
    {
        DeterministicRandom original = new(8675309);
        _ = original.NextUInt64();
        _ = original.NextUInt64();

        DeterministicRandom restored = DeterministicRandom.Restore(original.State);

        Assert.Equal(original.NextUInt64(), restored.NextUInt64());
    }
}

public sealed class FoundationMetadataTests
{
    [Fact]
    public void BuildMetadataIsPopulated()
    {
        Assert.NotEmpty(BuildMetadata.Current.ApplicationVersion);
        Assert.Equal("26.03", BuildMetadata.Current.RulesVersion);
    }

    [Fact]
    public void PresentationFixtureHasFiveOrderedEvents()
    {
        IReadOnlyList<GameEvent> events = FoundationEventBatch.Create();

        Assert.Equal(5, events.Count);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], events.Select(gameEvent => gameEvent.Sequence));
        Assert.Equal("match-created", events[0].Kind);
        Assert.Equal("run-ended", events[^1].Kind);
    }
}
