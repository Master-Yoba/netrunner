namespace Netrunner.Core;

/// <summary>
/// Optional compiled card-class vocabulary used by generic hidden-zone
/// filters. The metadata is supplied by composition (for example the Cards
/// assembly); Core never parses titles or rules text to infer a class.
/// </summary>
public interface IBehaviorCardClassificationCatalog
{
    bool TryGetCardType(CardDefinitionId definitionId, out BehaviorCardType cardType);
}
