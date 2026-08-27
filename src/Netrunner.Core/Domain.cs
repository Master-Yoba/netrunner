namespace Netrunner.Core;

public enum PlayerSide
{
    Corp,
    Runner,
}

public readonly record struct ActionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct CardInstanceId(string Value)
{
    public override string ToString() => Value;
}

public sealed record LegalAction(
    ActionId Id,
    string Kind,
    string Description,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record GameEvent(
    long Sequence,
    string Kind,
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record Observation(
    PlayerSide Viewer,
    string DecisionId,
    IReadOnlyList<LegalAction> LegalActions,
    IReadOnlyDictionary<string, object?> PublicState,
    IReadOnlyDictionary<string, object?> PrivateState);

public interface IGameAgent
{
    ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default);
}

