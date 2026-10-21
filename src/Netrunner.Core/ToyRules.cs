using System.Collections.ObjectModel;

namespace Netrunner.Core;

/// <summary>
/// Stable IDs for the deliberately tiny, engine-only Slice 4 fixture. These
/// IDs are not a card-data parser or an official card behavior registry.
/// </summary>
public static class ToyCardDefinitionIds
{
    public static readonly CardDefinitionId CorpAgenda3 = new("fixture:corp-agenda-3");
    public static readonly CardDefinitionId RunnerProgram = new("fixture:runner-program");
}

public enum ToyCardKind
{
    CorpAgenda,
    RunnerProgram,
}

/// <summary>One explicitly registered fixture behavior; card text is ignored.</summary>
public sealed record ToyCardBehavior(
    CardDefinitionId DefinitionId,
    PlayerSide OwnerSide,
    ToyCardKind Kind,
    int InstallCost,
    int AgendaPoints,
    int AdvancementRequirement);

/// <summary>
/// Build-versioned fixture behavior catalog. Unknown definitions fail closed
/// instead of being interpreted from their code or text.
/// </summary>
public static class ToyCardBehaviorCatalog
{
    public const string Version = "fixture-1";

    private static readonly IReadOnlyDictionary<CardDefinitionId, ToyCardBehavior> Behaviors =
        new ReadOnlyDictionary<CardDefinitionId, ToyCardBehavior>(new Dictionary<CardDefinitionId, ToyCardBehavior>
        {
            [ToyCardDefinitionIds.CorpAgenda3] = new(
                ToyCardDefinitionIds.CorpAgenda3,
                PlayerSide.Corp,
                ToyCardKind.CorpAgenda,
                InstallCost: 2,
                AgendaPoints: 3,
                AdvancementRequirement: 3),
            [ToyCardDefinitionIds.RunnerProgram] = new(
                ToyCardDefinitionIds.RunnerProgram,
                PlayerSide.Runner,
                ToyCardKind.RunnerProgram,
                InstallCost: 2,
                AgendaPoints: 0,
                AdvancementRequirement: 0),
        });

    public static IReadOnlyCollection<ToyCardBehavior> All => Behaviors.Values.ToArray();

    public static bool TryGet(CardDefinitionId definitionId, out ToyCardBehavior behavior) =>
        Behaviors.TryGetValue(definitionId, out behavior!);

    public static ToyCardBehavior Get(CardDefinitionId definitionId) =>
        TryGet(definitionId, out ToyCardBehavior? behavior)
            ? behavior
            : throw new ArgumentException(
                $"Card definition '{definitionId}' is not registered in the fixture behavior catalog.",
                nameof(definitionId));
}

/// <summary>
/// Input recipe for a deterministic fixture deck. It is setup input, not
/// authoritative state, and intentionally contains only opaque definition IDs.
/// </summary>
public sealed class ToyDeckRecipe
{
    public ToyDeckRecipe(
        string recipeId,
        PlayerSide side,
        IReadOnlyList<CardDefinitionId> definitionIds)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            throw new ArgumentException("A fixture deck recipe ID is required.", nameof(recipeId));
        }

        ArgumentNullException.ThrowIfNull(definitionIds);
        RecipeId = recipeId;
        Side = side;
        DefinitionIds = new ReadOnlyCollection<CardDefinitionId>(definitionIds.ToArray());
    }

    public string RecipeId { get; }

    public PlayerSide Side { get; }

    public IReadOnlyList<CardDefinitionId> DefinitionIds { get; }
}

public static class ToyRules
{
    public const string BehaviorRegistryVersion = ToyCardBehaviorCatalog.Version;
    public const int AgendaPointsToWin = 3;
    public const int OpeningHandSize = 2;
    public const int CorpClicksPerTurn = 3;
    public const int RunnerClicksPerTurn = 4;

    public static readonly ServerKind FixtureRemoteKind = ServerKind.Remote;
}

/// <summary>One expected, allocator-independent toy legal-action identity.</summary>
public readonly record struct ToyActionSpec(ActionKind Kind, CardInstanceId? Target);

