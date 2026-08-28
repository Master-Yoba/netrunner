using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public static class ResolutionBehaviorIds
{
    public const string ChooseNumberThenAdjustCredits = "fixture.choose-number-then-adjust-credits";
    public const string ChooseNumber = "fixture.choose-number";
    public const string ChooseCard = "fixture.choose-card";
    public const string AdjustCredits = "fixture.adjust-credits";
    public const string ToyTurn = "fixture.toy-turn";
}

public enum ResolutionContinuation
{
    Complete,
    AdjustCredits,
}

/// <summary>
/// Persisted stage of the nested fixture. The outer choice is a real pending
/// decision; after it is accepted, the parent records that choice and retains
/// the child prompt on the LIFO stack.
/// </summary>
public enum NestedNumberStage
{
    OuterPrompt,
    InnerPrompt,
}

/// <summary>
/// Serializable data only. The registry in GameEngine owns executable frame
/// behavior; a frame never stores a delegate, service, or code pointer.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ChooseNumberThenAdjustCreditsFrame), "choose-number-then-adjust-credits")]
[JsonDerivedType(typeof(ChooseNumberFrame), "choose-number")]
[JsonDerivedType(typeof(ChooseCardFrame), "choose-card")]
[JsonDerivedType(typeof(AdjustCreditsFrame), "adjust-credits")]
[JsonDerivedType(typeof(ToyTurnFrame), "toy-turn")]
public abstract record ResolutionFrame
{
    protected ResolutionFrame(int version, string behaviorId)
    {
        Version = version;
        BehaviorId = behaviorId;
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonIgnore]
    public int FrameVersion => Version;

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }
}

