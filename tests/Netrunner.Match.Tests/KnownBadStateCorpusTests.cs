using System.Text.Json;
using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The known-bad state corpus (engine plan E2 gate). Real positions from
/// random-legal developer games are corrupted by one mutation each, the kinds
/// of corruption the validator exists to catch: duplicated or unzoned cards,
/// broken zone references, impossible counters and card states, and decisions
/// whose legal actions don't match the state. The committed manifest records
/// which cases authoritative Apply rejected, and with which violation codes,
/// before the validator was optimized; every change must reject the same.
/// Set NETRUNNER_REGENERATE_KNOWN_BAD=1 to rewrite the manifest deliberately.
/// </summary>
public sealed class KnownBadStateCorpusTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static readonly string ManifestPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures",
        "known-bad-state-corpus.json");

    [Fact]
    public void AuthoritativeApplyRejectsEveryKnownBadStateItRejectedBefore()
    {
        List<CorpusCase> cases = BuildCorpus();
        Dictionary<string, CorpusResult> results = cases.ToDictionary(value => value.Id, Evaluate, StringComparer.Ordinal);
        if (Environment.GetEnvironmentVariable("NETRUNNER_REGENERATE_KNOWN_BAD") == "1")
        {
            File.WriteAllText(Path.GetFullPath(ManifestPath), JsonSerializer.Serialize(
                results.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value),
                Indented) + "\n");
        }

        Dictionary<string, CorpusResult> expected = JsonSerializer.Deserialize<Dictionary<string, CorpusResult>>(
            File.ReadAllText(Path.GetFullPath(ManifestPath)))!;
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), results.Keys.Order(StringComparer.Ordinal));
        var regressions = expected.Where(pair => !pair.Value.Equivalent(results[pair.Key]))
            .Select(pair => $"{pair.Key}: expected {pair.Value}, got {results[pair.Key]}").ToList();
        Assert.True(regressions.Count == 0, string.Join("\n", regressions.Take(20)));
        // The corpus is only useful if it is mostly rejected.
        Assert.True(results.Values.Count(value => value.Rejected) * 10 >= results.Count * 9,
            $"only {results.Values.Count(value => value.Rejected)} of {results.Count} known-bad states were rejected");
    }

    private static CorpusResult Evaluate(CorpusCase value)
    {
        PendingDecision? pending = value.Valid.PendingDecision;
        var submission = new ActionSubmission(pending!.Owner, pending.Id, pending.Revision, pending.LegalActions[0].Id);
        ApplyResult result = GameEngine.Apply(value.Bad, submission, value.Definition.Behavior, value.Definition.Runtime);
        IReadOnlyList<StateInvariantViolation> violations =
            StateInvariantValidator.Collect(value.Bad, value.Definition.Behavior, value.Definition.Runtime);
        return new CorpusResult(!result.Accepted && result.Rejection?.Code == ApplyRejectionCode.InvalidInputState,
            [.. violations.Select(violation => violation.Code).Distinct().Order(StringComparer.Ordinal)]);
    }

    private sealed record CorpusCase(string Id, FullMatchDefinition Definition, GameState Valid, GameState Bad);

    public sealed record CorpusResult(bool Rejected, string[] Codes)
    {
        public bool Equivalent(CorpusResult other) => Rejected == other.Rejected && Codes.SequenceEqual(other.Codes);

        public override string ToString() => $"{(Rejected ? "rejected" : "accepted")} [{string.Join(", ", Codes)}]";
    }

    /// <summary>Every fifth position of ten developer games, each corrupted by every applicable mutation.</summary>
    private static List<CorpusCase> BuildCorpus()
    {
        var cases = new List<CorpusCase>();
        foreach (ulong seed in new ulong[] { 3, 5, 8, 13, 21, 34, 55, 89, 144, 233 })
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.DeveloperContentId, seed);
            var corp = new RandomLegalAgent(seed * 2);
            var runner = new RandomLegalAgent(seed * 2 + 1);
            GameState state = definition.Start().State;
            for (int action = 0; action < 400 && state.Outcome.Kind == GameOutcomeKind.Ongoing
                && state.PendingDecision is { } pending; action++)
            {
                if (action % 5 == 0)
                {
                    foreach ((string name, Func<GameState, GameState?> mutate) in Mutations)
                    {
                        if (mutate(state) is { } bad) cases.Add(new($"dev-{seed}/{action:D3}/{name}", definition, state, bad));
                    }
                }
                PlayerSide side = state.GetPlayer(pending.Owner).Side;
                ActionId chosen = (side == PlayerSide.Corp ? corp : runner)
                    .ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior)).AsTask().GetAwaiter().GetResult();
                ApplyResult applied = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen),
                    definition.Behavior, definition.Runtime);
                Assert.True(applied.Accepted, applied.Rejection?.Message);
                state = applied.State;
            }
        }
        return cases;
    }

    private static readonly (string Name, Func<GameState, GameState?> Mutate)[] Mutations =
    [
        ("card-duplicate-id", state => Rebuild(state, cards: [.. state.Cards, state.Cards[0]])),
        ("card-negative-advancement", state => WithCard(state, card => Card(card, advancement: -1))),
        ("card-rezzed-facedown", state => WithCard(state, card => Card(card, faceUp: false, rezzed: true))),
        ("card-wrong-zone-reference", state => WithCard(state, card => Card(card,
            zone: state.Zones.First(zone => zone.Id != card.ZoneId).Id))),
        ("card-unzoned", state => state.Zones.FirstOrDefault(zone => zone.CardIds.Count > 0) is { } zone
            ? Rebuild(state, zones: Replace(state.Zones, zone, Zone(zone, zone.CardIds.Skip(1)))) : null),
        ("card-in-two-zones", state => state.Zones.FirstOrDefault(zone => zone.CardIds.Count > 0) is { } source
            && state.Zones.FirstOrDefault(zone => zone.Id != source.Id) is { } other
                ? Rebuild(state, zones: Replace(state.Zones, other, Zone(other, other.CardIds.Append(source.CardIds[0])))) : null),
        ("zone-unknown-card", state => Rebuild(state, zones: Replace(state.Zones, state.Zones[0],
            Zone(state.Zones[0], state.Zones[0].CardIds.Append(new CardInstanceId("card:forged")))))),
        ("heap-card-facedown", state => state.Zones.FirstOrDefault(zone => zone.Kind == ZoneKind.RunnerHeap && zone.CardIds.Count > 0)
            is { } heap ? WithCard(state, card => Card(card, faceUp: false), heap.CardIds[0]) : null),
        ("player-negative-credits", state => Rebuild(state, players: Replace(state.Players, state.Players[0],
            new PlayerState(state.Players[0].Id, state.Players[0].Side, -1, state.Players[0].Clicks, state.Players[0].AgendaPoints)))),
        ("player-negative-clicks", state => Rebuild(state, players: Replace(state.Players, state.Players[1],
            new PlayerState(state.Players[1].Id, state.Players[1].Side, state.Players[1].Credits, -1, state.Players[1].AgendaPoints)))),
        ("decision-missing-action", state => state.PendingDecision is { LegalActions.Count: > 1 } pending
            ? Rebuild(state, pending: Decision(pending, pending.LegalActions.Skip(1))) : null),
        ("decision-duplicated-action", state => state.PendingDecision is { } pending
            ? Rebuild(state, pending: Decision(pending, pending.LegalActions.Append(pending.LegalActions[0]))) : null),
        ("decision-wrong-owner", state => state.PendingDecision is { } pending
            ? Rebuild(state, pending: new PendingDecision(pending.Id, pending.Revision,
                state.Players.First(player => player.Id != pending.Owner).Id, pending.Kind, pending.Prompt, pending.LegalActions)) : null),
        ("resolution-top-frame-dropped", state => state.ResolutionStack.Count > 0
            ? Rebuild(state, stack: [.. state.ResolutionStack.Take(state.ResolutionStack.Count - 1)]) : null),
        ("resolution-bottom-frame-dropped", state => state.ResolutionStack.Count > 1
            ? Rebuild(state, stack: [.. state.ResolutionStack.Skip(1)]) : null),
        ("resolution-top-frame-duplicated", state => state.ResolutionStack.Count > 0
            ? Rebuild(state, stack: [.. state.ResolutionStack, state.ResolutionStack[^1]]) : null),
        ("run-state-removed", state => state.RunState is not null ? Rebuild(state, removeRun: true) : null),
        ("outcome-won-while-deciding", state => Rebuild(state,
            outcome: new GameOutcome(GameOutcomeKind.Won, PlayerSide.Corp, GameWinReason.AgendaPoints))),
    ];

    private static GameState? WithCard(GameState state, Func<CardInstanceState, CardInstanceState> change, CardInstanceId? id = null)
    {
        CardInstanceState? card = id is { } wanted ? state.Cards.FirstOrDefault(value => value.Id == wanted)
            : state.Cards.Count > 0 ? state.Cards[0] : null;
        return card is null ? null : Rebuild(state, cards: Replace(state.Cards, card, change(card)));
    }

    private static CardInstanceState Card(CardInstanceState card, bool? faceUp = null, bool? rezzed = null, int? advancement = null,
        ZoneId? zone = null) =>
        new(card.Id, card.DefinitionId, zone ?? card.ZoneId, card.OwnerSide, faceUp ?? card.FaceUp, rezzed ?? card.Rezzed,
            advancement ?? card.AdvancementCounters, card.HostCardId);

    private static ZoneState Zone(ZoneState zone, IEnumerable<CardInstanceId> cards) =>
        new(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [.. cards]);

    private static PendingDecision Decision(PendingDecision pending, IEnumerable<LegalAction> actions) =>
        new(pending.Id, pending.Revision, pending.Owner, pending.Kind, pending.Prompt, [.. actions]);

    private static T[] Replace<T>(IReadOnlyList<T> values, T old, T replacement) where T : class =>
        [.. values.Select(value => ReferenceEquals(value, old) ? replacement : value)];

    private static GameState Rebuild(GameState state, IReadOnlyList<CardInstanceState>? cards = null,
        IReadOnlyList<ZoneState>? zones = null, IReadOnlyList<PlayerState>? players = null, PendingDecision? pending = null,
        GameOutcome? outcome = null, IReadOnlyList<ResolutionFrame>? stack = null, bool removeRun = false) =>
        new(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, players ?? state.Players,
            cards ?? state.Cards, zones ?? state.Zones, state.Servers, state.Turn, outcome ?? state.Outcome, state.RandomState,
            state.Allocator, state.DecisionRevision, state.EventSequence, stack ?? state.ResolutionStack, pending ?? state.PendingDecision,
            state.ActionSequence, state.InitialSeed, removeRun ? null : state.RunState, state.RulesState, state.BehaviorRegistryVersion,
            state.BehaviorCatalogFingerprint, state.RuntimeCardCatalogVersion, state.RuntimeCardCatalogFingerprint);
}
