using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Detached, typed policy inputs. Nullable facts are unknown, not zero.</summary>
public sealed record AgentFeatures(PlayerSide Side, AgentDecision? Decision,
    ImmutableArray<AgentAction> Actions, AgentBoard Board, AgentPrivateCards PrivateCards)
{
    public const string Version = "agent-features-v2";
    /// <summary>Adds public advancement counters on hidden installed cards.</summary>
    public const string VersionWithHiddenAdvancements = "agent-features-v3";

    public static bool IsSupported(string? version) => version is Version or VersionWithHiddenAdvancements;
    public AgentPlayer Self => Side == PlayerSide.Corp ? Board.Players.Corp : Board.Players.Runner;
    public AgentPlayer Opponent => Side == PlayerSide.Corp ? Board.Players.Runner : Board.Players.Corp;
    public ImmutableArray<AgentCard> Hand => Side == PlayerSide.Corp ? PrivateCards.Hand : PrivateCards.Grip;

    public IEnumerable<(AgentCard Card, string Location)> VisibleCards()
    {
        foreach (AgentServer server in Board.Servers)
        {
            foreach (AgentCard? card in server.Ice) if (card is not null) yield return (card, $"server/{server.Id}/ice");
            foreach (AgentCard? card in server.Root) if (card is not null) yield return (card, $"server/{server.Id}/root");
        }
        foreach (var zone in new (ImmutableArray<AgentCard> Cards, string Name)[]
        {
            (Board.CorpDiscard, "corp-discard"), (Board.RunnerHeap, "runner-heap"),
            (Board.RunnerRig, "runner-rig"), (Board.CorpScore, "corp-score"), (Board.RunnerScore, "runner-score"),
            (Board.CorpIdentity, "corp-identity"), (Board.RunnerIdentity, "runner-identity"),
            (Board.CorpPlayArea, "corp-play"), (Board.RunnerPlayArea, "runner-play"),
            (Board.CorpRemovedFromGame, "corp-removed"), (Board.RunnerRemovedFromGame, "runner-removed"),
            (Board.RunnerHostedFaceUp, "runner-hosted"), (Board.CorpHostedFaceUp, "corp-hosted"),
            (PrivateCards.Hand, "own-hand"), (PrivateCards.Grip, "own-hand"),
            (PrivateCards.FaceDownArchives, "corp-discard"), (PrivateCards.FaceDownIce, "own-ice"),
            (PrivateCards.ChoiceCards, "private-choice"),
        }) foreach (AgentCard card in zone.Cards) yield return (card, zone.Name);
        if (Board.Run is { } run)
        {
            if (run.CurrentIce is { } ice) yield return (ice, $"server/{run.AttackedServer}/ice");
            foreach (AgentCard? card in run.Breach.Chosen.Concat(run.Breach.Accessed).Concat(run.Breach.Candidates))
                if (card is not null) yield return (card, "access");
        }
    }
}

// Neither prompt prose nor action descriptions are policy inputs.
public sealed record AgentDecision(DecisionId Id, long Revision, PlayerId Owner, DecisionKind Kind);
public sealed record AgentAction(ActionId Id, ActionKind Kind, ActionParameters Parameters);

public sealed record AgentBoard
{
    public required AgentTurn Turn { get; init; }
    public required AgentPlayers Players { get; init; }
    public required AgentOutcome Outcome { get; init; }
    public required AgentHiddenCounts HiddenZoneCounts { get; init; }
    public ImmutableArray<AgentServer> Servers { get; init; } = [];
    public ImmutableArray<AgentCard> CorpDiscard { get; init; } = [];
    public ImmutableArray<AgentCard> RunnerHeap { get; init; } = [];
    public ImmutableArray<AgentCard> RunnerRig { get; init; } = [];
    public ImmutableArray<AgentCard> CorpScore { get; init; } = [];
    public ImmutableArray<AgentCard> RunnerScore { get; init; } = [];
    public ImmutableArray<AgentCard> CorpIdentity { get; init; } = [];
    public ImmutableArray<AgentCard> RunnerIdentity { get; init; } = [];
    public ImmutableArray<AgentCard> CorpPlayArea { get; init; } = [];
    public ImmutableArray<AgentCard> RunnerPlayArea { get; init; } = [];
    public ImmutableArray<AgentCard> CorpRemovedFromGame { get; init; } = [];
    public ImmutableArray<AgentCard> RunnerRemovedFromGame { get; init; } = [];
    /// <summary>EL.4: Runner cards hosted face up on a console (Bling, Madani), playable as if in the grip.</summary>
    public ImmutableArray<AgentCard> RunnerHostedFaceUp { get; init; } = [];
    /// <summary>EL.7: Corp cards hosted face up on a Runner card (Detente).</summary>
    public ImmutableArray<AgentCard> CorpHostedFaceUp { get; init; } = [];
    public AgentDiscardCounts? ZoneCounts { get; init; }
    public AgentTurnHistory? TurnFacts { get; init; }
    public AgentRun? Run { get; init; }
    public AgentRunEnd? RunEnd { get; init; }
    public AgentDamage? PendingDamage { get; init; }
    public ImmutableArray<AgentValueQuote> ConditionalValues { get; init; } = [];
    public ImmutableArray<AgentIcePosition> IcePositions { get; init; } = [];