public sealed record ChooseNumberThenAdjustCreditsFrame : ResolutionFrame
{
    [JsonConstructor]
    public ChooseNumberThenAdjustCreditsFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        PlayerId target,
        int minimum,
        int maximum,
        int creditsPerChoice,
        NestedNumberStage stage = NestedNumberStage.OuterPrompt,
        int? chosenOuterValue = null)
        : base(version, behaviorId)
    {
        Owner = owner;
        Target = target;
        Minimum = minimum;
        Maximum = maximum;
        CreditsPerChoice = creditsPerChoice;
        Stage = stage;
        ChosenOuterValue = chosenOuterValue;
    }

    public ChooseNumberThenAdjustCreditsFrame(
        PlayerId owner,
        PlayerId target,
        int minimum,
        int maximum,
        int creditsPerChoice)
        : this(
            1,
            ResolutionBehaviorIds.ChooseNumberThenAdjustCredits,
            owner,
            target,
            minimum,
            maximum,
            creditsPerChoice,
            NestedNumberStage.OuterPrompt,
            null)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Minimum { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Maximum { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditsPerChoice { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public NestedNumberStage Stage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int? ChosenOuterValue { get; private set; }

    [JsonIgnore]
    public bool ChildPrompted => Stage == NestedNumberStage.InnerPrompt;
}

public sealed record ChooseNumberFrame : ResolutionFrame
{
    [JsonConstructor]
    public ChooseNumberFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        int minimum,
        int maximum,
        ResolutionContinuation continuation = ResolutionContinuation.Complete,
        PlayerId? adjustmentTarget = null,
        int creditsPerChoice = 0)
        : base(version, behaviorId)
    {
        Owner = owner;
        Minimum = minimum;
        Maximum = maximum;
        Continuation = continuation;
        AdjustmentTarget = adjustmentTarget;
        CreditsPerChoice = creditsPerChoice;
    }

    public ChooseNumberFrame(
        PlayerId owner,
        int minimum,
        int maximum,
        ResolutionContinuation continuation = ResolutionContinuation.Complete,
        PlayerId? adjustmentTarget = null,
        int creditsPerChoice = 0)
        : this(
            1,
            ResolutionBehaviorIds.ChooseNumber,
            owner,
            minimum,
            maximum,
            continuation,
            adjustmentTarget,
            creditsPerChoice)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Minimum { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Maximum { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ResolutionContinuation Continuation { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId? AdjustmentTarget { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditsPerChoice { get; private set; }
}

public sealed record ChooseCardFrame : ResolutionFrame
{
    [JsonConstructor]
    public ChooseCardFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        IReadOnlyList<CardInstanceId> cardOptions)
        : base(version, behaviorId)
    {
        ArgumentNullException.ThrowIfNull(cardOptions);
        Owner = owner;
        CardOptions = new ReadOnlyCollection<CardInstanceId>(cardOptions.ToArray());
    }

    public ChooseCardFrame(PlayerId owner, IReadOnlyList<CardInstanceId> cardOptions)
        : this(1, ResolutionBehaviorIds.ChooseCard, owner, cardOptions)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CardOptions { get; private set; }
}

public sealed record AdjustCreditsFrame : ResolutionFrame
{
    [JsonConstructor]
    public AdjustCreditsFrame(
        int version,
        string behaviorId,
        PlayerId target,
        int delta)
        : base(version, behaviorId)
    {
        Target = target;
        Delta = delta;
    }

    public AdjustCreditsFrame(PlayerId target, int delta)
        : this(1, ResolutionBehaviorIds.AdjustCredits, target, delta)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }
}

/// <summary>
/// The serializable control frame for the Slice 4 fixture turn loop. The
/// reducer owns behavior for this ID; the frame contains no executable code.
/// </summary>
public sealed record ToyTurnFrame : ResolutionFrame
{
    [JsonConstructor]
    public ToyTurnFrame(
        int version,
        string behaviorId,
        PlayerSide activeSide,
        ToyTurnStage stage)
        : base(version, behaviorId)
    {
        ActiveSide = activeSide;
        Stage = stage;
    }

    public ToyTurnFrame(
        PlayerSide activeSide,
        ToyTurnStage stage = ToyTurnStage.AwaitingAction)
        : this(1, ResolutionBehaviorIds.ToyTurn, activeSide, stage)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide ActiveSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ToyTurnStage Stage { get; private set; }
}

public static class ResolutionFrameValidator
{
    public static IReadOnlyList<StateInvariantViolation> Collect(
        ResolutionFrame? frame,
        string path = "resolutionStack")
    {
        var violations = new List<StateInvariantViolation>();
        if (frame is null)
        {
            violations.Add(new StateInvariantViolation("resolution.null-frame", path, "Resolution frames must not be null."));
            return violations.AsReadOnly();
        }

        if (frame.Version != 1)
        {
            violations.Add(new StateInvariantViolation(
                "resolution.unsupported-version",
                $"{path}.version",
                $"Resolution frame version {frame.Version} is not supported; expected 1."));
        }

        if (!StableIdentifier.TryValidate(frame.BehaviorId))
        {
            violations.Add(new StateInvariantViolation(
                "resolution.invalid-behavior",
                $"{path}.behaviorId",
                "Resolution behavior ID must be non-empty and valid."));
        }

        switch (frame)
        {
            case ChooseNumberThenAdjustCreditsFrame chooseThenAdjust:
                ValidateBehavior(frame, ResolutionBehaviorIds.ChooseNumberThenAdjustCredits, path, violations);
                ValidatePlayer(chooseThenAdjust.Owner, $"{path}.owner", violations);
                ValidatePlayer(chooseThenAdjust.Target, $"{path}.target", violations);
                ValidateNumberRange(chooseThenAdjust.Minimum, chooseThenAdjust.Maximum, path, violations);
                if (!Enum.IsDefined(chooseThenAdjust.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-stage",
                        $"{path}.stage",
                        $"'{chooseThenAdjust.Stage}' is not a supported nested-number stage."));
                }
                else if (chooseThenAdjust.Stage == NestedNumberStage.OuterPrompt
                    && chooseThenAdjust.ChosenOuterValue.HasValue)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.unexpected-outer-choice",
                        $"{path}.chosenOuterValue",
                        "An outer-prompt frame must not contain an outer choice."));
                }
                else if (chooseThenAdjust.Stage == NestedNumberStage.InnerPrompt
                    && (!chooseThenAdjust.ChosenOuterValue.HasValue
                        || chooseThenAdjust.ChosenOuterValue.Value < chooseThenAdjust.Minimum
                        || chooseThenAdjust.ChosenOuterValue.Value > chooseThenAdjust.Maximum))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-outer-choice",
                        $"{path}.chosenOuterValue",
                        "An inner-prompt frame must retain an outer choice within the original range."));
                }

                break;
            case ChooseNumberFrame chooseNumber:
                ValidateBehavior(frame, ResolutionBehaviorIds.ChooseNumber, path, violations);
                ValidatePlayer(chooseNumber.Owner, $"{path}.owner", violations);
                ValidateNumberRange(chooseNumber.Minimum, chooseNumber.Maximum, path, violations);
                if (!Enum.IsDefined(chooseNumber.Continuation))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-continuation",
                        $"{path}.continuation",
                        $"'{chooseNumber.Continuation}' is not a supported continuation."));
                }

                if (chooseNumber.Continuation == ResolutionContinuation.AdjustCredits)
                {
                    if (!chooseNumber.AdjustmentTarget.HasValue)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.missing-continuation-target",
                            $"{path}.adjustmentTarget",
                            "AdjustCredits continuation requires a target player."));
                    }
                    else
                    {
                        ValidatePlayer(chooseNumber.AdjustmentTarget.Value, $"{path}.adjustmentTarget", violations);
                    }
                }
                else if (chooseNumber.AdjustmentTarget.HasValue || chooseNumber.CreditsPerChoice != 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.unused-continuation-arguments",
                        path,
                        "A completed number-choice frame must not carry credit-adjustment arguments."));
                }

                break;
            case ChooseCardFrame chooseCard:
                ValidateBehavior(frame, ResolutionBehaviorIds.ChooseCard, path, violations);
                ValidatePlayer(chooseCard.Owner, $"{path}.owner", violations);
                if (chooseCard.CardOptions.Count == 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.empty-card-options",
                        $"{path}.cardOptions",
                        "A card-choice frame must contain at least one card option."));
                }

                var cardIds = new HashSet<CardInstanceId>();
                for (int index = 0; index < chooseCard.CardOptions.Count; index++)
                {
                    CardInstanceId cardId = chooseCard.CardOptions[index];
                    if (!StableIdentifier.TryValidate(cardId.Value))
                    {
                        violations.Add(new StateInvariantViolation(
                            "id.invalid",
                            $"{path}.cardOptions[{index}]",
                            "Card option ID is empty or invalid."));
                    }
                    else if (!cardIds.Add(cardId))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.duplicate-card-option",
                            $"{path}.cardOptions[{index}]",
                            $"Card option '{cardId}' occurs more than once."));
                    }
                }

                break;
            case AdjustCreditsFrame adjustCredits:
                ValidateBehavior(frame, ResolutionBehaviorIds.AdjustCredits, path, violations);
                ValidatePlayer(adjustCredits.Target, $"{path}.target", violations);
                break;
            case ToyTurnFrame toyTurn:
                ValidateBehavior(frame, ResolutionBehaviorIds.ToyTurn, path, violations);
                if (toyTurn.ActiveSide is not (PlayerSide.Corp or PlayerSide.Runner))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-side",
                        $"{path}.activeSide",
                        $"'{toyTurn.ActiveSide}' is not a supported toy-turn side."));
                }

                if (!Enum.IsDefined(toyTurn.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-stage",
                        $"{path}.stage",
                        $"'{toyTurn.Stage}' is not a supported toy-turn stage."));
                }

                break;
            default:
                violations.Add(new StateInvariantViolation(
                    "resolution.unknown-frame",
                    path,
                    $"Resolution frame type '{frame.GetType().Name}' is not registered."));
                break;
        }

        return violations.AsReadOnly();
    }

    private static void ValidateBehavior(
        ResolutionFrame frame,
        string expected,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (!string.Equals(frame.BehaviorId, expected, StringComparison.Ordinal))
        {
            violations.Add(new StateInvariantViolation(
                "resolution.unknown-behavior",
                $"{path}.behaviorId",
                $"Behavior '{frame.BehaviorId}' is not valid for frame type '{frame.GetType().Name}'; expected '{expected}'."));
        }
    }

    private static void ValidatePlayer(
        PlayerId player,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (!StableIdentifier.TryValidate(player.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", path, "Player ID is empty or invalid."));
        }
    }

    private static void ValidateNumberRange(
        int minimum,
        int maximum,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (minimum > maximum)
        {
            violations.Add(new StateInvariantViolation(
                "resolution.invalid-range",
                path,
                $"Number range minimum {minimum} must not exceed maximum {maximum}."));
        }

        if ((long)maximum - minimum > 64)
        {
            violations.Add(new StateInvariantViolation(
                "resolution.range-too-large",
                path,
                "A fixture number-choice range may contain at most 65 values."));
        }
    }
}

