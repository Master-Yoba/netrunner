using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>
/// Selects an issued payment, never inventing spendable sources. RunCredits
/// (called HostedCredits in action contracts) includes expiring Overclock credits.
/// Ordinary hosted card counters are available only through their own card effects.
/// </summary>
public static class RunPaymentPreference
{
    public static LegalActionViewModel[] Apply(IEnumerable<LegalActionViewModel> actions) =>
        IssuedRunPaymentPreference.Select(actions, action => (action.Id, action.Kind, action.Parameters)).ToArray();
}