internal static class ToyActionRules
{
    public static IReadOnlyList<ToyActionSpec> Expected(
        PlayerSide side,
        PlayerState player,
        ZoneState hand,
        ZoneState stack,
        ZoneState? remoteRoot,
        IReadOnlyList<CardInstanceState> cards)
    {
        var cardById = cards
            .GroupBy(card => card.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var expected = new List<ToyActionSpec>();
        if (player.Clicks > 0)
        {
            expected.Add(new ToyActionSpec(ActionKind.GainCredit, null));
            if (side == PlayerSide.Corp || stack.CardIds.Count > 0)
            {
                expected.Add(new ToyActionSpec(ActionKind.DrawCard, null));
            }

            bool corpRemoteAvailable = side != PlayerSide.Corp || remoteRoot is not null && remoteRoot.CardIds.Count == 0;
            foreach (CardInstanceId cardId in hand.CardIds)
            {
                if (!cardById.TryGetValue(cardId, out CardInstanceState? card)
                    || !ToyCardBehaviorCatalog.TryGet(card.DefinitionId, out ToyCardBehavior? behavior)
                    || behavior.OwnerSide != side
                    || card.OwnerSide != side
                    || behavior.InstallCost > player.Credits
                    || !((side == PlayerSide.Corp && behavior.Kind == ToyCardKind.CorpAgenda)
                        || (side == PlayerSide.Runner && behavior.Kind == ToyCardKind.RunnerProgram))
                    || !corpRemoteAvailable)
                {
                    continue;
                }

                expected.Add(new ToyActionSpec(ActionKind.InstallCard, cardId));
            }

            if (side == PlayerSide.Corp && remoteRoot is not null)
            {
                foreach (CardInstanceId cardId in remoteRoot.CardIds)
                {
                    if (!cardById.TryGetValue(cardId, out CardInstanceState? card)
                        || !ToyCardBehaviorCatalog.TryGet(card.DefinitionId, out ToyCardBehavior? behavior)
                        || behavior.Kind != ToyCardKind.CorpAgenda
                        || card.OwnerSide != PlayerSide.Corp)
                    {
                        continue;
                    }

                    if (card.AdvancementCounters < behavior.AdvancementRequirement && player.Credits > 0)
                    {
                        expected.Add(new ToyActionSpec(ActionKind.AdvanceAgenda, cardId));
                    }
                }
            }
            else if (side == PlayerSide.Runner && remoteRoot is not null)
            {
                foreach (CardInstanceId cardId in remoteRoot.CardIds)
                {
                    if (cardById.TryGetValue(cardId, out CardInstanceState? card)
                        && ToyCardBehaviorCatalog.TryGet(card.DefinitionId, out ToyCardBehavior? behavior)
                        && behavior.Kind == ToyCardKind.CorpAgenda
                        && card.OwnerSide == PlayerSide.Corp)
                    {
                        expected.Add(new ToyActionSpec(ActionKind.StealAgenda, cardId));
                    }
                }
            }
        }

        // A completed agenda is scoreable even when the Corp has no click left.
        if (side == PlayerSide.Corp && remoteRoot is not null)
        {
            foreach (CardInstanceId cardId in remoteRoot.CardIds)
            {
                if (cardById.TryGetValue(cardId, out CardInstanceState? card)
                    && ToyCardBehaviorCatalog.TryGet(card.DefinitionId, out ToyCardBehavior? behavior)
                    && behavior.Kind == ToyCardKind.CorpAgenda
                    && card.OwnerSide == PlayerSide.Corp
                    && card.AdvancementCounters >= behavior.AdvancementRequirement)
                {
                    expected.Add(new ToyActionSpec(ActionKind.ScoreAgenda, cardId));
                }
            }
        }

        expected.Add(new ToyActionSpec(ActionKind.EndTurn, null));
        return expected.AsReadOnly();
    }
}

public enum ToyTurnStage
{
    CorpTurnStartDraw,
    RunnerTurnStart,
    AwaitingAction,
}

/// <summary>Deterministic setup for the two-card-kind fixture rules.</summary>
public static class ToyGameSetup
{
    public static GameState Create(
        ulong seed,
        ToyDeckRecipe corpRecipe,
        ToyDeckRecipe runnerRecipe)
    {
        return CreateCore(seed, corpRecipe, runnerRecipe, null);
    }

    /// <summary>
    /// Non-production test boundary for proving setup reconstruction retains
    /// the runtime-card catalog identity carried by its base state.
    /// </summary>
    internal static GameState CreateWithRuntimeCatalogForTesting(
        ulong seed,
        ToyDeckRecipe corpRecipe,
        ToyDeckRecipe runnerRecipe,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        return CreateCore(seed, corpRecipe, runnerRecipe, runtimeCardCatalog);
    }