internal static class ResolutionFrameCloner
{
    public static ResolutionFrame Copy(ResolutionFrame frame) => frame switch
    {
        ChooseNumberThenAdjustCreditsFrame chooseThenAdjust => new ChooseNumberThenAdjustCreditsFrame(
            chooseThenAdjust.Version,
            chooseThenAdjust.BehaviorId,
            chooseThenAdjust.Owner,
            chooseThenAdjust.Target,
            chooseThenAdjust.Minimum,
            chooseThenAdjust.Maximum,
            chooseThenAdjust.CreditsPerChoice,
            chooseThenAdjust.Stage,
            chooseThenAdjust.ChosenOuterValue),
        ChooseNumberFrame chooseNumber => new ChooseNumberFrame(
            chooseNumber.Version,
            chooseNumber.BehaviorId,
            chooseNumber.Owner,
            chooseNumber.Minimum,
            chooseNumber.Maximum,
            chooseNumber.Continuation,
            chooseNumber.AdjustmentTarget,
            chooseNumber.CreditsPerChoice),
        ChooseCardFrame chooseCard => new ChooseCardFrame(
            chooseCard.Version,
            chooseCard.BehaviorId,
            chooseCard.Owner,
            chooseCard.CardOptions),
        AdjustCreditsFrame adjustCredits => new AdjustCreditsFrame(
            adjustCredits.Version,
            adjustCredits.BehaviorId,
            adjustCredits.Target,
            adjustCredits.Delta),
        ToyTurnFrame toyTurn => new ToyTurnFrame(
            toyTurn.Version,
            toyTurn.BehaviorId,
            toyTurn.ActiveSide,
            toyTurn.Stage),
        _ => throw new InvalidOperationException($"No cloner is registered for frame type '{frame.GetType().Name}'."),
    };
}
