using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch2BehaviorSpecTests
{
    [Fact]
    public void SpinDoctorIsComposedWithItsTypedAssetAndStableGenericDeclaration()
    {
        BehaviorCatalog first = GatewayBehaviorCatalog.Create();
        BehaviorCatalog second = GatewayBehaviorCatalog.Create();

        Assert.Equal(
            new CardDefinitionId[]
            {
                GatewayBatch2CardCodes.Fermenter,
                GatewayBatch2CardCodes.Cookbook,
                GatewayBatch2CardCodes.Pennyshaver,
                GatewayBatch2CardCodes.Carmen,
                GatewayBatch2CardCodes.Marjanah,
                GatewayBatch2CardCodes.RedTeam,
                GatewayBatch2CardCodes.Pantograph,
                GatewayBatch2CardCodes.Conduit,
                GatewayBatch2CardCodes.TeleworkContract,
                GatewayBatch2CardCodes.SmartwareDistributor,
                GatewayCardCodes.NicoCampaign,
                GatewayBatch2CardCodes.SpinDoctor,
                GatewayBatch2CardCodes.RegolithMiningLicense,
            },
            GatewayBatch2BehaviorSpecs.AssignedDefinitionIds);
        Assert.Empty(GatewayBatch2BehaviorSpecs.BlockedDefinitionIds);
        Assert.Equal(
            new CardDefinitionId[]
            {
                GatewayBatch2CardCodes.Fermenter,
                GatewayBatch2CardCodes.Cookbook,
                GatewayBatch2CardCodes.Pennyshaver,
                GatewayBatch2CardCodes.Carmen,
                GatewayBatch2CardCodes.Marjanah,
                GatewayBatch2CardCodes.RedTeam,
                GatewayBatch2CardCodes.Pantograph,
                GatewayBatch2CardCodes.Conduit,
                GatewayBatch2CardCodes.TeleworkContract,
                GatewayBatch2CardCodes.SmartwareDistributor,
                GatewayCardCodes.NicoCampaign,
                GatewayBatch2CardCodes.SpinDoctor,
                GatewayBatch2CardCodes.RegolithMiningLicense,
            },
            GatewayBatch2BehaviorSpecs.GenericSpecs.Select(static spec => spec.DefinitionId));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Fingerprint, first.BehaviorRegistryFingerprint);
        Assert.Equal(first.GetDefinitionFingerprint(GatewayBatch2CardCodes.SpinDoctor),
            second.GetDefinitionFingerprint(GatewayBatch2CardCodes.SpinDoctor));
        Assert.Contains(GatewayBatch2CardCodes.SpinDoctor, first.AllDefinitionIds);
        Assert.True(first.TryGet(GatewayBatch2CardCodes.SpinDoctor, out CardBehaviorDefinition? definition));
        Assert.Equal(PlayerSide.Corp, definition.Controller);

        AssetBehaviorSpec asset = GatewayAssetBehaviorSpecs.AssetSpecs.Single(
            static spec => spec.DefinitionId == GatewayBatch2CardCodes.SpinDoctor);
        Assert.Equal(2, asset.TrashCost);

        BehaviorTrigger rezTrigger = Assert.Single(definition.Triggers);
        Assert.Equal("spin-doctor-rez", rezTrigger.Key);
        Assert.Equal(OccurrenceKind.CardRezzed, rezTrigger.Occurrence);
        Assert.Equal(TriggerMode.Mandatory, rezTrigger.Mode);
        Assert.Equal(BehaviorTriggerScope.Self, rezTrigger.Scope);
        BehaviorInstruction draw = Assert.Single(rezTrigger.Instructions);
        Assert.Equal(BehaviorInstructionKind.Draw, draw.Kind);
        Assert.Equal(2, draw.Amount);

        BehaviorPaidAbility ability = Assert.Single(definition.PaidAbilities);
        Assert.Equal("spin-doctor-archives", ability.Key);
        Assert.Equal(BehaviorAbilityTiming.AnyPaidWindow, ability.Timing);
        Assert.Equal(BehaviorAbilityActivation.PaidAbility, ability.Activation);
        Assert.Equal(BehaviorAbilityUsageLimit.Unlimited, ability.UsageLimit);
        BehaviorCost remove = Assert.Single(ability.Costs);
        Assert.Equal(BehaviorCostKind.RemoveSelfFromGame, remove.Kind);
        Assert.Equal(1, remove.Amount);
        Assert.Empty(ability.Conditions);
        Assert.Empty(ability.Targets);
        Assert.Equal(
            [BehaviorInstructionKind.Choose, BehaviorInstructionKind.ShuffleIntoDeck],
            ability.Instructions.Select(static instruction => instruction.Kind));

        BehaviorChoiceSpec choice = Assert.IsType<BehaviorChoiceSpec>(ability.Instructions[0].Choice);
        Assert.Equal("spin-doctor-archives", choice.Key);
        Assert.Equal(BehaviorChooser.Controller, choice.Chooser);
        Assert.Equal(BehaviorChoiceMode.TargetSelection, choice.Mode);
        Assert.Equal(BehaviorChoiceCardinality.UpTo(2), choice.Cardinality);
        Assert.Empty(choice.Options);
        BehaviorTarget archives = Assert.IsType<BehaviorTarget>(choice.Target);
        Assert.Equal(BehaviorTargetKind.ZoneCards, archives.Kind);
        Assert.Equal(BehaviorZone.Archives, archives.Zone);
        Assert.Equal(BehaviorCardType.Any, archives.Filter?.CardType);
        Assert.Equal(PlayerSide.Corp, archives.Filter?.OwnerSide);
        Assert.False(archives.Reveal);

        BehaviorInstruction shuffle = ability.Instructions[1];
        Assert.Equal(BehaviorTargetKind.ChosenCard, shuffle.Target?.Kind);
        Assert.Equal("spin-doctor-archives", shuffle.Target?.Selector);
        Assert.Equal(BehaviorZone.Archives, shuffle.SourceZone);
        Assert.Equal(BehaviorZone.Deck, shuffle.DestinationZone);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SyntheticUseAbilityRemovesSourceRoundTripsChoiceAndShufflesExactArchivesSelection(int count)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        CardInstanceId sourceId = new("card:spin-doctor-source");
        CardInstanceId[] archives =
        [
            new("card:spin-doctor-archives-0"),
            new("card:spin-doctor-archives-1"),
            new("card:spin-doctor-archives-2"),
        ];
        GameState state = BuildSyntheticState(catalog, sourceId, archives);
        TurnFrame parent = new(
            1,
            ResolutionBehaviorIds.Turn,
            PlayerSide.Corp,
            state.Turn.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnPriorityWindowKind.PaidRezAndScore,
            PlayerSide.Corp);

        GameState prompt = GameEngine.BeginResolution(state, parent, catalog);
        PendingDecision useDecision = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        LegalAction use = Assert.Single(
            useDecision.LegalActions,
            action => action.Kind == ActionKind.UseAbility
                && Assert.IsType<AbilityActionParameters>(action.Parameters).SourceCardId == sourceId);

        ApplyResult opened = GameEngine.Apply(
            prompt,
            new ActionSubmission(useDecision.Owner, useDecision.Id, useDecision.Revision, use.Id),
            catalog);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(
            ZoneKind.CorpRemovedFromGame,
            opened.State.GetZone(opened.State.GetCard(sourceId).ZoneId).Kind);
        Assert.True(opened.State.GetCard(sourceId).FaceUp);
        Assert.Equal(PlayerSide.Corp, opened.State.GetPlayer(opened.State.PendingDecision!.Owner).Side);

        BehaviorResolutionFrame behavior = Assert.IsType<BehaviorResolutionFrame>(opened.State.ResolutionStack[^1]);
        BehaviorChoiceMaterialization materialization = Assert.IsType<BehaviorChoiceMaterialization>(behavior.Choice);
        Assert.Equal(archives, materialization.CandidateCardIds);
        Observation corpObservation = ObservationBuilder.Build(opened.State, PlayerSide.Corp, catalog);
        Observation runnerObservation = ObservationBuilder.Build(opened.State, PlayerSide.Runner, catalog);
        Assert.Equal(opened.State.PendingDecision.LegalActions, corpObservation.LegalActions);
        Assert.Empty(runnerObservation.LegalActions);
        Assert.Contains(sourceId.Value, EngineJson.Serialize(corpObservation), StringComparison.Ordinal);
        Assert.Contains(sourceId.Value, EngineJson.Serialize(runnerObservation), StringComparison.Ordinal);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        PendingDecision restoredDecision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction select = restoredDecision.LegalActions
            .Where(action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.FinalizeSelection
                && parameters.CardIds.Count == count)
            .First();
        CardInstanceId[] selected = Assert.IsType<BehaviorChoiceParameters>(select.Parameters).CardIds.ToArray();

        ApplyResult completed = GameEngine.Apply(
            restored,
            new ActionSubmission(restoredDecision.Owner, restoredDecision.Id, restoredDecision.Revision, select.Id),
            catalog);
        Assert.True(completed.Accepted, completed.Rejection?.Message);
        Assert.Equal(
            ZoneKind.CorpRemovedFromGame,
            completed.State.GetZone(completed.State.GetCard(sourceId).ZoneId).Kind);
        Assert.All(selected, cardId =>
            Assert.Contains(cardId, completed.State.GetZone(ZoneKind.CorpDeck).CardIds));
        Assert.DoesNotContain(selected, cardId => completed.State.GetZone(ZoneKind.CorpDiscard).CardIds.Contains(cardId));
        Assert.Single(
            completed.Events,
            @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled
                && Assert.IsType<BehaviorCardsShuffledEventPayload>(@event.Payload).CardCount == count);
        foreach (CardInstanceId cardId in archives.Append(sourceId))
        {
            Assert.Equal(1, completed.State.Zones.Count(zone => zone.CardIds.Contains(cardId)));
        }
    }

    private static GameState BuildSyntheticState(
        BehaviorCatalog catalog,
        CardInstanceId sourceId,
        IReadOnlyList<CardInstanceId> archives)
    {
        GameState baseline = GameState.CreateNew(
            seed: 44103,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        ZoneState sourceZone = baseline.GetZone(
            baseline.Servers.Single(server => server.Kind == ServerKind.RAndD).RootZoneId);
        ZoneState discard = baseline.GetZone(ZoneKind.CorpDiscard);
        ZoneState deck = baseline.GetZone(ZoneKind.CorpDeck);
        CardInstanceState[] cards =
        [
            new(sourceId, GatewayBatch2CardCodes.SpinDoctor, sourceZone.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            .. archives.Select(cardId => new CardInstanceState(
                cardId,
                GatewayBatch1CardCodes.HedgeFund,
                discard.Id,
                PlayerSide.Corp,
                faceUp: true)),
            new(
                new CardInstanceId("card:spin-doctor-deck-0"),
                GatewayBatch1CardCodes.HedgeFund,
                deck.Id,
                PlayerSide.Corp,
                faceUp: false),
        ];
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == sourceZone.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [sourceId])
                : zone.Id == discard.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, archives)
                    : zone.Id == deck.Id
                        ? new ZoneState(
                            zone.Id,
                            zone.Kind,
                            zone.OwnerSide,
                            zone.ServerId,
                            [new CardInstanceId("card:spin-doctor-deck-0")])
                        : zone)
            .ToArray();
        TurnFactsState facts = new(
            1,
            PlayerSide.Corp,
            1,
            3,
            0,
            turnBegan: true,
            mandatoryDrawPerformed: true,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        TurnState turn = new(
            PlayerSide.Corp,
            TurnPhase.Corp,
            1,
            TurnStep.CorpAction,
            new TurnHistoryState(1, facts, null, null, null));
        RulesState defaults = RulesState.CreateDefault(baseline.Players);
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers,
            turn,
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            rulesState: defaults,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
    }
}
