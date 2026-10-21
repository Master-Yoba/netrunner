using System.Text.Json.Serialization;

namespace Netrunner.Core;

/// <summary>CR 1.6.6a: Corp chooses first, one redraw each, second hand mandatory.</summary>
public sealed record SetupFrame : ResolutionFrame
{
    public const string Behavior = "rules.setup-26.03";

    [JsonConstructor]
    public SetupFrame(int version, string behaviorId, PlayerSide side, bool redrawn)
        : base(version, behaviorId)
    {
        Side = side;
        Redrawn = redrawn;
    }

    public SetupFrame(PlayerSide side, bool redrawn = false) : this(1, Behavior, side, redrawn) { }

    [JsonRequired]
    [JsonInclude]
    public PlayerSide Side { get; private set; }

    [JsonRequired]
    [JsonInclude]
    public bool Redrawn { get; private set; }
}

public static partial class GameEngine
{
    /// <summary>
    /// Creates physical cards and the first setup decision from composition-validated
    /// decks. Core enforces executable side/class/data contracts; Cards owns deckbuilding.
    /// </summary>
    public static TurnStartResult CreateMatch(ulong seed, DeckRequest corpDeck, DeckRequest runnerDeck,
        string cardDataVersion, IBehaviorCatalog behaviorCatalog, IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(corpDeck);
        ArgumentNullException.ThrowIfNull(runnerDeck);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        ArgumentNullException.ThrowIfNull(runtimeCardCatalog);
        if (corpDeck.Side != PlayerSide.Corp || runnerDeck.Side != PlayerSide.Runner)
        {
            throw new ArgumentException("Setup requires one Corp deck and one Runner deck.");
        }

        GameState initial = GameState.CreateNew(seed, cardDataVersion: cardDataVersion,
            behaviorRegistryVersion: behaviorCatalog.Version, behaviorCatalogFingerprint: behaviorCatalog.Fingerprint,
            runtimeCardCatalogVersion: runtimeCardCatalog.Version, runtimeCardCatalogFingerprint: runtimeCardCatalog.Fingerprint);
        WorkingState working = WorkingState.From(initial, behaviorCatalog, runtimeCardCatalog);
        foreach (DeckRequest deck in new[] { corpDeck, runnerDeck })
        {
            if (deck.SchemaVersion != DeckRequest.CurrentSchemaVersion
                || deck.Cards.Any(entry => entry is null || entry.Quantity <= 0 || entry.Quantity > 1000)
                || deck.Cards.Sum(entry => (long)entry.Quantity) is < 5 or > 1000
                || deck.Cards.Select(entry => entry.Code).Distinct(StringComparer.Ordinal).Count() != deck.Cards.Count
                || deck.CardDataVersion is not null && deck.CardDataVersion != cardDataVersion)
            {
                throw new ArgumentException("Setup requires data-pinned decks of 5–1000 cards with unique codes and positive quantities.");
            }

            CardDefinitionId identity = new(deck.IdentityCode);
            ValidateDefinition(identity, deck.Side, identityCard: true);
            ZoneId identityZone = working.Allocator.NextZoneId();
            CardInstanceId identityId = working.Allocator.NextCardInstanceId();
            working.Zones.Add(new ZoneState(identityZone,
                deck.Side == PlayerSide.Corp ? ZoneKind.CorpIdentity : ZoneKind.RunnerIdentity,
                deck.Side, cardIds: [identityId]));
            working.Cards.Add(new CardInstanceState(identityId, identity, identityZone, deck.Side, faceUp: true));
            ZoneState drawZone = GetZone(working, deck.Side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack);
            var ids = new List<CardInstanceId>();
            foreach (DeckCardEntry entry in deck.Cards.OrderBy(entry => entry.Code, StringComparer.Ordinal))
            {
                CardDefinitionId definition = new(entry.Code);
                ValidateDefinition(definition, deck.Side, identityCard: false);
                for (int copy = 0; copy < entry.Quantity; copy++)
                {
                    CardInstanceId id = working.Allocator.NextCardInstanceId();
                    ids.Add(id);
                    working.Cards.Add(new CardInstanceState(id, definition, drawZone.Id, deck.Side));
                }
            }

            ReplaceZone(working, new ZoneState(drawZone.Id, drawZone.Kind, deck.Side, cardIds: ids));
            ShuffleSetupDeck(working, deck.Side);
            PlayerState player = GetPlayer(working, deck.Side);
            ReplacePlayer(working, new PlayerState(player.Id, player.Side, credits: 5, clicks: 0));
            DealOpeningHand(working, deck.Side);
        }

        working.Turn = new TurnState(PlayerSide.Corp, TurnPhase.Setup);
        working.RulesState = RulesState.CreateDefault(working.Players);
        working.ResolutionStack.Add(new SetupFrame(PlayerSide.Corp));
        Advance(working, DefaultResolutionStepBudget);
        GameState state = working.Build(initial);
        StateInvariantValidator.Validate(state, behaviorCatalog, runtimeCardCatalog);
        return new TurnStartResult(state, FreezeEvents(working.Events));

        void ValidateDefinition(CardDefinitionId id, PlayerSide side, bool identityCard)
        {
            if (!runtimeCardCatalog.TryGet(id, out CardRuntimeSpec? spec) || spec.Side != side
                || (spec.CardClass == RuntimeCardClass.Identity) != identityCard
                || !behaviorCatalog.AllDefinitionIds.Contains(id))
            {
                throw new ArgumentException($"Setup card '{id}' requires matching runtime side/class and registered behavior.");
            }
        }
    }

