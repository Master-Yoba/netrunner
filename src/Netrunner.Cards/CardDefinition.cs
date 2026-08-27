namespace Netrunner.Cards;

public sealed record CardDefinition(
    string Code,
    string Title,
    string Side,
    string Faction,
    string Type,
    string? Subtypes,
    int? Cost,
    int? Strength,
    int? AgendaPoints,
    int? AdvancementRequirement,
    string Text,
    string SetCode,
    int Quantity);

public interface ICardBehavior
{
    string CardCode { get; }
}

