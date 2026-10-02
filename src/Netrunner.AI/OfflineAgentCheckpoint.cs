using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

public sealed record OfflineAgentCheckpoint(int SchemaVersion, string ConfigurationDigest, ulong RandomState,
    long CommittedDecisionCount, long ObservationCursor, string? ObservationDigest,
    AgentKnowledge Knowledge, AgentObjective? Objective, AgentCommit? LastCommit)
{
    public const int CurrentSchema = 1;
    public static OfflineAgentCheckpoint Initial(OfflineAgentConfiguration configuration) => new(CurrentSchema,
        configuration.Digest, configuration.PolicySeed, 0, -1, null, AgentKnowledge.Empty, null, null);
}

public sealed record AgentCommit(DecisionId DecisionId, long Revision, ActionId ActionId, long ActionSequence);
public sealed record AgentObjective(string Key, string? RunId, string? EncounterId, string? TargetCardId);
public sealed record AgentRememberedCard(string Id, string DefinitionId, string Location, bool? Rezzed, int? AdvancementCounters);
public sealed record AgentReveal(string DefinitionId, long LastSeenSequence);
public sealed record AgentHistoryEvent(long Sequence, string Kind, string? CardId, string? DefinitionId, string? ServerId);

/// <summary>
/// What the agent last saw of the Corp's central servers (A4a): the Corp's deck
/// and hand sizes when its last successful R&amp;D and HQ runs ended. Public counts
/// only; a later draw shows how much of HQ or R&amp;D is new since.
/// </summary>
public sealed record AgentCentralMemory(int? DeckAfterRdAccess, int? HandAfterHqAccess, int? DeckAfterHqAccess)
{
    /// <summary>The Corp's deck size seen during the current run, to tell whether the access removed R&amp;D's top card.</summary>
    public int? DeckDuringRun { get; init; }
}
/// <summary>
/// A face-down root card the Runner has accessed (Runner v13, playtest 30 September): known by its
/// server and position, while that server's root keeps the same number of cards and the card's
/// public advancement doesn't drop. It was forgotten as soon as it turned face down again, so the
/// Runner kept running a known Urtica Cipher.
/// </summary>
public sealed record AgentRootMemory(string ServerId, int Index, int RootCount, string DefinitionId, int Advancement);

