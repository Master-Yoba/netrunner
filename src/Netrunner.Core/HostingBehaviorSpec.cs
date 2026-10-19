using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>Printed ICE-hosting restriction and optional hosted-virus abilities.</summary>
public sealed record HostingBehaviorSpec
{
    [JsonConstructor]
    public HostingBehaviorSpec(bool breakHostSubroutine = false, int? derezAtVirusCounters = null, bool hostGainsEveryIceType = false)
    {
        HostGainsEveryIceType = hostGainsEveryIceType;
        if (derezAtVirusCounters is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(derezAtVirusCounters));
        }
        BreakHostSubroutine = breakHostSubroutine;
        DerezAtVirusCounters = derezAtVirusCounters;
    }

    public bool BreakHostSubroutine { get; }
    public int? DerezAtVirusCounters { get; }

    /// <summary>EL-ICE-GAINS-SUBTYPE: the host ICE gains barrier, code gate and sentry (Chromatophores).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HostGainsEveryIceType { get; }
}

/// <summary>
/// VP-HOST-RESOURCES: resources of the named subtypes (unique ones, when <see cref="UniqueOnly"/>) may be installed onto
/// the card, each at <see cref="InstallDiscount"/> less; while it hosts one of every subtype, its owner's maximum hand size
/// is <see cref="HandSizeWhileHostingEach"/> more (Hackerspace).
/// </summary>
public sealed record ResourceHostingSpec
{
    [JsonConstructor]
    public ResourceHostingSpec(IReadOnlyList<string> subtypes, bool uniqueOnly, int installDiscount, int handSizeWhileHostingEach)
    {
        ArgumentNullException.ThrowIfNull(subtypes);
        if (subtypes.Count == 0 || subtypes.Any(static subtype => string.IsNullOrWhiteSpace(subtype)) || installDiscount < 0
            || handSizeWhileHostingEach < 0)
        {
            throw new ArgumentException("A resource host names its subtypes and a nonnegative discount and hand size.", nameof(subtypes));
        }

        Subtypes = subtypes.ToArray();
        UniqueOnly = uniqueOnly;
        InstallDiscount = installDiscount;
        HandSizeWhileHostingEach = handSizeWhileHostingEach;
    }

    public IReadOnlyList<string> Subtypes { get; }

    public bool UniqueOnly { get; }

    public int InstallDiscount { get; }

    public int HandSizeWhileHostingEach { get; }

    public bool Equals(ResourceHostingSpec? other) => other is not null && Subtypes.SequenceEqual(other.Subtypes, StringComparer.Ordinal)
        && UniqueOnly == other.UniqueOnly && InstallDiscount == other.InstallDiscount
        && HandSizeWhileHostingEach == other.HandSizeWhileHostingEach;

    public override int GetHashCode() => HashCode.Combine(Subtypes.Count, UniqueOnly, InstallDiscount, HandSizeWhileHostingEach);

    /// <summary>Whether a resource of the subtypes (and uniqueness) may be installed onto the host.</summary>
    public bool Accepts(CardRuntimeSpec resource) => resource.CardClass == RuntimeCardClass.Resource
        && (!UniqueOnly || resource.Unique)
        && Subtypes.Any(subtype => resource.Subtypes.Contains(subtype, StringComparer.OrdinalIgnoreCase));
}

