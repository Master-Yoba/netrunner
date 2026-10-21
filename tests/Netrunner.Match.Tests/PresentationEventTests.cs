using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class PresentationEventTests
{
    [Fact]
    public void EveryCorePayloadTypeHasAnExplicitPresentationRule()
    {
        Type[] payloads = typeof(EngineEventPayload).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(EngineEventPayload)) && !type.IsAbstract).ToArray();

        Assert.NotEmpty(payloads);
        Assert.Empty(payloads.Where(type => !PresentationEventProjector.Rules.ContainsKey(type)).Select(type => type.Name));
    }

    [Fact]
    public async Task RandomGamesProjectTheSliceWithoutLeakingHiddenCards()
    {
        var kinds = new HashSet<PresentationEventKind>();
        bool hiddenAdvance = false;
        foreach (ulong seed in new ulong[] { 11, 12, 13, 14, 15, 16 })
        {
            foreach (Step step in await PlayAsync(seed, PlayerSide.Runner))
            {
                foreach (PresentationEvent value in step.Events)
                {
                    kinds.Add(value.Kind);
                    foreach (string? key in new[] { value.CardKey, value.PreviousCardKey })
                    {
                        if (key is null || key.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal)) continue;
                        Assert.True(Visible(step.Before, key) || Visible(step.After, key),
                            $"{value.Kind} names hidden card {key} to the Runner.");
                    }
                    if (value is { Kind: PresentationEventKind.CardAdvanced, CardKey: { } advanced }
                        && advanced.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal))
                        hiddenAdvance = true;
                }
            }
        }

        Assert.Superset(new HashSet<PresentationEventKind>
        {
            PresentationEventKind.TurnStarted, PresentationEventKind.CardInstalled, PresentationEventKind.CardRezzed,
            PresentationEventKind.RunStarted, PresentationEventKind.IceApproached, PresentationEventKind.EncounterStarted,
            PresentationEventKind.SubroutineFired, PresentationEventKind.RunEnded,
        }, kinds);
        // The developer decks install no agendas, so only official content advances cards.
        if (Official) Assert.True(hiddenAdvance, "No face-down advance reached the Runner as a tracked card.");
    }

    [Fact]
    public async Task TrackedKeysStayStableWhileTheCardStaysInstalledAndHidden()
    {
        var history = new Dictionary<CardInstanceId, string>();
        foreach (Step step in await PlayAsync(11, PlayerSide.Runner))
        {
            foreach ((CardInstanceId id, string key) in step.KeysAfter.Where(pair =>
                pair.Value.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal)))
            {
                if (step.KeysBefore.TryGetValue(id, out string? previous)
                    && previous.StartsWith(CardKeyTracker.TrackedPrefix, StringComparison.Ordinal))
                    Assert.Equal(previous, key);
                history.TryAdd(id, key);
            }
        }
        Assert.NotEmpty(history);
        Assert.Equal(history.Count, history.Values.Distinct().Count());
    }

    [Fact]
    public async Task PublishedViewsKeyEveryServerPositionAndMapReveals()
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
            Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, 41);
        await using var coordinator = new MatchCoordinator(definition, new RandomLegalAgent(41),
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Runner });
        var snapshots = new System.Collections.Concurrent.ConcurrentQueue<MatchSnapshot>();
        coordinator.SnapshotPublished += snapshots.Enqueue;
        await coordinator.StartAsync();
        var human = new RandomLegalAgent(42);
        for (int count = 0; count < 250; count++)
        {
            Observation observation = await coordinator.GetReadyObservationAsync(PlayerSide.Runner).WaitAsync(TimeSpan.FromSeconds(10));
            if (coordinator.CurrentSnapshot is { IsTerminal: true }) break;
            Assert.True((await coordinator.SubmitHumanActionAsync(await human.ChooseActionAsync(observation))).Accepted);
        }

        bool tracked = false, revealed = false;
        foreach (MatchSnapshot snapshot in snapshots)
        {
            foreach (ServerViewModel server in snapshot.View.Servers)
            {
                Assert.Equal(server.Ice.Count, server.IceKeys.Count);
                Assert.Equal(server.Root.Count, server.RootKeys.Count);
                foreach ((CardViewModel? card, string? key) in server.Ice.Zip(server.IceKeys).Concat(server.Root.Zip(server.RootKeys)))
                {
                    if (card is null) Assert.StartsWith(CardKeyTracker.TrackedPrefix, key);
                    else Assert.Equal(card.Id, key);
                    tracked |= card is null;
                }
            }
            foreach ((string id, string previous) in snapshot.View.RevealedFrom)
            {
                Assert.StartsWith(CardKeyTracker.TrackedPrefix, previous);
                Assert.Contains(snapshot.View.Servers, server => server.IceKeys.Concat(server.RootKeys).Contains(id));
                revealed = true;
            }
        }
        Assert.True(tracked && revealed, $"tracked={tracked} revealed={revealed}");
    }

    private sealed record Step(GameState Before, GameState After, IReadOnlyList<PresentationEvent> Events,
        IReadOnlyDictionary<CardInstanceId, string> KeysBefore, IReadOnlyDictionary<CardInstanceId, string> KeysAfter);

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static bool Visible(GameState state, string id) =>
        ObservationBuilder.VisibleCardIds(state, PlayerSide.Runner).Contains(new CardInstanceId(id));

    private static async Task<IReadOnlyList<Step>> PlayAsync(ulong seed, PlayerSide viewer)
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
            Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, seed);
        var tracker = new CardKeyTracker();
        var corp = new RandomLegalAgent(seed);
        var runner = new RandomLegalAgent(seed + 1000);
        GameState state = definition.Start().State;
        IReadOnlyDictionary<CardInstanceId, string> keys = tracker.Update(state, viewer);
        var steps = new List<Step>();
        for (int count = 0; count < 600 && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending; count++)
        {
            PlayerSide side = state.GetPlayer(pending.Owner).Side;
            ActionId chosen = await (side == PlayerSide.Corp ? corp : runner)
                .ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior));
            ApplyResult applied = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen),
                definition.Behavior, definition.Runtime);
            Assert.True(applied.Accepted);
            IReadOnlyDictionary<CardInstanceId, string> after = tracker.Update(applied.State, viewer);
            steps.Add(new(state, applied.State, PresentationEventProjector.Project(state, applied.State, applied.Events, viewer,
                keys, after, definition.Behavior), keys, after));
            state = applied.State;
            keys = after;
        }
        return steps;
    }
}
