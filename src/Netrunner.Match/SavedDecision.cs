using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match.Regret;

namespace Netrunner.Match;

/// <summary>A bot's choice at a recorded decision, and the state it chose in.</summary>
public sealed record SavedChoice(LegalAction Chosen, GameState State);

/// <summary>
/// Asks a bot to choose at a recorded decision (bot plan S1.2): the bot watches the recorded game from its start to the
/// decision, as it would have in play, and chooses there. The saved-decision tests and the regret suite's roots are the
/// same thing: a game and a sequence number.
/// </summary>
public static class SavedDecision
{
    /// <summary>The one saved ladder game in <paramref name="directory"/> (<c>ai-ladder --save-games</c>).</summary>
    public static (ReplayArtifact Replay, FullMatchDefinition Definition) ReadSaved(string directory)
    {
        (LadderSavedGame Game, FullMatchDefinition Definition)[] games = [.. LadderGameArchive.Read(directory)];
        return games.Length == 1 ? (games[0].Game.Replay, games[0].Definition)
            : throw new InvalidDataException($"'{directory}' holds {games.Length} saved games, not one.");
    }

    /// <summary>A replay file of a game played on <paramref name="definition"/>'s deal.</summary>
    public static ReplayArtifact ReadReplay(string path, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return EngineJson.Deserialize<ReplayArtifact>(File.ReadAllText(path), definition.Behavior, definition.Runtime);
    }

    /// <summary>
    /// The <paramref name="version"/> bot of <paramref name="side"/> at <paramref name="profile"/>, as a player meets it;
    /// with <paramref name="diagnostics"/>, it keeps its evaluation of every candidate (and so its reasons).
    /// </summary>
    public static OfflineAgent Agent(FullMatchDefinition definition, PlayerSide side, string? version, string profile = "standard",
        bool diagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
            profile, version);
        if (!diagnostics) return agent;
        var keeping = new OfflineAgent(agent.Configuration, OfflinePolicies.Create(agent.Configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior), null, captureDiagnostics: true);
        if (agent.Checkpoint.Knowledge.OpponentList is { } list) keeping.KnowOpponentList(list.Select(entry => (entry.DefinitionId, entry.Quantity)));
        return keeping;
    }

    /// <summary>The <paramref name="version"/> bot's choice at <paramref name="sequence"/> of a replay.</summary>
    public static Task<SavedChoice> ChooseAsync(ReplayArtifact replay, FullMatchDefinition definition, long sequence, PlayerSide side,
        string? version, string profile = "standard", CancellationToken cancellationToken = default) =>
        ChooseAsync(Positions(replay, definition), definition, sequence, Agent(definition, side, version, profile), null, cancellationToken);

    /// <summary>
    /// <paramref name="agent"/>'s choice at <paramref name="sequence"/> of a replay. <paramref name="edit"/> changes the state at
    /// the decision before the agent observes it, as if the game had reached it so.
    /// </summary>
    public static Task<SavedChoice> ChooseAsync(ReplayArtifact replay, FullMatchDefinition definition, long sequence, OfflineAgent agent,
        Func<GameState, GameState>? edit = null, CancellationToken cancellationToken = default) =>
        ChooseAsync(Positions(replay, definition), definition, sequence, agent, edit, cancellationToken);

    /// <summary><paramref name="agent"/>'s choice at <paramref name="sequence"/> of a regret suite game.</summary>
    public static Task<SavedChoice> ChooseAsync(RegretGame game, long sequence, OfflineAgent agent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        FullMatchDefinition definition = RegretGames.Definition(game);
        return ChooseAsync(RegretGames.Positions(game, definition), definition, sequence, agent, null, cancellationToken);
    }

    /// <summary>The last decision up to <paramref name="limit"/> where <paramref name="match"/> holds, by its sequence number.</summary>
    public static long LastMatching(ReplayArtifact replay, FullMatchDefinition definition, Func<GameState, PendingDecision, bool> match, long limit)
    {
        ArgumentNullException.ThrowIfNull(match);
        long found = -1;
        foreach (RegretPosition position in Positions(replay, definition).TakeWhile(position => position.Sequence <= limit))
            if (match(position.State, position.State.PendingDecision!)) found = position.Sequence;
        return found >= 0 ? found : throw new InvalidOperationException($"The game has no matching decision by action {limit}.");
    }

    /// <summary>Every decision of a replay, before it is applied.</summary>
    public static IEnumerable<RegretPosition> Positions(ReplayArtifact replay, FullMatchDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(definition);
        GameState state = replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in replay.Actions)
        {
            yield return new RegretPosition(state, recorded.Sequence, -1);
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
        }
    }

    private static async Task<SavedChoice> ChooseAsync(IEnumerable<RegretPosition> positions, FullMatchDefinition definition, long sequence,
        OfflineAgent agent, Func<GameState, GameState>? edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        foreach (RegretPosition position in positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameState state = position.Sequence == sequence && edit is not null ? edit(position.State) : position.State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
            if (position.Sequence != sequence) continue;
            PendingDecision pending = state.PendingDecision!;
            if (state.GetPlayer(pending.Owner).Side != agent.ControlledSide)
                throw new InvalidOperationException($"The decision at action {sequence} is not the {agent.ControlledSide}'s.");
            ActionId id = await agent.ChooseActionAsync(
                ObservationBuilder.Build(state, agent.ControlledSide, definition.Behavior, definition.Runtime), cancellationToken)
                .ConfigureAwait(false);
            return new SavedChoice(pending.LegalActions.Single(action => action.Id == id), state);
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }
}
