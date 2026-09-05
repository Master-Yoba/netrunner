using System.Globalization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Shared deterministic ordering for issued action IDs.</summary>
internal static class AgentActionOrdering
{
    private const string AllocatorPrefix = "action:";

    /// <summary>
    /// Returns a stable order key for an engine-issued action ID. Exact
    /// allocator IDs use invariant numeric ordering; synthetic stable IDs
    /// use ordinal text ordering.
    /// </summary>
    public static string IssuedOrderKey(ActionId id)
    {
        string value = id.Value;
        if (value.StartsWith(AllocatorPrefix, StringComparison.Ordinal))
        {
            ReadOnlySpan<char> suffix = value.AsSpan(AllocatorPrefix.Length);
            if (!suffix.IsEmpty
                && IsAsciiDigits(suffix)
                && ulong.TryParse(
                    suffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ulong sequence)
                && sequence > 0
                && suffix.SequenceEqual(sequence.ToString(CultureInfo.InvariantCulture).AsSpan()))
            {
                return $"0:{sequence.ToString("D20", CultureInfo.InvariantCulture)}";
            }
        }

        return $"1:{value}";
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