public sealed record AgentKnowledge(ImmutableArray<AgentRememberedCard> VisibleCards,
    ImmutableArray<AgentReveal> Reveals, ImmutableArray<AgentHistoryEvent> History, string? RunId, string? ServerId, bool? RunSuccessful)
{
    public static AgentKnowledge Empty { get; } = new([], [], [], null, null, null);

    /// <summary>The Corp's central servers as last accessed; null until a successful central run ends. Older checkpoints load without it.</summary>
    public AgentCentralMemory? Centrals { get; init; }

    /// <summary>Face-down root cards the Runner has accessed. Older checkpoints load without it.</summary>
    public ImmutableArray<AgentRootMemory>? Roots { get; init; }

    /// <summary>
    /// The Corp's installed cards the Runner has accessed, by instance: a trap the Runner has seen won't be run into
    /// again. Kept by the Corp only; older checkpoints load without it.
    /// </summary>
    public ImmutableArray<string>? Exposed { get; init; }

    /// <param name="keepRootsThroughInstalls">
    /// Runner v20: a remembered root card stays known when the Corp installs another card in its root. Installs append, so
    /// the remembered card keeps its index; a root that shrinks still forgets, since a removal moves the later cards.
    /// </param>
    public AgentKnowledge Observe(AgentFeatures features, long sequence, bool keepRootsThroughInstalls = false)
    {
        // Precise identity/location exists only while currently visible. Reveals
        // remember a definition, not an instance in a shuffled/hidden deck.
        ImmutableArray<AgentRememberedCard> visible = features.VisibleCards().GroupBy(pair => pair.Card.Id, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(pair => pair.Card.Id, StringComparer.Ordinal)
            .Select(pair => new AgentRememberedCard(pair.Card.Id, pair.Card.DefinitionId, pair.Location,
                pair.Card.Rezzed, pair.Card.AdvancementCounters)).ToImmutableArray();
        var reveals = Reveals.ToDictionary(reveal => reveal.DefinitionId, StringComparer.Ordinal);
        foreach (AgentRememberedCard card in visible) reveals[card.DefinitionId] = new(card.DefinitionId, sequence);
        var history = History.ToBuilder();
        var previous = VisibleCards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        if (sequence > 0)
        {
            foreach (AgentRememberedCard card in visible)
            {
                previous.TryGetValue(card.Id, out AgentRememberedCard? old);
                if (old?.Location != card.Location && IsInstalled(card.Location)) Add("install-or-reveal", card);
                if (card.Rezzed == true && old?.Rezzed != true) Add("rez", card);
                if (card.AdvancementCounters > (old?.AdvancementCounters ?? 0)) Add("advance", card);
            }
            if (features.Board.Run is { } run && run.Id != RunId)
                history.Add(new(sequence, "run", null, null, run.AttackedServer));
            if (features.Board.Run is { Successful: true } successful && (RunId != successful.Id || RunSuccessful != true))
                history.Add(new(sequence, "successful-run", null, null, successful.AttackedServer));
            if (features.Board.Run is null && RunId is not null)
                history.Add(new(sequence, "run-ended", null, null, ServerId));
        }
        AgentCentralMemory? centrals = Centrals;
        int deck = features.Board.HiddenZoneCounts.CorpDeck;
        int hand = features.Board.HiddenZoneCounts.CorpHand;
        if (features.Board.Run is not null)
            centrals = (centrals ?? new(null, null, null)) with { DeckDuringRun = centrals?.DeckDuringRun ?? deck };
        // A successful run on R&D or HQ has just ended: remember the public sizes after the access.
        else if (sequence > 0 && RunId is not null && RunSuccessful == true
            && features.Board.Servers.FirstOrDefault(server => server.Id == ServerId) is { } accessed)
        {
            AgentCentralMemory memory = centrals ?? new(null, null, null);
            centrals = accessed.Kind switch
            {
                // A stolen or trashed top card leaves an unseen card on top: nothing to remember.
                ServerKind.RAndD => memory with { DeckAfterRdAccess = memory.DeckDuringRun == deck ? deck : null },
                ServerKind.Headquarters => memory with { HandAfterHqAccess = hand, DeckAfterHqAccess = deck },
                _ => memory,
            };
        }
        if (features.Board.Run is null && centrals?.DeckDuringRun is not null) centrals = centrals with { DeckDuringRun = null };
        ImmutableArray<AgentRootMemory>? roots = features.Side == PlayerSide.Runner ? RememberRoots(features, Roots, keepRootsThroughInstalls) : Roots;
        ImmutableArray<string>? exposed = Exposed;
        if (features.Side == PlayerSide.Corp && features.Board.Run?.Breach.Accessed is { Length: > 0 } breached)
        {
            string[] installed = [.. breached.OfType<AgentCard>().Where(card => features.Board.Servers.Any(server => server.Root.Any(root => root?.Id == card.Id)))
                .Select(card => card.Id)];
            if (installed.Length > 0) exposed = [.. (Exposed ?? []).Union(installed, StringComparer.Ordinal).Order(StringComparer.Ordinal).TakeLast(64)];
        }
        return new(visible, reveals.Values.OrderByDescending(reveal => reveal.LastSeenSequence)
            .ThenBy(reveal => reveal.DefinitionId, StringComparer.Ordinal).Take(256).ToImmutableArray(),
            history.TakeLast(128).ToImmutableArray(), features.Board.Run?.Id, features.Board.Run?.AttackedServer,
            features.Board.Run?.Successful)
        { Centrals = centrals, Roots = roots, Exposed = exposed };

        void Add(string kind, AgentRememberedCard card) => history.Add(new(sequence, kind, card.Id, card.DefinitionId, null));
    }

    /// <summary>
    /// Keeps what is still true of the remembered root cards and adds every root card visible now
    /// (an accessed card is visible to the Runner for the rest of its breach).
    /// </summary>
    private static ImmutableArray<AgentRootMemory>? RememberRoots(AgentFeatures features, ImmutableArray<AgentRootMemory>? previous,
        bool keepThroughInstalls)
    {
        var kept = new List<AgentRootMemory>();
        foreach (AgentRootMemory memory in previous ?? [])
        {
            AgentServer? server = features.Board.Servers.FirstOrDefault(value => value.Id == memory.ServerId);
            bool counted = server is not null && (server.Root.Length == memory.RootCount || keepThroughInstalls && server.Root.Length > memory.RootCount);
            if (server is null || !counted || memory.Index >= server.Root.Length || server.Root[memory.Index] is not null)
                continue;
            int advancement = features.Board.HiddenAdvancements?.FirstOrDefault(entry => entry.ServerId == memory.ServerId
                && entry.Zone == "root" && entry.Index == memory.Index)?.AdvancementCounters ?? 0;
            if (advancement < memory.Advancement) continue;
            kept.Add(memory with { Advancement = advancement, RootCount = server.Root.Length });
        }
        foreach (AgentServer server in features.Board.Servers)
            for (int index = 0; index < server.Root.Length; index++)
                if (server.Root[index] is { } card && !string.IsNullOrEmpty(card.DefinitionId))
                {
                    kept.RemoveAll(memory => memory.ServerId == server.Id && memory.Index == index);
                    kept.Add(new(server.Id, index, server.Root.Length, card.DefinitionId, card.AdvancementCounters ?? 0));
                }
        if (kept.Count == 0 && previous is null) return null;
        return kept.OrderBy(memory => memory.ServerId, StringComparer.Ordinal).ThenBy(memory => memory.Index).Take(64).ToImmutableArray();
    }

    private static bool IsInstalled(string location) => location.StartsWith("server/", StringComparison.Ordinal)
        || location is "runner-rig" or "own-ice";
}
