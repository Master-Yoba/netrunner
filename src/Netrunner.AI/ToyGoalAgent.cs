using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// The three deterministic policies used by the M1 toy-game proof. These are
/// contract agents, not general-purpose Netrunner strategy implementations.
/// </summary>
public enum ToyGoal
{
    CorpScore,
    RunnerSteal,
    DeckOut,
}

/// <summary>
/// A rules-agnostic contract agent for the M1 fixture game.
///
/// The agent consumes only the filtered observation's viewer, typed legal
/// action kinds, and typed card targets. It deliberately does not inspect
/// descriptions, decision-id strings, state dictionaries, or authoritative
/// <see cref="GameState"/> values.
/// </summary>
public sealed class ToyGoalAgent : IGameAgent
{
    public ToyGoalAgent(ToyGoal goal)
    {
        if (!Enum.IsDefined(goal))
        {
            throw new ArgumentOutOfRangeException(nameof(goal), goal, "Unknown toy-agent goal.");
        }

        Goal = goal;
    }

    public ToyGoal Goal { get; }

    public ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        if (observation.LegalActions is null)
        {
            throw new InvalidOperationException("The observation did not contain a legal-action list.");
        }

        LegalAction? selected = Goal switch
        {
            ToyGoal.CorpScore when observation.Viewer == PlayerSide.Corp => Select(
                observation.LegalActions,
                ActionKind.ScoreAgenda,
                ActionKind.AdvanceAgenda,
                ActionKind.InstallCard,
                ActionKind.GainCredit,
                ActionKind.EndTurn),
            ToyGoal.CorpScore => Select(
                observation.LegalActions,
                ActionKind.EndTurn),
            ToyGoal.RunnerSteal when observation.Viewer == PlayerSide.Corp => Select(
                observation.LegalActions,
                ActionKind.InstallCard,
                ActionKind.EndTurn),
            ToyGoal.RunnerSteal => Select(
                observation.LegalActions,
                ActionKind.StealAgenda,
                ActionKind.EndTurn),
            ToyGoal.DeckOut => Select(
                observation.LegalActions,
                ActionKind.EndTurn),
            _ => null,
        };

        if (selected is null)
        {
            throw new InvalidOperationException(
                $"No supported action was offered for toy goal '{Goal}' and viewer '{observation.Viewer}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(selected.Id);
    }

    private static LegalAction? Select(
        IReadOnlyList<LegalAction> legalActions,
        params ActionKind[] priorities)
    {
        foreach (ActionKind priority in priorities)
        {
            LegalAction[] candidates = legalActions
                .Where(action => IsSupported(action, priority))
                .ToArray();

            if (candidates.Length > 1
                && candidates
                    .GroupBy(TargetKey, StringComparer.Ordinal)
                    .Any(group => group.Skip(1).Any()))
            {
                throw new InvalidOperationException(
                    $"The observation contains duplicate '{priority}' targets.");
            }

            LegalAction? selected = candidates
                .OrderBy(TargetKey, StringComparer.Ordinal)
                .FirstOrDefault();

            if (selected is not null)
            {
                return selected;
            }
        }

        return null;
    }

    private static bool IsSupported(LegalAction? action, ActionKind expectedKind)
    {
        if (action is null || action.Kind != expectedKind)
        {
            return false;
        }

        return expectedKind switch
        {
            ActionKind.InstallCard
                or ActionKind.AdvanceAgenda
                or ActionKind.ScoreAgenda
                or ActionKind.StealAgenda => action.Parameters is CardChoiceParameters,
            ActionKind.GainCredit
                or ActionKind.DrawCard
                or ActionKind.EndTurn => action.Parameters is NoActionParameters,
            _ => false,
        };
    }

    private static string TargetKey(LegalAction action) =>
        action.Parameters is CardChoiceParameters cardChoice
            ? cardChoice.CardId.Value
            : string.Empty;
}

/// <summary>
/// Naming alias for callers that prefer the contract-agent terminology used
/// in the M1 architecture document.
/// </summary>
public sealed class ContractAgent : IGameAgent
{
    private readonly ToyGoalAgent _agent;

    public ContractAgent(ToyGoal goal) => _agent = new ToyGoalAgent(goal);

    public ToyGoal Goal => _agent.Goal;

    public ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default) =>
        _agent.ChooseActionAsync(observation, cancellationToken);
}
