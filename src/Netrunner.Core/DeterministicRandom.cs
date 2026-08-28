namespace Netrunner.Core;

/// <summary>
/// Serializable SplitMix64 random stream. Game-affecting randomness must use an
/// engine-owned instance rather than System.Random.
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong Increment = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong State => _state;

    public static DeterministicRandom Restore(ulong state) => new(state);

    public ulong NextUInt64()
    {
        _state += Increment;
        ulong value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);

        ulong bound = (ulong)maxExclusive;
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value < threshold);

        return (int)(value % bound);
    }

    /// <summary>
    /// Applies the unbiased Fisher-Yates permutation using this stream. The
    /// list order is semantic (for example, the order of a deck), so it is
    /// intentionally not sorted before shuffling.
    /// </summary>
    public void Shuffle<T>(IList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = NextInt(index + 1);
            if (swapIndex == index)
            {
                continue;
            }

            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}
