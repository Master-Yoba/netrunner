using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>Typed costs that can be paid by the M2 reducer.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreditCost), "credits")]
[JsonDerivedType(typeof(ClickCost), "clicks")]
public abstract record CostInstruction;

public sealed record CreditCost : CostInstruction
{
    [JsonConstructor]
    public CreditCost(PlayerId payer, int amount)
    {
        Payer = payer;
        Amount = amount;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Payer { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Amount { get; private set; }
}

public sealed record ClickCost : CostInstruction
{
    [JsonConstructor]
    public ClickCost(PlayerId payer, int amount)
    {
        Payer = payer;
        Amount = amount;
    }

    public ClickCost(PlayerId payer)
        : this(payer, 1)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Payer { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Amount { get; private set; }
}

/// <summary>Frozen ordered list of typed costs.</summary>
public sealed record CostBundle
{
    [JsonConstructor]
    public CostBundle(IReadOnlyList<CostInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        Instructions = new ReadOnlyCollection<CostInstruction>(instructions.Select(CloneInstruction).ToArray());
    }

    public CostBundle(params CostInstruction[] instructions)
        : this((IReadOnlyList<CostInstruction>)instructions)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CostInstruction> Instructions { get; private set; }

    internal CostBundle Copy() => new(Instructions);

    private static CostInstruction CloneInstruction(CostInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return instruction switch
        {
            CreditCost cost => new CreditCost(cost.Payer, cost.Amount),
            ClickCost cost => new ClickCost(cost.Payer, cost.Amount),
            _ => throw new ArgumentException($"Unknown cost instruction type '{instruction.GetType().Name}'.", nameof(instruction)),
        };
    }
}

/// <summary>Typed effect families executed by the M2 rules reducer.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreditAdjustmentEffect), "credits")]
[JsonDerivedType(typeof(ClickAdjustmentEffect), "clicks")]
[JsonDerivedType(typeof(DamageEffect), "damage")]
[JsonDerivedType(typeof(TagAdjustmentEffect), "tags")]
[JsonDerivedType(typeof(MemoryAdjustmentEffect), "memory")]
[JsonDerivedType(typeof(RegisterProgramMemoryEffect), "register-program-memory")]
[JsonDerivedType(typeof(RemoveProgramMemoryEffect), "remove-program-memory")]
[JsonDerivedType(typeof(CardCounterAdjustmentEffect), "counter")]
[JsonDerivedType(typeof(VirusPurgeEffect), "virus-purge")]
[JsonDerivedType(typeof(AddLingeringEffect), "add-lingering")]
[JsonDerivedType(typeof(RemoveLingeringEffect), "remove-lingering")]
[JsonDerivedType(typeof(MoveCardEffect), "move-card")]
[JsonDerivedType(typeof(TurnBoundaryEffect), "turn-boundary")]
[JsonDerivedType(typeof(GameplayOccurrenceEffect), "gameplay-occurrence")]
[JsonDerivedType(typeof(BasicDrawEffect), "basic-draw")]
public abstract record EffectInstruction;

public sealed record CreditAdjustmentEffect : EffectInstruction
{
    [JsonConstructor]
    public CreditAdjustmentEffect(PlayerId target, int delta)
    {
        Target = target;
        Delta = delta;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }
}

public sealed record ClickAdjustmentEffect : EffectInstruction
{
    [JsonConstructor]
    public ClickAdjustmentEffect(PlayerId target, int delta)
    {
        Target = target;
        Delta = delta;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }
}

public sealed record DamageEffect : EffectInstruction
{
    [JsonConstructor]
    public DamageEffect(PlayerId target, DamageKind kind, int amount)
    {
        Target = target;
        Kind = kind;
        Amount = amount;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public DamageKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Amount { get; private set; }
}

public sealed record TagAdjustmentEffect : EffectInstruction
{
    [JsonConstructor]
    public TagAdjustmentEffect(PlayerId target, int delta)
    {
        Target = target;
        Delta = delta;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }
}

public sealed record MemoryAdjustmentEffect : EffectInstruction
{
    [JsonConstructor]
    public MemoryAdjustmentEffect(PlayerId target, int baseDelta)
    {
        Target = target;
        BaseDelta = baseDelta;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int BaseDelta { get; private set; }
}

public sealed record RegisterProgramMemoryEffect : EffectInstruction
{
    [JsonConstructor]
    public RegisterProgramMemoryEffect(CardInstanceId cardId, PlayerId owner, int memoryCost)
    {
        CardId = cardId;
        Owner = owner;
        MemoryCost = memoryCost;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int MemoryCost { get; private set; }
}

public sealed record RemoveProgramMemoryEffect : EffectInstruction
{
    [JsonConstructor]
    public RemoveProgramMemoryEffect(CardInstanceId cardId)
    {
        CardId = cardId;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }
}

public sealed record CardCounterAdjustmentEffect : EffectInstruction
{
    [JsonConstructor]
    public CardCounterAdjustmentEffect(CardInstanceId cardId, string counterName, int delta)
    {
        ArgumentNullException.ThrowIfNull(counterName);
        CardId = cardId;
        CounterName = counterName;
        Delta = delta;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CounterName { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Delta { get; private set; }
}

public sealed record VirusPurgeEffect : EffectInstruction
{
    [JsonConstructor]
    public VirusPurgeEffect()
    {
    }
}

public sealed record AddLingeringEffect : EffectInstruction
{
    [JsonConstructor]
    public AddLingeringEffect(GameLingeringEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        Effect = effect.Copy();
    }

    [JsonInclude]
    [JsonRequired]
    public GameLingeringEffect Effect { get; private set; }
}

public sealed record RemoveLingeringEffect : EffectInstruction
{
    [JsonConstructor]
    public RemoveLingeringEffect(RunEffectId effectId)
    {
        EffectId = effectId;
    }

    [JsonInclude]
    [JsonRequired]
    public RunEffectId EffectId { get; private set; }
}

/// <summary>
/// Moves one physical card to an already materialized destination zone.  The
/// reducer updates the card's zone reference and both ordered zone lists as a
/// single transaction; callers never provide a source or mutate lists.
/// </summary>
public sealed record MoveCardEffect : EffectInstruction
{
    [JsonConstructor]
    public MoveCardEffect(CardInstanceId cardId, ZoneId destinationZoneId)
    {
        CardId = cardId;
        DestinationZoneId = destinationZoneId;
    }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId DestinationZoneId { get; private set; }
}

/// <summary>
/// A no-op instruction whose committed occurrence is a formal CR turn
/// boundary.  Keeping the boundary as an effect instruction lets the existing
/// serializable checkpoint/trigger machinery materialize and resolve
/// turn-begin/end occurrences without inventing a second trigger channel.
/// </summary>
public sealed record TurnBoundaryEffect : EffectInstruction
{
    [JsonConstructor]
    public TurnBoundaryEffect(OccurrenceKind kind, PlayerId target, int turnNumber)
    {
        Kind = kind;
        Target = target;
        TurnNumber = turnNumber;
    }

    public TurnBoundaryEffect(OccurrenceKind kind, PlayerId target)
        : this(kind, target, 1)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Target { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int TurnNumber { get; private set; }
}

/// <summary>
/// A typed gameplay occurrence marker.  The marker itself has no state
/// mutation; the normal effect checkpoint commits it as a no-op and then
/// evaluates the occurrence's catalog triggers.
/// </summary>
public sealed record GameplayOccurrenceEffect : EffectInstruction
{
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunPersistentContext? RunContext { get; private set; }

    [JsonConstructor]
    public GameplayOccurrenceEffect(
        OccurrenceKind kind,
        PlayerId controller,
        CardInstanceId? subjectCardId = null,
        ServerId? serverId = null,
        bool? successful = null,
        RunPersistentContext? runContext = null,
        bool? subjectWasRezzed = null)
    {
        if (!Enum.IsDefined(kind)
            || kind is not (OccurrenceKind.CardInstalled
                or OccurrenceKind.CardRezzed
                or OccurrenceKind.ServerApproached
                or OccurrenceKind.IceEncountered
                or OccurrenceKind.IceApproached
                or OccurrenceKind.RunSuccessful
                or OccurrenceKind.RunEnded
                or OccurrenceKind.AgendaScoredOrStolen
                or OccurrenceKind.CardAccessed
                or OccurrenceKind.HostedCreditPoolEmptied
                or OccurrenceKind.CardAdvancedFromZero
                or OccurrenceKind.AccessedCardTrashed
                or OccurrenceKind.DiscardPhaseEnded
                or OccurrenceKind.ActionPhaseEnded
                or OccurrenceKind.RunBegan
                or OccurrenceKind.AgendaForfeited
                or OccurrenceKind.FirstTagRemovalThisTurn
                or OccurrenceKind.FirstBadPublicityThisTurn
                or OccurrenceKind.CreditsSpentFromOutsidePool
                or OccurrenceKind.ArchivesCardsTurnedFaceUp
                or OccurrenceKind.CardAdvanced
                or OccurrenceKind.FirstCardCreditGainThisTurn
                or OccurrenceKind.FirstOperationPlayedThisTurn
                or OccurrenceKind.FirstEventPlayedThisTurn
                or OccurrenceKind.IcePassed
                or OccurrenceKind.IceBypassed
                or OccurrenceKind.IceFullyBroken
                or OccurrenceKind.SubroutineBroken
                or OccurrenceKind.EncounterEnded
                or OccurrenceKind.ServerWouldBeApproached
                or OccurrenceKind.DamageDone
                or OccurrenceKind.CardsTrashedFromHq
                or OccurrenceKind.HandSizeDiscarded))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        // VP-NO-SUCCESSFUL-RUN: a RunEnded occurrence without an outcome is a run never declared successful (Flagship).
        if (kind != OccurrenceKind.RunEnded && successful is not null)
        {
            throw new ArgumentException(
                "Only a RunEnded occurrence may carry a success outcome.",
                nameof(successful));
        }

        if (kind == OccurrenceKind.ServerApproached
            && (serverId is null || subjectCardId is not null))
        {
            throw new ArgumentException(
                "A ServerApproached occurrence must identify its server and must not identify a subject card.",
                nameof(serverId));
        }

        if (kind is OccurrenceKind.IceEncountered or OccurrenceKind.IceApproached
            && (serverId is null || subjectCardId is null))
        {
            throw new ArgumentException(
                "An IceEncountered occurrence must identify both its encountered ICE subject and server.",
                nameof(subjectCardId));
        }

        Kind = kind;
        Controller = controller;
        SubjectCardId = subjectCardId;
        ServerId = serverId;
        Successful = successful;
        if (runContext is not null && kind != OccurrenceKind.RunEnded)
            throw new ArgumentException("Run context belongs only to a run-end occurrence.", nameof(runContext));
        RunContext = runContext?.Copy();
        if (subjectWasRezzed is not null && kind != OccurrenceKind.AccessedCardTrashed)
            throw new ArgumentException("Only a trashed-card occurrence records whether its subject was rezzed.", nameof(subjectWasRezzed));
        SubjectWasRezzed = subjectWasRezzed;
    }

    [JsonInclude]
    [JsonRequired]
    public OccurrenceKind Kind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Controller { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? SubjectCardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId? ServerId { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Successful { get; private set; }

    /// <summary>
    /// Last-known information for a trashed card: whether it was rezzed when
    /// trashed, so its own "when trashed" ability can still trigger. Recorded
    /// only by catalogs that track turn events, so Gateway bytes are unchanged.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SubjectWasRezzed { get; private set; }
}

/// <summary>
/// Typed draw instruction for the real click-for-card action.  Replacement
/// children carry the same primitive with <see cref="IsReplacement"/> set so
/// they resolve as ordinary draws and cannot recursively re-materialize the
/// first-draw replacement window.
/// </summary>
public sealed record BasicDrawEffect : EffectInstruction
{
    [JsonConstructor]
    public BasicDrawEffect(
        PlayerSide side,
        int drawCount,
        bool isReplacement = false,
        OccurrenceId? occurrenceId = null,
        bool isFirstBasicDraw = false)
    {
        Side = side;
        DrawCount = drawCount;
        IsReplacement = isReplacement;
        OccurrenceId = occurrenceId;
        IsFirstBasicDraw = isFirstBasicDraw;
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int DrawCount { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool IsReplacement { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OccurrenceId? OccurrenceId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool IsFirstBasicDraw { get; private set; }
}

/// <summary>Frozen ordered list of typed effects.</summary>
public sealed record EffectBundle
{
    [JsonConstructor]
    public EffectBundle(IReadOnlyList<EffectInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        Instructions = new ReadOnlyCollection<EffectInstruction>(instructions.Select(CloneInstruction).ToArray());
    }

    public EffectBundle(params EffectInstruction[] instructions)
        : this((IReadOnlyList<EffectInstruction>)instructions)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<EffectInstruction> Instructions { get; private set; }

    internal EffectBundle Copy() => new(Instructions);

    private static EffectInstruction CloneInstruction(EffectInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return instruction switch
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
            _ => throw new ArgumentException($"Unknown effect instruction type '{instruction.GetType().Name}'.", nameof(instruction)),
        };
    }
}

/// <summary>
/// Structural validation for serialized M2 cost/effect instructions.  Rules
/// that depend on the current state (resources, card zones, and so on) remain
/// in <see cref="StateInvariantValidator"/> and the reducer.
/// </summary>
public static class EffectInstructionValidator
{
    public static IReadOnlyList<StateInvariantViolation> Collect(
        CostBundle? costs,
        EffectBundle? effects,
        string path = "resolution")
    {
        var violations = new List<StateInvariantViolation>();
        if (costs is null)
        {
            violations.Add(new StateInvariantViolation(
                "effects.missing-costs",
                $"{path}.costs",
                "A resolution frame must contain a typed cost bundle."));
        }
        else
        {
            for (int index = 0; index < costs.Instructions.Count; index++)
            {
                CostInstruction? instruction = costs.Instructions[index];
                string instructionPath = $"{path}.costs.instructions[{index}]";
                if (instruction is null)
                {
                    violations.Add(new StateInvariantViolation(
                        "effects.null-cost",
                        instructionPath,
                        "Cost instructions must not be null."));
                    continue;
                }

                switch (instruction)
                {
                    case CreditCost credit:
                        RequireId(credit.Payer.Value, $"{instructionPath}.payer", "player", violations);
                        RequirePositive(credit.Amount, $"{instructionPath}.amount", "Credit cost", violations);
                        break;
                    case ClickCost click:
                        RequireId(click.Payer.Value, $"{instructionPath}.payer", "player", violations);
                        RequirePositive(click.Amount, $"{instructionPath}.amount", "Click cost", violations);
                        break;
                    default:
                        violations.Add(new StateInvariantViolation(
                            "effects.unknown-cost",
                            instructionPath,
                            $"Cost instruction type '{instruction.GetType().Name}' is not registered."));
                        break;
                }
            }
        }

        if (effects is null)
        {
            violations.Add(new StateInvariantViolation(
                "effects.missing-effects",
                $"{path}.effects",
                "A resolution frame must contain a typed effect bundle."));
        }
        else
        {
            for (int index = 0; index < effects.Instructions.Count; index++)
            {
                EffectInstruction? instruction = effects.Instructions[index];
                string instructionPath = $"{path}.effects.instructions[{index}]";
                if (instruction is null)
                {
                    violations.Add(new StateInvariantViolation(
                        "effects.null-effect",
                        instructionPath,
                        "Effect instructions must not be null."));
                    continue;
                }

                switch (instruction)
                {
                    case CreditAdjustmentEffect credit:
                        RequireId(credit.Target.Value, $"{instructionPath}.target", "player", violations);
                        break;
                    case ClickAdjustmentEffect clicks:
                        RequireId(clicks.Target.Value, $"{instructionPath}.target", "player", violations);
                        break;
                    case DamageEffect damage:
                        RequireId(damage.Target.Value, $"{instructionPath}.target", "player", violations);
                        if (!Enum.IsDefined(damage.Kind))
                        {
                            Add(violations, "effects.invalid-damage-kind", $"{instructionPath}.kind", $"'{damage.Kind}' is not a supported damage kind.");
                        }

                        RequirePositive(damage.Amount, $"{instructionPath}.amount", "Damage", violations);
                        break;
                    case TagAdjustmentEffect tags:
                        RequireId(tags.Target.Value, $"{instructionPath}.target", "player", violations);
                        break;
                    case MemoryAdjustmentEffect memory:
                        RequireId(memory.Target.Value, $"{instructionPath}.target", "player", violations);
                        break;
                    case RegisterProgramMemoryEffect register:
                        RequireId(register.CardId.Value, $"{instructionPath}.cardId", "card", violations);
                        RequireId(register.Owner.Value, $"{instructionPath}.owner", "player", violations);
                        if (register.MemoryCost < 0)
                        {
                            Add(violations, "effects.invalid-memory-cost", $"{instructionPath}.memoryCost", "Program memory cost must be nonnegative.");
                        }

                        break;
                    case RemoveProgramMemoryEffect remove:
                        RequireId(remove.CardId.Value, $"{instructionPath}.cardId", "card", violations);
                        break;
                    case CardCounterAdjustmentEffect counter:
                        RequireId(counter.CardId.Value, $"{instructionPath}.cardId", "card", violations);
                        RequireId(counter.CounterName, $"{instructionPath}.counterName", "counter", violations);
                        break;
                    case VirusPurgeEffect:
                        break;
                    case AddLingeringEffect add:
                        ValidateLingering(add.Effect, instructionPath, violations);
                        break;
                    case RemoveLingeringEffect remove:
                        RequireId(remove.EffectId.Value, $"{instructionPath}.effectId", "effect", violations);
                        break;
                    case MoveCardEffect move:
                        RequireId(move.CardId.Value, $"{instructionPath}.cardId", "card", violations);
                        RequireId(move.DestinationZoneId.Value, $"{instructionPath}.destinationZoneId", "zone", violations);
                        break;
                    case TurnBoundaryEffect boundary:
                        if (boundary.Kind is not (OccurrenceKind.TurnBegin or OccurrenceKind.TurnEnd))
                        {
                            Add(violations, "effects.invalid-turn-boundary-kind", $"{instructionPath}.kind", $"'{boundary.Kind}' is not a supported turn boundary.");
                        }

                        RequireId(boundary.Target.Value, $"{instructionPath}.target", "player", violations);
                        RequirePositive(boundary.TurnNumber, $"{instructionPath}.turnNumber", "Turn number", violations);
                        break;
                    case GameplayOccurrenceEffect occurrence:
                        if (!Enum.IsDefined(occurrence.Kind)
                            || occurrence.Kind is not (OccurrenceKind.CardInstalled
                                or OccurrenceKind.CardRezzed
                                or OccurrenceKind.ServerApproached
                                or OccurrenceKind.IceEncountered
                                or OccurrenceKind.IceApproached
                                or OccurrenceKind.RunSuccessful
                                or OccurrenceKind.RunEnded
                                or OccurrenceKind.AgendaScoredOrStolen
                                or OccurrenceKind.CardAccessed
                                or OccurrenceKind.HostedCreditPoolEmptied
                or OccurrenceKind.CardAdvancedFromZero
                or OccurrenceKind.AccessedCardTrashed
                or OccurrenceKind.DiscardPhaseEnded
                or OccurrenceKind.ActionPhaseEnded
                or OccurrenceKind.RunBegan
                or OccurrenceKind.AgendaForfeited
                or OccurrenceKind.FirstTagRemovalThisTurn
                or OccurrenceKind.FirstBadPublicityThisTurn
                or OccurrenceKind.CreditsSpentFromOutsidePool
                or OccurrenceKind.ArchivesCardsTurnedFaceUp
                or OccurrenceKind.CardAdvanced
                or OccurrenceKind.FirstCardCreditGainThisTurn
                or OccurrenceKind.FirstOperationPlayedThisTurn
                or OccurrenceKind.FirstEventPlayedThisTurn
                or OccurrenceKind.IcePassed
                or OccurrenceKind.IceBypassed
                or OccurrenceKind.IceFullyBroken
                or OccurrenceKind.SubroutineBroken
                or OccurrenceKind.EncounterEnded
                or OccurrenceKind.ServerWouldBeApproached
                or OccurrenceKind.DamageDone
                or OccurrenceKind.CardsTrashedFromHq
                or OccurrenceKind.HandSizeDiscarded))
                        {
                            Add(violations, "effects.invalid-gameplay-occurrence-kind", $"{instructionPath}.kind", $"'{occurrence.Kind}' is not a supported gameplay occurrence.");
                        }

                        RequireId(occurrence.Controller.Value, $"{instructionPath}.controller", "player", violations);
                        if (occurrence.SubjectCardId is { } subjectCardId)
                        {
                            RequireId(subjectCardId.Value, $"{instructionPath}.subjectCardId", "card", violations);
                        }

                        if (occurrence.ServerId is { } serverId)
                        {
                            RequireId(serverId.Value, $"{instructionPath}.serverId", "server", violations);
                        }

                        if (occurrence.Kind == OccurrenceKind.ServerApproached
                            && (occurrence.ServerId is null || occurrence.SubjectCardId is not null))
                        {
                            Add(
                                violations,
                                "effects.server-approached-context",
                                instructionPath,
                                "A ServerApproached occurrence must identify its server and must not identify a subject card.");
                        }

                        if (occurrence.Kind is OccurrenceKind.IceEncountered or OccurrenceKind.IceApproached
                            && (occurrence.ServerId is null || occurrence.SubjectCardId is null))
                        {
                            Add(
                                violations,
                                "effects.ice-encountered-context",
                                instructionPath,
                                "An IceEncountered occurrence must identify both its encountered ICE subject and server.");
                        }

                        // VP-NO-SUCCESSFUL-RUN: a RunEnded occurrence without an outcome is a run never declared successful
                        // nor made unsuccessful (Flagship).
                        if (occurrence.Kind != OccurrenceKind.RunEnded && occurrence.Successful is not null)
                        {
                            Add(
                                violations,
                                "effects.unexpected-run-outcome",
                                $"{instructionPath}.successful",
                                "Only a RunEnded occurrence may carry a success outcome.");
                        }

                        break;
                    case BasicDrawEffect draw:
                        if (draw.Side is not (PlayerSide.Corp or PlayerSide.Runner))
                        {
                            Add(violations, "effects.invalid-basic-draw-side", $"{instructionPath}.side", $"'{draw.Side}' is not a supported draw side.");
                        }

                        RequirePositive(draw.DrawCount, $"{instructionPath}.drawCount", "Basic draw count", violations);
                        if (draw.IsReplacement && draw.IsFirstBasicDraw)
                        {
                            Add(violations, "effects.invalid-basic-draw-flags", instructionPath, "A replacement draw cannot also mark the first basic draw action.");
                        }

                        if (draw.IsReplacement && draw.OccurrenceId is not null)
                        {
                            Add(violations, "effects.unexpected-basic-draw-occurrence", $"{instructionPath}.occurrenceId", "A replacement draw must not carry the root basic-draw occurrence ID.");
                        }

                        if (!draw.IsReplacement && draw.OccurrenceId is null)
                        {
                            Add(violations, "effects.missing-basic-draw-occurrence", $"{instructionPath}.occurrenceId", "A real basic draw must carry its materialized occurrence ID.");
                        }

                        if (draw.OccurrenceId is { } occurrenceId)
                        {
                            RequireId(occurrenceId.Value, $"{instructionPath}.occurrenceId", "occurrence", violations);
                        }

                        break;
                    default:
                        violations.Add(new StateInvariantViolation(
                            "effects.unknown-effect",
                            instructionPath,
                            $"Effect instruction type '{instruction.GetType().Name}' is not registered."));
                        break;
                }
            }
        }

        return violations.AsReadOnly();

        static void ValidateLingering(
            GameLingeringEffect effect,
            string path,
            List<StateInvariantViolation> violations)
        {
            if (effect is null)
            {
                Add(violations, "effects.missing-lingering", $"{path}.effect", "A lingering effect instruction requires an effect record.");
                return;
            }

            if (effect.Version != 1)
            {
                Add(violations, "effects.unsupported-lingering-version", $"{path}.effect.version", "Only lingering effect version 1 is supported.");
            }

            RequireId(effect.EffectId.Value, $"{path}.effect.effectId", "effect", violations);
            RequireId(effect.BehaviorId, $"{path}.effect.behaviorId", "behavior", violations);
            if (effect.SourceCardId.HasValue)
            {
                RequireId(effect.SourceCardId.Value.Value, $"{path}.effect.sourceCardId", "card", violations);
            }

            if (!Enum.IsDefined(effect.Expiry))
            {
                Add(violations, "effects.invalid-lingering-expiry", $"{path}.effect.expiry", $"'{effect.Expiry}' is not a supported lingering-effect expiry.");
            }
        }

        static void RequireId(
            string? value,
            string path,
            string kind,
            List<StateInvariantViolation> violations)
        {
            if (!StableIdentifier.TryValidate(value))
            {
                Add(violations, "id.invalid", path, $"{kind} ID must be non-empty and stable.");
            }
        }

        static void RequirePositive(int value, string path, string label, List<StateInvariantViolation> violations)
        {
            if (value <= 0)
            {
                Add(violations, "effects.nonpositive-amount", path, $"{label} must be positive.");
            }
        }

        static void Add(List<StateInvariantViolation> violations, string code, string path, string message) =>
            violations.Add(new StateInvariantViolation(code, path, message));
    }
}
