using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Observation-only metadata queries shared by the strategic evaluators.</summary>
internal sealed class HeuristicContext
{
    private readonly Dictionary<string, AgentCard> _visible;

    public HeuristicContext(AgentFeatures features, ICardEvaluationCatalog catalog, OfflineAgentConfiguration configuration,
        int generation = 1)
    {
        Features = features;
        Catalog = catalog;
        Configuration = configuration;
        Generation = generation;
        _visible = features.VisibleCards().Select(pair => pair.Card).Concat(features.PrivateCards.Triggers.Select(trigger => trigger.Source))
            .GroupBy(card => card.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public AgentFeatures Features { get; }

    /// <summary>The agent's memory, for versions that read it (Runner v10's central memory); null otherwise.</summary>
    public AgentKnowledge? Knowledge { get; init; }
    public ICardEvaluationCatalog Catalog { get; }
    public OfflineAgentConfiguration Configuration { get; }

    /// <summary>1 for the frozen M5 policies (Runner v1, Corp v2); 2 enables the A2 terms.</summary>
    /// <summary>
    /// The amount an instruction applies: its static amount, a source counter
    /// times a multiplier, or (Elevation) a typed amount estimated from the
    /// source and the observed board.
    /// </summary>
    /// <summary>A card-effect install choice (Synapse Global, Off the Books) valued as the equivalent install action.</summary>
    public static InstallCardParameters AsInstall(BehaviorInstallChoice choice) =>
        new(choice.CardId, choice.DestinationKind, choice.ServerId, choice.CreateNewRemote, choice.CreditCost);

    public static int EffectAmount(HeuristicContext context, BehaviorInstruction instruction, AgentCard? source)
    {
        if (instruction.AmountOperand is not { } operand)
        {
            return instruction.Amount;
        }

        int counted = operand.Kind switch
        {
            null or BehaviorAmountKind.SourceCounter =>
                source?.Counters.Where(counter => counter.CounterName == operand.SourceCounterName).Sum(counter => counter.Count) ?? 0,
            BehaviorAmountKind.SourceAdvancementCounters => source?.AdvancementCounters ?? 0,
            BehaviorAmountKind.SourceExcessAdvancement => 1,
            BehaviorAmountKind.ControllerClicks => context.Self.Clicks,
            BehaviorAmountKind.ChosenCardInstallCost => 2,
            BehaviorAmountKind.Recorded => 1,
            _ => 0,
        };
        return counted * operand.Multiplier + operand.Offset;
    }

    public int Generation { get; }

    /// <summary>
    /// Bot loop 1's deferred fixes (Runner v24, Corp v28): an Elevation subroutine written as a behavior (N-Pot, Lamplighter,
    /// Semak-samun) does what its instructions do; earlier versions saw it as harmless.
    /// </summary>
    public bool ReadsBehaviorSubroutines => Features.Side == PlayerSide.Runner ? Generation >= 20 : Generation >= 23;

    public AgentPlayer Self => Features.Self;
    public AgentPlayer Opponent => Features.Opponent;
    public AgentCard? Card(CardInstanceId id) => _visible.GetValueOrDefault(id.Value);
    public CardEvaluationDescriptor? Definition(AgentCard? card) => card is not null && Catalog.TryGet(new(card.DefinitionId), out var descriptor) ? descriptor : null;
    public CardEvaluationDescriptor? Definition(CardInstanceId id) => Definition(Card(id));
    public AgentServer? Server(ServerId? id) => id is { } server ? Features.Board.Servers.FirstOrDefault(value => value.Id == server.Value) : null;
    public AgentServer? ServerOf(string cardId) => Features.Board.Servers.FirstOrDefault(server => server.Root.Concat(server.Ice).Any(card => card?.Id == cardId));
    private RunnerArchetypeProfile? _runnerProfile;

    /// <summary>The Runner's archetype profile (A8, Runner v11); generic for earlier versions.</summary>
    public RunnerArchetypeProfile RunnerProfile => _runnerProfile ??= Features.Side == PlayerSide.Runner && Generation >= 8
        ? WithPersonality(RunnerArchetypes.Profile((RunnerArchetype)Parameter("archetype-primary", 0), (RunnerArchetype)Parameter("archetype-secondary", 0)))
        : RunnerArchetypeProfile.Generic;

    /// <summary>A6: a personality leads with its archetype's traits, the deck's own profile second.</summary>
    private RunnerArchetypeProfile WithPersonality(RunnerArchetypeProfile deck) =>
        AgentPersonalities.RunnerArchetypeOf(AgentPersonalities.Of(Configuration)) is { } archetype
            ? RunnerArchetypeProfile.Blend(RunnerArchetypes.Profile(archetype), deck, AgentPersonalities.DeckWeight)
            : deck;

    /// <summary>Whether the Runner's installed breakers cover barriers, code gates and sentries.</summary>
    public bool RigComplete
    {
        get
        {
            IceType?[] types = [.. Features.Board.RunnerRig.Select(card => Definition(card)?.Icebreaker?.InterfaceType).Distinct()];
            return types.Contains(IceType.Any) || types.Count(type => type is not null) >= 3;
        }
    }

    private CorpArchetypeProfile? _corpProfile;

    /// <summary>The Corp's archetype profile (A8, Corp v12); generic for earlier versions and undeclared decks.</summary>
    public CorpArchetypeProfile CorpProfile => _corpProfile ??= Generation >= 7
        ? WithPersonality(CorpArchetypes.Profile((CorpArchetype)Parameter("archetype-primary", 0), (CorpArchetype)Parameter("archetype-secondary", 0)))
        : CorpArchetypeProfile.Generic;

    private CorpArchetypeProfile WithPersonality(CorpArchetypeProfile deck) =>
        AgentPersonalities.CorpProfileOf(AgentPersonalities.Of(Configuration)) is { } personality
            ? CorpArchetypeProfile.Blend(personality, deck, AgentPersonalities.DeckWeight)
            : deck;

    private Dictionary<string, object>? _memo;

    /// <summary>A value computed once per decision.</summary>
    public T Memo<T>(string key, Func<T> compute) where T : notnull
    {
        _memo ??= new(StringComparer.Ordinal);
        if (_memo.TryGetValue(key, out object? value)) return (T)value;
        T computed = compute();
        _memo[key] = computed;
        return computed;
    }

    public int Parameter(string key, int fallback) => Configuration.Parameters.TryGetValue(key, out int value) ? value : fallback;
    public static int Hosted(AgentCard? card) => card?.Counters.Where(counter => counter.Purpose == CardCounterPurpose.HostedCredit).Sum(counter => counter.Count) ?? 0;
    public int AgendaCount => Features.Hand.Count(card => Definition(card)?.Agenda is not null);
    public int CreditValue => (Self.Credits < 3 ? 140 : Self.Credits < 8 ? 110 : Self.Credits < 15 ? 80 : 45) * Parameter("credit-scale", 100) / 100;
    public int DrawValue => (Generation >= 3 && Features.Side == PlayerSide.Corp
        ? Features.Hand.Length < 2 ? 260 : Features.Hand.Length < 3 ? 190 : Features.Hand.Length < 4 ? 140
            : Features.Hand.Length < (Self.MaximumHandSize ?? 5) ? 70 : 10
        : Features.Hand.Length < 2 ? 230 : Features.Hand.Length < 4 ? 125 : Features.Hand.Length < (Self.MaximumHandSize ?? 5) ? 70 : 10) * Parameter("draw-scale", 100) / 100;

    public int CardValue(AgentCard? card, bool installed = false)
    {
        CardEvaluationDescriptor? definition = Definition(card);
        if (definition is null) return 80; // no identity belief from an opaque ID
        CardRuntimeSpec printed = definition.Printed;
        if (definition.Agenda is not null) return 180 + printed.AgendaPoints * 60;
        if (definition.Ice is not null)
        {
            int need = Features.Hand.Count(known => Definition(known)?.Ice is not null) <= 1 ? 100 : 0;
            return 100 + need + Math.Max(0, 7 - (printed.RezCost ?? 0)) * 15 + AgendaCount * 40;
        }
        if (definition.Icebreaker is { } breaker)
        {
            int duplicates = Features.Board.RunnerRig.Count(known => known.Id != card!.Id && Definition(known)?.Icebreaker?.InterfaceType == breaker.InterfaceType);
            return (duplicates == 0 ? 430 : 140) + Math.Max(0, printed.InstallCost ?? 0) * 15;
        }
        int hosted = Hosted(card);
        if (hosted > 0) return 100 + hosted * 70;
        if (printed.CardClass == RuntimeCardClass.Upgrade) return 180;
        if (printed.CardClass is RuntimeCardClass.Operation or RuntimeCardClass.Event)
        {
            int gain = definition.Behavior?.Instructions.Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits).Sum(instruction => instruction.Amount) ?? 0;
            return gain > 0 ? 170 + Math.Max(0, gain - (printed.PlayCost ?? 0)) * 20 - Math.Max(0, (printed.PlayCost ?? 0) - Self.Credits) * 30 : 90;
        }
        if (printed.CardClass == RuntimeCardClass.Asset) return installed ? 50 : 150;
        return 80 + Math.Max(0, printed.InstallCost ?? 0) * 20;
    }

    public int ServerValue(AgentServer? server)
    {
        if (server is null) return 0;
        return (server.Kind switch
        {
            ServerKind.Headquarters => 150 + AgendaCount * 100,
            ServerKind.RAndD => 220 + Features.Board.RunnerRig.Sum(card => Definition(card)?.Behavior?.Triggers.Any(trigger =>
                trigger.Instructions.Any(instruction => instruction.RunOperand is not null)) == true ? 40 : 0)
                + (Features.Board.RunnerRig.Any(card => Definition(card)?.Behavior?.PaidAbilities.Any(ability =>
                    Flatten(ability.Instructions).Any(instruction => instruction.RunOperand is { ServerKind: ServerKind.RAndD, BreachAccessDeltaPerCounter: > 0 })) == true) ? 180 : 0),
            ServerKind.Archives => 30 + Features.PrivateCards.FaceDownArchives.Concat(Features.Board.CorpDiscard).Count(card => Definition(card)?.Agenda is not null) * 100,
            _ => server.Root.Where(card => card is not null).Sum(card => Definition(card)?.Agenda is not null ? 400 : Math.Min(250, CardValue(card, installed: true)))
                + (server.Ice.Length > 0 ? 100 : 0),
        }) * Parameter("protection-scale", 100) / 100;
    }

    public int RezReserve => Features.Board.Servers.Where(server => server.Kind is ServerKind.Headquarters or ServerKind.RAndD
            || server.Root.Any(card => Definition(card)?.Agenda is not null))
        .SelectMany(server => server.Ice).Where(card => card is { Rezzed: false })
        .Select(card => Definition(card)?.Printed.RezCost ?? 0).DefaultIfEmpty(0).Min();

    /// <summary>Public advancement counters on a hidden installed card, or 0 when none are visible.</summary>
    /// <summary>
    /// Runner v13: a face-down root card the Runner has accessed, as a card with its remembered
    /// identity and current public counters; null when nothing is remembered there.
    /// </summary>
    public AgentCard? RememberedRoot(AgentServer server, int index) =>
        Generation >= 9 && Features.Side == PlayerSide.Runner && index < server.Root.Length && server.Root[index] is null
        && Knowledge?.Roots?.FirstOrDefault(memory => memory.ServerId == server.Id && memory.Index == index) is { } memory
            ? new AgentCard { Id = $"remembered/{server.Id}/{index}", DefinitionId = memory.DefinitionId, AdvancementCounters = HiddenAdvancement(server.Id, "root", index) }
            : null;

    public int HiddenAdvancement(string serverId, string zone, int index) =>
        Features.Board.HiddenAdvancements?.FirstOrDefault(entry => entry.ServerId == serverId && entry.Zone == zone && entry.Index == index)
            ?.AdvancementCounters ?? 0;

    /// <summary>
    /// Estimated agenda points per unseen Corp card, relative to a fresh deck.
    /// The total follows the deck-size rule (2 per 5 cards, plus 2) using only
    /// public counts; points already scored, stolen or seen in Archives are
    /// removed. Returns 1 for a deck at its starting density.
    /// </summary>
    public double AgendaDensityFactor()
    {
        AgentBoard board = Features.Board;
        int hiddenRoots = board.Servers.Sum(server => server.Root.Count(card => card is null || Definition(card) is null));
        int hidden = board.HiddenZoneCounts.CorpHand + board.HiddenZoneCounts.CorpDeck + hiddenRoots;
        int deckSize = board.HiddenZoneCounts.CorpHand + board.HiddenZoneCounts.CorpDeck
            + board.Servers.Sum(server => server.Root.Length + server.Ice.Length)
            + (board.ZoneCounts?.CorpDiscard ?? board.CorpDiscard.Length) + board.CorpScore.Length + board.RunnerScore.Length;
        if (hidden <= 0 || deckSize <= 0) return 0;
        int total = 2 * (deckSize / 5) + 2;
        int seen = board.Players.Corp.AgendaPoints + board.Players.Runner.AgendaPoints
            + board.CorpDiscard.Sum(card => Definition(card)?.Agenda?.AgendaPoints ?? 0);
        double density = Math.Max(0, total - seen) / (double)hidden;
        return Math.Clamp(density / (total / (double)deckSize), 0.25, 2.5);
    }

    /// <summary>Score-race pressure from the opponent's points: 1 below 5, rising to 1.6 at 6 or more.</summary>
    public double OpponentScorePressure => Opponent.AgendaPoints switch
    {
        >= 6 => 1.6,
        5 => 1.3,
        _ => 1.0,
    };

    public static IEnumerable<BehaviorInstruction> Flatten(IEnumerable<BehaviorInstruction> instructions)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            yield return instruction;
            if (instruction.Choice is { } choice)
                foreach (BehaviorInstruction child in choice.Options.SelectMany(option => Flatten(option.Instructions))) yield return child;
            if (instruction.Branch is { } branch)
                foreach (BehaviorInstruction child in Flatten(branch.WhenTrue.Concat(branch.WhenFalse))) yield return child;
        }
    }
}
