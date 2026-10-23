using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>What auto-pass needs to know about a priority window besides its actions.</summary>
public sealed record PriorityWindow(bool InRun, bool Encountering)
{
    /// <summary>The Runner is past every piece of ICE protecting the attacked server.</summary>
    public bool PastIce { get; init; }

    /// <summary>An encounter whose subroutines are all broken or resolved: pumping or breaking more changes nothing.</summary>
    public bool NothingToBreak { get; init; }

    /// <summary>The last windows before the window owner's own turn: the other side's discard step, or the owner's draw step.</summary>
    public bool BeforeOwnTurn { get; init; }

    /// <summary>
    /// The window owner's own action phase. Its rez windows stay open for the
    /// player, because the client shows them as part of the turn (invisible windows).
    /// </summary>
    public bool OwnActionPhase { get; init; }

    /// <summary>Cards installed in the attacked server, by instance ID.</summary>
    public IReadOnlySet<string> AttackedServerCards { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>ICE protecting the attacked server, by instance ID. Its rez on approach always matters.</summary>
    public IReadOnlySet<string> AttackedServerIce { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>When the offered rezzes can matter, by card instance ID; a card not listed is <see cref="RezTiming.Anytime"/>.</summary>
    public IReadOnlyDictionary<string, RezTiming> RezTimings { get; init; } = new Dictionary<string, RezTiming>(StringComparer.Ordinal);

    /// <summary>The offered paid abilities that act on a run (see <see cref="RunAbilityClassifier"/>), by ability key.</summary>
    public IReadOnlySet<string> RunAbilities { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public static PriorityWindow From(GameState state, PlayerSide owner, IBehaviorCatalog? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var timings = new Dictionary<string, RezTiming>(StringComparer.Ordinal);
        if (behavior is not null)
            foreach (RezCardParameters rez in (state.PendingDecision?.LegalActions ?? []).Select(action => action.Parameters).OfType<RezCardParameters>())
                timings[rez.CardId.Value] = RezTimingClassifier.Classify(behavior, state.GetCard(rez.CardId).DefinitionId);
        bool inRun = state.RunState is { EndReason: null };
        RunState? run = inRun ? state.RunState : null;
        IReadOnlySet<string> runAbilities = RunAbilityClassifier.Offered(behavior,
            (state.PendingDecision?.LegalActions ?? []).Select(action => action.Parameters));
        return new(inRun, run?.Encounter is { Status: RunEncounterStatus.Initialized })
        {
            PastIce = run?.CurrentIceIndex < 0,
            NothingToBreak = run?.Encounter is { Status: RunEncounterStatus.Initialized } encounter
                && encounter.Subroutines.All(subroutine => subroutine.Broken || subroutine.Resolved),
            BeforeOwnTurn = BeforeTurn(owner, state.Turn.ActiveSide, state.Turn.Step),
            OwnActionPhase = state.Turn.ActiveSide == owner && state.Turn.Step is TurnStep.CorpAction or TurnStep.RunnerAction,
            AttackedServerCards = run is null ? new HashSet<string>(StringComparer.Ordinal)
                : state.Zones.Where(zone => zone.ServerId == run.AttackedServer).SelectMany(zone => zone.CardIds)
                    .Select(id => id.Value).ToHashSet(StringComparer.Ordinal),
            AttackedServerIce = run is null ? new HashSet<string>(StringComparer.Ordinal)
                : state.Zones.Where(zone => zone.ServerId == run.AttackedServer && zone.Kind == ZoneKind.ServerIce)
                    .SelectMany(zone => zone.CardIds).Select(id => id.Value).ToHashSet(StringComparer.Ordinal),
            RezTimings = timings,
            RunAbilities = runAbilities,
        };
    }

    public static PriorityWindow From(MatchViewModel view, IBehaviorCatalog? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var timings = new Dictionary<string, RezTiming>(StringComparer.Ordinal);
        if (behavior is not null)
        {
            Dictionary<string, CardViewModel> installed = view.Servers.SelectMany(server => server.Root.Concat(server.Ice))
                .OfType<CardViewModel>().GroupBy(card => card.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (RezCardParameters rez in view.LegalActions.Select(action => action.Parameters).OfType<RezCardParameters>())
                if (installed.TryGetValue(rez.CardId.Value, out CardViewModel? card) && card.DefinitionId is { } definition)
                    timings[rez.CardId.Value] = RezTimingClassifier.Classify(behavior, new CardDefinitionId(definition));
        }
        RunFocusViewModel? run = view.Run is { EndReason: null } active ? active : null;
        return new(run is not null, run?.Encounter is { Status: RunEncounterStatus.Initialized })
        {
            PastIce = run?.CurrentIceIndex < 0,
            NothingToBreak = run?.Encounter is { Status: RunEncounterStatus.Initialized } encounter && encounter.RemainingSubroutines == 0,
            BeforeOwnTurn = view.Turn.ActiveSide is { } side && view.Turn.Step is { } step && BeforeTurn(view.Viewer, side, step),
            OwnActionPhase = view.Turn.ActiveSide == view.Viewer && view.Turn.Step is TurnStep.CorpAction or TurnStep.RunnerAction,
            AttackedServerCards = view.Servers.Where(server => server.Id == run?.AttackedServerId)
                .SelectMany(server => server.Root.Concat(server.Ice)).OfType<CardViewModel>().Select(card => card.Id)
                .ToHashSet(StringComparer.Ordinal),
            AttackedServerIce = view.Servers.Where(server => server.Id == run?.AttackedServerId)
                .SelectMany(server => server.Ice).OfType<CardViewModel>().Select(card => card.Id)
                .ToHashSet(StringComparer.Ordinal),
            RezTimings = timings,
            RunAbilities = RunAbilityClassifier.Offered(behavior, view.LegalActions.Select(action => action.Parameters)),
        };
    }

    private static bool BeforeTurn(PlayerSide owner, PlayerSide active, TurnStep step) => active == owner
        ? step == TurnStep.CorpDraw
        : step is TurnStep.RunnerDiscard or TurnStep.CorpDiscard;
}

/// <summary>
/// Which of a player's priority windows the client passes for them (UI plan U3).
/// About 70% of all decisions are passes, so a player who opts in only stops
/// where passing is a real choice: an encounter (breaking, bioroid clicks), an
/// ICE rez, a score, or a rez at a moment that matters. After chiriboga's
/// usability rule, a rez matters in the player's own action phase (the client
/// shows those windows as part of the turn), in the last windows before their
/// own turn (when assets are usually rezzed), for ICE the Runner approaches,
/// and during a run once the Runner is past the ICE, for a card in the
/// attacked server. A paid ability that acts on a run stops the window during
/// a run. Other rez windows, paid abilities and pumps outside an encounter
/// pass; the player can still stop at the next window on request (playtests,
/// 27 September).
/// </summary>
public static class AutoPassPolicy
{
    /// <summary>Whether a pass is the only legal action; such windows always pass when auto-pass is on at all.</summary>
    public static bool OnlyPass(IReadOnlyList<LegalAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.Count == 1 && actions[0].Kind == ActionKind.PassPriority;
    }

    /// <summary>Whether a window with other options is optional enough to pass for the player.</summary>
    public static bool OptionalWindow(IReadOnlyList<LegalAction> actions, PriorityWindow window)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(window);
        return actions.Any(action => action.Kind == ActionKind.PassPriority)
            && actions.All(action => action.Kind switch
            {
                ActionKind.PassPriority => true,
                // A paid ability that ends, moves or restricts the run is the reason its card is in the deck (The Red Room,
                // Event Horizon, LEO Construction, Proprionegation, Perfect Recall); other paid abilities pass.
                ActionKind.UseAbility => !(window.InRun && action.Parameters is AbilityActionParameters ability
                    && window.RunAbilities.Contains(ability.AbilityKey)),
                // Playtest, 29 September: once every subroutine is broken, the encounter has nothing left to decide.
                ActionKind.PumpBreaker or ActionKind.BreakSubroutines or ActionKind.UseBioroidAbility => !window.Encountering || window.NothingToBreak,
                ActionKind.RezCard => !RezMatters(action, window),
                _ => false,
            });
    }

    private static bool RezMatters(LegalAction action, PriorityWindow window)
    {
        if (action.Parameters is not RezCardParameters rez) return window.BeforeOwnTurn || window.OwnActionPhase;
        string id = rez.CardId.Value;
        bool pastIceOnItsServer = window.InRun && window.PastIce && window.AttackedServerCards.Contains(id);
        if (window.InRun && window.AttackedServerIce.Contains(id)) return true;
        return window.RezTimings.GetValueOrDefault(id, RezTiming.Anytime) switch
        {
            RezTiming.Never => false,
            RezTiming.RunOnServer => pastIceOnItsServer,
            _ => window.BeforeOwnTurn || window.OwnActionPhase || pastIceOnItsServer,
        };
    }

    public static ActionId PassAction(IReadOnlyList<LegalAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return actions.First(action => action.Kind == ActionKind.PassPriority).Id;
    }
}

/// <summary>Which paid abilities act on a run: their effects end it, move the Runner, or change what the run can do.</summary>
public static class RunAbilityClassifier
{
    private static readonly HashSet<BehaviorInstructionKind> RunEffects =
    [
        BehaviorInstructionKind.EndRun, BehaviorInstructionKind.JackOut, BehaviorInstructionKind.MoveRunnerToServer,
        BehaviorInstructionKind.ChangeAttackedServer, BehaviorInstructionKind.RestrictRun, BehaviorInstructionKind.SwapApproachedIce,
        BehaviorInstructionKind.BypassEncounteredIce, BehaviorInstructionKind.EncounteredIceGainsSubroutine,
        BehaviorInstructionKind.ModifyAllIceStrengthForRun, BehaviorInstructionKind.AddBreachAccess,
    ];

    /// <summary>The ability keys among <paramref name="offered"/> that act on a run; none without a behavior catalog.</summary>
    public static IReadOnlySet<string> Offered(IBehaviorCatalog? behavior, IEnumerable<ActionParameters> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (behavior is null) return keys;
        foreach (AbilityActionParameters ability in offered.OfType<AbilityActionParameters>())
        {
            if (behavior.TryGet(ability.DefinitionId, out CardBehaviorDefinition spec)
                && spec.PaidAbilities.FirstOrDefault(candidate => candidate.Key == ability.AbilityKey) is { } paid
                && ActsOnRun(paid.Instructions))
                keys.Add(ability.AbilityKey);
        }
        return keys;
    }

    private static bool ActsOnRun(IEnumerable<BehaviorInstruction> instructions) => instructions.Any(instruction =>
        RunEffects.Contains(instruction.Kind)
        || instruction.Branch is { } branch && (ActsOnRun(branch.WhenTrue) || ActsOnRun(branch.WhenFalse)));
}

/// <summary>When rezzing a card can matter to the player.</summary>
public enum RezTiming
{
    /// <summary>Its abilities are useful at any time: rez windows stop as usual.</summary>
    Anytime,
    /// <summary>It acts only during a run on its own server: stop only there, once past the ICE.</summary>
    RunOnServer,
    /// <summary>It works unrezzed: never stop for its rez.</summary>
    Never,
}

/// <summary>Reads a card's rez timing from its behavior definition.</summary>
public static class RezTimingClassifier
{
    private static readonly HashSet<OccurrenceKind> RunOccurrences =
    [
        OccurrenceKind.ServerApproached, OccurrenceKind.RunEnded, OccurrenceKind.RunSuccessful,
        OccurrenceKind.CardAccessed, OccurrenceKind.IceEncountered, OccurrenceKind.AccessedCardTrashed,
    ];

    public static RezTiming Classify(IBehaviorCatalog behavior, CardDefinitionId definitionId)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        if (!behavior.TryGet(definitionId, out CardBehaviorDefinition spec) || spec.Triggers.Count == 0
            || spec.PaidAbilities.Count > 0 || spec.StaticModifiers.Count > 0 || spec.Instructions.Count > 0)
            return RezTiming.Anytime;
        if (spec.Triggers.All(trigger => trigger.ActiveWhileUnrezzed)) return RezTiming.Never;
        return spec.Triggers.All(trigger => RunOccurrences.Contains(trigger.Occurrence) && trigger.RequireSourceOnAttackedServer)
            ? RezTiming.RunOnServer : RezTiming.Anytime;
    }
}
