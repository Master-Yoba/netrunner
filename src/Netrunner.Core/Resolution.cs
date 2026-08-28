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
    public const string RunTiming = "rules.run-timing-26.03";
    public const string EffectResolution = "rules.effects-2a";
    public const string MemoryCleanup = "rules.memory-cleanup-2a";
    public const string EffectCheckpoint = "rules.effect-checkpoint-2b";
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

public enum EffectResolutionStage
{
    PayCosts,
    ApplyEffects,
    AwaitingCheckpoint,
    AwaitingMemoryCleanup,
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
[JsonDerivedType(typeof(RunTimingFrame), "run-timing")]
[JsonDerivedType(typeof(EffectResolutionFrame), "effect-resolution")]
[JsonDerivedType(typeof(MemoryCleanupFrame), "memory-cleanup")]
[JsonDerivedType(typeof(EffectCheckpointFrame), "effect-checkpoint")]
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

/// <summary>
/// Serializable CR 26.03 section 6.9 timing cursor.  The reducer registry
/// owns the behavior for this frame; the frame contains only stable data.
/// </summary>
public sealed record RunTimingFrame : ResolutionFrame
{
    [JsonConstructor]
    public RunTimingFrame(
        int version,
        string behaviorId,
        RunId runId,
        RunTimingStep step)
        : base(version, behaviorId)
    {
        RunId = runId;
        Step = step;
    }

    public RunTimingFrame(RunId runId, RunTimingStep step)
        : this(1, ResolutionBehaviorIds.RunTiming, runId, step)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunTimingStep Step { get; private set; }
}

/// <summary>
/// Serializable typed cost/effect transaction.  Executable behavior remains
/// in the reducer registry; this frame stores only materialized instructions.
/// </summary>
public sealed record EffectResolutionFrame : ResolutionFrame
{
    [JsonConstructor]
    public EffectResolutionFrame(
        int version,
        string behaviorId,
        PlayerId controller,
        CostBundle costs,
        EffectBundle effects,
        EffectResolutionStage stage,
        int effectCursor = 0,
        OccurrenceSnapshot? sourceOccurrence = null,
        IReadOnlyList<TriggerId>? suppressedTriggers = null,
        IReadOnlyList<InterruptId>? suppressedInterrupts = null)
        : base(version, behaviorId)
    {
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(effects);
        Controller = controller;
        Costs = costs.Copy();
        Effects = effects.Copy();
        Stage = stage;
        EffectCursor = effectCursor;
        SourceOccurrence = sourceOccurrence?.Copy();
        SuppressedTriggers = new ReadOnlyCollection<TriggerId>((suppressedTriggers ?? []).ToArray());
        SuppressedInterrupts = new ReadOnlyCollection<InterruptId>((suppressedInterrupts ?? []).ToArray());
    }

    public EffectResolutionFrame(PlayerId controller, CostBundle costs, EffectBundle effects)
        : this(1, ResolutionBehaviorIds.EffectResolution, controller, costs, effects, EffectResolutionStage.PayCosts)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CostBundle Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectBundle Effects { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectResolutionStage Stage { get; private set; }

    /// <summary>
    /// Index of the next effect instruction to make imminent. The cursor is
    /// persisted before a checkpoint child is pushed, so resuming a save never
    /// reapplies an already committed instruction.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public int EffectCursor { get; private set; }

    /// <summary>Original occurrence context when this frame is a replacement child.</summary>
    [JsonInclude]
    [JsonRequired]
    public OccurrenceSnapshot? SourceOccurrence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<TriggerId> SuppressedTriggers { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<InterruptId> SuppressedInterrupts { get; private set; }
}

/// <summary>
/// Mandatory cleanup choice when a Runner program memory change exceeds the
/// current base memory. Candidates are frozen in the frame so loading cannot
/// change the materialized legal action set.
/// </summary>
public sealed record MemoryCleanupFrame : ResolutionFrame
{
    [JsonConstructor]
    public MemoryCleanupFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        int requiredReduction,
        IReadOnlyList<ProgramMemoryState> candidates)
        : base(version, behaviorId)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Owner = owner;
        RequiredReduction = requiredReduction;
        Candidates = new ReadOnlyCollection<ProgramMemoryState>(candidates.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
    }

    public MemoryCleanupFrame(PlayerId owner, int requiredReduction, IReadOnlyList<ProgramMemoryState> candidates)
        : this(1, ResolutionBehaviorIds.MemoryCleanup, owner, requiredReduction, candidates)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int RequiredReduction { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<ProgramMemoryState> Candidates { get; private set; }
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
            case RunTimingFrame runTiming:
                ValidateBehavior(frame, ResolutionBehaviorIds.RunTiming, path, violations);
                if (!StableIdentifier.TryValidate(runTiming.RunId.Value))
                {
                    violations.Add(new StateInvariantViolation(
                        "id.invalid",
                        $"{path}.runId",
                        "Run ID is empty or invalid."));
                }

                if (!Enum.IsDefined(runTiming.Step))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-run-step",
                        $"{path}.step",
                        $"'{runTiming.Step}' is not a supported run timing step."));
                }

