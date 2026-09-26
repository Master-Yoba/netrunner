using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Which agent drives a seat in a fuzz game.</summary>
public enum FuzzAgentKind
{
    RandomLegal,
    Heuristic,
}

/// <summary>How a fuzz game ended. Only <see cref="Terminal"/> and <see cref="Capped"/> are not defects.</summary>
public enum FuzzGameStatus
{
    Terminal,
    Capped,
    Stalled,
    Rejected,
    EngineException,
    AgentException,
    SerializationFailure,
    ReplayFailure,
    /// <summary>Simulation-mode Apply produced a different state or events than authoritative Apply.</summary>
    SimulationMismatch,
}

/// <summary>One deterministic fuzz game: content, seed and the agent kind in each seat.</summary>
public sealed record FuzzGameCase(string Id, string ContentId, ulong GameSeed, FuzzAgentKind Corp, FuzzAgentKind Runner);

/// <summary>
/// The exact position before a failing action. <see cref="ReplayPrefix"/>
/// replays to <see cref="StateBefore"/>; applying <see cref="FailingSubmission"/>
/// to that state reproduces the failure.
/// </summary>
public sealed record FuzzFailure(
    ReplayArtifact ReplayPrefix,
    GameSnapshot StateBefore,
    ActionSubmission? FailingSubmission,
    ActionKind? FailingActionKind,
    string? RejectionCode);

public sealed record FuzzGameResult(
    FuzzGameCase Case,
    FuzzGameStatus Status,
    int ActionCount,
    GameOutcome? Outcome,
    string? Message,
    FuzzFailure? Failure)
{
    public bool IsDefect => Status is not (FuzzGameStatus.Terminal or FuzzGameStatus.Capped);
}

/// <summary>
/// Random-legal rules fuzzing over the authoritative reducer. Every action runs
/// with full invariant validation and a serialization round-trip, and every
/// completed game has its replay verified. Failures keep the exact prior
/// position so they can be turned into regression fixtures.
/// </summary>
public static class RulesFuzzRunner
{
    public const string DeveloperContentId = "developer";
    public const int DefaultActionCap = 3000;

    /// <summary>Official preset pairings, in the benchmark's corp/runner order.</summary>
    public static IReadOnlyList<string> OfficialContentIds { get; } =
        ["basic-basic", "basic-expanded", "expanded-basic", "expanded-expanded"];

    /// <summary>Seat pairings cycled across games: random-random, random-heuristic and heuristic-random.</summary>
    public static IReadOnlyList<(FuzzAgentKind Corp, FuzzAgentKind Runner)> Pairings { get; } =
    [
        (FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal),
        (FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic),
        (FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal),
    ];

    /// <summary>Builds a deterministic case list that cycles content and seat pairings.</summary>
    public static IReadOnlyList<FuzzGameCase> Plan(int games, ulong baseSeed, IReadOnlyList<string> contentIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        ArgumentNullException.ThrowIfNull(contentIds);
        if (contentIds.Count == 0) throw new ArgumentException("At least one content ID is required.", nameof(contentIds));
        var cases = new List<FuzzGameCase>(games);
        for (int index = 0; index < games; index++)
        {
            string content = contentIds[index % contentIds.Count];
            (FuzzAgentKind corp, FuzzAgentKind runner) = Pairings[index / contentIds.Count % Pairings.Count];
            cases.Add(new FuzzGameCase(
                $"{content}-{index + 1:D5}", content, DeriveGameSeed(baseSeed, index), corp, runner));
        }
        return cases;
    }

