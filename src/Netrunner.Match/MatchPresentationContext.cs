using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Client presentation data owned by Match; never attached to an agent observation.</summary>
public sealed record MatchPresentationContext(
    IReadOnlyList<CardPositionViewModel> Positions,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> ChoiceCards,
    CardSelectionViewModel? Selection,
    IReadOnlyList<ActionSourceContext> ActionSources)
{
    public static MatchPresentationContext Empty { get; } = new([], [], null, []);
    public IReadOnlyList<HandCardCondition> HandConditions { get; init; } = [];
}

public sealed record CardPositionViewModel(string CardId, string ServerId, ZoneKind Zone, int Index);

public sealed record CardSelectionViewModel(
    string ContinuationId, string ChoiceKey, BehaviorChoiceMode Mode,
    BehaviorChoiceCardinalityKind Cardinality, int? Count,
    IReadOnlyList<string> Candidates, IReadOnlyList<string> Selected);

public sealed record ActionSourceContext(
    ActionId ActionId, CardInstanceId CardId, CardDefinitionId DefinitionId,
    string? RuleKey, int? ReplacementDrawCount = null);
