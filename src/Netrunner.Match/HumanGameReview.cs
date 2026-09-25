using System.Text.RegularExpressions;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record ReviewCandidate(string ActionId, ActionKind Kind, string Description, int Priority, int Score, string Reason);

/// <summary>One human decision next to the bot's choice for the same position.</summary>
public sealed record ReviewedDecision(
    long Sequence,
    int? Turn,
    DecisionKind DecisionKind,
    int LegalActions,
    string HumanActionId,
    ActionKind? HumanKind,
    string? HumanDescription,
    string BotActionId,
    ActionKind? BotKind,
    string? BotDescription,
    bool Agrees,
    IReadOnlyList<ReviewCandidate> TopCandidates);

/// <summary>
/// Replays a recorded game and asks an offline policy for its choice at every
/// decision the reviewed side made. The policy only observes; it never commits
/// a choice, so its knowledge follows the real game. The review sees exactly
/// the reviewed side's observations, never hidden opponent state.
/// </summary>
public static partial class HumanGameReview
{
    [GeneratedRegex(@"\b(card|server):\d+\b")]
    private static partial Regex IdPattern();

    public static async Task<IReadOnlyList<ReviewedDecision>> ReviewAsync(ReplayArtifact replay, PlayerSide reviewedSide,
        IBehaviorCatalog behavior, IRuntimeCardCatalog runtime, ICardMetadataResolver metadata, string profile = "standard",
        int topCandidates = 3, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(behavior);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(metadata);
        GameState state = GameState.FromSnapshot(replay.InitialSnapshot, behavior, runtime);
        ulong seed = AgentLifecycleHost.DerivePolicySeed(state.InitialSeed, reviewedSide);
        var agent = new OfflineAgent(
            reviewedSide == PlayerSide.Corp ? CorpPolicyProfiles.Create(profile, seed) : RunnerPolicyProfiles.Create(profile, seed),
            reviewedSide == PlayerSide.Corp ? new CorpHeuristicPolicy() : new RunnerHeuristicPolicy(),
            new GatewayEvaluationCatalog(runtime, behavior),
            captureDiagnostics: true);
        AgentLifecycleHost.Observe(agent, state, behavior);

        var reviewed = new List<ReviewedDecision>();
        foreach (ReplayAction recorded in replay.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingDecision pending = state.PendingDecision
                ?? throw new InvalidOperationException($"Replay action {recorded.Sequence} has no pending decision.");
            if (state.GetPlayer(pending.Owner).Side == reviewedSide)
            {
                Observation observation = ObservationBuilder.Build(state, reviewedSide, behavior);
                // Names come from the reviewed side's own view, so hidden cards stay unnamed.
                Func<LegalAction?, string?> describe = Describer(ObservationViewMapper.Build(observation, metadata, behavior));
                ActionId bot = await agent.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
                AgentEvaluation? evaluation = agent.LastEvaluation;
                agent.DiscardDecision();
                LegalAction? human = pending.LegalActions.FirstOrDefault(action => action.Id == recorded.Submission.ActionId);
                LegalAction? chosen = pending.LegalActions.FirstOrDefault(action => action.Id == bot);
                reviewed.Add(new ReviewedDecision(
                    recorded.Sequence,
                    state.Turn.TurnNumber,
                    pending.Kind,
                    pending.LegalActions.Count,
                    recorded.Submission.ActionId.Value,
                    human?.Kind,
                    describe(human),
                    bot.Value,
                    chosen?.Kind,
                    describe(chosen),
                    bot == recorded.Submission.ActionId,
                    evaluation is null ? [] : evaluation.Candidates
                        .OrderByDescending(candidate => candidate.Utility.Priority)
                        .ThenByDescending(candidate => candidate.Utility.Score)
                        .Take(topCandidates)
                        .Select(candidate =>
                        {
                            LegalAction? action = pending.LegalActions.FirstOrDefault(value => value.Id == candidate.ActionId);
                            return new ReviewCandidate(candidate.ActionId.Value, action?.Kind ?? ActionKind.Legacy,
                                describe(action) ?? string.Empty, candidate.Utility.Priority, candidate.Utility.Score,
                                candidate.Utility.Reason);
                        })
                        .ToArray()));
            }

            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, behavior, runtime);
            if (!applied.Accepted)
                throw new InvalidOperationException($"Replay action {recorded.Sequence} was rejected: {applied.Rejection?.Message}");
            state = applied.State;
            AgentLifecycleHost.Observe(agent, state, behavior);
        }

        return reviewed;
    }

    /// <summary>Describes an issued action with the card and server names visible in <paramref name="view"/>.</summary>
    public static Func<LegalAction?, string?> Describer(MatchViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CardViewModel card in view.Servers.SelectMany(server => server.Root.Concat(server.Ice)).OfType<CardViewModel>()
            .Concat(view.CorpHand.VisibleCards).Concat(view.RunnerHand.VisibleCards).Concat(view.RunnerRig).Concat(view.CorpDiscard)
            .Concat(view.RunnerHeap).Concat(view.ChoiceCards).Concat(view.CorpScore).Concat(view.RunnerScore))
        {
            if (card.IsKnown) titles.TryAdd(card.Id, card.Title);
        }

        var servers = new Dictionary<string, string>(StringComparer.Ordinal);
        int remote = 0;
        foreach (ServerViewModel server in view.Servers)
        {
            servers[server.Id] = server.Kind switch
            {
                ServerKind.Headquarters => "HQ",
                ServerKind.RAndD => "R&D",
                ServerKind.Archives => "Archives",
                ServerKind.Remote => $"Remote {++remote}",
                _ => "a server",
            };
        }

        return action => action is null ? null : action.Parameters switch
        {
            PlayCardParameters play => $"Play {Card(play.CardId)}"
                + (play.TargetServerId is { } target ? $" on {Server(target)}" : string.Empty),
            InstallCardParameters install => $"Install {Card(install.CardId)}"
                + (install.CreateNewRemote ? " in a new remote" : install.ServerId is { } server ? $" in {Server(server)}" : string.Empty),
            RezCardParameters rez => $"Rez {Card(rez.CardId)}",
            RunIceParameters ice => $"Rez {Card(ice.IceId)}",
            AdvanceCardParameters advance => $"Advance {Card(advance.CardId)}",
            RunServerParameters run => $"Run {Server(run.ServerId)}",
            RunBreakParameters breaking => $"Break with {Card(breaking.BreakerId)}",
            RunBreakerParameters pump => $"Boost {Card(pump.BreakerId)}",
            AbilityActionParameters ability => $"Use {Card(ability.SourceCardId)}",
            BreachCandidateParameters { CardId: { } candidate } => $"Access {Card(candidate)}",
            CardChoiceParameters choice => $"{Names(action.Description)}: {Card(choice.CardId)}",
            _ => Names(action.Description),
        };

        // Core descriptions can embed raw IDs; map them through the same visible names.
        string Names(string text) => IdPattern().Replace(text, match => match.Value.StartsWith("card:", StringComparison.Ordinal)
            ? titles.GetValueOrDefault(match.Value, "a hidden card")
            : servers.GetValueOrDefault(match.Value, "a server"));

        string Card(CardInstanceId id) => titles.GetValueOrDefault(id.Value, "a hidden card");
        string Server(ServerId id) => servers.GetValueOrDefault(id.Value, "a server");
    }
}