    private static GameState CreateCore(
        ulong seed,
        ToyDeckRecipe corpRecipe,
        ToyDeckRecipe runnerRecipe,
        IRuntimeCardCatalog? runtimeCardCatalog)
    {
        ValidateRecipe(corpRecipe, PlayerSide.Corp, nameof(corpRecipe));
        ValidateRecipe(runnerRecipe, PlayerSide.Runner, nameof(runnerRecipe));

        GameState baseState = GameState.CreateNew(
            seed,
            rulesVersion: "26.03",
            cardDataVersion: "fixture-1",
            runtimeCardCatalogVersion: runtimeCardCatalog?.Version,
            runtimeCardCatalogFingerprint: runtimeCardCatalog?.Fingerprint);
        var allocator = new StableIdAllocator(baseState.Allocator);
        var servers = baseState.Servers.ToList();
        var zones = baseState.Zones.ToList();

        ServerId remoteId = allocator.NextServerId();
        ZoneId remoteIceId = allocator.NextZoneId();
        ZoneId remoteRootId = allocator.NextZoneId();
        servers.Add(new ServerState(remoteId, ServerKind.Remote, remoteIceId, remoteRootId));
        zones.Add(new ZoneState(remoteIceId, ZoneKind.ServerIce, PlayerSide.Corp, remoteId));
        zones.Add(new ZoneState(remoteRootId, ZoneKind.ServerRoot, PlayerSide.Corp, remoteId));

        ZoneState corpDeckZone = zones.Single(zone => zone.Kind == ZoneKind.CorpDeck);
        ZoneState corpHandZone = zones.Single(zone => zone.Kind == ZoneKind.CorpHand);
        ZoneState runnerStackZone = zones.Single(zone => zone.Kind == ZoneKind.RunnerStack);
        ZoneState runnerGripZone = zones.Single(zone => zone.Kind == ZoneKind.RunnerGrip);
        var cards = new List<CardInstanceState>();
        var corpDeck = new List<CardInstanceId>();
        var runnerStack = new List<CardInstanceId>();

        foreach (CardDefinitionId definitionId in corpRecipe.DefinitionIds)
        {
            CardInstanceId cardId = allocator.NextCardInstanceId();
            corpDeck.Add(cardId);
            cards.Add(new CardInstanceState(cardId, definitionId, corpDeckZone.Id, PlayerSide.Corp));
        }

        foreach (CardDefinitionId definitionId in runnerRecipe.DefinitionIds)
        {
            CardInstanceId cardId = allocator.NextCardInstanceId();
            runnerStack.Add(cardId);
            cards.Add(new CardInstanceState(cardId, definitionId, runnerStackZone.Id, PlayerSide.Runner));
        }

        var random = DeterministicRandom.Restore(baseState.RandomState);
        random.Shuffle(corpDeck);
        random.Shuffle(runnerStack);

        var corpHand = new List<CardInstanceId>();
        var runnerGrip = new List<CardInstanceId>();
        DealOpeningHand(corpDeck, corpHand, cards, corpHandZone.Id, ToyRules.OpeningHandSize);
        DealOpeningHand(runnerStack, runnerGrip, cards, runnerGripZone.Id, ToyRules.OpeningHandSize);

        var cardsById = cards.ToDictionary(card => card.Id);
        var cardsByZone = zones.ToDictionary(zone => zone.Id, zone => zone.CardIds.ToList());
        cardsByZone[corpDeckZone.Id] = corpDeck;
        cardsByZone[corpHandZone.Id] = corpHand;
        cardsByZone[runnerStackZone.Id] = runnerStack;
        cardsByZone[runnerGripZone.Id] = runnerGrip;

        var finalZones = zones.Select(zone => new ZoneState(
            zone.Id,
            zone.Kind,
            zone.OwnerSide,
            zone.ServerId,
            cardsByZone[zone.Id])).ToArray();
        var expanded = new GameState(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            baseState.Players,
            cardsById.Values.ToArray(),
            finalZones,
            servers,
            baseState.Turn,
            baseState.Outcome,
            random.State,
            allocator.State,
            baseState.DecisionRevision,
            baseState.EventSequence,
            [],
            null,
            baseState.ActionSequence,
            baseState.InitialSeed,
            baseState.RunState,
            baseState.RulesState,
            baseState.BehaviorRegistryVersion,
            baseState.BehaviorCatalogFingerprint,
            baseState.RuntimeCardCatalogVersion,
            baseState.RuntimeCardCatalogFingerprint);
        StateInvariantValidator.Validate(expanded, null, runtimeCardCatalog);

        return GameEngine.BeginResolution(
            expanded,
            new ToyTurnFrame(PlayerSide.Corp, ToyTurnStage.CorpTurnStartDraw));
    }

    private static void ValidateRecipe(ToyDeckRecipe recipe, PlayerSide expectedSide, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.Side != expectedSide)
        {
            throw new ArgumentException(
                $"Recipe '{recipe.RecipeId}' is for '{recipe.Side}', but '{expectedSide}' is required.",
                parameterName);
        }

        if (recipe.DefinitionIds.Count == 0)
        {
            throw new ArgumentException($"Recipe '{recipe.RecipeId}' must contain at least one card.", parameterName);
        }

        foreach (CardDefinitionId definitionId in recipe.DefinitionIds)
        {
            ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(definitionId);
            if (behavior.OwnerSide != expectedSide)
            {
                throw new ArgumentException(
                    $"Card definition '{definitionId}' belongs to '{behavior.OwnerSide}', not '{expectedSide}'.",
                    parameterName);
            }
        }
    }

    private static void DealOpeningHand(
        List<CardInstanceId> deck,
        List<CardInstanceId> hand,
        List<CardInstanceState> cards,
        ZoneId handZoneId,
        int count)
    {
        int dealCount = Math.Min(count, deck.Count);
        for (int index = 0; index < dealCount; index++)
        {
            CardInstanceId cardId = deck[0];
            deck.RemoveAt(0);
            hand.Add(cardId);
            int cardIndex = cards.FindIndex(card => card.Id == cardId);
            CardInstanceState card = cards[cardIndex];
            cards[cardIndex] = new CardInstanceState(
                card.Id,
                card.DefinitionId,
                handZoneId,
                card.OwnerSide,
                card.FaceUp,
                card.Rezzed,
                card.AdvancementCounters);
        }
    }
}
