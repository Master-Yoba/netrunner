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
/// <param name="VerifyReplay">Whether a game that ends normally has its replay verified; a capped game always does.</param>
public sealed record FuzzGameCase(string Id, string ContentId, ulong GameSeed, FuzzAgentKind Corp, FuzzAgentKind Runner,
    bool VerifyReplay = true);

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

    /// <summary>
    /// Official preset pairings that fuzzing, the ladder and the tests play: the
    /// expanded decks only. The basic decks are a subset of their cards and aren't
    /// what anyone plays, so they only cost time (27 September).
    /// </summary>
    public static IReadOnlyList<string> OfficialContentIds { get; } = ["expanded-expanded"];

    /// <summary>
    /// Every pairing of the System Gateway sample decks (Wave 4 C1), named
    /// <c>corpPresetId~runnerPresetId</c>: four Corp decks against three Runner decks.
    /// </summary>
    public static IReadOnlyList<string> GatewayContentIds { get; } =
        OfficialPresetCatalog.Embedded().Gateway.Where(preset => preset.Side == PlayerSide.Corp)
            .SelectMany(corp => OfficialPresetCatalog.Embedded().Gateway.Where(preset => preset.Side == PlayerSide.Runner)
                .Select(runner => $"{corp.PresetId}{PairSeparator}{runner.PresetId}"))
            .ToArray();

    /// <summary>Separates the Corp and Runner preset IDs in a named-preset content ID.</summary>
    public const char PairSeparator = '~';

    /// <summary>
    /// Every pairing of the Elevation simulation decks (the batch proofs), on
    /// the System Gateway + Elevation pool.
    /// </summary>
    public static IReadOnlyList<string> ElevationContentIds =>
        OfficialPresetCatalog.ElevationSimulation().ElevationSimulationDecks.Where(preset => preset.Side == PlayerSide.Corp)
            .SelectMany(corp => OfficialPresetCatalog.ElevationSimulation().ElevationSimulationDecks.Where(preset => preset.Side == PlayerSide.Runner)
                .Select(runner => $"{corp.PresetId}{PairSeparator}{runner.PresetId}"))
            .ToArray();

    /// <summary>
    /// EL.8: every pairing of the System Gateway + Elevation format's decks (D-EL-4),
    /// the deck pool of that format.
    /// </summary>
    public static IReadOnlyList<string> GatewayElevationContentIds =>
        OfficialPresetCatalog.GatewayElevation().ElevationDecks.Where(preset => preset.Side == PlayerSide.Corp)
            .SelectMany(corp => OfficialPresetCatalog.GatewayElevation().ElevationDecks.Where(preset => preset.Side == PlayerSide.Runner)
                .Select(runner => $"{corp.PresetId}{PairSeparator}{runner.PresetId}"))
            .ToArray();

    /// <summary>VP.3: every pairing of the Vantage Point simulation decks (the batch proofs), on the Startup pool.</summary>
    public static IReadOnlyList<string> VantagePointContentIds =>
        OfficialPresetCatalog.VantagePointSimulation().VantagePointSimulationDecks.Where(preset => preset.Side == PlayerSide.Corp)
            .SelectMany(corp => OfficialPresetCatalog.VantagePointSimulation().VantagePointSimulationDecks.Where(preset => preset.Side == PlayerSide.Runner)
                .Select(runner => $"{corp.PresetId}{PairSeparator}{runner.PresetId}"))
            .ToArray();

    /// <summary>Every pairing of NSG's Core Sets decks, the deck pool of the NSG Core Sets format.</summary>
    public static IReadOnlyList<string> NsgCoreSetsContentIds =>
        OfficialPresetCatalog.NsgCoreSets().NsgCoreSetsDecks.Where(preset => preset.Side == PlayerSide.Corp)
            .SelectMany(corp => OfficialPresetCatalog.NsgCoreSets().NsgCoreSetsDecks.Where(preset => preset.Side == PlayerSide.Runner)
                .Select(runner => $"{corp.PresetId}{PairSeparator}{runner.PresetId}"))
            .ToArray();

    /// <summary>
    /// The <c>--content</c> options: developer, official (expanded), gateway, elevation (simulation),
    /// gateway-elevation, nsg-core-sets or vantage-point (simulation).
    /// </summary>
    public static IReadOnlyList<string> ContentNames { get; } =
        ["developer", "official", "gateway", "elevation", "gateway-elevation", "nsg-core-sets", "vantage-point"];

    /// <summary>Whether a content ID is a pairing of one of the official pools (Gateway, the Elevation pools, the expanded decks).</summary>
    public static bool IsKnownContentId(string contentId)
    {
        ArgumentNullException.ThrowIfNull(contentId);
        return ContentNames.Where(name => name != "developer").Any(name => ContentIds(name).Contains(contentId, StringComparer.Ordinal));
    }

    /// <summary>The content IDs a <c>--content</c> option names.</summary>
    public static IReadOnlyList<string> ContentIds(string content) => content switch
    {
        "developer" => [DeveloperContentId],
        "official" => OfficialContentIds,
        "gateway" => GatewayContentIds,
        "elevation" => ElevationContentIds,
        "gateway-elevation" => GatewayElevationContentIds,
        "nsg-core-sets" => NsgCoreSetsContentIds,
        "vantage-point" => VantagePointContentIds,
        _ => throw new ArgumentException($"Unknown content '{content}'; expected {string.Join(", ", ContentNames)}.", nameof(content)),
    };

    /// <summary>
    /// The version of a <c>--content</c> deck pool (A1b). Ratings are compared only
    /// within one pool version; a deck joins the pool at a version bump.
    /// </summary>
    public static string PoolVersion(string content) => content switch
    {
        "developer" => "developer",
        "official" => "expanded-v1",
        "gateway" => "gateway-v1",
        "elevation" => "elevation-simulation-v3",
        // v2: the Core Battle Box replaced our decks (D-EL-10).
        "gateway-elevation" => "gateway-elevation-v2",
        "nsg-core-sets" => "nsg-core-sets-v1",
        "vantage-point" => "vantage-point-simulation-v1",
        _ => throw new ArgumentException($"Unknown content '{content}'; expected {string.Join(", ", ContentNames)}.", nameof(content)),
    };

    /// <summary>Every official preset pairing, in the benchmark's corp/runner order (the frozen M5 suites).</summary>
    public static IReadOnlyList<string> AllOfficialContentIds { get; } =
        ["basic-basic", "basic-expanded", "expanded-basic", "expanded-expanded"];

    /// <summary>Seat pairings cycled across games: random-random, random-heuristic and heuristic-random.</summary>
    public static IReadOnlyList<(FuzzAgentKind Corp, FuzzAgentKind Runner)> Pairings { get; } =
    [
        (FuzzAgentKind.RandomLegal, FuzzAgentKind.RandomLegal),
        (FuzzAgentKind.RandomLegal, FuzzAgentKind.Heuristic),
        (FuzzAgentKind.Heuristic, FuzzAgentKind.RandomLegal),
    ];

    /// <summary>
    /// Builds a deterministic case list that cycles content and seat pairings. With <paramref name="verifyReplaysEvery"/>
    /// above 1, only every Nth game that ends normally has its replay verified (a per-slice run; the milestone run
    /// verifies every game).
    /// </summary>
    public static IReadOnlyList<FuzzGameCase> Plan(int games, ulong baseSeed, IReadOnlyList<string> contentIds,
        int verifyReplaysEvery = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verifyReplaysEvery);
        ArgumentNullException.ThrowIfNull(contentIds);
        if (contentIds.Count == 0) throw new ArgumentException("At least one content ID is required.", nameof(contentIds));
        var cases = new List<FuzzGameCase>(games);
        for (int index = 0; index < games; index++)
        {
            string content = contentIds[index % contentIds.Count];
            (FuzzAgentKind corp, FuzzAgentKind runner) = Pairings[index / contentIds.Count % Pairings.Count];
            cases.Add(new FuzzGameCase(
                $"{content}-{index + 1:D5}", content, DeriveGameSeed(baseSeed, index), corp, runner,
                VerifyReplay: index % verifyReplaysEvery == 0));
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
        (string corpPreset, string runnerPreset) = PresetIds(contentId);
        if (!string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
            throw new InvalidOperationException("Official fuzz content requires the embedded official card snapshot.");
        OfficialPresetCatalog presets = CatalogOf(corpPreset);
        bool startup = ReferenceEquals(presets, OfficialPresetCatalog.VantagePointSimulation());
        bool elevation = !startup && !ReferenceEquals(presets, OfficialPresetCatalog.Embedded());
        // The Gateway + Elevation pool's packs, not every embedded pack: Vantage Point's cards stay out of these games (VP.1);
        // the Vantage Point simulation decks play on the Startup pool (VP.3).
        CardDataSet data = startup ? CardDataResource.LoadEmbeddedPool(CardPools.Startup.PackCodes)
            : elevation ? CardDataResource.LoadEmbeddedPool(CardPools.GatewayElevation.PackCodes)
            : CardDataResource.LoadEmbedded();
        BehaviorCatalog behavior = startup ? VantagePointBehaviorCatalog.Create()
            : elevation ? ElevationBehaviorCatalog.Create()
            : GatewayBehaviorCatalog.Create();
        MatchRequest request = MatchRequestComposer.ComposeOfficial(
            corpPreset, runnerPreset, data, seed, presets, behaviorCatalog: behavior);
        return FullMatchDefinition.FromRequest(request, data, behavior, presets);
    }

    /// <summary>The Corp and Runner preset IDs a content ID names: <c>basic-expanded</c> style, or <c>corpId~runnerId</c>.</summary>
    public static (string Corp, string Runner) PresetIds(string contentId)
    {
        ArgumentNullException.ThrowIfNull(contentId);
        if (contentId.Split(PairSeparator) is [string corp, string runner])
        {
            OfficialPresetCatalog catalog = CatalogOf(corp);
            if (!catalog.TryGet(corp, out OfficialPreset corpPreset) || corpPreset.Side != PlayerSide.Corp
                || !catalog.TryGet(runner, out OfficialPreset runnerPreset) || runnerPreset.Side != PlayerSide.Runner)
                throw new ArgumentException($"Unknown fuzz content '{contentId}'.", nameof(contentId));
            return (corp, runner);
        }
        string[] parts = contentId.Split('-');
        if (parts.Length != 2 || parts.Any(part => part is not ("basic" or "expanded")))
            throw new ArgumentException($"Unknown fuzz content '{contentId}'.", nameof(contentId));
        return ($"system-gateway-{parts[0]}-corp", $"system-gateway-{parts[1]}-runner");
    }

    /// <summary>
    /// The preset catalog a Corp preset ID belongs to: the simulation decks, the Elevation format's decks,
    /// NSG's Core Sets decks or the embedded catalog.
    /// </summary>
    private static OfficialPresetCatalog CatalogOf(string corpPresetId) =>
        corpPresetId.StartsWith(OfficialPresetCatalog.ElevationSimulationPrefix, StringComparison.Ordinal) ? OfficialPresetCatalog.ElevationSimulation()
        : corpPresetId.StartsWith(OfficialPresetCatalog.VantagePointSimulationPrefix, StringComparison.Ordinal) ? OfficialPresetCatalog.VantagePointSimulation()
        : OfficialPresetCatalog.IsElevationDeck(corpPresetId) ? OfficialPresetCatalog.GatewayElevation()
        : OfficialPresetCatalog.IsNsgCoreSetsDeck(corpPresetId) ? OfficialPresetCatalog.NsgCoreSets()
        : OfficialPresetCatalog.Embedded();

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
            if (status == FuzzGameStatus.Terminal && !fuzzCase.VerifyReplay)
                return Task.FromResult(new FuzzGameResult(fuzzCase, status, actions.Count, state.Outcome, message, null));
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
