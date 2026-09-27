using System.Collections.Concurrent;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>What one audited game showed from one seat.</summary>
public sealed record PresentationAuditGame(
    FuzzGameCase Case,
    PlayerSide Viewer,
    int Publications,
    int Beats,
    IReadOnlyDictionary<PresentationEventKind, int> BeatKinds,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> CaptionLeaks,
    bool Terminal,
    string? Failure)
{
    /// <summary>The first captions shown, in order, for reviewing wording.</summary>
    public IReadOnlyList<string> Captions { get; init; } = [];
}

/// <summary>
/// Replays fuzz-planned games through the real coordinator from each seat and
/// checks the presenter's contract headlessly (UI plan U3 gate): every
/// publication's events must explain the settled board (zero snap
/// mismatches), and no beat caption may name a hidden card or leak an ID. The
/// local seat plays random-legal; the opponent seat plays as the fuzz case says.
/// </summary>
public static class PresentationAudit
{
    /// <summary>The beat kinds the full catalog must show over an audit.</summary>
    public static IReadOnlyList<PresentationEventKind> Catalog { get; } =
    [
        PresentationEventKind.TurnStarted, PresentationEventKind.CardDrawn, PresentationEventKind.CreditsChanged,
        PresentationEventKind.CardInstalled, PresentationEventKind.CardPlayed, PresentationEventKind.CardRezzed,
        PresentationEventKind.CardAdvanced, PresentationEventKind.AgendaScored, PresentationEventKind.RunStarted,
        PresentationEventKind.EncounterStarted, PresentationEventKind.SubroutinesBroken, PresentationEventKind.SubroutineFired,
        PresentationEventKind.CardAccessed, PresentationEventKind.AgendaStolen, PresentationEventKind.CardMoved,
        PresentationEventKind.DamageDealt, PresentationEventKind.TagsChanged, PresentationEventKind.RunEnded,
        PresentationEventKind.GameEnded,
    ];

    private const int CaptionSample = 400;

    public static async Task<IReadOnlyList<PresentationAuditGame>> RunAsync(IReadOnlyList<FuzzGameCase> cases, int workers,
        int actionCap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var work = cases.SelectMany(fuzzCase => new[] { (fuzzCase, PlayerSide.Corp), (fuzzCase, PlayerSide.Runner) }).ToArray();
        var results = new ConcurrentDictionary<int, PresentationAuditGame>();
        await Parallel.ForEachAsync(Enumerable.Range(0, work.Length),
            new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken },
            async (index, cancellation) => results[index] = await AuditAsync(work[index].fuzzCase, work[index].Item2, actionCap,
                cancellation).ConfigureAwait(false)).ConfigureAwait(false);
        return [.. Enumerable.Range(0, work.Length).Select(index => results[index])];
    }

    public static async Task<PresentationAuditGame> AuditAsync(FuzzGameCase fuzzCase, PlayerSide viewer, int actionCap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fuzzCase);
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(fuzzCase.ContentId, fuzzCase.GameSeed);
        PlayerSide opponent = viewer == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        FuzzAgentKind opponentKind = opponent == PlayerSide.Corp ? fuzzCase.Corp : fuzzCase.Runner;
        IGameAgent bot = opponentKind == FuzzAgentKind.Heuristic
            ? OfflineMatchFactory.CreateOpponent(definition, viewer)
            : new RandomLegalAgent(AgentLifecycleHost.DerivePolicySeed(fuzzCase.GameSeed, opponent));
        var local = new RandomLegalAgent(AgentLifecycleHost.DerivePolicySeed(fuzzCase.GameSeed, viewer));
        var snapshots = new ConcurrentQueue<MatchSnapshot>();
        string? failure = null;
        await using (var coordinator = new MatchCoordinator(definition, bot, new MatchCoordinatorOptions { HumanSide = viewer }))
        {
            coordinator.SnapshotPublished += snapshots.Enqueue;
            try
            {
                await coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
                for (int count = 0; count < actionCap; count++)
                {
                    Observation observation = await coordinator.GetReadyObservationAsync(viewer, cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                    if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
                    MatchSubmissionResult result = await coordinator.SubmitHumanActionAsync(
                        await local.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false), cancellationToken)
                        .ConfigureAwait(false);
                    if (!result.Accepted)
                    {
                        failure = $"local action rejected: {result.Feedback}";
                        break;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failure = $"{exception.GetType().Name}: {exception.Message}";
            }
        }
        return Check(fuzzCase, viewer, [.. snapshots.OrderBy(snapshot => snapshot.Sequence)], failure);
    }

    /// <summary>Checks consecutive publications; public for tests that already hold snapshots.</summary>
    public static PresentationAuditGame Check(FuzzGameCase fuzzCase, PlayerSide viewer, IReadOnlyList<MatchSnapshot> snapshots,
        string? failure = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var mismatches = new List<string>();
        var leaks = new List<string>();
        var kinds = new SortedDictionary<PresentationEventKind, int>();
        var captions = new List<string>();
        var names = new ServerNames();
        int beats = 0;
        for (int index = 1; index < snapshots.Count; index++)
        {
            MatchSnapshot previous = snapshots[index - 1], current = snapshots[index];
            names.Observe(current.View);
            if (current.PresentationEvents.Count == 0) continue;
            BoardDisplayState shown = current.PresentationEvents.Aggregate(BoardDisplayState.From(previous.View),
                (state, value) => state.Apply(value));
            foreach (string difference in shown.Differences(BoardDisplayState.From(current.View)))
                mismatches.Add($"action {current.ActionCount}: {difference} "
                    + $"(events {string.Join(",", current.PresentationEvents.Select(value => value.Kind))})");
            foreach (Beat beat in BeatPlanner.Plan(current.PresentationEvents, previous.View, current.View, names.Name))
            {
                beats++;
                if (captions.Count < CaptionSample) captions.Add(beat.Caption);
                kinds[beat.Kind] = kinds.GetValueOrDefault(beat.Kind) + 1;
                if (string.IsNullOrWhiteSpace(beat.Caption) || beat.Caption.Contains("card:", StringComparison.Ordinal)
                    || beat.Caption.Contains(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal))
                    leaks.Add($"action {current.ActionCount}: '{beat.Caption}'");
            }
        }
        return new(fuzzCase, viewer, snapshots.Count, beats, kinds, mismatches, leaks,
            snapshots.Count > 0 && snapshots[^1].IsTerminal, failure)
        { Captions = captions };
    }

    /// <summary>Player-facing server names, numbering remotes in the order they appear, as the board does.</summary>
    private sealed class ServerNames
    {
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
        private int _remotes;

        public void Observe(MatchViewModel view)
        {
            foreach (ServerViewModel server in view.Servers)
            {
                if (_names.ContainsKey(server.Id)) continue;
                _names[server.Id] = server.Kind switch
                {
                    ServerKind.Headquarters => "HQ",
                    ServerKind.RAndD => "R&D",
                    ServerKind.Archives => "Archives",
                    _ => $"Remote {++_remotes}",
                };
            }
        }

        public string Name(string id) => _names.GetValueOrDefault(id, "a server");
    }
}
