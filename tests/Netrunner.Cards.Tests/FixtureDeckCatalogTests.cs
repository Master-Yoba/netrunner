using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class FixtureDeckCatalogTests
{
    [Fact]
    public void CatalogContainsExactlyOneValidatedRecipePerSide()
    {
        FixtureDeckCatalog.Validate();

        IReadOnlyList<ToyDeckRecipe> recipes = FixtureDeckCatalog.All;

        Assert.Equal(2, recipes.Count);
        Assert.Single(recipes, recipe => recipe.Side == PlayerSide.Corp);
        Assert.Single(recipes, recipe => recipe.Side == PlayerSide.Runner);
        Assert.All(recipes, recipe => Assert.Equal(FixtureDeckCatalog.FixtureCardCount, recipe.DefinitionIds.Count));
        Assert.All(recipes, recipe => Assert.False(string.IsNullOrWhiteSpace(recipe.RecipeId)));
    }

    [Fact]
    public void EveryDefinitionIsUsedAndExplicitlyBoundToTheMatchingSide()
    {
        FixtureDeckCatalog.Validate();

        IReadOnlyList<CardDefinition> definitions = FixtureDeckCatalog.Definitions;
        IReadOnlyList<ToyDeckRecipe> recipes = FixtureDeckCatalog.All;

        Assert.Equal(2, definitions.Count);
        foreach (CardDefinition definition in definitions)
        {
            CardDefinitionId definitionId = new(definition.Code);
            Assert.Contains(
                recipes,
                recipe => recipe.DefinitionIds.Contains(definitionId));
            Assert.True(
                ToyCardBehaviorCatalog.TryGet(definitionId, out ToyCardBehavior? behavior));
            Assert.NotNull(behavior);
            Assert.Equal(
                definition.Side,
                behavior!.OwnerSide == PlayerSide.Corp ? "corp" : "runner");
            Assert.Equal(FixtureDeckCatalog.FixtureSetCode, definition.SetCode);
            Assert.Equal(FixtureDeckCatalog.FixtureCardCount, definition.Quantity);
            Assert.False(string.IsNullOrWhiteSpace(definition.Title));
            Assert.False(string.IsNullOrWhiteSpace(definition.Text));
            Assert.False(definition.Code.All(char.IsDigit));
            Assert.DoesNotContain("system-gateway", definition.Text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(
            definitions,
            definition => definition.Code == ToyCardDefinitionIds.CorpAgenda3.Value);
        Assert.Contains(
            definitions,
            definition => definition.Code == ToyCardDefinitionIds.RunnerProgram.Value);
    }

    [Fact]
    public void RecipesAndCollectionsAreFreshAndCannotBeMutatedThroughPublicBoundaries()
    {
        ToyDeckRecipe firstCorp = FixtureDeckCatalog.CorpRecipe;
        ToyDeckRecipe secondCorp = FixtureDeckCatalog.CorpRecipe;
        Assert.NotSame(firstCorp, secondCorp);
        Assert.Equal(firstCorp.DefinitionIds, secondCorp.DefinitionIds);
        Assert.IsAssignableFrom<IList<CardDefinitionId>>(firstCorp.DefinitionIds);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CardDefinitionId>)firstCorp.DefinitionIds).Add(ToyCardDefinitionIds.RunnerProgram));

        IReadOnlyList<ToyDeckRecipe> firstAll = FixtureDeckCatalog.All;
        IReadOnlyList<ToyDeckRecipe> secondAll = FixtureDeckCatalog.All;
        Assert.NotSame(firstAll, secondAll);
        Assert.NotSame(firstAll[0], secondAll[0]);
        Assert.IsAssignableFrom<IList<ToyDeckRecipe>>(firstAll);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ToyDeckRecipe>)firstAll).Clear());

        IReadOnlyList<CardDefinition> firstDefinitions = FixtureDeckCatalog.Definitions;
        IReadOnlyList<CardDefinition> secondDefinitions = FixtureDeckCatalog.Definitions;
        Assert.NotSame(firstDefinitions, secondDefinitions);
        Assert.NotSame(firstDefinitions[0], secondDefinitions[0]);
        Assert.IsAssignableFrom<IList<CardDefinition>>(firstDefinitions);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<CardDefinition>)firstDefinitions).Clear());
    }

    [Fact]
    public void OfficialLookingMetadataRemainsDataOnlyWhenNotFixtureBound()
    {
        CardDefinitionId officialCode = new("30001");

        Assert.False(ToyCardBehaviorCatalog.TryGet(officialCode, out _));

        CardDefinition officialMetadata = new(
            officialCode.Value,
            "Official-looking metadata",
            "runner",
            "neutral-runner",
            "event",
            null,
            0,
            null,
            null,
            null,
            "This data-only card has no fixture behavior.",
            "system-gateway",
            3);

        Assert.DoesNotContain(
            FixtureDeckCatalog.Definitions,
            definition => definition.Code == officialMetadata.Code);
        Assert.False(ToyCardBehaviorCatalog.TryGet(new CardDefinitionId(officialMetadata.Code), out _));
    }
}
