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
        actions.GroupBy(action => (action.Kind, action.Parameters) switch
        {
            (ActionKind.PumpBreaker, RunBreakerParameters p) => (object)(action.Kind, p.BreakerId),
            (ActionKind.BreakSubroutines, RunBreakParameters p) =>
                (action.Kind, p.BreakerId, p.Quantity, p.SubroutineId,
                    p.SubroutineOrdinals is { } targets ? string.Join(",", targets.Order()) : null, p.CostQuote),
            (ActionKind.TrashAccessedCard, RunCardPaymentParameters p) => (action.Kind, p.CardId),
            (ActionKind.ResolveIceSubroutine, IceSubroutineChoiceParameters p) when p.Choice == IceSubroutineChoiceKind.PayCredits =>
                (action.Kind, p.FrameIdentity, p.Choice, p.CardId, p.DestinationKind, p.ServerId, p.CreateNewRemote, p.CreditCost),
            _ => action.Id,
        }).Select(group => group.MaxBy(action => action.Parameters switch
        {
            RunBreakerParameters p => p.HostedCredits,
            RunBreakParameters p => p.HostedCredits,
            RunCardPaymentParameters p => p.HostedCredits,
            IceSubroutineChoiceParameters p => p.HostedCredits,
            _ => 0,
        })!).ToArray();
}
