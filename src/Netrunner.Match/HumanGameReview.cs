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

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Slug();

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
        OfflineAgentConfiguration configuration = OfflinePolicies.Configure(reviewedSide, profile, seed);
        var agent = new OfflineAgent(
            configuration,
            OfflinePolicies.Create(configuration),
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
                Observation observation = ObservationBuilder.Build(state, reviewedSide, behavior, runtime);
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
            .Concat(view.RunnerHeap).Concat(view.ChoiceCards).Concat(view.CorpScore).Concat(view.RunnerScore)
            // Bot loop 1: the identities (Topan's ability read "Use a hidden card"), hosted faceup cards, the play area and
            // removed-from-game cards.
            .Concat(view.HostedFaceUp).Concat(view.PlayArea).Concat(view.CorpRemovedFromGame).Concat(view.RunnerRemovedFromGame)
            .Concat(new[] { view.CorpIdentity, view.RunnerIdentity }.OfType<CardViewModel>()))
        {
            if (card.IsKnown) titles.TryAdd(card.Id, card.Title);
        }

        var servers = new Dictionary<string, string>(StringComparer.Ordinal);
        // Rig cards by ID, told apart by their counters when there are two of a title (two Fermenters).
        Dictionary<string, CardViewModel> rig = view.RunnerRig.ToDictionary(card => card.Id, StringComparer.Ordinal);
        HashSet<string> repeated = [.. view.RunnerRig.GroupBy(card => card.Title, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];
        // Installed Corp cards by ID, and where each is (see LlmBoardText.Locations).
        Dictionary<string, CardViewModel> installed = view.Servers.SelectMany(server => server.Root.Concat(server.Ice)).OfType<CardViewModel>()
            .DistinctBy(card => card.Id).ToDictionary(card => card.Id, StringComparer.Ordinal);
        Dictionary<string, string> locations = Llm.LlmBoardText.Locations(view);
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
                + (play.TargetServerId is { } target ? $" on {Server(target)}" : string.Empty)
                + (play.TrashCostCardId is { } trashed ? $", trashing {Card(trashed)}" : string.Empty),
            InstallCardParameters install => $"Install {Card(install.CardId)}"
                // An unrezzed host's position reaches the Runner only once a card is hosted (ObservationBuilder.RunEnd).
                + (install.HostCardId is { } host ? titles.ContainsKey(host.Value) || locations.ContainsKey(host.Value) ? $" on {Card(host)}{Where(host)}" : " on unrezzed ICE"
                    : install.CreateNewRemote ? " in a new remote" : install.ServerId is { } server ? $" in {Server(server)}" : string.Empty)
                + (install.SelectedReplacementCardIds is { Count: > 0 } replaced ? $", trashing {string.Join(" and ", replaced.Select(Card))}" : string.Empty),
            RezCardParameters rez => $"Rez {Card(rez.CardId)}{Where(rez.CardId)}",
            RunIceParameters ice => $"Rez {Card(ice.IceId)}{Where(ice.IceId)}",
            AdvanceCardParameters advance => $"Advance {Card(advance.CardId)}{Where(advance.CardId)}{Progress(advance.CardId)}",
            RunServerParameters run => $"Run {Server(run.ServerId)}",
            RunBreakParameters breaking => $"Break with {Card(breaking.BreakerId)}",
            RunBreakerParameters pump => $"Boost {Card(pump.BreakerId)}",
            AbilityActionParameters ability => $"Use {Card(ability.SourceCardId)}{Where(ability.SourceCardId)}{Ability(ability)}",
            TrashTaggedResourceParameters trash => $"Trash {Card(trash.CardId)} (the Runner is tagged)",
            BreachCandidateParameters { CardId: { } candidate } => $"Access {Card(candidate)}",
            CardChoiceParameters choice => $"{Names(action.Description)}: {Card(choice.CardId)}",
            _ => Names(action.Description),
        };

        // Core descriptions can embed raw IDs; map them through the same visible names.
        string Names(string text) => IdPattern().Replace(text, match => match.Value.StartsWith("card:", StringComparison.Ordinal)
            ? titles.GetValueOrDefault(match.Value, "a hidden card")
            : servers.GetValueOrDefault(match.Value, "a server"));

        string Card(CardInstanceId id) => titles.GetValueOrDefault(id.Value, locations.ContainsKey(id.Value) ? "unrezzed ICE" : "a hidden card");
        string Server(ServerId id) => servers.GetValueOrDefault(id.Value, "a server");
        string Where(CardInstanceId id) => locations.TryGetValue(id.Value, out string? at) ? $" in {at}"
            : rig.TryGetValue(id.Value, out CardViewModel? card) && repeated.Contains(card.Title) && Llm.LlmBoardText.Counters(card) is { Length: > 0 } counters
                ? $" ({counters})" : string.Empty;

        // The ability's name from its key, without the card's name: Red Team's "red-team-run-rnd" is "run R&D", Smartware
        // Distributor's "smartware-place" is "place".
        string Ability(AbilityActionParameters ability)
        {
            string[] key = ability.AbilityKey.Split('-'), title = Slug().Replace(Card(ability.SourceCardId).ToLowerInvariant(), "-").Split('-');
            int shared = key.Zip(title).TakeWhile(pair => pair.First == pair.Second).Count();
            return shared == key.Length ? string.Empty
                : ": " + string.Join(" ", key.Skip(shared).Select(word => word switch { "rnd" => "R&D", "hq" => "HQ", _ => word }));
        }

        // An advanceable card's counters, against its requirement when the viewer knows it.
        string Progress(CardInstanceId id) => !installed.TryGetValue(id.Value, out CardViewModel? card) ? string.Empty
            : card.AdvancementRequirement is { } requirement ? $" ({card.AdvancementCounters} of {requirement} advancements)"
            : $" ({card.AdvancementCounters} advancements)";
    }
}