    private static void ShuffleSetupDeck(WorkingState working, PlayerSide side)
    {
        ZoneState deck = GetZone(working, side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack);
        var ids = deck.CardIds.ToList();
        var random = DeterministicRandom.Restore(working.RandomState);
        random.Shuffle(ids);
        working.RandomState = random.State;
        ReplaceZone(working, new ZoneState(deck.Id, deck.Kind, side, cardIds: ids));
    }

    private static void DealOpeningHand(WorkingState working, PlayerSide side)
    {
        for (int i = 0; i < 5; i++)
        {
            if (!TryDrawCard(working, side))
            {
                throw new ResolutionFailureException("An opening hand requires five cards.");
            }
        }
    }

    private static void StepSetup(WorkingState working, SetupFrame frame)
    {
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        var actions = new List<LegalAction>
        {
            new(working.Allocator.NextActionId(), ActionKind.ChooseNumber, "Keep hand", new NumberChoiceParameters(0)),
        };
        if (!frame.Redrawn)
        {
            actions.Add(new LegalAction(working.Allocator.NextActionId(), ActionKind.ChooseNumber, "Mulligan", new NumberChoiceParameters(1)));
        }

        working.PendingDecision = new PendingDecision(working.Allocator.NextDecisionId(), working.DecisionRevision,
            GetPlayer(working, frame.Side).Id, DecisionKind.Setup,
            frame.Redrawn ? "Keep your second hand" : "Keep your hand or take one mulligan", actions);
    }

    private static void ConsumeSetupChoice(WorkingState working, SetupFrame frame, int choice)
    {
        if (choice < 0 || choice > (frame.Redrawn ? 0 : 1))
        {
            throw new ResolutionFailureException("This setup choice is not available.");
        }

        if (choice == 1)
        {
            ZoneKind handKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
            ZoneKind deckKind = frame.Side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack;
            foreach (CardInstanceId id in GetZone(working, handKind).CardIds.ToArray())
            {
                MoveCard(working, GetCard(working, id), GetZone(working, handKind), GetZone(working, deckKind));
            }

            ShuffleSetupDeck(working, frame.Side);
            DealOpeningHand(working, frame.Side);
            working.ResolutionStack[^1] = new SetupFrame(frame.Side, redrawn: true);
        }
        else
        {
            working.ResolutionStack[^1] = frame.Side == PlayerSide.Corp
                ? new SetupFrame(PlayerSide.Runner) : new TurnFrame(PlayerSide.Corp, 1);
        }
    }
}
