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