    public static ulong DeriveGameSeed(ulong baseSeed, int index)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            "netrunner/rules-fuzz/v1/" + baseSeed.ToString(CultureInfo.InvariantCulture)
            + "/" + index.ToString(CultureInfo.InvariantCulture)));
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    /// <summary>Creates the match definition for a content ID; official IDs need the embedded official snapshot.</summary>
    public static FullMatchDefinition CreateDefinition(string contentId, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(contentId);
        if (contentId == DeveloperContentId) return FullMatchDefinition.Developer(seed);
        string[] parts = contentId.Split('-');
        if (parts.Length != 2 || parts.Any(part => part is not ("basic" or "expanded")))
            throw new ArgumentException($"Unknown fuzz content '{contentId}'.", nameof(contentId));
        if (!string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
            throw new InvalidOperationException("Official fuzz content requires the embedded official card snapshot.");
        CardDataSet data = CardDataResource.LoadEmbedded();
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        MatchRequest request = MatchRequestComposer.ComposeOfficial(
            $"system-gateway-{parts[0]}-corp", $"system-gateway-{parts[1]}-runner", data, seed,
            OfficialPresetCatalog.Embedded(), behaviorCatalog: behavior);
        return FullMatchDefinition.FromRequest(request, data, behavior);
    }

    public static Task<FuzzGameResult> RunAsync(FuzzGameCase fuzzCase, int actionCap = DefaultActionCap,
        CancellationToken cancellationToken = default) =>
        RunAsync(fuzzCase, actionCap, CreateAgent, differential: false, cancellationToken);

    /// <summary>As <see cref="RunAsync(FuzzGameCase, int, CancellationToken)"/>, optionally checking Simulation mode against every action.</summary>
    public static Task<FuzzGameResult> RunAsync(FuzzGameCase fuzzCase, int actionCap, bool differential,
        CancellationToken cancellationToken = default) =>
        RunAsync(fuzzCase, actionCap, CreateAgent, differential, cancellationToken);

    /// <summary>Test seam: substitutes the agent for a seat, for example one that returns an unissued action.</summary>
    internal static async Task<FuzzGameResult> RunAsync(FuzzGameCase fuzzCase, int actionCap,
        Func<FuzzAgentKind, PlayerSide, FullMatchDefinition, IGameAgent> agentFactory,
        bool differential = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fuzzCase);
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionCap);
        FullMatchDefinition definition = CreateDefinition(fuzzCase.ContentId, fuzzCase.GameSeed);
        IGameAgent corp = agentFactory(fuzzCase.Corp, PlayerSide.Corp, definition);
        IGameAgent runner = agentFactory(fuzzCase.Runner, PlayerSide.Runner, definition);
        AgentLifecycleHost.ValidateSeat(corp, PlayerSide.Corp);
        AgentLifecycleHost.ValidateSeat(runner, PlayerSide.Runner);

        GameState state = definition.Start().State;
        GameSnapshot initial = state.CreateSnapshot(definition.Behavior, definition.Runtime);
        var actions = new List<ReplayAction>();
        AgentLifecycleHost.Observe(corp, state, definition.Behavior);
        AgentLifecycleHost.Observe(runner, state, definition.Behavior);

        while (state.Outcome.Kind == GameOutcomeKind.Ongoing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (actions.Count >= actionCap)
            {
                return await FinishAsync(FuzzGameStatus.Capped, $"Reached the {actionCap}-action cap.").ConfigureAwait(false);
            }

            if (state.PendingDecision is not { } pending)
            {
                return Fail(FuzzGameStatus.Stalled, "The game is ongoing without a pending decision.", null, null, null);
            }

            PlayerSide side = state.GetPlayer(pending.Owner).Side;
            IGameAgent agent = side == PlayerSide.Corp ? corp : runner;
            ActionId chosen;
            try
            {
                chosen = await agent.ChooseActionAsync(
                    ObservationBuilder.Build(state, side, definition.Behavior), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AgentLifecycleHost.Discard(agent);
                return Fail(FuzzGameStatus.AgentException, Describe(exception), null, null, null);
            }

            var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen);
            ActionKind? kind = pending.LegalActions.FirstOrDefault(action => action.Id == chosen)?.Kind;
            // Every decision is a persistence boundary, as in FullMatchRunner.
            GameState restored;
            try
            {
                restored = EngineJson.DeserializeState(EngineJson.Serialize(state), definition.Behavior, definition.Runtime);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Fail(FuzzGameStatus.SerializationFailure, Describe(exception), submission, kind, null);
            }

            if (differential)
            {
                string? mismatch;
                try
                {
                    mismatch = GameEngine.VerifySimulationEquivalence(restored, submission, definition.Behavior, definition.Runtime);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    mismatch = Describe(exception);
                }

                if (mismatch is not null)
                {
                    return Fail(FuzzGameStatus.SimulationMismatch, mismatch, submission, kind, null);
                }
            }

            ApplyResult applied;
            try
            {
                applied = GameEngine.Apply(restored, submission, definition.Behavior, definition.Runtime);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AgentLifecycleHost.Discard(agent);
                return Fail(FuzzGameStatus.EngineException, Describe(exception), submission, kind, null);
            }

            if (!applied.Accepted)
            {
                AgentLifecycleHost.Discard(agent);
                return Fail(FuzzGameStatus.Rejected, applied.Rejection?.Message ?? "The engine rejected an issued action.",
                    submission, kind, applied.Rejection?.Code.ToString());
            }

            state = applied.State;
            actions.Add(ReplayAction.From(applied.AcceptedAction!));
            AgentLifecycleHost.Accepted(corp, submission, state, definition.Behavior);
            AgentLifecycleHost.Accepted(runner, submission, state, definition.Behavior);
        }

        return await FinishAsync(FuzzGameStatus.Terminal, null).ConfigureAwait(false);

        Task<FuzzGameResult> FinishAsync(FuzzGameStatus status, string? message)
        {
            ReplayArtifact replay = Replay();
            ReplayVerificationResult verified;
            try
            {
                verified = ReplayVerifier.Verify(
                    EngineJson.Deserialize<ReplayArtifact>(EngineJson.Serialize(replay), definition.Behavior, definition.Runtime),
                    definition.Behavior, definition.Runtime);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Task.FromResult(Fail(FuzzGameStatus.ReplayFailure, Describe(exception), null, null, null));
            }

            return Task.FromResult(verified.Success
                ? new FuzzGameResult(fuzzCase, status, actions.Count, state.Outcome, message, null)
                : Fail(FuzzGameStatus.ReplayFailure, verified.Message, null, null, null));
        }

        FuzzGameResult Fail(FuzzGameStatus status, string? message, ActionSubmission? submission, ActionKind? kind, string? code) =>
            new(fuzzCase, status, actions.Count, state.Outcome, message,
                new FuzzFailure(Replay(), state.CreateSnapshot(definition.Behavior, definition.Runtime), submission, kind, code));

        ReplayArtifact Replay() => new(initial, state.InitialSeed, actions.ToArray(),
            expectedFinalStateHash: DeterministicHash.ForState(state));
    }

    internal static IGameAgent CreateAgent(FuzzAgentKind kind, PlayerSide side, FullMatchDefinition definition)
    {
        ulong seed = AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, side);
        return kind switch
        {
            FuzzAgentKind.RandomLegal => new RandomLegalAgent(seed),
            FuzzAgentKind.Heuristic => OfflineMatchFactory.CreateOpponent(
                definition, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";
}
