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
