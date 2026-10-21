using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>Stable registry key for the generic card-behavior interpreter.</summary>
public static partial class ResolutionBehaviorIds
{
    public const string CardBehavior = "rules.card-behavior-4.2";
}

/// <summary>
/// The caller-owned provenance for one generic behavior invocation.  The
/// invocation is data only; executable behavior is selected from the injected
/// catalog by the source card's definition ID.
/// </summary>
public sealed record BehaviorInvocationContext
{
    [JsonConstructor]
    public BehaviorInvocationContext(
        CardInstanceId sourceCardId,
        PlayerId controller,
        PlayerId? actingPlayer = null)
    {
        SourceCardId = sourceCardId;
        Controller = controller;
        ActingPlayer = actingPlayer;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    /// <summary>Player used by an ActingPlayer chooser; defaults to the source owner.</summary>
    [JsonInclude]
    [JsonRequired]
    public PlayerId? ActingPlayer { get; private set; }
}

/// <summary>Frozen card IDs bound to one declarative target/choice key.</summary>
public sealed record BehaviorBinding
{
    [JsonConstructor]
    public BehaviorBinding(string key, IReadOnlyList<CardInstanceId>? cardIds = null)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException("A behavior binding key must be stable.", nameof(key));
        }

        Key = key;
        CardIds = new ReadOnlyCollection<CardInstanceId>((cardIds ?? []).ToArray());
    }

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CardIds { get; private set; }
}

