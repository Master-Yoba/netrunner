using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Netrunner.Core;

public static partial class ResolutionBehaviorIds
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
    public const string Turn = "rules.turn-26.03";
    public const string TurnDiscard = "rules.turn-discard-26.03";
    public const string Install = "rules.install-26.03";
    public const string CardPlay = "rules.card-play-26.03";
    public const string IceSubroutine = "rules.ice-subroutine-26.03";
}

public enum ResolutionContinuation
{
    Complete,
    AdjustCredits,
}

/// <summary>
/// Identifies which authoritative reducer opened an install cursor.  The
/// default value intentionally remains the original basic-action shape so
/// snapshots written before behavior-owned installs were introduced retain
/// their byte representation.
/// </summary>
public enum InstallInitiationKind
{
    BasicAction = 0,
    BehaviorEffect = 1,
    /// <summary>Corp card install initiated by a choice ICE subroutine.</summary>
    IceSubroutineNormal = 2,
    /// <summary>Corp ICE install directly inward from a choice ICE subroutine.</summary>
    IceSubroutineInwardIgnoreCosts = 3,
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
[JsonDerivedType(typeof(SetupFrame), "setup")]
[JsonDerivedType(typeof(ChooseCardFrame), "choose-card")]
[JsonDerivedType(typeof(AdjustCreditsFrame), "adjust-credits")]
[JsonDerivedType(typeof(ToyTurnFrame), "toy-turn")]
[JsonDerivedType(typeof(TurnFrame), "turn")]
[JsonDerivedType(typeof(TurnDiscardFrame), "turn-discard")]
[JsonDerivedType(typeof(InstallFrame), "install")]
[JsonDerivedType(typeof(CardPlayFrame), "card-play")]
[JsonDerivedType(typeof(RunTimingFrame), "run-timing")]
[JsonDerivedType(typeof(EffectResolutionFrame), "effect-resolution")]
[JsonDerivedType(typeof(MemoryCleanupFrame), "memory-cleanup")]
[JsonDerivedType(typeof(EffectCheckpointFrame), "effect-checkpoint")]
[JsonDerivedType(typeof(BehaviorResolutionFrame), "card-behavior")]
[JsonDerivedType(typeof(AbilityFrame), "ability")]
[JsonDerivedType(typeof(IceSubroutineFrame), "ice-subroutine")]
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
/// Serializable CR 8.5 install cursor. A first install action opens this
/// frame when the rules allow an optional or required pre-install trash
/// selection for either side. Subsequent actions incrementally select frozen
/// candidates or finalize the exact selected set; payment and movement happen
/// only at finalization.
/// </summary>
public sealed record InstallFrame : ResolutionFrame
{
    [JsonConstructor]
    public InstallFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        RuntimeCardClass cardClass,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        int installCost,
        IReadOnlyList<CardInstanceId>? candidateReplacementCardIds = null,
        IReadOnlyList<CardInstanceId>? requiredReplacementCardIds = null,
        IReadOnlyList<CardInstanceId>? selectedReplacementCardIds = null,
        ZoneId? destinationZoneId = null,
        IReadOnlyList<CardDefinitionId>? candidateReplacementDefinitionIds = null,
        InstallInitiationKind initiationKind = InstallInitiationKind.BasicAction,
        string? parentFrameIdentity = null,
        CostQuote? costQuote = null,
        CardInstanceId? inwardAnchorIceId = null,
        CardInstanceId? hostCardId = null)
        : base(version, behaviorId)
    {
        if (!Enum.IsDefined(cardClass))
        {
            throw new ArgumentOutOfRangeException(nameof(cardClass));
        }

        if (!Enum.IsDefined(destinationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(destinationKind));
        }

        if (!Enum.IsDefined(initiationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(initiationKind));
        }

        if (initiationKind == InstallInitiationKind.BasicAction && parentFrameIdentity is not null)
        {
            throw new ArgumentException(
                "Basic-action install frames must not retain a behavior parent identity.",
                nameof(parentFrameIdentity));
        }

        if ((initiationKind is InstallInitiationKind.BehaviorEffect
                or InstallInitiationKind.IceSubroutineNormal
                or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
            && !StableIdentifier.TryValidate(parentFrameIdentity))
        {
            throw new ArgumentException(
                "Behavior-effect install frames require a stable parent frame identity.",
                nameof(parentFrameIdentity));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(installCost);
        if (costQuote is not null && costQuote.EffectiveCost != installCost)
        {
            throw new ArgumentException(
                "The install cost must equal the effective cost in the frozen quote.",
                nameof(installCost));
        }

        Owner = owner;
        CardId = cardId;
        DefinitionId = definitionId;
        CardClass = cardClass;
        SourceZoneId = sourceZoneId;
        SourceZoneKind = sourceZoneKind;
        DestinationKind = destinationKind;
        ServerId = serverId;
        CreateNewRemote = createNewRemote;
        InstallCost = installCost;
        CandidateReplacementCardIds = Freeze(candidateReplacementCardIds);
        RequiredReplacementCardIds = Freeze(requiredReplacementCardIds);
        SelectedReplacementCardIds = Freeze(selectedReplacementCardIds);
        DestinationZoneId = destinationZoneId;
        CandidateReplacementDefinitionIds = FreezeDefinitions(candidateReplacementDefinitionIds);
        InitiationKind = initiationKind;
        ParentFrameIdentity = parentFrameIdentity;
        CostQuote = costQuote;
        InwardAnchorIceId = inwardAnchorIceId;
        HostCardId = hostCardId;

        if (initiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts
            && (destinationKind != InstallDestinationKind.ServerIce
                || serverId is null
                || createNewRemote
                || inwardAnchorIceId is null
                || cardClass != RuntimeCardClass.Ice
                || installCost != 0
                || CandidateReplacementCardIds.Count != 0
                || RequiredReplacementCardIds.Count != 0
                || SelectedReplacementCardIds.Count != 0))
        {
            throw new ArgumentException(
                "An inward ICE-subroutine install must target an existing server ICE zone, cost zero, and have no replacement selection.",
                nameof(inwardAnchorIceId));
        }

        if (initiationKind is InstallInitiationKind.IceSubroutineNormal
            or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
        {
            if (inwardAnchorIceId is not null
                && initiationKind != InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
            {
                throw new ArgumentException(
                    "Only an inward ICE-subroutine install may carry an inward anchor.",
                    nameof(inwardAnchorIceId));
            }
        }
        else if (inwardAnchorIceId is not null)
        {
            throw new ArgumentException(
                "Only an inward ICE-subroutine install may carry an inward anchor.",
                nameof(inwardAnchorIceId));
        }
    }

    public InstallFrame(
        PlayerId owner,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        RuntimeCardClass cardClass,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind,
        InstallDestinationKind destinationKind,
        ServerId? serverId,
        bool createNewRemote,
        int installCost,
        IReadOnlyList<CardInstanceId>? candidateReplacementCardIds = null,
        IReadOnlyList<CardInstanceId>? requiredReplacementCardIds = null,
        IReadOnlyList<CardInstanceId>? selectedReplacementCardIds = null,
        ZoneId? destinationZoneId = null,
        IReadOnlyList<CardDefinitionId>? candidateReplacementDefinitionIds = null,
        InstallInitiationKind initiationKind = InstallInitiationKind.BasicAction,
        string? parentFrameIdentity = null,
        CostQuote? costQuote = null,
        CardInstanceId? inwardAnchorIceId = null,
        CardInstanceId? hostCardId = null)
        : this(
            1,
            ResolutionBehaviorIds.Install,
            owner,
            cardId,
            definitionId,
            cardClass,
            sourceZoneId,
            sourceZoneKind,
            destinationKind,
            serverId,
            createNewRemote,
            installCost,
            candidateReplacementCardIds,
            requiredReplacementCardIds,
            selectedReplacementCardIds,
            destinationZoneId,
            candidateReplacementDefinitionIds,
            initiationKind,
            parentFrameIdentity,
            costQuote,
            inwardAnchorIceId, hostCardId)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RuntimeCardClass CardClass { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId SourceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneKind SourceZoneKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public InstallDestinationKind DestinationKind { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServerId? ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public bool CreateNewRemote { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int InstallCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> CandidateReplacementCardIds { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> RequiredReplacementCardIds { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> SelectedReplacementCardIds { get; private set; }

    /// <summary>
    /// Frozen runtime definition identities for the physical replacement
    /// candidates.  This is an internal semantic snapshot: it lets a loaded
    /// Corp selection reject a root whose metadata changed even when the
    /// candidate IDs and required subset happen to remain the same.
    /// Runner frames created before this field was introduced may omit it;
    /// current engine-issued frames always retain the list.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CardDefinitionId>? CandidateReplacementDefinitionIds { get; private set; }

    /// <summary>
    /// Identity of the physical destination zone captured when the install
    /// was materialized.  The destination kind/server pair is not sufficient
    /// to detect a reconstructed state whose Runner rig or Corp server-root
    /// topology was replaced while a selection decision was pending.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ZoneId? DestinationZoneId { get; private set; }

    /// <summary>
    /// The reducer that opened this cursor. BasicAction is omitted from JSON
    /// to preserve the pre-4.2 install-frame shape.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public InstallInitiationKind InitiationKind { get; private set; }

    /// <summary>
    /// Exact identity of the BehaviorResolutionFrame that owns a
    /// BehaviorEffect install cursor. It is absent for ordinary installs.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentFrameIdentity { get; private set; }

    /// <summary>
    /// Optional detached install-cost explanation. It is omitted for
    /// unmodified installs so legacy frame JSON remains byte-compatible.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CostQuote? CostQuote { get; private set; }

    /// <summary>
    /// Physical ICE retained as the anchor for a directly-inward install.
    /// The incoming ICE is inserted immediately before this card in the
    /// inner-to-outer zone ordering.
    /// </summary>
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardInstanceId? InwardAnchorIceId { get; private set; }

    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CardInstanceId? HostCardId { get; private set; }

    internal InstallFrame WithHost(CardInstanceId hostCardId) => new(
        Version, BehaviorId, Owner, CardId, DefinitionId, CardClass, SourceZoneId, SourceZoneKind,
        DestinationKind, ServerId, CreateNewRemote, InstallCost, CandidateReplacementCardIds,
        RequiredReplacementCardIds, SelectedReplacementCardIds, DestinationZoneId,
        CandidateReplacementDefinitionIds, InitiationKind, ParentFrameIdentity, CostQuote,
        InwardAnchorIceId, hostCardId);

    internal InstallFrame Select(CardInstanceId cardId, int installCost, CostQuote? costQuote = null) => new(
        Version,
        BehaviorId,
        Owner,
        CardId,
        DefinitionId,
        CardClass,
        SourceZoneId,
        SourceZoneKind,
        DestinationKind,
        ServerId,
        CreateNewRemote,
        installCost,
        CandidateReplacementCardIds,
        RequiredReplacementCardIds,
        SelectedReplacementCardIds.Append(cardId).ToArray(),
        DestinationZoneId,
        CandidateReplacementDefinitionIds,
        InitiationKind,
        ParentFrameIdentity,
        costQuote,
        InwardAnchorIceId, HostCardId);

    internal InstallFrame WithInstallCost(int installCost, CostQuote? costQuote = null) => new(
        Version,
        BehaviorId,
        Owner,
        CardId,
        DefinitionId,
        CardClass,
        SourceZoneId,
        SourceZoneKind,
        DestinationKind,
        ServerId,
        CreateNewRemote,
        installCost,
        CandidateReplacementCardIds,
        RequiredReplacementCardIds,
        SelectedReplacementCardIds,
        DestinationZoneId,
        CandidateReplacementDefinitionIds,
        InitiationKind,
        ParentFrameIdentity,
        costQuote,
        InwardAnchorIceId, HostCardId);

    private static ReadOnlyCollection<CardInstanceId> Freeze(IEnumerable<CardInstanceId>? values) =>
        new((values ?? []).ToArray());

    private static ReadOnlyCollection<CardDefinitionId>? FreezeDefinitions(IEnumerable<CardDefinitionId>? values) =>
        values is null
            ? null
            : new ReadOnlyCollection<CardDefinitionId>(values.ToArray());
}

/// <summary>
/// Serializable ordinary card-play cursor. The frame freezes the physical
/// source, public play area, discard destination, printed cost, and catalog
/// identities needed to resume the generic card behavior safely.
/// </summary>
public sealed record CardPlayFrame : ResolutionFrame
{
    [JsonConstructor]
    public CardPlayFrame(
        int version,
        string behaviorId,
        PlayerId owner,
        PlayerSide side,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        RuntimeCardClass cardClass,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind,
        ZoneId playAreaZoneId,
        ZoneId discardZoneId,
        int creditCost,
        string catalogFingerprint,
        string definitionFingerprint)
        : base(version, behaviorId)
    {
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!Enum.IsDefined(cardClass))
        {
            throw new ArgumentOutOfRangeException(nameof(cardClass));
        }

        if (!Enum.IsDefined(sourceZoneKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceZoneKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(creditCost);

        Owner = owner;
        Side = side;
        CardId = cardId;
        DefinitionId = definitionId;
        CardClass = cardClass;
        SourceZoneId = sourceZoneId;
        SourceZoneKind = sourceZoneKind;
        PlayAreaZoneId = playAreaZoneId;
        DiscardZoneId = discardZoneId;
        CreditCost = creditCost;
        CatalogFingerprint = catalogFingerprint;
        DefinitionFingerprint = definitionFingerprint;
    }

    public CardPlayFrame(
        PlayerId owner,
        PlayerSide side,
        CardInstanceId cardId,
        CardDefinitionId definitionId,
        RuntimeCardClass cardClass,
        ZoneId sourceZoneId,
        ZoneKind sourceZoneKind,
        ZoneId playAreaZoneId,
        ZoneId discardZoneId,
        int creditCost,
        string catalogFingerprint,
        string definitionFingerprint)
        : this(
            1,
            ResolutionBehaviorIds.CardPlay,
            owner,
            side,
            cardId,
            definitionId,
            cardClass,
            sourceZoneId,
            sourceZoneKind,
            playAreaZoneId,
            discardZoneId,
            creditCost,
            catalogFingerprint,
            definitionFingerprint)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public PlayerId Owner { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public PlayerSide Side { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId CardId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId DefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RuntimeCardClass CardClass { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId SourceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneKind SourceZoneKind { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId PlayAreaZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId DiscardZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int CreditCost { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CatalogFingerprint { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string DefinitionFingerprint { get; private set; }
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

/// <summary>Persisted stage of a printed ICE subroutine continuation.</summary>
public enum IceSubroutineFrameStage
{
    AwaitingChoice,
    AwaitingInstall,
    AwaitingDamage,
}

/// <summary>
/// Serializable, authenticated continuation for one printed ICE
/// subroutine.  The run timing frame is replaced while this cursor is live;
/// therefore the cursor itself carries every identity needed to resume the
/// exact run, encounter, ICE, ordinal, server, and physical ICE-zone
/// topology after save/load.  Choices are frozen as typed payloads and are
/// re-materialized/validated by the reducer before any effect is applied.
/// </summary>
public sealed record IceSubroutineFrame : ResolutionFrame
{
    [JsonConstructor]
    public IceSubroutineFrame(
        int version,
        string behaviorId,
        string frameIdentity,
        RunId runId,
        EncounterId encounterId,
        CardInstanceId iceId,
        CardDefinitionId iceDefinitionId,
        SubroutineId subroutineId,
        int ordinal,
        ServerId serverId,
        ZoneId iceZoneId,
        IReadOnlyList<CardInstanceId> iceZoneCardIds,
        string catalogFingerprint,
        IceSubroutineFrameStage stage,
        IReadOnlyList<IceSubroutineChoiceParameters> choices,
        CardInstanceId? damageTrashedCardId = null)
        : base(version, behaviorId)
    {
        if (!StableIdentifier.TryValidate(frameIdentity))
        {
            throw new ArgumentException("An ICE subroutine frame requires a stable frame identity.", nameof(frameIdentity));
        }

        if (!StableIdentifier.TryValidate(catalogFingerprint)
            || !DeterministicHash.IsSha256Hex(catalogFingerprint))
        {
            throw new ArgumentException("An ICE subroutine frame requires a lowercase SHA-256 catalog fingerprint.", nameof(catalogFingerprint));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        ArgumentNullException.ThrowIfNull(iceZoneCardIds);
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Any(choice => choice is null))
        {
            throw new ArgumentException("ICE subroutine frame choices must not contain null entries.", nameof(choices));
        }

        FrameIdentity = frameIdentity;
        RunId = runId;
        EncounterId = encounterId;
        IceId = iceId;
        IceDefinitionId = iceDefinitionId;
        SubroutineId = subroutineId;
        Ordinal = ordinal;
        ServerId = serverId;
        IceZoneId = iceZoneId;
        IceZoneCardIds = new ReadOnlyCollection<CardInstanceId>(iceZoneCardIds.ToArray());
        CatalogFingerprint = catalogFingerprint;
        Stage = stage;
        DamageTrashedCardId = damageTrashedCardId;
        Choices = new ReadOnlyCollection<IceSubroutineChoiceParameters>(choices
            .Select(choice => new IceSubroutineChoiceParameters(
                choice.FrameIdentity,
                choice.Choice,
                choice.CardId,
                choice.DestinationKind,
                choice.ServerId,
                choice.CreateNewRemote,
                choice.CreditCost,
                choice.HostedCredits))
            .ToArray());
    }

    public IceSubroutineFrame(
        RunId runId,
        EncounterId encounterId,
        CardInstanceId iceId,
        CardDefinitionId iceDefinitionId,
        SubroutineId subroutineId,
        int ordinal,
        ServerId serverId,
        ZoneId iceZoneId,
        IReadOnlyList<CardInstanceId> iceZoneCardIds,
        string catalogFingerprint,
        IReadOnlyList<IceSubroutineChoiceParameters> choices)
        : this(
            1,
            ResolutionBehaviorIds.IceSubroutine,
            ComputeIdentity(runId, encounterId, iceId, subroutineId, ordinal),
            runId,
            encounterId,
            iceId,
            iceDefinitionId,
            subroutineId,
            ordinal,
            serverId,
            iceZoneId,
            iceZoneCardIds,
            catalogFingerprint,
            IceSubroutineFrameStage.AwaitingChoice,
            choices)
    {
    }

    [JsonInclude]
    [JsonRequired]
    public string FrameIdentity { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public RunId RunId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public EncounterId EncounterId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId IceId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardDefinitionId IceDefinitionId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public SubroutineId SubroutineId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public int Ordinal { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ServerId ServerId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public ZoneId IceZoneId { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<CardInstanceId> IceZoneCardIds { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public string CatalogFingerprint { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IceSubroutineFrameStage Stage { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public IReadOnlyList<IceSubroutineChoiceParameters> Choices { get; private set; }

    [JsonInclude]
    [JsonRequired]
    public CardInstanceId? DamageTrashedCardId { get; private set; }

    internal static string ComputeIdentity(
        RunId runId,
        EncounterId encounterId,
        CardInstanceId iceId,
        SubroutineId subroutineId,
        int ordinal)
    {
        string seed = string.Join('|', runId.Value, encounterId.Value, iceId.Value, subroutineId.Value, ordinal);
        return "ice-subroutine:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
    }

    internal IceSubroutineFrame WithStage(IceSubroutineFrameStage stage) => new(
        Version,
        BehaviorId,
        FrameIdentity,
        RunId,
        EncounterId,
        IceId,
        IceDefinitionId,
        SubroutineId,
        Ordinal,
        ServerId,
        IceZoneId,
        IceZoneCardIds,
        CatalogFingerprint,
        stage,
        Choices,
        DamageTrashedCardId);
    internal IceSubroutineFrame WithDamageResult(CardInstanceId cardId) => this with { DamageTrashedCardId = cardId };

    internal IceSubroutineFrame WithChoices(IReadOnlyList<IceSubroutineChoiceParameters> choices) => this with
    {
        Stage = IceSubroutineFrameStage.AwaitingChoice,
        Choices = new ReadOnlyCollection<IceSubroutineChoiceParameters>(choices.ToArray()),
    };

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
            case SetupFrame setup:
                ValidateBehavior(frame, SetupFrame.Behavior, path, violations);
                if (!Enum.IsDefined(setup.Side))
                {
                    violations.Add(new StateInvariantViolation("setup.invalid-side", path, "Setup must name a valid side."));
                }
                break;
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
            case TurnFrame turn:
                ValidateBehavior(frame, ResolutionBehaviorIds.Turn, path, violations);
                if (turn.ActiveSide is not (PlayerSide.Corp or PlayerSide.Runner))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-side",
                        $"{path}.activeSide",
                        $"'{turn.ActiveSide}' is not a supported turn side."));
                }

                if (turn.TurnNumber < 1)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-turn-number",
                        $"{path}.turnNumber",
                        "A turn frame number must be positive."));
                }

                if (!Enum.IsDefined(turn.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-stage",
                        $"{path}.stage",
                        $"'{turn.Stage}' is not a supported CR turn stage."));
                }

                if (turn.PriorityWindow.HasValue && !Enum.IsDefined(turn.PriorityWindow.Value))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-priority-window",
                        $"{path}.priorityWindow",
                        $"'{turn.PriorityWindow}' is not a supported CR turn priority window."));
                }

                if (turn.ActiveSide == PlayerSide.Corp
                    && turn.PriorityWindow.HasValue
                    && turn.PriorityWindow.Value != TurnPriorityWindowKind.PaidRezAndScore)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-priority-window",
                        $"{path}.priorityWindow",
                        "Corp turn windows must expose paid, rez, and score capabilities together."));
                }

                if (turn.ActiveSide == PlayerSide.Runner
                    && turn.PriorityWindow.HasValue
                    && turn.PriorityWindow.Value != TurnPriorityWindowKind.PaidAndRez)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-priority-window",
                        $"{path}.priorityWindow",
                        "Runner turn windows must expose paid and rez capabilities."));
                }

                if (turn.ActiveSide == PlayerSide.Runner && turn.Stage == TurnFrameStage.MandatoryDraw)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-stage",
                        $"{path}.stage",
                        "Runner turns do not have a mandatory draw stage."));
                }

                bool priorityStage = turn.Stage is TurnFrameStage.BeforeTurnPriority
                    or TurnFrameStage.ActionPriority
                    or TurnFrameStage.DiscardPriority;
                if (priorityStage && !turn.PriorityWindow.HasValue)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.missing-priority-window",
                        path,
                        "A turn priority stage must retain its capability window."));
                }

                if (!priorityStage && (turn.PriorityWindow.HasValue || turn.PriorityHolder.HasValue || turn.ConsecutivePasses != 0))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.unexpected-priority-state",
                        path,
                        "Priority state is only valid while a turn paid-ability window is open."));
                }

                if (priorityStage && turn.PriorityHolder is null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.missing-priority-holder",
                        $"{path}.priorityHolder",
                        "An open turn priority window must identify its holder."));
                }

                if (turn.ConsecutivePasses is < 0 or > 1)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-consecutive-passes",
                        $"{path}.consecutivePasses",
                        "A turn paid-ability window has zero or one consecutive pass before it closes."));
                }

                break;
            case TurnDiscardFrame turnDiscard:
                ValidateBehavior(frame, ResolutionBehaviorIds.TurnDiscard, path, violations);
                ValidatePlayer(turnDiscard.Owner, $"{path}.owner", violations);
                if (turnDiscard.RequiredCount <= 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-discard-count",
                        $"{path}.requiredCount",
                        "A discard decision must require at least one card."));
                }

                ValidateCardOptions(turnDiscard.Candidates, $"{path}.candidates", violations);
                ValidateCardOptions(turnDiscard.Selected, $"{path}.selected", violations);
                if (turnDiscard.Selected.Count > turnDiscard.RequiredCount)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.too-many-selected-discards",
                        $"{path}.selected",
                        "Selected discards must not exceed the required count."));
                }

                if (turnDiscard.Candidates.Count < turnDiscard.RequiredCount)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.insufficient-discard-candidates",
                        $"{path}.candidates",
                        "A discard decision must offer at least the required number of cards."));
                }

                if (turnDiscard.Selected.Any(cardId => !turnDiscard.Candidates.Contains(cardId)))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.selected-discard-not-offered",
                        $"{path}.selected",
                        "Every selected discard must be one of the materialized candidates."));
                }

                break;
            case InstallFrame install:
                ValidateBehavior(frame, ResolutionBehaviorIds.Install, path, violations);
                ValidatePlayer(install.Owner, $"{path}.owner", violations);
                ValidateCardId(install.CardId, $"{path}.cardId", violations);
                if (!Enum.IsDefined(install.InitiationKind))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-install-initiation",
                        $"{path}.initiationKind",
                        $"'{install.InitiationKind}' is not a supported install initiation kind."));
                }
                else if (install.InitiationKind == InstallInitiationKind.BasicAction
                    && install.ParentFrameIdentity is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.unexpected-install-parent",
                        $"{path}.parentFrameIdentity",
                        "A basic-action install frame must not retain a behavior parent identity."));
                }
                else if (install.InitiationKind == InstallInitiationKind.BehaviorEffect
                    && !StableIdentifier.TryValidate(install.ParentFrameIdentity))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.missing-install-parent",
                        $"{path}.parentFrameIdentity",
                        "A behavior-effect install frame must retain a stable behavior parent identity."));
                }
                else if (install.InitiationKind is InstallInitiationKind.IceSubroutineNormal
                    or InstallInitiationKind.IceSubroutineInwardIgnoreCosts)
                {
                    if (!StableIdentifier.TryValidate(install.ParentFrameIdentity))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.missing-install-parent",
                            $"{path}.parentFrameIdentity",
                            "An ICE subroutine install frame must retain a stable ICE subroutine parent identity."));
                    }

                    bool inward = install.InitiationKind == InstallInitiationKind.IceSubroutineInwardIgnoreCosts;
                    if (inward
                        && (install.InwardAnchorIceId is not { } anchor
                            || !StableIdentifier.TryValidate(anchor.Value)
                            || install.DestinationKind != InstallDestinationKind.ServerIce
                            || install.CreateNewRemote
                            || install.ServerId is null
                            || install.InstallCost != 0
                            || install.CandidateReplacementCardIds.Count != 0
                            || install.RequiredReplacementCardIds.Count != 0
                            || install.SelectedReplacementCardIds.Count != 0))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-inward-install",
                            path,
                            "A directly-inward ICE subroutine install must target an existing ICE zone, carry its anchor, and have zero cost without replacements."));
                    }

                    if (!inward && install.InwardAnchorIceId is not null)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.unexpected-install-anchor",
                            $"{path}.inwardAnchorIceId",
                            "A normal ICE subroutine install must not carry an inward anchor."));
                    }
                }
                if (!StableIdentifier.TryValidate(install.DefinitionId.Value))
                {
                    violations.Add(new StateInvariantViolation("id.invalid", $"{path}.definitionId", "Definition ID is empty or invalid."));
                }
                ValidateZoneId(install.SourceZoneId, $"{path}.sourceZoneId", violations);
                if (install.DestinationZoneId is { } destinationZoneId)
                {
                    ValidateZoneId(destinationZoneId, $"{path}.destinationZoneId", violations);
                }
                if (!Enum.IsDefined(install.SourceZoneKind))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-source-zone-kind",
                        $"{path}.sourceZoneKind",
                        $"'{install.SourceZoneKind}' is not a supported install source zone."));
                }

                if (install.DestinationKind == InstallDestinationKind.RunnerRig
                    && (install.ServerId is not null || install.CreateNewRemote))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-install-destination",
                        $"{path}.destinationKind",
                        "Runner-rig install frames must not identify a server or new remote."));
                }

                bool serverDestination = install.DestinationKind is InstallDestinationKind.ServerRoot or InstallDestinationKind.ServerIce;
                if (serverDestination && (install.ServerId is null) == !install.CreateNewRemote)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-install-destination",
                        $"{path}.serverId",
                        "Server install frames must identify exactly one existing server or a new remote."));
                }

                if (install.InstallCost < 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-install-cost",
                        $"{path}.installCost",
                        "Install credit cost must not be negative."));
                }

                ValidateCardOptions(install.CandidateReplacementCardIds, $"{path}.candidateReplacementCardIds", violations);
                ValidateCardOptions(install.RequiredReplacementCardIds, $"{path}.requiredReplacementCardIds", violations);
                ValidateCardOptions(install.SelectedReplacementCardIds, $"{path}.selectedReplacementCardIds", violations);
                if (install.CandidateReplacementDefinitionIds is not null)
                {
                    for (int definitionIndex = 0; definitionIndex < install.CandidateReplacementDefinitionIds.Count; definitionIndex++)
                    {
                        CardDefinitionId definitionId = install.CandidateReplacementDefinitionIds[definitionIndex];
                        if (!StableIdentifier.TryValidate(definitionId.Value))
                        {
                            violations.Add(new StateInvariantViolation(
                                "id.invalid",
                                $"{path}.candidateReplacementDefinitionIds[{definitionIndex}]",
                                "Candidate replacement definition ID is empty or invalid."));
                        }
                    }
                }
                if (install.RequiredReplacementCardIds.Any(cardId => !install.CandidateReplacementCardIds.Contains(cardId)))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.install-required-not-offered",
                        path,
                        "Every required install replacement must be one of the frozen candidates."));
                }

                if (install.SelectedReplacementCardIds.Any(cardId => !install.CandidateReplacementCardIds.Contains(cardId)))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.install-selected-not-offered",
                        path,
                        "Every selected install replacement must be one of the frozen candidates."));
                }

                break;
            case CardPlayFrame cardPlay:
                ValidateBehavior(frame, ResolutionBehaviorIds.CardPlay, path, violations);
                ValidatePlayer(cardPlay.Owner, $"{path}.owner", violations);
                ValidateCardId(cardPlay.CardId, $"{path}.cardId", violations);
                if (!StableIdentifier.TryValidate(cardPlay.DefinitionId.Value))
                {
                    violations.Add(new StateInvariantViolation(
                        "id.invalid",
                        $"{path}.definitionId",
                        "Definition ID is empty or invalid."));
                }

                ValidateZoneId(cardPlay.SourceZoneId, $"{path}.sourceZoneId", violations);
                ValidateZoneId(cardPlay.PlayAreaZoneId, $"{path}.playAreaZoneId", violations);
                ValidateZoneId(cardPlay.DiscardZoneId, $"{path}.discardZoneId", violations);

                if (!Enum.IsDefined(cardPlay.Side))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-side",
                        $"{path}.side",
                        $"'{cardPlay.Side}' is not a supported card-play side."));
                }

                if (!Enum.IsDefined(cardPlay.CardClass))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-card-class",
                        $"{path}.cardClass",
                        $"'{cardPlay.CardClass}' is not a supported card-play class."));
                }
                else if ((cardPlay.Side, cardPlay.CardClass) is not
                    (PlayerSide.Corp, RuntimeCardClass.Operation)
                    and not (PlayerSide.Runner, RuntimeCardClass.Event))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-card-class",
                        $"{path}.cardClass",
                        "Ordinary card play supports only Corp Operations and Runner Events."));
                }

                if (!Enum.IsDefined(cardPlay.SourceZoneKind))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-source-zone-kind",
                        $"{path}.sourceZoneKind",
                        $"'{cardPlay.SourceZoneKind}' is not a supported card-play source zone."));
                }
                else if ((cardPlay.Side, cardPlay.SourceZoneKind) is not
                    (PlayerSide.Corp, ZoneKind.CorpHand)
                    and not (PlayerSide.Runner, ZoneKind.RunnerGrip))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-source-zone-kind",
                        $"{path}.sourceZoneKind",
                        "Ordinary card play must source Corp Operations from CorpHand or Runner Events from RunnerGrip."));
                }

                if (cardPlay.CreditCost < 0)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-play-cost",
                        $"{path}.creditCost",
                        "Play credit cost must not be negative."));
                }

                if (!StableIdentifier.TryValidate(cardPlay.CatalogFingerprint)
                    || !DeterministicHash.IsSha256Hex(cardPlay.CatalogFingerprint))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-catalog-fingerprint",
                        $"{path}.catalogFingerprint",
                        "Card-play frames must retain a lowercase SHA-256 catalog fingerprint."));
                }

                if (!StableIdentifier.TryValidate(cardPlay.DefinitionFingerprint)
                    || !DeterministicHash.IsSha256Hex(cardPlay.DefinitionFingerprint))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-definition-fingerprint",
                        $"{path}.definitionFingerprint",
                        "Card-play frames must retain a lowercase SHA-256 definition fingerprint."));
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
            case IceSubroutineFrame iceSubroutine:
                ValidateBehavior(frame, ResolutionBehaviorIds.IceSubroutine, path, violations);
                ValidateIceSubroutineFrame(iceSubroutine, path, violations);
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

                if (checkpoint.ChildKind == EffectCheckpointChildKind.Trigger
                    && (!checkpoint.ChildTriggerId.HasValue || checkpoint.ChildInterruptId.HasValue))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-checkpoint-trigger-child",
                        path,
                        "A trigger checkpoint child must identify only a trigger invocation."));
                }

                if (checkpoint.ChildKind == EffectCheckpointChildKind.Replacement
                    && (!checkpoint.ChildInterruptId.HasValue || checkpoint.ChildTriggerId.HasValue))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-checkpoint-replacement-child",
                        path,
                        "A replacement checkpoint child must identify only an interrupt invocation."));
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
            case BehaviorResolutionFrame behavior:
                ValidateBehavior(frame, ResolutionBehaviorIds.CardBehavior, path, violations);
                ValidatePlayer(behavior.Controller, $"{path}.controller", violations);
                ValidateCardId(behavior.SourceCardId, $"{path}.sourceCardId", violations);
                if (!StableIdentifier.TryValidate(behavior.DefinitionId.Value))
                {
                    violations.Add(new StateInvariantViolation(
                        "id.invalid",
                        $"{path}.definitionId",
                        "Behavior definition ID is empty or invalid."));
                }

                if (!StableIdentifier.TryValidate(behavior.CatalogFingerprint)
                    || !DeterministicHash.IsSha256Hex(behavior.CatalogFingerprint))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-catalog-fingerprint",
                        $"{path}.catalogFingerprint",
                        "Behavior frames must retain a lowercase SHA-256 catalog fingerprint."));
                }

                if (!StableIdentifier.TryValidate(behavior.DefinitionFingerprint)
                    || !DeterministicHash.IsSha256Hex(behavior.DefinitionFingerprint))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-definition-fingerprint",
                        $"{path}.definitionFingerprint",
                        "Behavior frames must retain a lowercase SHA-256 definition fingerprint."));
                }

                if (!Enum.IsDefined(behavior.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-stage",
                        $"{path}.stage",
                        $"'{behavior.Stage}' is not a supported behavior resolution stage."));
                }

                if (behavior.Stage == BehaviorResolutionStage.AwaitingOccurrence
                    && behavior.OccurrenceContinuation is null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.behavior-occurrence-continuation-missing",
                        path,
                        "An awaiting behavior occurrence must retain its typed continuation context."));
                }
                else if (behavior.OccurrenceContinuation is { } occurrenceContinuation)
                {
                    if (occurrenceContinuation.Kind != OccurrenceKind.HostedCreditPoolEmptied)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-kind-invalid",
                            $"{path}.occurrenceContinuation.kind",
                            "Behavior occurrence continuations currently support only hosted-credit exhaustion."));
                    }

                    if (occurrenceContinuation.SourceCardId != behavior.SourceCardId)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-source-mismatch",
                            $"{path}.occurrenceContinuation.sourceCardId",
                            "A behavior occurrence continuation must identify its parent source card."));
                    }

                    if (!BehaviorFrameIdentity.TryValidateIssued(occurrenceContinuation.FrameIdentity, out _)
                        || !string.Equals(occurrenceContinuation.FrameIdentity, behavior.FrameIdentity, StringComparison.Ordinal))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-frame-mismatch",
                            $"{path}.occurrenceContinuation.frameIdentity",
                            "A behavior occurrence continuation must identify its exact parent frame."));
                    }

                    if (occurrenceContinuation.InstructionCursor < 0)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-cursor-invalid",
                            $"{path}.occurrenceContinuation.instructionCursor",
                            "A behavior occurrence continuation cursor must be nonnegative."));
                    }

                    if (behavior.Stage == BehaviorResolutionStage.AwaitingOccurrence
                        && (occurrenceContinuation.SourceMovedAfterCompletion
                            || occurrenceContinuation.InstructionCursor != behavior.InstructionCursor))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-pending-mismatch",
                            path,
                            "An awaiting behavior occurrence must retain the post-instruction cursor without completed movement authorization."));
                    }

                    if (behavior.Stage != BehaviorResolutionStage.AwaitingOccurrence
                        && !occurrenceContinuation.SourceMovedAfterCompletion)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-completion-mismatch",
                            path,
                            "A completed behavior occurrence continuation may only remain as moved-source authorization."));
                    }
                }

                if (!BehaviorFrameIdentity.TryValidateIssued(behavior.FrameIdentity, out _))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-frame-identity",
                        $"{path}.frameIdentity",
                        "Behavior frames must retain an engine-issued behavior-frame ID."));
                }

                if (behavior.SourceZoneId is not { } sourceZoneId
                    || !StableIdentifier.TryValidate(sourceZoneId.Value)
                    || behavior.SourceZoneKind is not { } sourceZoneKind
                    || !Enum.IsDefined(sourceZoneKind))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-source-location",
                        $"{path}.sourceZoneId",
                        "Behavior frames must retain a valid source zone ID and zone kind."));
                }

                if (behavior.ParentFrameIdentity is not null
                    && !StableIdentifier.TryValidate(behavior.ParentFrameIdentity))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-parent-identity",
                        $"{path}.parentFrameIdentity",
                        "Behavior parent frame identities must be stable identifiers."));
                }

                if (behavior.AbilityKey is not null
                    && !StableIdentifier.TryValidate(behavior.AbilityKey))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-ability-key",
                        $"{path}.abilityKey",
                        "An ability behavior child key must be a stable identifier."));
                }

                if (behavior.TriggerKey is not null
                    && !StableIdentifier.TryValidate(behavior.TriggerKey))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-trigger-key",
                        $"{path}.triggerKey",
                        "A trigger behavior child key must be a stable identifier."));
                }

                if (behavior.AbilityKey is not null && behavior.TriggerKey is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.behavior-multiple-declarations",
                        path,
                        "A behavior frame cannot carry both an ability key and a trigger key."));
                }

                ValidateBehaviorInstructions(behavior.RootInstructions, $"{path}.rootInstructions", violations);
                ValidateBehaviorInstructions(behavior.Instructions, $"{path}.instructions", violations);
                if (behavior.InstructionCursor < 0
                    || (behavior.Instructions.Count > 0
                        && behavior.InstructionCursor > behavior.Instructions.Count))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-behavior-cursor",
                        $"{path}.instructionCursor",
                        "Behavior instruction cursor must identify the next instruction or the end of the program."));
                }

                ValidateBehaviorBindings(behavior.Bindings, $"{path}.bindings", violations);
                ValidateBehaviorSetAside(behavior.SetAside, $"{path}.setAside", violations);
                ValidateBehaviorContinuation(behavior.Continuation, $"{path}.continuation", violations);
                if (behavior.SetAsideZoneId is { } setAsideZoneId
                    && !StableIdentifier.TryValidate(setAsideZoneId.Value))
                {
                    violations.Add(new StateInvariantViolation(
                        "id.invalid",
                        $"{path}.setAsideZoneId",
                        "Behavior set-aside zone IDs must be stable identifiers."));
                }

                if (behavior.SetAside.Count == 0 && behavior.SetAsideZoneId is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.behavior-empty-set-aside-zone",
                        $"{path}.setAsideZoneId",
                        "An empty behavior selection must not retain a set-aside zone."));
                }
                if (behavior.Choice is not null)
                {
                    if (behavior.Stage == BehaviorResolutionStage.AwaitingOccurrence)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-occurrence-choice-mismatch",
                            path,
                            "A behavior frame awaiting a hosted-credit occurrence cannot retain a pending choice."));
                    }

                    BehaviorChoiceMaterialization choice = behavior.Choice;
                    ValidatePlayer(choice.Owner, $"{path}.choice.owner", violations);
                    if (!Enum.IsDefined(choice.Mode)
                        || !Enum.IsDefined(choice.CardinalityKind))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-behavior-choice",
                            $"{path}.choice",
                            "Behavior choice mode and cardinality must be defined values."));
                    }

                    if (choice.CardinalityKind is BehaviorChoiceCardinalityKind.Exactly
                        or BehaviorChoiceCardinalityKind.UpTo
                        && (choice.CardinalityCount is null || choice.CardinalityCount < 1))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-behavior-cardinality",
                            $"{path}.choice.cardinalityCount",
                            "Exactly and up-to behavior choices require a positive count."));
                    }

                    if (choice.CardinalityKind == BehaviorChoiceCardinalityKind.AnyNumber
                        && choice.CardinalityCount is not null)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-behavior-cardinality",
                            $"{path}.choice.cardinalityCount",
                            "Any-number behavior choices must not carry a count."));
                    }

                    ValidateStringKeys(choice.OptionKeys, $"{path}.choice.optionKeys", violations);
                    ValidateCardIds(choice.CandidateCardIds, $"{path}.choice.candidateCardIds", violations);
                    ValidateCardIds(choice.SelectedCardIds, $"{path}.choice.selectedCardIds", violations);
                    if (choice.SelectedCardIds.Any(cardId => !choice.CandidateCardIds.Contains(cardId)))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-selected-not-candidate",
                            $"{path}.choice.selectedCardIds",
                            "Behavior partial selections must be frozen candidates."));
                    }

                    if (choice.Mode is BehaviorChoiceMode.Mode or BehaviorChoiceMode.OrderSelection
                        && choice.SelectedCardIds.Count > 0)
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.behavior-option-selection-mismatch",
                            $"{path}.choice.selectedCardIds",
                            "Mode and ordered choices select option keys, not card candidates."));
                    }
                }

                break;
            case AbilityFrame ability:
                ValidateBehavior(frame, ResolutionBehaviorIds.Ability, path, violations);
                ValidateCardId(ability.SourceCardId, $"{path}.sourceCardId", violations);
                ValidatePlayer(ability.Controller, $"{path}.controller", violations);
                ValidateZoneId(ability.SourceZoneId, $"{path}.sourceZoneId", violations);
                if (!Enum.IsDefined(ability.SourceZoneKind))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-source-zone-kind",
                        $"{path}.sourceZoneKind",
                        "Ability source zone kind is not a supported value."));
                }

                if (!Enum.IsDefined(ability.Stage))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-stage",
                        $"{path}.stage",
                        "Ability lifecycle stage is not a supported value."));
                }

                if (!StableIdentifier.TryValidate(ability.DefinitionId.Value))
                {
                    violations.Add(new StateInvariantViolation(
                        "id.invalid",
                        $"{path}.definitionId",
                        "Ability definition ID is empty or invalid."));
                }

                if (!StableIdentifier.TryValidate(ability.AbilityKey))
                {
                    violations.Add(new StateInvariantViolation(
                        "id.invalid",
                        $"{path}.abilityKey",
                        "Ability keys must be stable identifiers."));
                }

                if (!StableIdentifier.TryValidate(ability.CatalogFingerprint)
                    || !DeterministicHash.IsSha256Hex(ability.CatalogFingerprint))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-catalog-fingerprint",
                        $"{path}.catalogFingerprint",
                        "Ability frames must retain a lowercase SHA-256 catalog fingerprint."));
                }

                if (!StableIdentifier.TryValidate(ability.DefinitionFingerprint)
                    || !DeterministicHash.IsSha256Hex(ability.DefinitionFingerprint))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-definition-fingerprint",
                        $"{path}.definitionFingerprint",
                        "Ability frames must retain a lowercase SHA-256 definition fingerprint."));
                }

                if (!Enum.IsDefined(ability.Timing))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-timing",
                        $"{path}.timing",
                        "Ability timing is not a supported value."));
                }

                if (!Enum.IsDefined(ability.Activation))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-activation",
                        $"{path}.activation",
                        "Ability activation is not a supported value."));
                }

                if (ability.ParentFrameIdentity is not null
                    && !StableIdentifier.TryValidate(ability.ParentFrameIdentity))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-parent-identity",
                        $"{path}.parentFrameIdentity",
                        "Ability parent frame identities must be stable identifiers."));
                }

                ValidateAbilityCosts(ability.Costs, $"{path}.costs", violations);
                ValidateCardIds(ability.TargetCardIds, $"{path}.targetCardIds", violations);
                if (ability.Usage is null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.missing-ability-usage",
                        $"{path}.usage",
                        "Ability frames must retain an exact usage snapshot."));
                }
                else
                {
                    if (!Enum.IsDefined(ability.Usage.Limit))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-ability-usage-limit",
                            $"{path}.usage.limit",
                            "Ability usage limit is not a supported value."));
                    }

                    if (ability.Usage.UsesThisTurn < 0
                        || (ability.Usage.Limit == BehaviorAbilityUsageLimit.OncePerTurn
                            && ability.Usage.UsesThisTurn > 1))
                    {
                        violations.Add(new StateInvariantViolation(
                            "resolution.invalid-ability-usage",
                            $"{path}.usage.usesThisTurn",
                            "Ability usage count is outside its declared limit."));
                    }
                }

                bool hasClickCost = ability.Costs.Any(static cost => cost is not null && cost.Kind == BehaviorCostKind.Clicks);
                bool hasLoseClickCost = ability.Costs.Any(static cost => cost is not null && cost.Kind == BehaviorCostKind.LoseClick);
                if (ability.Activation == BehaviorAbilityActivation.ClickAction
                    && (ability.Timing != BehaviorAbilityTiming.ActionPhase
                        || !hasClickCost
                        || hasLoseClickCost
                        || ability.Costs.Count(cost => cost is not null && cost.Kind == BehaviorCostKind.Clicks) != 1
                        || ability.Costs.SingleOrDefault(cost => cost is not null && cost.Kind == BehaviorCostKind.Clicks)?.Amount != 1))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-combination",
                        path,
                        "A click-action frame requires exactly one Click cost and action-phase timing."));
                }
                else if (ability.Activation == BehaviorAbilityActivation.PaidAbility && hasClickCost)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-combination",
                        path,
                        "A paid-ability frame may not carry an action Click cost."));
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

    private static void ValidateIceSubroutineFrame(
        IceSubroutineFrame frame,
        string path,
        List<StateInvariantViolation> violations)
    {
        ValidateStable(frame.FrameIdentity, $"{path}.frameIdentity", "ICE subroutine frame identity", violations);
        ValidateStable(frame.RunId.Value, $"{path}.runId", "Run ID", violations);
        ValidateStable(frame.EncounterId.Value, $"{path}.encounterId", "Encounter ID", violations);
        ValidateCardId(frame.IceId, $"{path}.iceId", violations);
        ValidateStable(frame.IceDefinitionId.Value, $"{path}.iceDefinitionId", "ICE definition ID", violations);
        ValidateStable(frame.SubroutineId.Value, $"{path}.subroutineId", "Subroutine ID", violations);
        ValidateStable(frame.ServerId.Value, $"{path}.serverId", "Server ID", violations);
        ValidateZoneId(frame.IceZoneId, $"{path}.iceZoneId", violations);
        if (frame.Ordinal < 0)
        {
            violations.Add(new StateInvariantViolation(
                "resolution.invalid-subroutine-ordinal",
                $"{path}.ordinal",
                "An ICE subroutine ordinal must be nonnegative."));
        }

        if (!Enum.IsDefined(frame.Stage))
        {
            violations.Add(new StateInvariantViolation(
                "resolution.invalid-subroutine-stage",
                $"{path}.stage",
                "ICE subroutine frame stage is not supported."));
        }

        if (!DeterministicHash.IsSha256Hex(frame.CatalogFingerprint))
        {
            violations.Add(new StateInvariantViolation(
                "resolution.invalid-catalog-fingerprint",
                $"{path}.catalogFingerprint",
                "ICE subroutine frames must retain a lowercase SHA-256 catalog fingerprint."));
        }

        string expectedIdentity = IceSubroutineFrame.ComputeIdentity(
            frame.RunId,
            frame.EncounterId,
            frame.IceId,
            frame.SubroutineId,
            frame.Ordinal);
        if (!string.Equals(frame.FrameIdentity, expectedIdentity, StringComparison.Ordinal))
        {
            violations.Add(new StateInvariantViolation(
                "resolution.invalid-subroutine-frame-identity",
                $"{path}.frameIdentity",
                "ICE subroutine frame identity does not match its serialized continuation fields."));
        }

        ValidateCardIds(frame.IceZoneCardIds, $"{path}.iceZoneCardIds", violations);
        if (!frame.IceZoneCardIds.Contains(frame.IceId))
        {
            violations.Add(new StateInvariantViolation(
                "resolution.subroutine-anchor-missing",
                $"{path}.iceZoneCardIds",
                "The active ICE must be retained in the frozen ICE-zone topology."));
        }

        var choiceSet = new HashSet<IceSubroutineChoiceParameters>();
        for (int index = 0; index < frame.Choices.Count; index++)
        {
            IceSubroutineChoiceParameters? choice = frame.Choices[index];
            if (choice is null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.null-subroutine-choice",
                    $"{path}.choices[{index}]",
                    "ICE subroutine choices must not be null."));
                continue;
            }

            if (!string.Equals(choice.FrameIdentity, frame.FrameIdentity, StringComparison.Ordinal))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.subroutine-choice-frame-mismatch",
                    $"{path}.choices[{index}].frameIdentity",
                    "Every ICE subroutine choice must bind the exact parent frame identity."));
            }

            if (!choiceSet.Add(choice))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.duplicate-subroutine-choice",
                    $"{path}.choices[{index}]",
                    "An ICE subroutine frame must not retain duplicate choices."));
            }
        }
    }

    private static void ValidateStable(
        string? value,
        string path,
        string description,
        List<StateInvariantViolation> violations)
    {
        if (!StableIdentifier.TryValidate(value))
        {
            violations.Add(new StateInvariantViolation(
                "id.invalid",
                path,
                $"{description} is empty or invalid."));
        }
    }

    private static void ValidateCardId(CardInstanceId cardId, string path, List<StateInvariantViolation> violations) =>
        ValidateCardIds([cardId], path, violations);

    private static void ValidateZoneId(ZoneId zoneId, string path, List<StateInvariantViolation> violations)
    {
        if (!StableIdentifier.TryValidate(zoneId.Value))
        {
            violations.Add(new StateInvariantViolation("id.invalid", path, "Zone ID is empty or invalid."));
        }
    }

    private static void ValidateCardIds(
        IReadOnlyList<CardInstanceId> cardIds,
        string path,
        List<StateInvariantViolation> violations)
    {
        var seen = new HashSet<CardInstanceId>();
        for (int index = 0; index < cardIds.Count; index++)
        {
            CardInstanceId cardId = cardIds[index];
            if (!StableIdentifier.TryValidate(cardId.Value))
            {
                violations.Add(new StateInvariantViolation("id.invalid", $"{path}[{index}]", "Card ID is empty or invalid."));
            }
            else if (!seen.Add(cardId))
            {
                violations.Add(new StateInvariantViolation("resolution.duplicate-behavior-card", $"{path}[{index}]", $"Card '{cardId}' occurs more than once."));
            }
        }
    }

    private static void ValidateStringKeys(
        IReadOnlyList<string> keys,
        string path,
        List<StateInvariantViolation> violations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < keys.Count; index++)
        {
            string? key = keys[index];
            if (!StableIdentifier.TryValidate(key))
            {
                violations.Add(new StateInvariantViolation("id.invalid", $"{path}[{index}]", "Choice key is empty or invalid."));
            }
            else if (!seen.Add(key))
            {
                violations.Add(new StateInvariantViolation("resolution.duplicate-behavior-key", $"{path}[{index}]", $"Choice key '{key}' occurs more than once."));
            }
        }
    }

    private static void ValidateBehaviorInstructions(
        IReadOnlyList<BehaviorInstruction> instructions,
        string path,
        List<StateInvariantViolation> violations)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index] is null)
            {
                violations.Add(new StateInvariantViolation("resolution.null-behavior-instruction", $"{path}[{index}]", "Behavior instructions must not be null."));
            }
        }
    }

    private static void ValidateAbilityCosts(
        IReadOnlyList<BehaviorCost> costs,
        string path,
        List<StateInvariantViolation> violations)
    {
        for (int index = 0; index < costs.Count; index++)
        {
            BehaviorCost? cost = costs[index];
            string costPath = $"{path}[{index}]";
            if (cost is null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.null-ability-cost",
                    costPath,
                    "Ability costs must not contain null entries."));
                continue;
            }

            if (!Enum.IsDefined(cost.Kind))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-cost-kind",
                    $"{costPath}.kind",
                    "Ability cost kind is not a supported value."));
            }

            bool invalidAmount = cost.Kind switch
            {
                BehaviorCostKind.Credits => cost.Amount < 0,
                BehaviorCostKind.Clicks
                    or BehaviorCostKind.LoseClick
                    or BehaviorCostKind.Counter
                    or BehaviorCostKind.Tags
                    or BehaviorCostKind.HostedCredits
                    or BehaviorCostKind.Additional => cost.Amount <= 0,
                BehaviorCostKind.TrashSelf
                    or BehaviorCostKind.RemoveSelfFromGame => cost.Amount != 1,
                _ => false,
            };
            if (invalidAmount)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-cost-amount",
                    $"{costPath}.amount",
                    "Ability cost amount must not be negative."));
            }

            if (cost.Kind == BehaviorCostKind.Counter
                && !StableIdentifier.TryValidate(cost.CounterName))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-counter-cost",
                    $"{costPath}.counterName",
                    "Counter costs must carry a stable counter name."));
            }

            if (cost.Kind == BehaviorCostKind.Additional
                && !StableIdentifier.TryValidate(cost.AdditionalKey))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-additional-cost",
                    $"{costPath}.additionalKey",
                    "Additional costs must carry a stable additional-cost key."));
            }

            if (cost.AdditionalKind is { } additionalKind
                && !Enum.IsDefined(additionalKind))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-additional-cost",
                    $"{costPath}.additionalKind",
                    "Additional-cost operation is not a supported value."));
            }

            if (cost.Kind == BehaviorCostKind.Additional)
            {
                if (cost.AdditionalKind is null && cost.AdditionalTarget is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-additional-cost",
                        costPath,
                        "A structured additional-cost target requires a typed operation."));
                }

                if (cost.AdditionalKind == BehaviorAdditionalCostKind.TrashCards
                    && (cost.AdditionalTarget is not
                    {
                        Kind: BehaviorTargetKind.ZoneCards,
                        Filter: not null,
                        Reveal: false,
                    }
                    || cost.AdditionalTarget.Filter.OwnerSide is null))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-additional-cost",
                        costPath,
                        "TrashCards requires a ZoneCards target with an explicit owner and reveal=false."));
                }
            }
            else
            {
                if (cost.AdditionalTarget is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-additional-cost",
                        $"{costPath}.additionalTarget",
                        "Only additional costs may carry an additional-cost target."));
                }

                if (cost.AdditionalKind is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-ability-additional-cost",
                        $"{costPath}.additionalKind",
                        "Only additional costs may carry an additional-cost operation."));
                }
            }

            if (cost.Kind != BehaviorCostKind.Counter && cost.CounterName is not null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-counter-cost",
                    $"{costPath}.counterName",
                    "Only counter costs may carry a counter name."));
            }

            if (cost.Kind != BehaviorCostKind.Additional && cost.AdditionalKey is not null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-ability-additional-cost",
                    $"{costPath}.additionalKey",
                    "Only additional costs may carry an additional-cost key."));
            }
        }

        if (costs.Count(static cost => cost is not null
                && cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame) > 1)
        {
            violations.Add(new StateInvariantViolation(
                "resolution.duplicate-ability-self-movement",
                path,
                "An ability may not declare more than one self-movement cost."));
        }
    }

    private static void ValidateBehaviorBindings(
        IReadOnlyList<BehaviorBinding> bindings,
        string path,
        List<StateInvariantViolation> violations)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < bindings.Count; index++)
        {
            BehaviorBinding? binding = bindings[index];
            if (binding is null)
            {
                violations.Add(new StateInvariantViolation("resolution.null-behavior-binding", $"{path}[{index}]", "Behavior bindings must not be null."));
                continue;
            }

            ValidateStringKeys([binding.Key], $"{path}[{index}].key", violations);
            if (!keys.Add(binding.Key))
            {
                violations.Add(new StateInvariantViolation("resolution.duplicate-behavior-binding", $"{path}[{index}].key", $"Behavior binding '{binding.Key}' occurs more than once."));
            }

            ValidateCardIds(binding.CardIds, $"{path}[{index}].cardIds", violations);
        }
    }

    private static void ValidateBehaviorSetAside(
        IReadOnlyList<BehaviorSetAsideCard> cards,
        string path,
        List<StateInvariantViolation> violations)
    {
        var ids = new HashSet<CardInstanceId>();
        for (int index = 0; index < cards.Count; index++)
        {
            BehaviorSetAsideCard? card = cards[index];
            if (card is null)
            {
                violations.Add(new StateInvariantViolation("resolution.null-behavior-set-aside", $"{path}[{index}]", "Behavior set-aside entries must not be null."));
                continue;
            }

            ValidateCardIds([card.CardId], $"{path}[{index}].cardId", violations);
            if (!ids.Add(card.CardId))
            {
                violations.Add(new StateInvariantViolation("resolution.duplicate-behavior-set-aside", $"{path}[{index}].cardId", $"Card '{card.CardId}' occurs more than once in behavior set-aside."));
            }

            if (!StableIdentifier.TryValidate(card.OriginalZoneId.Value))
            {
                violations.Add(new StateInvariantViolation("id.invalid", $"{path}[{index}].originalZoneId", "Original zone ID is empty or invalid."));
            }
        }
    }

    private static void ValidateBehaviorContinuation(
        IReadOnlyList<BehaviorContinuationStep> continuation,
        string path,
        List<StateInvariantViolation> violations)
    {
        for (int index = 0; index < continuation.Count; index++)
        {
            BehaviorContinuationStep? step = continuation[index];
            string stepPath = $"{path}[{index}]";
            if (step is null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.null-behavior-continuation",
                    stepPath,
                    "Behavior continuation entries must not be null."));
                continue;
            }

            if (step.InstructionCursor < 0)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-behavior-continuation-cursor",
                    $"{stepPath}.instructionCursor",
                    "A behavior continuation cursor must not be negative."));
            }

            if (!StableIdentifier.TryValidate(step.Key))
            {
                violations.Add(new StateInvariantViolation(
                    "id.invalid",
                    $"{stepPath}.key",
                    "Behavior continuation keys must be stable identifiers."));
            }

            if (!Enum.IsDefined(step.Kind))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-behavior-continuation-kind",
                    $"{stepPath}.kind",
                    $"'{step.Kind}' is not a supported behavior continuation kind."));
            }

            if (step.OptionKey is not null && !StableIdentifier.TryValidate(step.OptionKey))
            {
                violations.Add(new StateInvariantViolation(
                    "id.invalid",
                    $"{stepPath}.optionKey",
                    "Behavior continuation option keys must be stable identifiers."));
            }

            if (step.Kind == BehaviorContinuationKind.ChoiceOption && step.OptionKey is null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.behavior-continuation-option-missing",
                    stepPath,
                    "A choice-option continuation requires its selected option key."));
            }

            if (step.Kind != BehaviorContinuationKind.ChoiceOption && step.OptionKey is not null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.behavior-continuation-option-unexpected",
                    $"{stepPath}.optionKey",
                    "Only a choice-option continuation may carry an option key."));
            }

            ValidateCardIds(
                step.CardIds,
                $"{stepPath}.cardIds",
                violations);
            if (step.Kind != BehaviorContinuationKind.ChoiceSelection && step.CardIds.Count > 0)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.behavior-continuation-cards-unexpected",
                    $"{stepPath}.cardIds",
                    "Only a choice-selection continuation may carry selected card IDs."));
            }

            if (step.CardIds.Distinct().Count() != step.CardIds.Count)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.duplicate-behavior-continuation-card",
                    $"{stepPath}.cardIds",
                    "A behavior continuation selection must contain distinct card IDs."));
            }
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

    private static void ValidateCardOptions(
        IReadOnlyList<CardInstanceId> options,
        string path,
        List<StateInvariantViolation> violations)
    {
        var ids = new HashSet<CardInstanceId>();
        for (int index = 0; index < options.Count; index++)
        {
            CardInstanceId cardId = options[index];
            if (!StableIdentifier.TryValidate(cardId.Value))
            {
                violations.Add(new StateInvariantViolation(
                    "id.invalid",
                    $"{path}[{index}]",
                    "Card option ID is empty or invalid."));
            }
            else if (!ids.Add(cardId))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.duplicate-card-option",
                    $"{path}[{index}]",
                    $"Card option '{cardId}' occurs more than once."));
            }
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

        if (occurrence.Kind is OccurrenceKind.TurnBegin or OccurrenceKind.TurnEnd)
        {
            if (occurrence.Effect is not TurnBoundaryEffect boundary)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.turn-boundary-effect-missing",
                    $"{path}.effect",
                    "A turn-boundary occurrence must carry a typed turn-boundary effect instruction."));
            }
            else
            {
                if (boundary.Kind != occurrence.Kind)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.turn-boundary-kind-mismatch",
                        $"{path}.effect.kind",
                        "Turn-boundary effect kind must match its occurrence kind."));
                }

                if (occurrence.TargetPlayer != boundary.Target)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.turn-boundary-target-mismatch",
                        $"{path}.effect.target",
                        "Turn-boundary effect target must match its occurrence target player."));
                }

                if (boundary.TurnNumber <= 0)
                {
                    // The first clause reports malformed boundary data while
                    // keeping this validator independent of the enclosing turn
                    // frame; exact cursor/side binding is checked by the state
                    // invariant validator.
                    violations.Add(new StateInvariantViolation(
                        "resolution.invalid-turn-boundary-number",
                        $"{path}.effect.turnNumber",
                        "Turn-boundary effect turn number must be positive."));
                }
            }
        }

        if (occurrence.Kind is OccurrenceKind.CardInstalled
            or OccurrenceKind.CardRezzed
            or OccurrenceKind.ServerApproached
            or OccurrenceKind.IceEncountered
            or OccurrenceKind.RunSuccessful
            or OccurrenceKind.RunEnded
            or OccurrenceKind.AgendaScoredOrStolen
            or OccurrenceKind.CardAccessed
            or OccurrenceKind.HostedCreditPoolEmptied)
        {
            if (occurrence.Effect is not GameplayOccurrenceEffect gameplay)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.gameplay-occurrence-effect-missing",
                    $"{path}.effect",
                    "A gameplay occurrence must carry a typed gameplay occurrence effect."));
            }
            else
            {
                if (gameplay.Kind != occurrence.Kind)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.gameplay-occurrence-kind-mismatch",
                        $"{path}.effect.kind",
                        "Gameplay occurrence effect kind must match its occurrence kind."));
                }

                if (gameplay.Controller != occurrence.Controller)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.gameplay-occurrence-controller-mismatch",
                        $"{path}.effect.controller",
                        "Gameplay occurrence effect controller must match its occurrence controller."));
                }

                if (gameplay.SubjectCardId != occurrence.SourceCardId)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.gameplay-occurrence-subject-mismatch",
                        $"{path}.effect.subjectCardId",
                        "Gameplay occurrence subject must match the occurrence source card."));
                }

                if (gameplay.Kind == OccurrenceKind.ServerApproached
                    && (gameplay.ServerId is null || gameplay.SubjectCardId is not null))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.server-approached-context",
                        $"{path}.effect",
                        "A ServerApproached occurrence must identify its server and must not identify a subject card."));
                }

                if (gameplay.Kind == OccurrenceKind.IceEncountered
                    && (gameplay.ServerId is null || gameplay.SubjectCardId is null))
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.ice-encountered-context",
                        $"{path}.effect",
                        "An IceEncountered occurrence must identify both its encountered ICE subject and server."));
                }

                if (gameplay.Kind == OccurrenceKind.RunEnded && gameplay.Successful is null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.gameplay-occurrence-outcome-missing",
                        $"{path}.effect.successful",
                        "A RunEnded occurrence must carry its success outcome."));
                }
                else if (gameplay.Kind != OccurrenceKind.RunEnded && gameplay.Successful is not null)
                {
                    violations.Add(new StateInvariantViolation(
                        "resolution.gameplay-occurrence-outcome-unexpected",
                        $"{path}.effect.successful",
                        "Only a RunEnded occurrence may carry a success outcome."));
                }

            }
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

        bool catalogFieldPresent = invocation.TriggerKey is not null
            || invocation.CatalogFingerprint is not null
            || invocation.DefinitionFingerprint is not null
            || invocation.SourceOwnerSide is not null
            || invocation.SourceZoneKind is not null;
        if (catalogFieldPresent)
        {
            if (!StableIdentifier.TryValidate(invocation.TriggerKey))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-trigger-key",
                    $"{path}.triggerKey",
                    "A catalog trigger invocation must carry a stable trigger key."));
            }

            if (!StableIdentifier.TryValidate(invocation.CatalogFingerprint)
                || !DeterministicHash.IsSha256Hex(invocation.CatalogFingerprint))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-trigger-catalog-fingerprint",
                    $"{path}.catalogFingerprint",
                    "A catalog trigger invocation must carry a lowercase SHA-256 catalog fingerprint."));
            }

            if (!StableIdentifier.TryValidate(invocation.DefinitionFingerprint)
                || !DeterministicHash.IsSha256Hex(invocation.DefinitionFingerprint))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-trigger-definition-fingerprint",
                    $"{path}.definitionFingerprint",
                    "A catalog trigger invocation must carry a lowercase SHA-256 definition fingerprint."));
            }

            if (invocation.SourceCardId is null
                || invocation.SourceDefinitionId is null
                || invocation.SourceZoneId is null
                || invocation.SourceOwnerSide is null
                || invocation.SourceZoneKind is null)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.missing-trigger-source-snapshot",
                    path,
                    "A catalog trigger invocation must retain its source card, owner, definition, and zone snapshot."));
            }

            if (invocation.SourceOwnerSide is { } sourceOwnerSide
                && !Enum.IsDefined(sourceOwnerSide))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-trigger-source-owner",
                    $"{path}.sourceOwnerSide",
                    "A catalog trigger source owner side must be Corp or Runner."));
            }

            if (invocation.SourceZoneKind is { } sourceZoneKind
                && !Enum.IsDefined(sourceZoneKind))
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.invalid-trigger-source-zone-kind",
                    $"{path}.sourceZoneKind",
                    "A catalog trigger source zone kind must be a defined zone kind."));
            }

            if (invocation.Costs.Instructions.Count != 0 || invocation.Effects.Instructions.Count != 0)
            {
                violations.Add(new StateInvariantViolation(
                    "resolution.catalog-trigger-executable-payload",
                    path,
                    "Catalog trigger invocations must not retain executable cost or effect payloads."));
            }
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
        SetupFrame setup => new SetupFrame(setup.Version, setup.BehaviorId, setup.Side, setup.Redrawn),
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
        TurnFrame turn => new TurnFrame(
            turn.Version,
            turn.BehaviorId,
            turn.ActiveSide,
            turn.TurnNumber,
            turn.Stage,
            turn.PriorityWindow,
            turn.PriorityHolder,
            turn.ConsecutivePasses,
            turn.ForcedActionEnd),
        TurnDiscardFrame turnDiscard => new TurnDiscardFrame(
            turnDiscard.Version,
            turnDiscard.BehaviorId,
            turnDiscard.Owner,
            turnDiscard.RequiredCount,
            turnDiscard.Candidates,
            turnDiscard.Selected),
        InstallFrame install => new InstallFrame(
            install.Version,
            install.BehaviorId,
            install.Owner,
            install.CardId,
            install.DefinitionId,
            install.CardClass,
            install.SourceZoneId,
            install.SourceZoneKind,
            install.DestinationKind,
            install.ServerId,
            install.CreateNewRemote,
            install.InstallCost,
            install.CandidateReplacementCardIds,
            install.RequiredReplacementCardIds,
            install.SelectedReplacementCardIds,
            install.DestinationZoneId,
            install.CandidateReplacementDefinitionIds,
            install.InitiationKind,
            install.ParentFrameIdentity,
            install.CostQuote, install.InwardAnchorIceId, install.HostCardId),
        CardPlayFrame cardPlay => new CardPlayFrame(
            cardPlay.Version,
            cardPlay.BehaviorId,
            cardPlay.Owner,
            cardPlay.Side,
            cardPlay.CardId,
            cardPlay.DefinitionId,
            cardPlay.CardClass,
            cardPlay.SourceZoneId,
            cardPlay.SourceZoneKind,
            cardPlay.PlayAreaZoneId,
            cardPlay.DiscardZoneId,
            cardPlay.CreditCost,
            cardPlay.CatalogFingerprint,
            cardPlay.DefinitionFingerprint),
        RunTimingFrame runTiming => new RunTimingFrame(
            runTiming.Version,
            runTiming.BehaviorId,
            runTiming.RunId,
            runTiming.Step),
        IceSubroutineFrame iceSubroutine => new IceSubroutineFrame(
            iceSubroutine.Version,
            iceSubroutine.BehaviorId,
            iceSubroutine.FrameIdentity,
            iceSubroutine.RunId,
            iceSubroutine.EncounterId,
            iceSubroutine.IceId,
            iceSubroutine.IceDefinitionId,
            iceSubroutine.SubroutineId,
            iceSubroutine.Ordinal,
            iceSubroutine.ServerId,
            iceSubroutine.IceZoneId,
            iceSubroutine.IceZoneCardIds,
            iceSubroutine.CatalogFingerprint,
            iceSubroutine.Stage,
            iceSubroutine.Choices,
            iceSubroutine.DamageTrashedCardId),
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
        BehaviorResolutionFrame behavior => new BehaviorResolutionFrame(
            behavior.Version,
            behavior.BehaviorId,
            behavior.DefinitionId,
            behavior.SourceCardId,
            behavior.Controller,
            behavior.ActingPlayer,
            behavior.CatalogFingerprint,
            behavior.DefinitionFingerprint,
            behavior.Stage,
            behavior.InstructionCursor,
            behavior.Bindings,
            behavior.SetAside,
            behavior.Choice,
            behavior.Continuation,
            behavior.SetAsideZoneId,
            behavior.ParentBehaviorId,
            behavior.FrameIdentity,
            behavior.SourceZoneId,
            behavior.SourceZoneKind,
            behavior.ParentFrameIdentity,
            behavior.AbilityKey,
            behavior.TriggerKey,
            behavior.CatalogTriggerSourceMovedBeforeStart,
            behavior.OccurrenceContinuation),
        AbilityFrame ability => new AbilityFrame(
            ability.Version,
            ability.BehaviorId,
            ability.DefinitionId,
            ability.SourceCardId,
            ability.AbilityKey,
            ability.Controller,
            ability.Timing,
            ability.Activation,
            ability.Costs,
            ability.TargetCardIds,
            ability.Usage,
            ability.CatalogFingerprint,
            ability.DefinitionFingerprint,
            ability.SourceZoneId,
            ability.SourceZoneKind,
            ability.Stage,
            ability.ParentFrameIdentity,
            ability.AdditionalCostSelections),
        _ => throw new InvalidOperationException($"No cloner is registered for frame type '{frame.GetType().Name}'."),
    };
}