    /// <summary>
    /// Public advancement counters on installed cards hidden from the viewer,
    /// by position only. Null for feature version v2, and omitted from the
    /// digest when null, so v2 digests are unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<AgentHiddenAdvancement>? HiddenAdvancements { get; init; }
}

public sealed record AgentHiddenAdvancement(string ServerId, string Zone, int Index, int AdvancementCounters);

public sealed record AgentPrivateCards
{
    public ImmutableArray<AgentCard> Hand { get; init; } = [];
    public ImmutableArray<AgentCard> Grip { get; init; } = [];
    public ImmutableArray<AgentCard> FaceDownIce { get; init; } = [];
    public ImmutableArray<AgentCard> FaceDownArchives { get; init; } = [];
    public ImmutableArray<AgentCard> ChoiceCards { get; init; } = [];
    public ImmutableArray<AgentTrigger> Triggers { get; init; } = [];
}

public sealed record AgentTrigger(string Id, string Key, TriggerMode Mode, AgentCard Source);

public sealed record AgentTurn([property: JsonRequired] PlayerSide ActiveSide, [property: JsonRequired] TurnPhase Phase,
    [property: JsonRequired] int TurnNumber, TurnStep? Step);
public sealed record AgentPlayers([property: JsonRequired] AgentPlayer Corp, [property: JsonRequired] AgentPlayer Runner);
public sealed record AgentPlayer([property: JsonRequired] string Id, [property: JsonRequired] int Credits,
    [property: JsonRequired] int Clicks, [property: JsonRequired] int AgendaPoints,
    int? MaximumHandSize, int? Tags, int? CoreDamage, int? UsedMemory, int? MemoryLimit);
public sealed record AgentOutcome(GameOutcomeKind Kind, PlayerSide? Winner, GameWinReason? Reason);
public sealed record AgentHiddenCounts([property: JsonRequired] int CorpHand, [property: JsonRequired] int CorpDeck,
    [property: JsonRequired] int RunnerGrip, [property: JsonRequired] int RunnerStack);
public sealed record AgentDiscardCounts(int CorpDiscard);
public sealed record AgentServer(string Id, ServerKind Kind, ImmutableArray<AgentCard?> Ice, ImmutableArray<AgentCard?> Root);
public sealed record AgentCard
{
    public required string Id { get; init; }
    public required string DefinitionId { get; init; }
    public bool? Rezzed { get; init; }
    public int? AdvancementCounters { get; init; }
    public string? HostCardId { get; init; }
    public ImmutableArray<AgentCounter> Counters { get; init; } = [];
}
public sealed record AgentCounter(string CounterName, CardCounterPurpose Purpose, int Count, HostedCreditSpendRestriction? SpendRestriction);
public sealed record AgentTurnHistory(AgentSideHistory Corp, AgentSideHistory Runner);
public sealed record AgentSideHistory(AgentTurnFacts? ThisTurn, AgentTurnFacts? LastTurn);
public sealed record AgentTurnFacts
{
    public PlayerSide Side { get; init; }
    public int TurnNumber { get; init; }
    public int ClicksAllotted { get; init; }
    public int ClicksLost { get; init; }
    public bool TurnBegan { get; init; }
    public bool MandatoryDrawPerformed { get; init; }
    public bool ActionPhaseEnded { get; init; }
    public bool DiscardPhaseEnded { get; init; }
    public bool SuccessfulRun { get; init; }
    public int ProgramInstallOccurrenceCount { get; init; }
    public int AccessedCardCount { get; init; }
    public int TrashedCardCount { get; init; }
    public ImmutableArray<string> InstalledCardIds { get; init; } = [];
    public ImmutableArray<string> ProgramInstallOccurrenceIds { get; init; } = [];
    public ImmutableArray<string> HqBreachOccurrenceServerIds { get; init; } = [];
    public ImmutableArray<string> AttemptedRunServerIds { get; init; } = [];
    public ImmutableArray<string> SuccessfulRunServerIds { get; init; } = [];
    public ImmutableArray<string> AccessedCardIds { get; init; } = [];
    public ImmutableArray<string> TrashedCardIds { get; init; } = [];
    public ImmutableArray<AgentAbilityUse> UsedAbilityKeys { get; init; } = [];
    public ImmutableArray<AgentFirstUse> FirstXUseKeys { get; init; } = [];
    public ImmutableArray<AgentAgendaChange> AgendaTurnChanges { get; init; } = [];
    public ImmutableArray<AgentTagChange> TagTurnChanges { get; init; } = [];
}
public sealed record AgentAbilityUse(string SourceCardId, string AbilityKey);
public sealed record AgentFirstUse(string SourceCardId, string Key, int Count);
public sealed record AgentAgendaChange(string CardId, PlayerSide BeneficiarySide, int Points, AgendaTurnChangeKind Kind);
public sealed record AgentTagChange(string AffectedPlayerId, int Delta);

