using Netrunner.Core;

namespace Netrunner.Match;

public sealed record HandConditionViewModel(string Summary, CostQuotePresentationViewModel? InstallCost);

public static class HandConditionPresenter
{
    public static CardViewModel Apply(CardViewModel card, IReadOnlyList<HandCardCondition> conditions)
    {
        HandCardCondition? condition = conditions.FirstOrDefault(value => value.CardId.Value == card.Id);
        if (!card.IsKnown || condition is null) return card;
        CostQuotePresentationViewModel? cost = condition.InstallCost is { } quote
            ? new("Install", quote.BaseCost, quote.EffectiveCost, quote.Adjustments.Select(adjustment =>
                new CostAdjustmentPresentationViewModel(adjustment.Kind, adjustment.Delta, adjustment.ReasonKey,
                    adjustment.SourceDefinitionId, adjustment.SourceCardId)).ToArray()) : null;
        IEnumerable<string> reasons = condition.MetPlayConditions.Select(Describe)
            .Concat(cost?.Adjustments.Select(adjustment => adjustment.ReasonLabel) ?? []);
        return card with { HandCondition = new(string.Join("; ", reasons.Distinct(StringComparer.Ordinal)), cost) };
    }

    private static string Describe(BehaviorCondition condition) => condition.Kind switch
    {
        BehaviorConditionKind.SuccessfulRunThisTurn => "successful run this turn",
        BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn => "successful run during the Runner's last turn",
        BehaviorConditionKind.Tagged => $"{condition.SubjectSide?.ToString() ?? "Controller"} is tagged",
        BehaviorConditionKind.NotTagged => $"{condition.SubjectSide?.ToString() ?? "Controller"} is untagged",
        BehaviorConditionKind.ClicksRemainingAtLeast => $"at least {condition.Value} clicks remaining",
        _ => "play requirement met",
    };
}
