using Xunit;

namespace Netrunner.Core.Tests;

public sealed class IssuedRunPaymentPreferenceTests
{
    [Fact]
    public void EquivalentIssuedActionsSpendExpiringRunCreditsFirst()
    {
        var breaker = new CardInstanceId("card:breaker");
        var accessed = new CardInstanceId("card:accessed");
        (ActionId Id, ActionKind Kind, ActionParameters Parameters)[] actions =
        [
            (new("action:pump-personal"), ActionKind.PumpBreaker, new RunBreakerParameters(breaker)),
            (new("action:pump-run"), ActionKind.PumpBreaker, new RunBreakerParameters(breaker, 1)),
            (new("action:break-personal"), ActionKind.BreakSubroutines, new RunBreakParameters(breaker, 1, subroutineOrdinals: [0])),
            (new("action:break-run"), ActionKind.BreakSubroutines, new RunBreakParameters(breaker, 1, 1, subroutineOrdinals: [0])),
            (new("action:trash-personal"), ActionKind.TrashAccessedCard, new RunCardPaymentParameters(accessed)),
            (new("action:trash-run"), ActionKind.TrashAccessedCard, new RunCardPaymentParameters(accessed, 2)),
            (new("action:break-other"), ActionKind.BreakSubroutines, new RunBreakParameters(breaker, 1, subroutineOrdinals: [1])),
        ];

        ActionId[] selected = IssuedRunPaymentPreference.Select(actions, action => action).Select(action => action.Id).ToArray();

        Assert.Contains(new ActionId("action:pump-run"), selected);
        Assert.Contains(new ActionId("action:break-run"), selected);
        Assert.Contains(new ActionId("action:trash-run"), selected);
        Assert.Contains(new ActionId("action:break-other"), selected);
        Assert.DoesNotContain(new ActionId("action:pump-personal"), selected);
        Assert.DoesNotContain(new ActionId("action:break-personal"), selected);
        Assert.DoesNotContain(new ActionId("action:trash-personal"), selected);
    }

    [Fact]
    public void SeparateTargetsAndNonPaymentChoicesRemainAvailable()
    {
        (ActionId Id, ActionKind Kind, ActionParameters Parameters)[] actions =
        [
            (new("action:one"), ActionKind.TrashAccessedCard, new RunCardPaymentParameters(new("card:one"))),
            (new("action:two"), ActionKind.TrashAccessedCard, new RunCardPaymentParameters(new("card:two"))),
            (new("action:pass"), ActionKind.PassPriority, new NoActionParameters()),
        ];

        Assert.Equal(3, IssuedRunPaymentPreference.Select(actions, action => action).Count());
    }

    [Fact]
    public void IceSubroutinePaymentKeepsTheFrameAndCreditCostDistinct()
    {
        (ActionId Id, ActionKind Kind, ActionParameters Parameters)[] actions =
        [
            (new("action:personal"), ActionKind.ResolveIceSubroutine,
                new IceSubroutineChoiceParameters("frame:one", IceSubroutineChoiceKind.PayCredits, creditCost: 2)),
            (new("action:run"), ActionKind.ResolveIceSubroutine,
                new IceSubroutineChoiceParameters("frame:one", IceSubroutineChoiceKind.PayCredits, creditCost: 2, hostedCredits: 2)),
            (new("action:different-frame"), ActionKind.ResolveIceSubroutine,
                new IceSubroutineChoiceParameters("frame:two", IceSubroutineChoiceKind.PayCredits, creditCost: 2)),
            (new("action:different-cost"), ActionKind.ResolveIceSubroutine,
                new IceSubroutineChoiceParameters("frame:one", IceSubroutineChoiceKind.PayCredits, creditCost: 3)),
        ];

        ActionId[] selected = IssuedRunPaymentPreference.Select(actions, action => action).Select(action => action.Id).ToArray();
        Assert.Equal(3, selected.Length);
        Assert.Contains(new ActionId("action:run"), selected);
        Assert.Contains(new ActionId("action:different-frame"), selected);
        Assert.Contains(new ActionId("action:different-cost"), selected);
    }
}