/// <summary>
/// A selected card retained across a behavior's reveal/move instructions.
/// The original location and visibility are persisted so a loaded frame can
/// reject a stale or forged selection before it moves a card.
/// </summary>
public sealed record BehaviorSetAsideCard
{
    [JsonConstructor]
    public BehaviorSetAsideCard(
        CardInstanceId cardId,
        ZoneId originalZoneId,
        bool originalFaceUp,
        bool originalRezzed,
        int originalIndex = -1)
    {
        CardId = cardId;
        OriginalZoneId = originalZoneId;
        OriginalFaceUp = originalFaceUp;
        OriginalRezzed = originalRezzed;
        OriginalIndex = originalIndex;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId OriginalZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool OriginalFaceUp { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool OriginalRezzed { get; private set; }

    /// <summary>
    /// Original ordering position in the source zone.  A skipped search is
    /// restored at this position; a selected card that is moved elsewhere
    /// never needs the value, but persisting it keeps a loaded frame
    /// deterministic.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public int OriginalIndex { get; private set; }
}

/// <summary>How a behavior instruction was expanded into its continuation.</summary>
public enum BehaviorContinuationKind
{
    ChoiceSelection,
    ChoiceOption,
    BranchTrue,
    BranchFalse,
}

internal static class BehaviorContinuation
{
    public static bool TryApply(
        IList<BehaviorInstruction> instructions,
        BehaviorContinuationStep step,
        out string? error)
    {
        error = null;
        if (step.InstructionCursor < 0 || step.InstructionCursor >= instructions.Count)
        {
            error = "The continuation cursor does not identify an instruction.";
            return false;
        }

        BehaviorInstruction instruction = instructions[step.InstructionCursor];
        IReadOnlyList<BehaviorInstruction>? replacement = step.Kind switch
        {
            BehaviorContinuationKind.ChoiceSelection when instruction.Kind is BehaviorInstructionKind.Choose or BehaviorInstructionKind.Search
                => [],
            BehaviorContinuationKind.ChoiceOption when instruction.Kind == BehaviorInstructionKind.Choose
                && instruction.Choice is not null
                && string.Equals(instruction.Choice.Key, step.Key, StringComparison.Ordinal)
                => instruction.Choice.Options
                    .SingleOrDefault(option => string.Equals(option.Key, step.OptionKey, StringComparison.Ordinal))?.Instructions,
            BehaviorContinuationKind.BranchTrue when instruction.Kind == BehaviorInstructionKind.Branch
                && instruction.Branch is not null
                && string.Equals(BranchKey(instruction, step.InstructionCursor), step.Key, StringComparison.Ordinal)
                => instruction.Branch.WhenTrue,
            BehaviorContinuationKind.BranchFalse when instruction.Kind == BehaviorInstructionKind.Branch
                && instruction.Branch is not null
                && string.Equals(BranchKey(instruction, step.InstructionCursor), step.Key, StringComparison.Ordinal)
                => instruction.Branch.WhenFalse,
            _ => null,
        };

        if (replacement is null)
        {
            error = "The continuation step does not match the catalog instruction at its cursor.";
            return false;
        }

        if (step.Kind == BehaviorContinuationKind.ChoiceSelection
            && ((instruction.Kind == BehaviorInstructionKind.Choose
                    && !string.Equals(instruction.Choice?.Key, step.Key, StringComparison.Ordinal))
                || (instruction.Kind == BehaviorInstructionKind.Search
                    && !string.Equals(SearchKey(instruction, step.InstructionCursor), step.Key, StringComparison.Ordinal))))
        {
            error = "The choice continuation key does not match the catalog declaration.";
            return false;
        }

        instructions.RemoveAt(step.InstructionCursor);
        for (int index = replacement.Count - 1; index >= 0; index--)
        {
            instructions.Insert(step.InstructionCursor, replacement[index]);
        }

        return true;
    }

    public static string SearchKey(BehaviorInstruction instruction, int cursor) =>
        StableIdentifier.TryValidate(instruction.Argument)
            ? instruction.Argument!
            : $"search-{cursor}";

    public static string BranchKey(BehaviorInstruction instruction, int cursor) =>
        instruction.Branch?.Key
        ?? $"branch-{cursor}";
}

/// <summary>
/// Small, data-only queries over the catalog-derived behavior program.  The
/// continuation is replayed before inspecting the cursor so lifecycle
/// validation never trusts an in-memory instruction projection.
/// </summary>
internal static class BehaviorInstructionPath
{
    public static bool CanReach(
        IReadOnlyList<BehaviorInstruction> instructions,
        BehaviorInstructionKind kind)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Kind == kind
                || instruction.Branch is { } branch
                    && (CanReach(branch.WhenTrue, kind) || CanReach(branch.WhenFalse, kind))
                || instruction.Choice is { } choice
                    && choice.Options.Any(option => CanReach(option.Instructions, kind)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasReached(
        IReadOnlyList<BehaviorInstruction> root,
        IReadOnlyList<BehaviorContinuationStep> continuation,
        int instructionCursor,
        BehaviorInstructionKind kind) =>
        HasReached(root, continuation, instructionCursor, instruction => instruction.Kind == kind);

    public static bool HasReached(
        IReadOnlyList<BehaviorInstruction> root,
        IReadOnlyList<BehaviorContinuationStep> continuation,
        int instructionCursor,
        Func<BehaviorInstruction, bool> predicate)
    {
        if (instructionCursor <= 0)
        {
            return false;
        }

        List<BehaviorInstruction> instructions = root.ToList();
        foreach (BehaviorContinuationStep step in continuation)
        {
            if (!BehaviorContinuation.TryApply(instructions, step, out _))
            {
                return false;
            }
        }

        int count = Math.Min(instructionCursor, instructions.Count);
        return instructions
            .Take(count)
            .Any(predicate);
    }
}

/// <summary>
/// Stable identity for the exact parent frame captured by a child behavior.
/// It is derived from the serialized data-only frame, so changing a parent
/// cursor, owner, or context invalidates a resumed child before mutation.
/// </summary>
internal static class BehaviorFrameIdentity
{
    public static bool TryValidateIssued(string? value, out ulong sequence)
    {
        sequence = 0;
        const string prefix = "behavior-frame:";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = value.AsSpan(prefix.Length);
        if (digits.IsEmpty)
        {
            return false;
        }

        foreach (char digit in digits)
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }
        }

        return ulong.TryParse(digits, out sequence) && sequence > 0;
    }

    public static string Compute(ResolutionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        string serialized = EngineJson.Serialize(frame);
        return "parent:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
    }
}

/// <summary>
/// Immutable expansion history for the executable instruction cursor.  The
/// engine replays these steps from the catalog root before executing it. A
/// caller cannot replace the current list with arbitrary catalog-vocabulary
/// effects without also producing a reachable continuation.
/// </summary>
public sealed record BehaviorContinuationStep
{
    [JsonConstructor]
    public BehaviorContinuationStep(
        int instructionCursor,
        string key,
        BehaviorContinuationKind kind,
        string? optionKey = null,
        IReadOnlyList<CardInstanceId>? cardIds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instructionCursor);

        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException("A behavior continuation key must be stable.", nameof(key));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (optionKey is not null && !StableIdentifier.TryValidate(optionKey))
        {
            throw new ArgumentException("A behavior continuation option key must be stable.", nameof(optionKey));
        }