                break;
            case EffectResolutionFrame effectResolution:
                ValidateBehavior(frame, ResolutionBehaviorIds.EffectResolution, path, violations);
                ValidatePlayer(effectResolution.Controller, $"{path}.controller", violations);
                if (!Enum.IsDefined(effectResolution.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-effect-stage",
                        $"{path}.stage",
                        $"'{effectResolution.Stage}' is not a supported effect-resolution stage."));
                }

                foreach (StateInvariantViolation violation in EffectInstructionValidator.Collect(
                    effectResolution.Costs,
                    effectResolution.Effects,
                    path))
                {
                    violations.Add(violation);
                }

                break;
            case EffectCheckpointFrame checkpoint:
                ValidateBehavior(frame, ResolutionBehaviorIds.EffectCheckpoint, path, violations);
                ValidatePlayer(checkpoint.Controller, $"{path}.controller", violations);
                ValidateOccurrence(checkpoint.Occurrence, $"{path}.occurrence", violations);
                if (checkpoint.ResolvedOccurrence is not null)
                {
                    ValidateOccurrence(checkpoint.ResolvedOccurrence, $"{path}.resolvedOccurrence", violations);
                    if (checkpoint.ResolvedOccurrence.Phase != OccurrencePhase.Resolved)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-resolved-occurrence",
                            $"{path}.resolvedOccurrence.phase",
                            "A resolved occurrence snapshot must use the resolved phase."));
                    }
                }

                if (!Enum.IsDefined(checkpoint.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-checkpoint-stage",
                        $"{path}.stage",
                        $"'{checkpoint.Stage}' is not a supported effect checkpoint stage."));
                }

                if (!Enum.IsDefined(checkpoint.ChildKind))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-checkpoint-child",
                        $"{path}.childKind",
                        $"'{checkpoint.ChildKind}' is not a supported effect checkpoint child kind."));
                }

                if (checkpoint.TriggerCursor < 0 || checkpoint.TriggerCursor > checkpoint.TriggerQueue.Count)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-trigger-cursor",
                        $"{path}.triggerCursor",
                        "Trigger cursor must identify the next unprocessed trigger or the end of the queue."));
                }

                if (checkpoint.InterruptCursor < 0 || checkpoint.InterruptCursor > checkpoint.InterruptQueue.Count)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-interrupt-cursor",
                        $"{path}.interruptCursor",
                        "Interrupt cursor must identify the next unprocessed interrupt or the end of the queue."));
                }

                if (checkpoint.ReplacementDepth < 0 || checkpoint.ReplacementDepth > 8)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-replacement-depth",
                        $"{path}.replacementDepth",
                        "Replacement depth must be between zero and eight."));
                }

                if (checkpoint.ChildKind == EffectCheckpointChildKind.None
                    && (checkpoint.ChildTriggerId.HasValue || checkpoint.ChildInterruptId.HasValue))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.unexpected-checkpoint-child",
                        path,
                        "A checkpoint without a child kind must not identify a child invocation."));
                }

                foreach (TriggerInvocation invocation in checkpoint.TriggerQueue)
                {
                    ValidateTriggerInvocation(invocation, $"{path}.triggerQueue", violations);
                }

                foreach (InterruptInvocation invocation in checkpoint.InterruptQueue)
                {
                    ValidateInterruptInvocation(invocation, $"{path}.interruptQueue", violations);
                }

                foreach (TriggerId triggerId in checkpoint.AppliedTriggers)
                {
                    if (!StableIdentifier.TryValidate(triggerId.Value))
                    {
                        violations.Add(new StateInvariantViolation(
                            "id.invalid",
                            $"{path}.appliedTriggers",
                            "Applied trigger IDs must be non-empty and stable."));
                    }
                }

                foreach (InterruptId interruptId in checkpoint.AppliedInterrupts)
                {
                    if (!StableIdentifier.TryValidate(interruptId.Value))
                    {
                        violations.Add(new StateInvariantViolation(
                            "id.invalid",
                            $"{path}.appliedInterrupts",
                            "Applied interrupt IDs must be non-empty and stable."));
                    }
                }

                foreach (StateInvariantViolation violation in EffectInstructionValidator.Collect(
                    new CostBundle(),
                    new EffectBundle(checkpoint.OriginalEffect),
                    $"{path}.originalEffect"))
                {
                    violations.Add(violation);
                }

                foreach (StateInvariantViolation violation in EffectInstructionValidator.Collect(
                    new CostBundle(),
                    new EffectBundle(checkpoint.CurrentEffect),
                    $"{path}.currentEffect"))
                {
                    // A prevention checkpoint may intentionally carry a zero
                    // damage instruction. It is consumed without producing a
                    // committed occurrence, but remains serializable until
                    // the checkpoint resolves.
                    if (checkpoint.CurrentEffect is DamageEffect { Amount: 0 }
                        && violation.Code == "effects.nonpositive-amount")
                    {
                        continue;
                    }

                    violations.Add(violation);
                }

                break;
            case MemoryCleanupFrame memoryCleanup:
                ValidateBehavior(frame, ResolutionBehaviorIds.MemoryCleanup, path, violations);
                ValidatePlayer(memoryCleanup.Owner, $"{path}.owner", violations);
                if (memoryCleanup.RequiredReduction <= 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-memory-reduction",
                        $"{path}.requiredReduction",
                        "Memory cleanup must reduce at least one memory unit."));
                }

                if (memoryCleanup.Candidates.Count == 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.empty-memory-candidates",
                        $"{path}.candidates",
                        "Memory cleanup must contain at least one materialized program candidate."));
                }

                var candidateIds = new HashSet<CardInstanceId>();
                for (int index = 0; index < memoryCleanup.Candidates.Count; index++)
                {
                    ProgramMemoryState? candidate = memoryCleanup.Candidates[index];
                    string candidatePath = $"{path}.candidates[{index}]";
                    if (candidate is null)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.null-memory-candidate",
                            candidatePath,
                            "Memory cleanup candidates must not be null."));
                        continue;
                    }

                    if (!StableIdentifier.TryValidate(candidate.CardId.Value))
                    {
                        violations.Add(new StateInvariantViolation(
                            "id.invalid",
                            $"{candidatePath}.cardId",
                            "Memory cleanup candidate card ID is empty or invalid."));
                    }
                    else if (!candidateIds.Add(candidate.CardId))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.duplicate-memory-candidate",
                            $"{candidatePath}.cardId",
                            $"Program '{candidate.CardId}' occurs more than once in memory cleanup candidates."));
                    }

                    ValidatePlayer(candidate.Owner, $"{candidatePath}.owner", violations);
                    if (candidate.MemoryCost < 0)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-memory-cost",
                            $"{candidatePath}.memoryCost",
                            "Memory cleanup candidate cost must be nonnegative."));
                    }
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

    private static void ValidateOccurrence(
        OccurrenceSnapshot occurrence,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (occurrence.Version != 1)
        {
            violations.Add(new StateInvariantViolation(
                "resolution.unsupported-occurrence-version",
                $"{path}.version",
                "Only occurrence snapshot version 1 is supported."));
        }

        if (!StableIdentifier.TryValidate(occurrence.Id.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", $"{path}.id", "Occurrence ID is empty or invalid."));
        }

        if (!Enum.IsDefined(occurrence.Kind))
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-occurrence-kind", $"{path}.kind", "Occurrence kind is not supported."));
        }

        if (!Enum.IsDefined(occurrence.Phase))
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-occurrence-phase", $"{path}.phase", "Occurrence phase is not supported."));
        }

        ValidatePlayer(occurrence.Controller, $"{path}.controller", violations);
        if (occurrence.TargetPlayer.HasValue)
        {
            ValidatePlayer(occurrence.TargetPlayer.Value, $"{path}.targetPlayer", violations);
        }

        if (occurrence.SourceCardId.HasValue
            && !StableIdentifier.TryValidate(occurrence.SourceCardId.Value.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", $"{path}.sourceCardId", "Source card ID is empty or invalid."));
        }

        if (occurrence.SourceDefinitionId.HasValue
            && !StableIdentifier.TryValidate(occurrence.SourceDefinitionId.Value.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", $"{path}.sourceDefinitionId", "Source definition ID is empty or invalid."));
        }

        if (occurrence.SourceZoneId.HasValue
            && !StableIdentifier.TryValidate(occurrence.SourceZoneId.Value.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", $"{path}.sourceZoneId", "Source zone ID is empty or invalid."));
        }
    }

    private static void ValidateTriggerInvocation(
        TriggerInvocation invocation,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (invocation.Version != 1)
        {
            violations.Add(new StateInvariantViolation("resolution.unsupported-trigger-version", $"{path}.version", "Only trigger invocation version 1 is supported."));
        }

        if (!StableIdentifier.TryValidate(invocation.Id.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", $"{path}.id", "Trigger invocation ID is empty or invalid."));
        }

        ValidatePlayer(invocation.Controller, $"{path}.controller", violations);
        if (!Enum.IsDefined(invocation.Mode))
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-trigger-mode", $"{path}.mode", "Trigger mode is not supported."));
        }

        if (!StableIdentifier.TryValidate(invocation.BehaviorId))
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-behavior", $"{path}.behaviorId", "Trigger behavior ID is empty or invalid."));
        }

        ValidateOccurrence(invocation.Occurrence, $"{path}.occurrence", violations);
    }

    private static void ValidateInterruptInvocation(
        InterruptInvocation invocation,
        string path,
        List<StateInvariantViolation> violations)
    {
        if (invocation.Version != 1)
        {
            violations.Add(new StateInvariantViolation("resolution.unsupported-interrupt-version", $"{path}.version", "Only interrupt invocation version 1 is supported."));
        }

        if (!StableIdentifier.TryValidate(invocation.Id.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", $"{path}.id", "Interrupt invocation ID is empty or invalid."));
        }

        ValidatePlayer(invocation.Controller, $"{path}.controller", violations);
        if (!Enum.IsDefined(invocation.Kind))
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-interrupt-kind", $"{path}.kind", "Interrupt kind is not supported."));
        }

        if (!StableIdentifier.TryValidate(invocation.BehaviorId))
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-behavior", $"{path}.behaviorId", "Interrupt behavior ID is empty or invalid."));
        }

        ValidateOccurrence(invocation.Occurrence, $"{path}.occurrence", violations);
        if (invocation.MaximumPreventionAmount < 0)
        {
            violations.Add(new StateInvariantViolation("resolution.invalid-prevention-amount", $"{path}.maximumPreventionAmount", "Maximum prevention amount must be nonnegative."));
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
        RunTimingFrame runTiming => new RunTimingFrame(
            runTiming.Version,
            runTiming.BehaviorId,
            runTiming.RunId,
            runTiming.Step),
        EffectResolutionFrame effectResolution => new EffectResolutionFrame(
            effectResolution.Version,
            effectResolution.BehaviorId,
            effectResolution.Controller,
            effectResolution.Costs,
            effectResolution.Effects,
            effectResolution.Stage,
            effectResolution.EffectCursor,
            effectResolution.SourceOccurrence,
            effectResolution.SuppressedTriggers,
            effectResolution.SuppressedInterrupts),
        MemoryCleanupFrame memoryCleanup => new MemoryCleanupFrame(
            memoryCleanup.Version,
            memoryCleanup.BehaviorId,
            memoryCleanup.Owner,
            memoryCleanup.RequiredReduction,
            memoryCleanup.Candidates),
        EffectCheckpointFrame checkpoint => checkpoint.Copy(),
        _ => throw new InvalidOperationException($"No cloner is registered for frame type '{frame.GetType().Name}'."),
    };
}
