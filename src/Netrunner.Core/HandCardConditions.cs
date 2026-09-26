namespace Netrunner.Core;

/// <summary>Read-only, owner-visible hand conditions; not an authorization to play a card.</summary>
public sealed record HandCardCondition(
    CardInstanceId CardId,
    IReadOnlyList<BehaviorCondition> MetPlayConditions,
    CostQuote? InstallCost);

public static partial class GameEngine
{
    public static IReadOnlyList<HandCardCondition> ObserveHandConditions(
        GameState state, PlayerSide viewer, IBehaviorCatalog behaviorCatalog, IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        if (!Enum.IsDefined(viewer)) throw new ArgumentOutOfRangeException(nameof(viewer));
        WorkingState working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        ZoneKind handKind = viewer == PlayerSide.Runner ? ZoneKind.RunnerGrip : ZoneKind.CorpHand;
        PlayerId owner = GetPlayer(working, viewer).Id;
        var result = new List<HandCardCondition>();
        foreach (CardInstanceState card in working.Cards.Where(card => card.OwnerSide == viewer
            && working.Zones.Any(zone => zone.Id == card.ZoneId && zone.Kind == handKind && zone.CardIds.Contains(card.Id))))
        {
            IReadOnlyList<BehaviorCondition> conditions = behaviorCatalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                && definition.Conditions.Count > 0 && AbilityConditionsMatch(working, card, owner, definition.Conditions)
                ? Array.AsReadOnly(definition.Conditions.ToArray()) : Array.Empty<BehaviorCondition>();
            CostQuote? quote = viewer == PlayerSide.Runner
                && runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec) && spec.InstallCost is { } cost
                ? CostModifierProjection.RunnerInstall(working, card, cost) : null;
            if (conditions.Count > 0 || quote is not null) result.Add(new(card.Id, conditions, quote));
        }
        return result.AsReadOnly();
    }
}
