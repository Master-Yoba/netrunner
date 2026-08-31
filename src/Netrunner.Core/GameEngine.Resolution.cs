namespace Netrunner.Core;

/// <summary>
/// Shared resolution-frame stepping and decision materialization helpers.
/// The implementation is a behavior-neutral extraction from the M2 reducer;
/// domain-specific frame reducers remain in their owning partials.
/// </summary>
public static partial class GameEngine
{
    private static void StepChooseNumberThenAdjustCredits(
        WorkingState working,
        ChooseNumberThenAdjustCreditsFrame frame)
    {
        if (frame.Stage != NestedNumberStage.OuterPrompt)
        {
            throw new ResolutionFailureException("An outer number prompt can only execute at the beginning of the nested fixture.");
        }

        YieldNumberDecision(
            working,
            frame.Owner,
            frame.Minimum,
            frame.Maximum,
            ResolutionContinuation.Complete,
            null,
            0);
    }

    private static void StepChooseNumber(WorkingState working, ChooseNumberFrame frame) =>
        YieldNumberDecision(
            working,
            frame.Owner,
            frame.Minimum,
            frame.Maximum,
            frame.Continuation,
            frame.AdjustmentTarget,
            frame.CreditsPerChoice);

    private static void StepChooseCard(WorkingState working, ChooseCardFrame frame) =>
        YieldCardDecision(working, frame.Owner, frame.CardOptions);

    private static void YieldNumberDecision(
        WorkingState working,
        PlayerId owner,
        int minimum,
        int maximum,
        ResolutionContinuation continuation,
        PlayerId? adjustmentTarget,
        int creditsPerChoice)
    {
        DecisionId decisionId = working.Allocator.NextDecisionId();
        var actions = new List<LegalAction>();
        for (long value = minimum; value <= maximum; value++)
        {
            int number = checked((int)value);
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseNumber,
                $"Choose {number}",
                new NumberChoiceParameters(number)));
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            decisionId,
            working.DecisionRevision,
            owner,
            DecisionKind.ChooseNumber,
            "Choose a number",
            actions);
    }

    private static void YieldCardDecision(
        WorkingState working,
        PlayerId owner,
        IReadOnlyList<CardInstanceId> cardOptions)
    {
        DecisionId decisionId = working.Allocator.NextDecisionId();
        var actions = new List<LegalAction>(cardOptions.Count);
        foreach (CardInstanceId cardId in cardOptions)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseCard,
                $"Choose card {cardId}",
                new CardChoiceParameters(cardId)));
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            decisionId,
            working.DecisionRevision,
            owner,
            DecisionKind.ChooseCard,
            "Choose a card",
            actions);
    }

    private static long NextRevision(long current) =>
        current == long.MaxValue
            ? throw new ResolutionFailureException("The decision revision counter is exhausted.")
            : checked(current + 1);

    private static long NextSequence(long current, string counterName) =>
        current == long.MaxValue
            ? throw new ResolutionFailureException($"The {counterName} sequence counter is exhausted.")
            : checked(current + 1);
}
