using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>
/// Explicit engine occurrence keys used by the checkpoint registry.  These
/// values are part of the save/replay contract; card text is never inspected
/// to infer an occurrence.
/// </summary>
public enum OccurrenceKind
{
    EffectImminent,
    EffectResolved,
    TurnBegin,
    TurnEnd,
    CreditAdjustment,
    ClickAdjustment,
    Damage,
    Tags,
    Memory,
    CardMove,
    CounterAdjustment,
    LingeringEffectAdded,
    LingeringEffectRemoved,
    CardInstalled,
    CardRezzed,
    RunSuccessful,
    RunEnded,
    AgendaScoredOrStolen,
    CardAccessed,
    HostedCreditPoolEmptied,
    BasicDraw,
    ServerApproached,
    IceEncountered,
    CardAdvancedFromZero,
    AccessedCardTrashed,
    DiscardPhaseEnded,

    /// <summary>
    /// Never dispatched: a trigger declared with this occurrence is a keyed
    /// instruction list resolved as an ICE subroutine
    /// (<see cref="BehaviorSubroutineSpec"/>), with the ICE as its source.
    /// </summary>
    IceSubroutine,

    /// <summary>EL-ACTION-PHASE-END: the active player's action phase ended.</summary>
    ActionPhaseEnded,

    /// <summary>A run began (after its initiation), on the attacked server.</summary>
    RunBegan,

    /// <summary>EL-FORFEIT: an agenda was forfeited; the subject is the agenda.</summary>
    AgendaForfeited,

    /// <summary>EL-TAG-REMOVED: the first time this turn one of the Runner's tags was removed.</summary>
    FirstTagRemovalThisTurn,

    /// <summary>
    /// The first time this turn the Corp gained credits through an ability on
    /// an agenda or operation; the subject is that card.
    /// </summary>
    FirstCardCreditGainThisTurn,

    /// <summary>The first time this turn the Corp played an operation; the subject is the operation.</summary>
    FirstOperationPlayedThisTurn,

    /// <summary>A side did damage (the controller is the side whose effect did it).</summary>
    DamageDone,

    /// <summary>A side trashed one or more cards from HQ (the controller is the side that trashed them).</summary>
    CardsTrashedFromHq,

    /// <summary>EL-HAND-SIZE-DISCARD: the active player discarded cards to reach maximum hand size.</summary>
    HandSizeDiscarded,

    /// <summary>EL-ICE-APPROACHED: the Runner approaches a piece of ICE (Mitra Aman); the subject is that ICE.</summary>
    IceApproached,

    /// <summary>VP-BAD-PUBLICITY: the first time this turn the Corp took bad publicity (Editorial Division).</summary>
    FirstBadPublicityThisTurn,
}

public enum OccurrencePhase
{
    Imminent,
    Resolved,
}

public enum TriggerMode
{
    Mandatory,
    Optional,
}

public enum InterruptKind
{
    Prevention,
    Replacement,
}

/// <summary>Cursor for the serializable imminent-effect checkpoint.</summary>
public enum EffectCheckpointStage
{
    InterruptChoice,
    Resolve,
    TriggerOrdering,
    TriggerChoice,
    AwaitingChild,
}

public enum EffectCheckpointChildKind
{
    None,
    Trigger,
    Replacement,
}

/// <summary>
/// A complete, detached description of what caused an occurrence.  The
/// source zone and definition are snapshots, not live references, so moving
/// the source card while a decision is pending cannot retarget the effect.
/// </summary>
public sealed record OccurrenceSnapshot
{
    [JsonConstructor]
    public OccurrenceSnapshot(
        int version,
        OccurrenceId id,
        OccurrenceKind kind,
        OccurrencePhase phase,
        PlayerId controller,
        PlayerId? targetPlayer,
        CardInstanceId? sourceCardId,
        CardDefinitionId? sourceDefinitionId,
        ZoneId? sourceZoneId,
        EffectInstruction effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        Version = version;
        Id = id;
        Kind = kind;
        Phase = phase;
        Controller = controller;
        TargetPlayer = targetPlayer;
        SourceCardId = sourceCardId;
        SourceDefinitionId = sourceDefinitionId;
        SourceZoneId = sourceZoneId;
        Effect = CloneEffect(effect);
    }

