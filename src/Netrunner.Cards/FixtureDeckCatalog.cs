using System.Collections.ObjectModel;
using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// The two M1 decks are synthetic test fixtures. They are not official
/// Gateway content and are not user-facing deck presets. Core owns the
/// executable fixture behavior; this project owns only the display metadata
/// and recipe composition.
/// </summary>
public static class FixtureDeckCatalog
{
    public const string FixtureSetCode = "m1-fixture";
    public const int FixtureCardCount = 6;

    /// <summary>A fresh Corp recipe containing six copies of the fixture agenda.</summary>
    public static ToyDeckRecipe CorpRecipe => new(
        "m1-corp-fixture",
        PlayerSide.Corp,
        Enumerable.Repeat(ToyCardDefinitionIds.CorpAgenda3, FixtureCardCount).ToArray());

    /// <summary>A fresh Runner recipe containing six copies of the fixture program.</summary>
    public static ToyDeckRecipe RunnerRecipe => new(
        "m1-runner-fixture",
        PlayerSide.Runner,
        Enumerable.Repeat(ToyCardDefinitionIds.RunnerProgram, FixtureCardCount).ToArray());

    // Short aliases keep composition ergonomic while still creating only the
    // two canonical recipes above.
    public static ToyDeckRecipe Corp => CorpRecipe;

    public static ToyDeckRecipe Runner => RunnerRecipe;

    /// <summary>Fresh immutable metadata records used by the two recipes.</summary>
    public static IReadOnlyList<CardDefinition> Definitions =>
        new ReadOnlyCollection<CardDefinition>(
        [
            new CardDefinition(
                ToyCardDefinitionIds.CorpAgenda3.Value,
                "Training Exercise",
                "corp",
                "neutral-corp",
                "agenda",
                "Research",
                null,
                null,
                3,
                3,
                "M1 fixture agenda; no official card rules are bound to this text.",
                FixtureSetCode,
                FixtureCardCount),
            new CardDefinition(
                ToyCardDefinitionIds.RunnerProgram.Value,
                "Sandbox Decoder",
                "runner",
                "neutral-runner",
                "program",
                "Utility",
                2,
                null,
                null,
                null,
                "M1 fixture program; no official card rules are bound to this text.",
                FixtureSetCode,
                FixtureCardCount),
        ]);

    /// <summary>Fresh copies of exactly the Corp and Runner fixture recipes.</summary>
    public static IReadOnlyList<ToyDeckRecipe> All =>
        new ReadOnlyCollection<ToyDeckRecipe>([CorpRecipe, RunnerRecipe]);

    public static void Validate()
    {
        IReadOnlyList<CardDefinition> definitions = Definitions;
        if (definitions.Count != 2)
        {
            throw new InvalidOperationException("The M1 fixture catalog must contain exactly two card definitions.");
        }

        var byCode = definitions.ToDictionary(definition => definition.Code, StringComparer.Ordinal);
        ValidateDefinition(
            byCode,
            ToyCardDefinitionIds.CorpAgenda3,
            "corp",
            "agenda");
        ValidateDefinition(
            byCode,
            ToyCardDefinitionIds.RunnerProgram,
            "runner",
            "program");

        IReadOnlyList<ToyDeckRecipe> recipes = All;
        if (recipes.Count != 2
            || recipes.Count(recipe => recipe.Side == PlayerSide.Corp) != 1
            || recipes.Count(recipe => recipe.Side == PlayerSide.Runner) != 1)
        {
            throw new InvalidOperationException("The M1 fixture catalog must contain exactly one Corp and one Runner recipe.");
        }

        foreach (ToyDeckRecipe recipe in recipes)
        {
            if (recipe.DefinitionIds.Count != FixtureCardCount)
            {
                throw new InvalidOperationException($"Fixture recipe '{recipe.RecipeId}' must contain {FixtureCardCount} cards.");
            }

            foreach (CardDefinitionId definitionId in recipe.DefinitionIds)
            {
                if (!byCode.ContainsKey(definitionId.Value)
                    || !ToyCardBehaviorCatalog.TryGet(definitionId, out ToyCardBehavior? behavior)
                    || behavior.OwnerSide != recipe.Side)
                {
                    throw new InvalidOperationException(
                        $"Fixture recipe '{recipe.RecipeId}' contains an unbound or wrong-side definition '{definitionId}'.");
                }
            }
        }
    }

    private static void ValidateDefinition(
        Dictionary<string, CardDefinition> byCode,
        CardDefinitionId expectedId,
        string expectedSide,
        string expectedType)
    {
        if (!byCode.TryGetValue(expectedId.Value, out CardDefinition? definition))
        {
            throw new InvalidOperationException($"Fixture definition '{expectedId}' is missing.");
        }

        if (string.IsNullOrWhiteSpace(definition.Code)
            || definition.Code.Any(char.IsWhiteSpace)
            || !string.Equals(definition.Side, expectedSide, StringComparison.Ordinal)
            || !string.Equals(definition.Type, expectedType, StringComparison.Ordinal)
            || definition.Quantity != FixtureCardCount
            || !string.Equals(definition.SetCode, FixtureSetCode, StringComparison.Ordinal)
            || definition.Code.All(char.IsDigit))
        {
            throw new InvalidOperationException($"Fixture definition '{expectedId}' has invalid metadata.");
        }

        _ = ToyCardBehaviorCatalog.Get(expectedId);
    }
}

/// <summary>Descriptive alias for code that wants to name the M1 fixture group.</summary>
public static class M1FixtureDecks
{
    public static ToyDeckRecipe Corp => FixtureDeckCatalog.CorpRecipe;

    public static ToyDeckRecipe Runner => FixtureDeckCatalog.RunnerRecipe;

    public static IReadOnlyList<CardDefinition> Definitions => FixtureDeckCatalog.Definitions;
}