public sealed record AgentRun
{
    public required string Id { get; init; }
    public required string AttackedServer { get; init; }
    public PlayerSide InitiatingSide { get; init; }
    public AgentCard? InitiatingSource { get; init; }
    public RunCrPhase Phase { get; init; }
    public RunTimingStep Step { get; init; }
    public int CurrentIceIndex { get; init; }
    public AgentCard? CurrentIce { get; init; }
    public bool Successful { get; init; }
    public bool Unsuccessful { get; init; }
    public RunEndReason? EndReason { get; init; }
    public AgentRunPriority? Priority { get; init; }
    public AgentEncounter? Encounter { get; init; }
    public required AgentBreach Breach { get; init; }
    public int RunCredits { get; init; }
    public int? AccessOccurrenceCount { get; init; }
    public AgentRunCues? Slice45Cues { get; init; }
    public ImmutableArray<AgentLingeringEffect> LingeringEffects { get; init; } = [];
    public ImmutableArray<AgentBreachModifier> BreachAccessModifiers { get; init; } = [];
}
public sealed record AgentRunPriority(RunPriorityWindowKind Window, PlayerSide Holder, int ConsecutivePasses,
    bool CorpMayRezApproachedIce, bool CorpMayRezNonIce);
public sealed record AgentEncounter
{
    public required string Id { get; init; }
    public AgentCard? Ice { get; init; }
    public RunEncounterStatus Status { get; init; }
    public ImmutableArray<AgentSubroutine> Subroutines { get; init; } = [];
    public AgentValueQuote? StrengthQuote { get; init; }
    public ImmutableArray<AgentBreakerStrength> BreakerStrengths { get; init; } = [];
}
public sealed record AgentSubroutine(int Ordinal, bool Broken, bool Resolved);
public sealed record AgentBreakerStrength(string CardId, AgentValueQuote StrengthQuote);
public sealed record AgentValueQuote
{
    public string? Key { get; init; }
    public BehaviorValueKind ValueKind { get; init; }
    public int BaseValue { get; init; }
    public int EffectiveValue { get; init; }
    public ImmutableArray<AgentValueTerm> Terms { get; init; } = [];
}
public sealed record AgentValueTerm(BehaviorValueSourceKind Source, BehaviorValueOperation Operation, int Delta,
    string ReasonKey, string? SourceDefinitionId, string? SourceCardId, int? Quantity, int? UnitDelta, string? VariableKey);
public sealed record AgentBreach(ImmutableArray<AgentCard?> Candidates, int? RandomAccessLimit,
    BreachHiddenCandidateSource? HiddenCandidateSource, ImmutableArray<AgentCard?> Chosen,
    ImmutableArray<AgentCard?> Accessed, RunAccessState AccessState);
public sealed record AgentRunCues(bool RunRestrictionActive, bool BioroidAbilityReady, bool MayflyBreakArmed);
public sealed record AgentLingeringEffect(string Id, string BehaviorId, AgentCard? SourceCard,
    RunLingeringEffectExpiry Expiry, RunLingeringEffectKind? Kind, AgentValueAdjustment? ValueAdjustment);
public sealed record AgentValueAdjustment(BehaviorValueKind ValueKind, BehaviorValueSourceKind Source,
    BehaviorValueOperation Operation, string SourceCardId, string TargetCardId, int Delta, string ReasonKey,
    string? VariableKey, int? Quantity, int? UnitDelta);
public sealed record AgentBreachModifier(string? SourceCardId, string AttackedServerId, int Delta,
    string ReasonKey, RunBreachAccessModifierSourceKind? SourceKind);
public sealed record AgentRunEnd(string RunId, string ServerId, int AccessOccurrenceCount);
public sealed record AgentDamage(DamageKind Kind, int Amount, string TargetId, string? SourceCardId, bool FlatlineRisk);
public sealed record AgentIcePosition(string CardId, string ServerId, int Index);
