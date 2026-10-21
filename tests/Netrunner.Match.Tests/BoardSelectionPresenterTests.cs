using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class BoardSelectionPresenterTests
{
    [Fact]
    public void ExactSelectionExplainsMissingCardsAndOnlyEnablesACompleteSelection()
    {
        CardSelectionViewModel context = Context(BehaviorChoiceCardinalityKind.Exactly, 2);
        BoardSelectionGuidance empty = BoardSelectionPresenter.Incremental(context, []);
        Assert.False(empty.CanConfirm);
        Assert.Contains("exactly 2 cards", empty.Instruction, StringComparison.Ordinal);
        Assert.Contains("select 2 more cards", empty.ConfirmationLabel, StringComparison.Ordinal);
        BoardSelectionGuidance partial = BoardSelectionPresenter.Incremental(context, ["a"]);
        Assert.False(partial.CanConfirm);
        Assert.Contains("1 selected", partial.Instruction, StringComparison.Ordinal);
        Assert.Contains("select 1 more card", partial.ConfirmationLabel, StringComparison.Ordinal);
        Assert.True(BoardSelectionPresenter.Incremental(context, ["a", "b"]).CanConfirm);
        Assert.False(BoardSelectionPresenter.Incremental(context, ["a", "b", "c"]).CanConfirm);
        Assert.False(BoardSelectionPresenter.Incremental(context, ["a", "a"]).CanConfirm);
    }

    [Theory]
    [InlineData(BehaviorChoiceCardinalityKind.UpTo, 2)]
    [InlineData(BehaviorChoiceCardinalityKind.AnyNumber, null)]
    public void EmptySelectionsExplicitlyContinueWithoutCards(BehaviorChoiceCardinalityKind cardinality, int? count)
    {
        CardSelectionViewModel context = Context(cardinality, count);
        BoardSelectionGuidance empty = BoardSelectionPresenter.Incremental(context, []);
        Assert.True(empty.CanConfirm);
        Assert.Equal("Continue without selecting cards", empty.ConfirmationLabel);
        Assert.DoesNotContain(cardinality.ToString(), empty.Instruction, StringComparison.Ordinal);
        Assert.True(BoardSelectionPresenter.Incremental(context, ["a", "b"]).CanConfirm);
        Assert.Equal(count is null, BoardSelectionPresenter.Incremental(context, ["a", "b", "c"]).CanConfirm);
        CardSelectionViewModel accepted = context with { Selected = ["a"] };
        Assert.False(BoardSelectionPresenter.Incremental(accepted, []).CanConfirm);
        Assert.DoesNotContain("without cards", BoardSelectionPresenter.Incremental(accepted, ["a"]).Instruction, StringComparison.Ordinal);
        Assert.False(BoardSelectionPresenter.Incremental(accepted, ["b", "a"]).CanConfirm);
    }

    [Fact]
    public void BreakGuidanceUsesExactIssuedSetsAndExplainsAPartialSelection()
    {
        BoardIcebreakerViewModel breaker = Breaker([0, 2]);
        BoardSelectionGuidance initial = BoardSelectionPresenter.Breaks(breaker, []);
        Assert.False(initial.CanConfirm);
        Assert.Contains("Select 2 subroutines", initial.Instruction, StringComparison.Ordinal);
        BoardSelectionGuidance partial = BoardSelectionPresenter.Breaks(breaker, [2]);
        Assert.False(partial.CanConfirm);
        Assert.Equal("Confirm break · select 1 more", partial.ConfirmationLabel);
        Assert.True(BoardSelectionPresenter.Breaks(breaker, [2, 0]).CanConfirm);
        Assert.False(BoardSelectionPresenter.Breaks(breaker, [0, 1]).CanConfirm);
    }

    [Fact]
    public void BreakGuidanceDoesNotInventIntermediateQuantities()
    {
        BoardIcebreakerViewModel breaker = Breaker([0], [0, 1, 2]);
        Assert.Contains("Select 1 or 3 subroutines", BoardSelectionPresenter.Breaks(breaker, []).Instruction, StringComparison.Ordinal);
        Assert.True(BoardSelectionPresenter.Breaks(breaker, [0]).CanConfirm);
        BoardSelectionGuidance partial = BoardSelectionPresenter.Breaks(breaker, [0, 2]);
        Assert.False(partial.CanConfirm);
        Assert.Contains("Select 3 subroutines", partial.Instruction, StringComparison.Ordinal);
        Assert.Equal("Confirm break · select 1 more", partial.ConfirmationLabel);
    }

    private static CardSelectionViewModel Context(BehaviorChoiceCardinalityKind cardinality, int? count) =>
        new("frame", "choice", BehaviorChoiceMode.Mandatory, cardinality, count, ["a", "b", "c"], []);

    private static BoardIcebreakerViewModel Breaker(params int[][] sets)
    {
        var encounter = new EncounterViewModel("encounter", null, RunEncounterStatus.Initialized,
            [new(0, false, false), new(1, false, false), new(2, false, false)]);
        LegalActionViewModel[] actions = sets.Select((targets, index) => new LegalActionViewModel(new($"break-{index}"),
            ActionKind.BreakSubroutines, "Break", new RunBreakParameters(new("breaker"), targets.Length, subroutineOrdinals: targets))).ToArray();
        return new(encounter.Id, null!, encounter, null, actions);
    }
}