    public OccurrenceSnapshot(
        OccurrenceId id,
        OccurrenceKind kind,
        PlayerId controller,
        PlayerId? targetPlayer,
        CardInstanceId? sourceCardId,
        CardDefinitionId? sourceDefinitionId,
        ZoneId? sourceZoneId,
        EffectInstruction effect)
        : this(1, id, kind, OccurrencePhase.Imminent, controller, targetPlayer, sourceCardId, sourceDefinitionId, sourceZoneId, effect)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrencePhase Phase { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId? TargetPlayer { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId? SourceDefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId? SourceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectInstruction Effect { get; private set; }

    internal OccurrenceSnapshot Copy() => new(
        Version,
        Id,
        Kind,
        Phase,
        Controller,
        TargetPlayer,
        SourceCardId,
        SourceDefinitionId,
        SourceZoneId,
        Effect);

    internal static EffectInstruction CloneEffect(EffectInstruction effect) => effect switch
    {
        CreditAdjustmentEffect value => new CreditAdjustmentEffect(value.Target, value.Delta),
        ClickAdjustmentEffect value => new ClickAdjustmentEffect(value.Target, value.Delta),
        DamageEffect value => new DamageEffect(value.Target, value.Kind, value.Amount),
        TagAdjustmentEffect value => new TagAdjustmentEffect(value.Target, value.Delta),
        MemoryAdjustmentEffect value => new MemoryAdjustmentEffect(value.Target, value.BaseDelta),
        RegisterProgramMemoryEffect value => new RegisterProgramMemoryEffect(value.CardId, value.Owner, value.MemoryCost),
        RemoveProgramMemoryEffect value => new RemoveProgramMemoryEffect(value.CardId),
        CardCounterAdjustmentEffect value => new CardCounterAdjustmentEffect(value.CardId, value.CounterName, value.Delta),
        VirusPurgeEffect => new VirusPurgeEffect(),
        AddLingeringEffect value => new AddLingeringEffect(value.Effect),
        RemoveLingeringEffect value => new RemoveLingeringEffect(value.EffectId),
        MoveCardEffect value => new MoveCardEffect(value.CardId, value.DestinationZoneId),
        TurnBoundaryEffect value => new TurnBoundaryEffect(value.Kind, value.Target, value.TurnNumber),
        GameplayOccurrenceEffect value => new GameplayOccurrenceEffect(value.Kind, value.Controller, value.SubjectCardId, value.ServerId, value.Successful, value.RunContext, value.SubjectWasRezzed),
        BasicDrawEffect value => new BasicDrawEffect(value.Side, value.DrawCount, value.IsReplacement, value.OccurrenceId, value.IsFirstBasicDraw),
        _ => throw new ArgumentException($"Unknown effect instruction type '{effect.GetType().Name}'.", nameof(effect)),
    };
}

/// <summary>
/// A typed predicate for a registered trigger or interrupt.  Every field is
/// data, and matching is performed by the Core registry; no delegate or text
/// parser is persisted in a save.
/// </summary>
public sealed record TriggerCondition
{
    [JsonConstructor]
    public TriggerCondition(
        int version,
        OccurrenceKind occurrenceKind,
        OccurrencePhase? phase,
        DamageKind? damageKind,
        int? minimumAmount,
        PlayerSide? targetSide,
        CardInstanceId? sourceCardId)
    {
        Version = version;
        OccurrenceKind = occurrenceKind;
        Phase = phase;
        DamageKind = damageKind;
        MinimumAmount = minimumAmount;
        TargetSide = targetSide;
        SourceCardId = sourceCardId;
    }

    public TriggerCondition(OccurrenceKind occurrenceKind)
        : this(1, occurrenceKind, null, null, null, null, null)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceKind OccurrenceKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrencePhase? Phase { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public DamageKind? DamageKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int? MinimumAmount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide? TargetSide { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    internal TriggerCondition Copy() => new(
        Version,
        OccurrenceKind,
        Phase,
        DamageKind,
        MinimumAmount,
        TargetSide,
        SourceCardId);

    internal bool Matches(OccurrenceSnapshot occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (Version != 1 || occurrence.Version != 1)
        {
            return false;
        }

        bool kindMatches = OccurrenceKind == occurrence.Kind
            || IsEffectOccurrenceKind(occurrence.Kind)
                && ((OccurrenceKind == Netrunner.Core.OccurrenceKind.EffectImminent
                        && occurrence.Phase == OccurrencePhase.Imminent)
                    || (OccurrenceKind == Netrunner.Core.OccurrenceKind.EffectResolved
                        && occurrence.Phase == OccurrencePhase.Resolved));
        if (!kindMatches)
        {
            return false;
        }

        if (Phase.HasValue && Phase.Value != occurrence.Phase)
        {
            return false;
        }

        if (DamageKind.HasValue
            && (occurrence.Effect is not DamageEffect typedDamage
                || typedDamage.Kind != DamageKind.Value))
        {
            return false;
        }

        if (MinimumAmount.HasValue
            && (!TryGetAmount(occurrence.Effect, out int amount) || amount < MinimumAmount.Value))
        {
            return false;
        }

        if (TargetSide.HasValue)
        {
            if (!occurrence.TargetPlayer.HasValue)
            {
                return false;
            }

            // Side resolution belongs to the state-aware registry. This
            // predicate only records the typed requested side.
        }

        return !SourceCardId.HasValue || SourceCardId == occurrence.SourceCardId;

        static bool IsEffectOccurrenceKind(OccurrenceKind kind) => kind is
            OccurrenceKind.EffectImminent
            or OccurrenceKind.EffectResolved
            or OccurrenceKind.CreditAdjustment
            or OccurrenceKind.ClickAdjustment
            or OccurrenceKind.Damage
            or OccurrenceKind.Tags
            or OccurrenceKind.Memory
            or OccurrenceKind.CardMove
            or OccurrenceKind.CounterAdjustment
            or OccurrenceKind.LingeringEffectAdded
            or OccurrenceKind.LingeringEffectRemoved
            or OccurrenceKind.BasicDraw;

        static bool TryGetAmount(EffectInstruction effect, out int amount)
        {
            switch (effect)
            {
                case DamageEffect damage:
                    amount = damage.Amount;
                    return true;
                case CreditAdjustmentEffect credits:
                    amount = Math.Abs(credits.Delta);
                    return true;
                case ClickAdjustmentEffect clicks:
                    amount = Math.Abs(clicks.Delta);
                    return true;
                case TagAdjustmentEffect tags:
                    amount = Math.Abs(tags.Delta);
                    return true;
                case MemoryAdjustmentEffect memory:
                    amount = Math.Abs(memory.BaseDelta);
                    return true;
                default:
                    amount = default;
                    return false;
            }
        }
    }
}

/// <summary>State-owned registration for a conditional triggered ability.</summary>
public sealed record TriggerRegistration
{
    [JsonConstructor]
    public TriggerRegistration(
        int version,
        TriggerId id,
        PlayerId controller,
        TriggerMode mode,
        TriggerCondition condition,
        string behaviorId,
        CardInstanceId? sourceCardId,
        CostBundle costs,
        EffectBundle effects)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(behaviorId);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(effects);
        Version = version;
        Id = id;
        Controller = controller;
        Mode = mode;
        Condition = condition.Copy();
        BehaviorId = behaviorId;
        SourceCardId = sourceCardId;
        Costs = costs.Copy();
        Effects = effects.Copy();
    }

    public TriggerRegistration(
        TriggerId id,
        PlayerId controller,
        TriggerMode mode,
        TriggerCondition condition,
        string behaviorId,
        CardInstanceId? sourceCardId,
        CostBundle costs,
        EffectBundle effects)
        : this(1, id, controller, mode, condition, behaviorId, sourceCardId, costs, effects)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerMode Mode { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerCondition Condition { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CostBundle Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectBundle Effects { get; private set; }

    internal TriggerRegistration Copy() => new(
        Version,
        Id,
        Controller,
        Mode,
        Condition,
        BehaviorId,
        SourceCardId,
        Costs,
        Effects);
}

/// <summary>State-owned registration for one prevention or replacement.</summary>
public sealed record InterruptRegistration
{
    [JsonConstructor]
    public InterruptRegistration(
        int version,
        InterruptId id,
        PlayerId controller,
        InterruptKind kind,
        bool mandatory,
        TriggerCondition condition,
        string behaviorId,
        CardInstanceId? sourceCardId,
        CostBundle costs,
        int maximumPreventionAmount,
        EffectBundle replacementEffects)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(behaviorId);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(replacementEffects);
        Version = version;
        Id = id;
        Controller = controller;
        Kind = kind;
        Mandatory = mandatory;
        Condition = condition.Copy();
        BehaviorId = behaviorId;
        SourceCardId = sourceCardId;
        Costs = costs.Copy();
        MaximumPreventionAmount = maximumPreventionAmount;
        ReplacementEffects = replacementEffects.Copy();
    }

    public InterruptRegistration(
        InterruptId id,
        PlayerId controller,
        InterruptKind kind,
        bool mandatory,
        TriggerCondition condition,
        string behaviorId,
        CardInstanceId? sourceCardId,
        CostBundle costs,
        int maximumPreventionAmount,
        EffectBundle replacementEffects)
        : this(1, id, controller, kind, mandatory, condition, behaviorId, sourceCardId, costs, maximumPreventionAmount, replacementEffects)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InterruptId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InterruptKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Mandatory { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerCondition Condition { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CostBundle Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int MaximumPreventionAmount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectBundle ReplacementEffects { get; private set; }

    internal InterruptRegistration Copy() => new(
        Version,
        Id,
        Controller,
        Kind,
        Mandatory,
        Condition,
        BehaviorId,
        SourceCardId,
        Costs,
        MaximumPreventionAmount,
        ReplacementEffects);
}

/// <summary>Detached trigger invocation materialized at an occurrence.</summary>
public sealed record TriggerInvocation
{
    [JsonConstructor]
    public TriggerInvocation(
        int version,
        TriggerId id,
        PlayerId controller,
        TriggerMode mode,
        string behaviorId,
        CardInstanceId? sourceCardId,
        CardDefinitionId? sourceDefinitionId,
        ZoneId? sourceZoneId,
        OccurrenceSnapshot occurrence,
        CostBundle costs,
        EffectBundle effects,
        string? triggerKey = null,
        string? catalogFingerprint = null,
        string? definitionFingerprint = null,
        PlayerSide? sourceOwnerSide = null,
        ZoneKind? sourceZoneKind = null)
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(effects);
        bool catalogFieldPresent = triggerKey is not null
            || catalogFingerprint is not null
            || definitionFingerprint is not null
            || sourceOwnerSide is not null
            || sourceZoneKind is not null;
        if (catalogFieldPresent
            && (!StableIdentifier.TryValidate(triggerKey)
                || !StableIdentifier.TryValidate(catalogFingerprint)
                || !StableIdentifier.TryValidate(definitionFingerprint)))
        {
            throw new ArgumentException(
                "A catalog trigger invocation requires a stable trigger key and both catalog fingerprints.");
        }

        if (catalogFieldPresent
            && (costs.Instructions.Count != 0 || effects.Instructions.Count != 0))
        {
            throw new ArgumentException(
                "Catalog trigger invocations must not persist executable cost or effect bodies.");
        }

        if (catalogFieldPresent
            && (sourceCardId is null
                || sourceDefinitionId is null
                || sourceZoneId is null
                || sourceOwnerSide is null
                || sourceZoneKind is null))
        {
            throw new ArgumentException(
                "A catalog trigger invocation requires a complete source card, owner, definition, and zone snapshot.");
        }

        Version = version;
        Id = id;
        Controller = controller;
        Mode = mode;
        BehaviorId = behaviorId;
        SourceCardId = sourceCardId;
        SourceDefinitionId = sourceDefinitionId;
        SourceZoneId = sourceZoneId;
        SourceOwnerSide = sourceOwnerSide;
        SourceZoneKind = sourceZoneKind;
        Occurrence = occurrence.Copy();
        Costs = costs.Copy();
        Effects = effects.Copy();
        TriggerKey = triggerKey;
        CatalogFingerprint = catalogFingerprint;
        DefinitionFingerprint = definitionFingerprint;
    }

    public TriggerInvocation(TriggerRegistration registration, OccurrenceSnapshot occurrence)
        : this(registration, occurrence, null, null)
    {
    }

    public TriggerInvocation(
        TriggerRegistration registration,
        OccurrenceSnapshot occurrence,
        CardDefinitionId? sourceDefinitionId,
        ZoneId? sourceZoneId)
        : this(
            registration.Version,
            registration.Id,
            registration.Controller,
            registration.Mode,
            registration.BehaviorId,
            registration.SourceCardId,
            sourceDefinitionId,
            sourceZoneId,
            occurrence,
            registration.Costs,
            registration.Effects)
    {
    }

    /// <summary>
    /// Creates the detached catalog form. The trigger's instruction tree is
    /// intentionally not copied into this invocation; it is reconstructed
    /// from the pinned catalog when the child frame runs.
    /// </summary>
    public TriggerInvocation(
        TriggerId id,
        PlayerId controller,
        BehaviorTrigger trigger,
        CardInstanceId sourceCardId,
        CardDefinitionId sourceDefinitionId,
        ZoneId sourceZoneId,
        PlayerSide sourceOwnerSide,
        ZoneKind sourceZoneKind,
        OccurrenceSnapshot occurrence,
        string catalogFingerprint,
        string definitionFingerprint)
        : this(
            1,
            id,
            controller,
            trigger?.Mode ?? throw new ArgumentNullException(nameof(trigger)),
            ResolutionBehaviorIds.CardBehavior,
            sourceCardId,
            sourceDefinitionId,
            sourceZoneId,
            occurrence,
            new CostBundle(),
            new EffectBundle(),
            trigger.Key,
            catalogFingerprint,
            definitionFingerprint,
            sourceOwnerSide,
            sourceZoneKind)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerMode Mode { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId? SourceDefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId? SourceZoneId { get; private set; }

    /// <summary>Owner side captured with a catalog trigger's source snapshot.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerSide? SourceOwnerSide { get; private set; }

    /// <summary>Zone kind captured with a catalog trigger's source snapshot.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZoneKind? SourceZoneKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceSnapshot Occurrence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CostBundle Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectBundle Effects { get; private set; }

    /// <summary>Null for legacy registered trigger invocations.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerKey { get; private set; }

    /// <summary>Catalog provenance for a catalog trigger invocation.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CatalogFingerprint { get; private set; }

    /// <summary>Definition provenance for a catalog trigger invocation.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefinitionFingerprint { get; private set; }

    [JsonIgnore]
    public bool IsCatalogTrigger => TriggerKey is not null;

    internal TriggerInvocation Copy() => new(
        Version,
        Id,
        Controller,
        Mode,
        BehaviorId,
        SourceCardId,
        SourceDefinitionId,
        SourceZoneId,
        Occurrence,
        Costs,
        Effects,
        TriggerKey,
        CatalogFingerprint,
        DefinitionFingerprint,
        SourceOwnerSide,
        SourceZoneKind);
}

/// <summary>Detached interrupt invocation materialized at an occurrence.</summary>
public sealed record InterruptInvocation
{
    [JsonConstructor]
    public InterruptInvocation(
        int version,
        InterruptId id,
        PlayerId controller,
        InterruptKind kind,
        bool mandatory,
        string behaviorId,
        CardInstanceId? sourceCardId,
        CardDefinitionId? sourceDefinitionId,
        ZoneId? sourceZoneId,
        OccurrenceSnapshot occurrence,
        CostBundle costs,
        int maximumPreventionAmount,
        EffectBundle replacementEffects,
        string? interruptKey = null,
        string? catalogFingerprint = null,
        string? definitionFingerprint = null,
        PlayerSide? sourceOwnerSide = null,
        ZoneKind? sourceZoneKind = null,
        int? basicDrawCount = null,
        string? replacementReasonKey = null)
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(replacementEffects);
        bool catalogFieldPresent = interruptKey is not null
            || catalogFingerprint is not null
            || definitionFingerprint is not null
            || sourceOwnerSide is not null
            || sourceZoneKind is not null
            || basicDrawCount is not null
            || replacementReasonKey is not null;
        if (catalogFieldPresent
            && (!StableIdentifier.TryValidate(interruptKey)
                || !StableIdentifier.TryValidate(catalogFingerprint)
                || !StableIdentifier.TryValidate(definitionFingerprint)
                || sourceCardId is null
                || sourceDefinitionId is null
                || sourceZoneId is null
                || sourceOwnerSide is null
                || sourceZoneKind is null
                || !Enum.IsDefined(sourceOwnerSide.Value)
                || !Enum.IsDefined(sourceZoneKind.Value)
                || !StableIdentifier.TryValidate(replacementReasonKey)
                || basicDrawCount is null
                || basicDrawCount <= 0))
        {
            throw new ArgumentException(
                "A catalog interrupt invocation requires stable provenance, a complete source snapshot, and a positive replacement count.");
        }

        if (catalogFieldPresent && replacementEffects.Instructions.Count == 0)
        {
            throw new ArgumentException("A catalog interrupt invocation requires a replacement effect.", nameof(replacementEffects));
        }
        Version = version;
        Id = id;
        Controller = controller;
        Kind = kind;
        Mandatory = mandatory;
        BehaviorId = behaviorId;
        SourceCardId = sourceCardId;
        SourceDefinitionId = sourceDefinitionId;
        SourceZoneId = sourceZoneId;
        Occurrence = occurrence.Copy();
        Costs = costs.Copy();
        MaximumPreventionAmount = maximumPreventionAmount;
        ReplacementEffects = replacementEffects.Copy();
        InterruptKey = interruptKey;
        CatalogFingerprint = catalogFingerprint;
        DefinitionFingerprint = definitionFingerprint;
        SourceOwnerSide = sourceOwnerSide;
        SourceZoneKind = sourceZoneKind;
        BasicDrawCount = basicDrawCount;
        ReplacementReasonKey = replacementReasonKey;
    }

    public InterruptInvocation(InterruptRegistration registration, OccurrenceSnapshot occurrence)
        : this(registration, occurrence, null, null)
    {
    }

    public InterruptInvocation(
        InterruptRegistration registration,
        OccurrenceSnapshot occurrence,
        CardDefinitionId? sourceDefinitionId,
        ZoneId? sourceZoneId)
        : this(
            registration.Version,
            registration.Id,
            registration.Controller,
            registration.Kind,
            registration.Mandatory,
            registration.BehaviorId,
            registration.SourceCardId,
            sourceDefinitionId,
            sourceZoneId,
            occurrence,
            registration.Costs,
            registration.MaximumPreventionAmount,
            registration.ReplacementEffects)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public int Version { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InterruptId Id { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InterruptKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool Mandatory { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string BehaviorId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SourceCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId? SourceDefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId? SourceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceSnapshot Occurrence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CostBundle Costs { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int MaximumPreventionAmount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectBundle ReplacementEffects { get; private set; }

    /// <summary>Null for state-owned legacy interrupt invocations.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InterruptKey { get; private set; }

    /// <summary>Catalog provenance for a materialized replacement.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CatalogFingerprint { get; private set; }

    /// <summary>Source-definition provenance for a materialized replacement.</summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefinitionFingerprint { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayerSide? SourceOwnerSide { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZoneKind? SourceZoneKind { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BasicDrawCount { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReplacementReasonKey { get; private set; }

    [JsonIgnore]
    public bool IsCatalogInterrupt => InterruptKey is not null;

    internal InterruptInvocation Copy() => new(
        Version,
        Id,
        Controller,
        Kind,
        Mandatory,
        BehaviorId,
        SourceCardId,
        SourceDefinitionId,
        SourceZoneId,
        Occurrence,
        Costs,
        MaximumPreventionAmount,
        ReplacementEffects,
        InterruptKey,
        CatalogFingerprint,
        DefinitionFingerprint,
        SourceOwnerSide,
        SourceZoneKind,
        BasicDrawCount,
        ReplacementReasonKey);
}

/// <summary>
/// LIFO child frame for one imminent effect. Its queues and cursor are fully
/// materialized so save/load cannot re-query a changed source or reapply an
/// earlier effect.
/// </summary>
public sealed record EffectCheckpointFrame : ResolutionFrame
{
    [JsonConstructor]
    public EffectCheckpointFrame(
        int version,
        string behaviorId,
        PlayerId controller,
        OccurrenceSnapshot occurrence,
        EffectInstruction originalEffect,
        EffectInstruction currentEffect,
        EffectCheckpointStage stage,
        IReadOnlyList<TriggerInvocation> triggerQueue,
        int triggerCursor,
        IReadOnlyList<InterruptInvocation> interruptQueue,
        int interruptCursor,
        IReadOnlyList<TriggerId> appliedTriggers,
        IReadOnlyList<InterruptId> appliedInterrupts,
        int replacementDepth,
        EffectCheckpointChildKind childKind,
        TriggerId? childTriggerId,
        InterruptId? childInterruptId,
        OccurrenceSnapshot? resolvedOccurrence)
        : base(version, behaviorId)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(originalEffect);
        ArgumentNullException.ThrowIfNull(currentEffect);
        ArgumentNullException.ThrowIfNull(triggerQueue);
        ArgumentNullException.ThrowIfNull(interruptQueue);
        ArgumentNullException.ThrowIfNull(appliedTriggers);
        ArgumentNullException.ThrowIfNull(appliedInterrupts);
        Controller = controller;
        Occurrence = occurrence.Copy();
        OriginalEffect = OccurrenceSnapshot.CloneEffect(originalEffect);
        CurrentEffect = OccurrenceSnapshot.CloneEffect(currentEffect);
        Stage = stage;
        TriggerQueue = new ReadOnlyCollection<TriggerInvocation>(triggerQueue.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        TriggerCursor = triggerCursor;
        InterruptQueue = new ReadOnlyCollection<InterruptInvocation>(interruptQueue.Select(static value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Copy();
        }).ToArray());
        InterruptCursor = interruptCursor;
        AppliedTriggers = new ReadOnlyCollection<TriggerId>(appliedTriggers.ToArray());
        AppliedInterrupts = new ReadOnlyCollection<InterruptId>(appliedInterrupts.ToArray());
        ReplacementDepth = replacementDepth;
        ChildKind = childKind;
        ChildTriggerId = childTriggerId;
        ChildInterruptId = childInterruptId;
        ResolvedOccurrence = resolvedOccurrence?.Copy();
    }

    public EffectCheckpointFrame(
        PlayerId controller,
        OccurrenceSnapshot occurrence,
        EffectInstruction effect,
        IReadOnlyList<TriggerInvocation> triggerQueue,
        IReadOnlyList<InterruptInvocation> interruptQueue)
        : this(
            1,
            ResolutionBehaviorIds.EffectCheckpoint,
            controller,
            occurrence,
            effect,
            effect,
            EffectCheckpointStage.TriggerOrdering,
            triggerQueue,
            0,
            interruptQueue,
            0,
            [],
            [],
            0,
            EffectCheckpointChildKind.None,
            null,
            null,
            null)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceSnapshot Occurrence { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectInstruction OriginalEffect { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectInstruction CurrentEffect { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectCheckpointStage Stage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<TriggerInvocation> TriggerQueue { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TriggerCursor { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<InterruptInvocation> InterruptQueue { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int InterruptCursor { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<TriggerId> AppliedTriggers { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<InterruptId> AppliedInterrupts { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int ReplacementDepth { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EffectCheckpointChildKind ChildKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public TriggerId? ChildTriggerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InterruptId? ChildInterruptId { get; private set; }

    /// <summary>
    /// The post-resolution occurrence is kept separately from the imminent
    /// occurrence so a trigger cannot be evaluated before its source effect
    /// has committed.
    /// </summary>
    [JsonInclude]
    [JsonRequired]
    public OccurrenceSnapshot? ResolvedOccurrence { get; private set; }

    internal EffectCheckpointFrame Copy() => new(
        Version,
        BehaviorId,
        Controller,
        Occurrence,
        OriginalEffect,
        CurrentEffect,
        Stage,
        TriggerQueue,
        TriggerCursor,
        InterruptQueue,
        InterruptCursor,
        AppliedTriggers,
        AppliedInterrupts,
        ReplacementDepth,
        ChildKind,
        ChildTriggerId,
        ChildInterruptId,
        ResolvedOccurrence);
}
