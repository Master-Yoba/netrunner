using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// A typed declaration for a draw caused by a successful run event. The
/// source card and the run that realizes the declaration are supplied by the
/// engine; this contract only records the printed amount and a stable reason
/// key for replay and presentation.
/// </summary>
public sealed record BehaviorRunSuccessDraw
{
    [JsonConstructor]
    public BehaviorRunSuccessDraw(int drawCount, string reasonKey)
    {
        if (drawCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drawCount),
                "A successful-run draw count must be positive.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException(
                "A successful-run draw reason key must be a stable identifier.",
                nameof(reasonKey));
        }

        DrawCount = drawCount;
        ReasonKey = reasonKey;
    }

    [JsonInclude]
    [JsonRequired]
    public int DrawCount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ReasonKey { get; private set; }
}

/// <summary>
/// A typed declaration for additional access granted when the run event's
/// attacked server is breached. The modifier applies to the breach of that
/// run only; the engine supplies physical source and server identities when
/// it realizes the declaration.
/// </summary>
public sealed record BehaviorRunBreachAccessModifier
{
    [JsonConstructor]
    public BehaviorRunBreachAccessModifier(int delta, string reasonKey)
    {
        if (delta <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                "A run breach-access modifier delta must be positive.");
        }

        if (!StableIdentifier.TryValidate(reasonKey))
        {
            throw new ArgumentException(
                "A run breach-access modifier reason key must be a stable identifier.",
                nameof(reasonKey));
        }

        Delta = delta;
        ReasonKey = reasonKey;
    }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string ReasonKey { get; private set; }
}
