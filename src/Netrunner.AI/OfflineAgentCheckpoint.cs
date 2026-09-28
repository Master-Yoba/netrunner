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
public sealed record AgentKnowledge(ImmutableArray<AgentRememberedCard> VisibleCards,
    ImmutableArray<AgentReveal> Reveals, ImmutableArray<AgentHistoryEvent> History, string? RunId, string? ServerId, bool? RunSuccessful)
{
    public static AgentKnowledge Empty { get; } = new([], [], [], null, null, null);

    /// <summary>The Corp's central servers as last accessed; null until a successful central run ends. Older checkpoints load without it.</summary>
    public AgentCentralMemory? Centrals { get; init; }

    public AgentKnowledge Observe(AgentFeatures features, long sequence)
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
        return new(visible, reveals.Values.OrderByDescending(reveal => reveal.LastSeenSequence)
            .ThenBy(reveal => reveal.DefinitionId, StringComparer.Ordinal).Take(256).ToImmutableArray(),
            history.TakeLast(128).ToImmutableArray(), features.Board.Run?.Id, features.Board.Run?.AttackedServer,
            features.Board.Run?.Successful)
        { Centrals = centrals };

        void Add(string kind, AgentRememberedCard card) => history.Add(new(sequence, kind, card.Id, card.DefinitionId, null));
    }

    private static bool IsInstalled(string location) => location.StartsWith("server/", StringComparison.Ordinal)
        || location is "runner-rig" or "own-ice";
}