        if (kind != BehaviorContinuationKind.ChoiceOption && optionKey is not null)
        {
            throw new ArgumentException(
                "Only a choice-option continuation may carry an option key.",
                nameof(optionKey));
        }

        if (kind == BehaviorContinuationKind.ChoiceOption && optionKey is null)
        {
            throw new ArgumentNullException(
                nameof(optionKey),
                "A choice-option continuation requires the selected option key.");
        }

        CardInstanceId[] selectedCards = (cardIds ?? []).ToArray();
        if (kind != BehaviorContinuationKind.ChoiceSelection && selectedCards.Length > 0)
        {
            throw new ArgumentException(
                "Only a card-selection continuation may carry selected card IDs.",
                nameof(cardIds));
        }

        if (selectedCards.Distinct().Count() != selectedCards.Length)
        {
            throw new ArgumentException(
                "A behavior continuation selection must contain distinct card IDs.",
                nameof(cardIds));
        }

        InstructionCursor = instructionCursor;
        Key = key;
        Kind = kind;
        OptionKey = optionKey;
        CardIds = new ReadOnlyCollection<CardInstanceId>(selectedCards);
    }

    [JsonInclude]
    [JsonRequired]
    public int InstructionCursor { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorContinuationKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string? OptionKey { get; private set; }

    /// <summary>
    /// The exact frozen card selection that caused a choice continuation.
    /// Branch and option continuations leave this empty.  Keeping the IDs on
    /// the path lets validation tie every binding back to a reachable catalog
    /// choice instead of trusting an independently forgeable binding list.
    /// </summary>
    [JsonInclude]
    public IReadOnlyList<CardInstanceId> CardIds { get; private set; }
}

/// <summary>
/// Frozen materialization of a pending declarative choice.  Dynamic hidden
/// candidates are captured here, not recomputed from a loaded state.
/// </summary>
public sealed record BehaviorChoiceMaterialization
{
    [JsonConstructor]
    public BehaviorChoiceMaterialization(
        string key,
        PlayerId owner,
        BehaviorChoiceMode mode,
        BehaviorChoiceCardinalityKind cardinalityKind,
        int? cardinalityCount,
        IReadOnlyList<string>? optionKeys = null,
        IReadOnlyList<CardInstanceId>? candidateCardIds = null,
        IReadOnlyList<CardInstanceId>? selectedCardIds = null)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException("A behavior choice key must be stable.", nameof(key));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        _ = new BehaviorChoiceCardinality(cardinalityKind, cardinalityCount);
        Key = key;
        Owner = owner;
        Mode = mode;
        CardinalityKind = cardinalityKind;
        CardinalityCount = cardinalityCount;
        OptionKeys = new ReadOnlyCollection<string>((optionKeys ?? []).ToArray());
        CandidateCardIds = new ReadOnlyCollection<CardInstanceId>((candidateCardIds ?? []).ToArray());
        SelectedCardIds = new ReadOnlyCollection<CardInstanceId>((selectedCardIds ?? []).ToArray());
        if (SelectedCardIds.Any(cardId => !CandidateCardIds.Contains(cardId))
            || SelectedCardIds.Distinct().Count() != SelectedCardIds.Count)
        {
            throw new ArgumentException(
                "A behavior choice's selected cards must be distinct frozen candidates.",
                nameof(selectedCardIds));
        }
    }

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorChoiceMode Mode { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorChoiceCardinalityKind CardinalityKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int? CardinalityCount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<string> OptionKeys { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CandidateCardIds { get; private set; }

    /// <summary>
    /// Partial selection used by bounded any-number/up-to materialization.
    /// Ordinary choices leave this empty and submit a complete set in one
    /// action; large candidate sets select incrementally and finalize with a
    /// single bounded payload.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> SelectedCardIds { get; private set; }
}

public enum BehaviorResolutionStage
{
    PayCosts,
    Execute,
    AwaitingOccurrence,
}

/// <summary>
/// Engine-issued continuation context for the occurrence raised by a behavior
/// instruction. The context remains on the parent cursor while the nested
/// occurrence effect resolves, and is retained only as a narrow authorization
/// when that child moved the same source card to its owner's discard.
/// </summary>
public sealed record BehaviorOccurrenceContinuation
{
    [JsonConstructor]
    public BehaviorOccurrenceContinuation(
        OccurrenceKind kind,
        CardInstanceId sourceCardId,
        string frameIdentity,
        int instructionCursor,
        bool sourceMovedAfterCompletion = false)
    {
        if (kind != OccurrenceKind.HostedCreditPoolEmptied)
        {
            throw new ArgumentException(
                "Behavior occurrence continuations currently support only hosted-credit pool exhaustion.",
                nameof(kind));
        }

        if (!BehaviorFrameIdentity.TryValidateIssued(frameIdentity, out _))
        {
            throw new ArgumentException(
                "A behavior occurrence continuation requires a stable frame identity.",
                nameof(frameIdentity));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(instructionCursor);

        Kind = kind;
        SourceCardId = sourceCardId;
        FrameIdentity = frameIdentity;
        InstructionCursor = instructionCursor;
        SourceMovedAfterCompletion = sourceMovedAfterCompletion;
    }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string FrameIdentity { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int InstructionCursor { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool SourceMovedAfterCompletion { get; private set; }
}

/// <summary>
/// Serializable cursor for a generic card behavior.  The root program and
/// current instruction sequence are intentionally not serialized. The
/// catalog fingerprint and continuation path reconstruct the executable
/// projection across save/load and keep branch/choice provenance independent
/// of mutable catalog objects.
/// </summary>
public sealed record BehaviorResolutionFrame : ResolutionFrame
{
    /// <summary>
    /// JSON constructor for a persisted behavior cursor. The compiled
    /// instruction tree is deliberately not a serialized field: it is
    /// reconstructed from the injected catalog and the continuation path.
    /// </summary>
    [JsonConstructor]
    public BehaviorResolutionFrame(
        int version,
        string behaviorId,
        CardDefinitionId definitionId,
        CardInstanceId sourceCardId,
        PlayerId controller,
        PlayerId? actingPlayer,
        string catalogFingerprint,
        string definitionFingerprint,
        BehaviorResolutionStage stage,
        int instructionCursor = 0,
        IReadOnlyList<BehaviorBinding>? bindings = null,
        IReadOnlyList<BehaviorSetAsideCard>? setAside = null,
        BehaviorChoiceMaterialization? choice = null,
        IReadOnlyList<BehaviorContinuationStep>? continuation = null,
        ZoneId? setAsideZoneId = null,
        string? parentBehaviorId = null,
        string? frameIdentity = null,
        ZoneId? sourceZoneId = null,
        ZoneKind? sourceZoneKind = null,
        string? parentFrameIdentity = null,
        string? abilityKey = null,
        string? triggerKey = null,
        bool catalogTriggerSourceMovedBeforeStart = false,
        BehaviorOccurrenceContinuation? occurrenceContinuation = null,
        IReadOnlyList<BehaviorRecordedAmount>? recordedAmounts = null,
        bool catalogTriggerSourceMovedAfterStart = false)
        : base(version, behaviorId)
    {
        if (abilityKey is not null && triggerKey is not null)
        {
            throw new ArgumentException("A behavior frame cannot carry both an ability key and a trigger key.");
        }

        if (abilityKey is not null && !StableIdentifier.TryValidate(abilityKey))
        {
            throw new ArgumentException("An ability key must be a stable identifier.", nameof(abilityKey));
        }

        if (triggerKey is not null && !StableIdentifier.TryValidate(triggerKey))
        {
            throw new ArgumentException("A trigger key must be a stable identifier.", nameof(triggerKey));
        }

        if (!StableIdentifier.TryValidate(catalogFingerprint))
        {
            throw new ArgumentException("A behavior frame requires a catalog fingerprint.", nameof(catalogFingerprint));
        }

        if (!StableIdentifier.TryValidate(definitionFingerprint))
        {
            throw new ArgumentException("A behavior frame requires a definition fingerprint.", nameof(definitionFingerprint));
        }

        DefinitionId = definitionId;
        SourceCardId = sourceCardId;
        Controller = controller;
        ActingPlayer = actingPlayer;
        CatalogFingerprint = catalogFingerprint;
        DefinitionFingerprint = definitionFingerprint;
        // These compatibility projections are populated only by the
        // engine-owned constructor/With seam. They are ignored by JSON and
        // never participate in validation of a restored frame.
        RootInstructions = [];
        Instructions = [];
        Stage = stage;
        InstructionCursor = instructionCursor;
        Bindings = Freeze(bindings);
        SetAside = Freeze(setAside);
        Choice = choice;
        Continuation = Freeze(continuation);
        SetAsideZoneId = setAsideZoneId;
        ParentBehaviorId = parentBehaviorId;
        FrameIdentity = frameIdentity;
        SourceZoneId = sourceZoneId;
        SourceZoneKind = sourceZoneKind;
        ParentFrameIdentity = parentFrameIdentity;
        AbilityKey = abilityKey;
        TriggerKey = triggerKey;
        CatalogTriggerSourceMovedBeforeStart = catalogTriggerSourceMovedBeforeStart;
        CatalogTriggerSourceMovedAfterStart = catalogTriggerSourceMovedAfterStart;
        OccurrenceContinuation = occurrenceContinuation;
        BehaviorRecordedAmount[] recorded = (recordedAmounts ?? []).ToArray();
        if (recorded.Any(static value => value is null)
            || recorded.Select(static value => value.Key).Distinct(StringComparer.Ordinal).Count() != recorded.Length)
        {
            throw new ArgumentException("Recorded behavior amounts must be distinct by key.", nameof(recordedAmounts));
        }

        RecordedAmounts = recorded.Length == 0 ? null : new ReadOnlyCollection<BehaviorRecordedAmount>(recorded);
    }

    /// <summary>
    /// EL-AMOUNT: amounts earlier instructions recorded (credits lost, tags
    /// removed), read by later recorded amounts. Omitted while empty.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BehaviorRecordedAmount>? RecordedAmounts { get; private set; }

    /// <summary>The recorded amount for a key, or 0.</summary>
    public int Recorded(string key) =>
        RecordedAmounts?.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal))?.Value ?? 0;

    public BehaviorResolutionFrame(
        CardBehaviorDefinition definition,
        BehaviorInvocationContext invocation,
        string catalogFingerprint,
        string definitionFingerprint,
        string? parentBehaviorId = null)
        : this(
            1,
            ResolutionBehaviorIds.CardBehavior,
            definition.DefinitionId,
            invocation.SourceCardId,
            invocation.Controller,
            invocation.ActingPlayer,
            catalogFingerprint,
            definitionFingerprint,
            BehaviorResolutionStage.PayCosts,
        continuation: null,
        setAsideZoneId: null,
        parentBehaviorId: parentBehaviorId,
        frameIdentity: null,
        sourceZoneId: null,
            sourceZoneKind: null,
            parentFrameIdentity: null)
    {
        RootInstructions = Freeze(definition.Instructions);
        Instructions = Freeze(definition.Instructions);
    }

    internal BehaviorResolutionFrame(
        CardBehaviorDefinition definition,
        BehaviorInvocationContext invocation,
        string catalogFingerprint,
        string definitionFingerprint,
        string frameIdentity,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind,
        string? parentBehaviorId,
        string? parentFrameIdentity,
        string? abilityKey = null,
        string? triggerKey = null,
        IReadOnlyList<BehaviorInstruction>? rootInstructions = null,
        IReadOnlyList<BehaviorInstruction>? instructions = null,
        bool catalogTriggerSourceMovedBeforeStart = false,
        BehaviorOccurrenceContinuation? occurrenceContinuation = null)
        : this(
            1,
            ResolutionBehaviorIds.CardBehavior,
            definition.DefinitionId,
            invocation.SourceCardId,
            invocation.Controller,
            invocation.ActingPlayer,
            catalogFingerprint,
            definitionFingerprint,
            BehaviorResolutionStage.PayCosts,
            continuation: null,
            setAsideZoneId: null,
            parentBehaviorId: parentBehaviorId,
            frameIdentity: frameIdentity,
            sourceZoneId: sourceZoneId,
            sourceZoneKind: sourceZoneKind,
            parentFrameIdentity: parentFrameIdentity,
            abilityKey: abilityKey,
            triggerKey: triggerKey,
            catalogTriggerSourceMovedBeforeStart: catalogTriggerSourceMovedBeforeStart,
            occurrenceContinuation: occurrenceContinuation)
    {
        RootInstructions = Freeze(rootInstructions ?? definition.Instructions);
        Instructions = Freeze(instructions ?? rootInstructions ?? definition.Instructions);
    }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId? ActingPlayer { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CatalogFingerprint { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string DefinitionFingerprint { get; private set; }

    [JsonIgnore]
    public IReadOnlyList<BehaviorInstruction> RootInstructions { get; private set; }

    [JsonIgnore]
    public IReadOnlyList<BehaviorInstruction> Instructions { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorResolutionStage Stage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int InstructionCursor { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorBinding> Bindings { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorSetAsideCard> SetAside { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public BehaviorChoiceMaterialization? Choice { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<BehaviorContinuationStep> Continuation { get; private set; }

    /// <summary>The physical transient zone holding selected hidden cards.</summary>
    [JsonInclude]
    [JsonRequired]
    public ZoneId? SetAsideZoneId { get; private set; }

    /// <summary>
    /// Parent frame identity captured when a behavior is nested.  A root
    /// behavior has no parent; a loaded child must remain immediately above
    /// the same parent while it is awaiting a decision.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public string? ParentBehaviorId { get; private set; }

    /// <summary>
    /// Engine-issued identity for this frame. It is distinct from the
    /// behavior registry ID, which is shared by every generic behavior.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public string? FrameIdentity { get; private set; }

    /// <summary>The exact source location captured at invocation.</summary>
    [JsonInclude]
    [JsonRequired]
    public ZoneId? SourceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneKind? SourceZoneKind { get; private set; }

    /// <summary>
    /// Identity of the exact parent frame captured by the child-push seam.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public string? ParentFrameIdentity { get; private set; }

    /// <summary>
    /// When present, this behavior cursor is the execution child of an
    /// AbilityFrame. The declaration is selected by key from the injected
    /// catalog; a null value preserves the pre-4.3 generic behavior shape.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AbilityKey { get; private set; }

    /// <summary>
    /// When present, this behavior cursor executes the immutable trigger
    /// declaration selected by key from the injected catalog. It is mutually
    /// exclusive with <see cref="AbilityKey"/>.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerKey { get; private set; }

    /// <summary>
    /// Engine-issued authorization for a catalog trigger whose source moved
    /// after materialization but before its child was started. It remains
    /// valid for the entire detached trigger child, including cursor
    /// progress, yielded decisions, and save/load resumes.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CatalogTriggerSourceMovedBeforeStart { get; private set; }

    /// <summary>
    /// Engine-issued authorization for a catalog trigger that began resolving
    /// and whose source another effect then moved, outside its owner's trash
    /// or removed-from-game zone (EL.9: Byte!, accessed on top of R&amp;D, is
    /// drawn by NBN: Reality Plus before its net damage). The trigger
    /// finishes, as one whose source moved before it started does.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CatalogTriggerSourceMovedAfterStart { get; private set; }

    /// <summary>
    /// Typed parent continuation for a hosted-credit exhaustion occurrence.
    /// While the frame is AwaitingOccurrence this identifies the exact nested
    /// occurrence effect; after completion it is retained only when that
    /// occurrence moved the parent source to its owner's discard.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BehaviorOccurrenceContinuation? OccurrenceContinuation { get; private set; }

    internal BehaviorResolutionFrame With(
        BehaviorResolutionStage? stage = null,
        IReadOnlyList<BehaviorInstruction>? instructions = null,
        int? instructionCursor = null,
        IReadOnlyList<BehaviorBinding>? bindings = null,
        IReadOnlyList<BehaviorSetAsideCard>? setAside = null,
        BehaviorChoiceMaterialization? choice = null,
        bool clearChoice = false,
        IReadOnlyList<BehaviorContinuationStep>? continuation = null,
        ZoneId? setAsideZoneId = null,
        bool clearSetAsideZone = false,
        string? abilityKey = null,
        string? triggerKey = null,
        bool? catalogTriggerSourceMovedBeforeStart = null,
        BehaviorOccurrenceContinuation? occurrenceContinuation = null,
        bool clearOccurrenceContinuation = false,
        IReadOnlyList<BehaviorRecordedAmount>? recordedAmounts = null,
        bool? catalogTriggerSourceMovedAfterStart = null) =>
        new(
            Version,
            BehaviorId,
            DefinitionId,
            SourceCardId,
            Controller,
            ActingPlayer,
            CatalogFingerprint,
            DefinitionFingerprint,
            stage: stage ?? Stage,
            instructionCursor: instructionCursor ?? InstructionCursor,
            bindings: bindings ?? Bindings,
            setAside: setAside ?? SetAside,
            choice: clearChoice ? null : choice ?? Choice,
            continuation: continuation ?? Continuation,
            setAsideZoneId: clearSetAsideZone ? null : setAsideZoneId ?? SetAsideZoneId,
            parentBehaviorId: ParentBehaviorId,
            frameIdentity: FrameIdentity,
            sourceZoneId: SourceZoneId,
            sourceZoneKind: SourceZoneKind,
            parentFrameIdentity: ParentFrameIdentity,
            abilityKey: AbilityKey,
            triggerKey: TriggerKey,
            catalogTriggerSourceMovedBeforeStart: catalogTriggerSourceMovedBeforeStart ?? CatalogTriggerSourceMovedBeforeStart,
            occurrenceContinuation: clearOccurrenceContinuation
                ? null
                : occurrenceContinuation ?? OccurrenceContinuation,
            recordedAmounts: recordedAmounts ?? RecordedAmounts,
            catalogTriggerSourceMovedAfterStart: catalogTriggerSourceMovedAfterStart ?? CatalogTriggerSourceMovedAfterStart)
        {
            RootInstructions = Freeze(RootInstructions),
            Instructions = Freeze(instructions ?? Instructions),
        };

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T>? values)
        where T : class =>
        new((values ?? []).Select(value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value;
        }).ToArray());
}

/// <summary>An amount one behavior instruction recorded for later instructions.</summary>
public sealed record BehaviorRecordedAmount
{
    [JsonConstructor]
    public BehaviorRecordedAmount(string key, int value)
    {
        if (!StableIdentifier.TryValidate(key))
        {
            throw new ArgumentException("A recorded amount key must be stable.", nameof(key));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Key = key;
        Value = value;
    }

    [JsonInclude]
    [JsonRequired]
    public string Key { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Value { get; private set; }
}
