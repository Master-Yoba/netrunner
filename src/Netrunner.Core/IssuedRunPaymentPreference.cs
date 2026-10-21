namespace Netrunner.Core;

/// <summary>Orders equivalent issued payments; ordinary hosted counters are never treated as run credits.</summary>
public static class IssuedRunPaymentPreference
{
    public static IEnumerable<T> Select<T>(IEnumerable<T> actions, Func<T, (ActionId Id, ActionKind Kind, ActionParameters Parameters)> project) =>
        actions.GroupBy(action => Key(project(action)))
            .Select(group => group.OrderByDescending(action => RunCredits(project(action).Parameters))
                .ThenBy(action => project(action).Id.Value, StringComparer.Ordinal).First());

    public static int RunCredits(ActionParameters parameters) => parameters switch
    {
        RunBreakerParameters value => value.RunCredits,
        RunBreakParameters value => value.RunCredits,
        RunCardPaymentParameters value => value.RunCredits,
        IceSubroutineChoiceParameters value => value.HostedCredits,
        _ => 0,
    };

    private static object Key((ActionId Id, ActionKind Kind, ActionParameters Parameters) action) => (action.Kind, action.Parameters) switch
    {
        (ActionKind.PumpBreaker, RunBreakerParameters value) => (action.Kind, value.BreakerId),
        (ActionKind.BreakSubroutines, RunBreakParameters value) => (action.Kind, value.BreakerId, value.Quantity, value.SubroutineId,
            value.SubroutineOrdinals is { } ordinals ? string.Join(",", ordinals) : null, EngineJson.Serialize(value.CostQuote)),
        (ActionKind.TrashAccessedCard, RunCardPaymentParameters value) => (action.Kind, value.CardId),
        (ActionKind.StealAgenda, RunCardPaymentParameters value) => (action.Kind, value.CardId),
        (ActionKind.ResolveIceSubroutine, IceSubroutineChoiceParameters value) when value.Choice == IceSubroutineChoiceKind.PayCredits =>
            (action.Kind, value.FrameIdentity, value.Choice, value.CardId, value.DestinationKind, value.ServerId, value.CreateNewRemote, value.CreditCost),
        _ => action.Id,
    };
}
