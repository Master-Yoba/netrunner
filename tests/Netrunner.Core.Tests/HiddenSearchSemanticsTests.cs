using Xunit;

namespace Netrunner.Core.Tests;

public sealed class HiddenSearchSemanticsTests
{
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public void PrivateChoiceDefinitionsExposeOnlyEligibleCardsWithoutDeckOrder(PlayerSide side)
    {
        var source = new CardInstanceId("card:search-source");
        var sourceDefinition = new CardDefinitionId("test:search-source");
        var candidate = new CardDefinitionId("test:search-candidate");
        var excluded = new CardDefinitionId("test:search-excluded");
        BehaviorZone deck = side == PlayerSide.Corp ? BehaviorZone.Deck : BehaviorZone.Stack;
        ZoneKind deckZone = side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack;
        ZoneKind handZone = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        var filter = new BehaviorCardFilter(definitionId: candidate, ownerSide: side);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:choice-projection")
            .Add(new CardBehaviorDefinition(sourceDefinition, side, instructions:
                [new BehaviorInstruction(BehaviorInstructionKind.Search, argument: "search", sourceZone: deck,
                    filter: filter, target: BehaviorTarget.CardInZone(deck, filter))]))
            .Add(new CardBehaviorDefinition(candidate, side))
            .Add(new CardBehaviorDefinition(excluded, side)).Build();
        var a = new CardInstanceId("card:eligible-a");
        var b = new CardInstanceId("card:eligible-b");
        var hidden = new CardInstanceId("card:ineligible");
        GameState First(bool reverse) => CreateState(catalog,
            (source, sourceDefinition, handZone, side),
            (reverse ? b : a, candidate, deckZone, side),
            (hidden, excluded, deckZone, side),
            (reverse ? a : b, candidate, deckZone, side));
        GameState Begin(GameState state) => GameEngine.BeginBehaviorForTesting(state,
            new BehaviorInvocationContext(source, state.Players.Single(player => player.Side == side).Id), catalog);
        GameState first = Begin(First(false));
        GameState second = Begin(First(true));
        string privateCards = EngineJson.Serialize(ObservationBuilder.Build(first, side).PrivateState["choiceCards"]);
        Assert.Equal(privateCards, EngineJson.Serialize(ObservationBuilder.Build(second, side).PrivateState["choiceCards"]));
        Assert.Contains(candidate.Value, privateCards, StringComparison.Ordinal);
        Assert.DoesNotContain(excluded.Value, privateCards, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden.Value, privateCards, StringComparison.Ordinal);
        Assert.False(ObservationBuilder.Build(first, side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp)
            .PrivateState.ContainsKey("choiceCards"));
        Assert.False(ObservationBuilder.Build(First(false), side).PrivateState.ContainsKey("choiceCards"));
        LegalAction decline = first.PendingDecision!.LegalActions.Single(action => IsCardSelection(action, "search", []));
        ApplyResult declined = Apply(first, catalog, decline);
        Assert.True(declined.Accepted);
        Assert.False(ObservationBuilder.Build(declined.State, side).PrivateState.ContainsKey("choiceCards"));
    }

    [Fact]
    public void FoundCardConditionIsParameterlessStrictSerializableAndFingerprinted()
    {
        BehaviorCondition condition = BehaviorCondition.FoundCard();

        Assert.Equal(BehaviorConditionKind.FoundCard, condition.Kind);
        Assert.Null(condition.Fact);
        Assert.Null(condition.Value);
        Assert.Null(condition.SubjectSide);
        Assert.Throws<ArgumentException>(() => new BehaviorCondition(
            BehaviorConditionKind.FoundCard,
            fact: "unexpected"));
        Assert.Throws<ArgumentException>(() => new BehaviorCondition(
            BehaviorConditionKind.FoundCard,
            value: 1));
        Assert.Throws<ArgumentException>(() => new BehaviorCondition(
            BehaviorConditionKind.FoundCard,
            subjectSide: PlayerSide.Runner));

        string json = EngineJson.Serialize(condition);
        Assert.Contains("\"kind\":\"foundCard\"", json, StringComparison.Ordinal);
        Assert.Equal(condition, EngineJson.Deserialize<BehaviorCondition>(json));

        CardDefinitionId definitionId = new("test:found-card-contract");
        BehaviorCatalog withoutCondition = new BehaviorCatalogBuilder("test:found-card-contract")
            .Add(new CardBehaviorDefinition(definitionId, PlayerSide.Runner))
            .Build();
        BehaviorCatalog withCondition = new BehaviorCatalogBuilder("test:found-card-contract")
            .Add(new CardBehaviorDefinition(
                definitionId,
                PlayerSide.Runner,
                conditions: [condition]))
            .Build();

        Assert.NotEqual(withoutCondition.Fingerprint, withCondition.Fingerprint);
        Assert.Equal(
            withCondition.Fingerprint,
            new BehaviorCatalogBuilder("test:found-card-contract")
                .Add(new CardBehaviorDefinition(
                    definitionId,
                    PlayerSide.Runner,
                    conditions: [BehaviorCondition.FoundCard()]))
                .Build()
                .Fingerprint);
    }

    [Fact]
    public void DeckAndStackSearchesMustRevealWithAnExplicitInstruction()
    {
        BehaviorCardFilter filter = new(BehaviorCardType.Icebreaker);

        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            sourceZone: BehaviorZone.Stack,
            filter: filter,
            target: BehaviorTarget.CardInZone(BehaviorZone.Stack, filter, reveal: true)));
        Assert.Throws<ArgumentException>(() => new BehaviorInstruction(
            BehaviorInstructionKind.Search,
            sourceZone: BehaviorZone.Deck,
            filter: filter,
            target: BehaviorTarget.CardInZone(BehaviorZone.Deck, filter, reveal: true)));

        BehaviorInstruction headquartersSearch = new(
            BehaviorInstructionKind.Search,
            sourceZone: BehaviorZone.Headquarters,
            filter: filter,
            target: BehaviorTarget.CardInZone(BehaviorZone.Headquarters, filter, reveal: true));
        Assert.True(headquartersSearch.Target!.Reveal);
    }

    [Fact]
    public void ConstrainedSearchIsPrivateMandatoryUpToAndRoundTrips()
    {
        CardDefinitionId sourceDefinition = new("test:hidden-search-constrained-source");
        CardDefinitionId candidateDefinition = new("test:hidden-search-constrained-candidate");
        CardInstanceId sourceCard = new("card:hidden-search-constrained-source");
        CardInstanceId candidateCard = new("card:hidden-search-constrained-candidate");
        BehaviorCardFilter filter = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        BehaviorTarget target = BehaviorTarget.CardInZone(BehaviorZone.Stack, filter);
        BehaviorCatalog catalog = BuildCatalog(
            sourceDefinition,
            candidateDefinition,
            [new BehaviorInstruction(
                BehaviorInstructionKind.Search,
                argument: "hidden-search",
                sourceZone: BehaviorZone.Stack,
                filter: filter,
                target: target)]);
        GameState state = CreateState(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceCard, runner),
            catalog);
        PendingDecision pending = Assert.IsType<PendingDecision>(prompt.PendingDecision);
        BehaviorResolutionFrame frame = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        BehaviorChoiceMaterialization choice = Assert.IsType<BehaviorChoiceMaterialization>(frame.Choice);

        Assert.Equal(runner, pending.Owner);
        Assert.Equal(BehaviorChoiceMode.Mandatory, choice.Mode);
        Assert.Equal(BehaviorChoiceCardinalityKind.UpTo, choice.CardinalityKind);
        Assert.Equal(1, choice.CardinalityCount);
        Assert.Equal([candidateCard], choice.CandidateCardIds);
        Assert.Contains(
            pending.LegalActions,
            action => IsCardSelection(action, choice.Key, []));
        Assert.Contains(
            pending.LegalActions,
            action => IsCardSelection(action, choice.Key, [candidateCard]));

        Observation corpObservation = ObservationBuilder.Build(prompt, PlayerSide.Corp);
        Assert.Null(corpObservation.Decision);
        Assert.DoesNotContain(candidateCard.Value, EngineJson.Serialize(corpObservation), StringComparison.Ordinal);

        string json = EngineJson.Serialize(prompt);
        GameState restored = EngineJson.DeserializeState(json, catalog);
        Assert.Equal(DeterministicHash.ForState(prompt), DeterministicHash.ForState(restored));
        Assert.Empty(StateInvariantValidator.Collect(restored, catalog));
        BehaviorChoiceMaterialization restoredChoice =
            Assert.IsType<BehaviorResolutionFrame>(restored.ResolutionStack.Single()).Choice!;
        Assert.Equal(choice.Mode, restoredChoice.Mode);
        Assert.Equal(choice.CardinalityKind, restoredChoice.CardinalityKind);
        Assert.Equal(choice.CardinalityCount, restoredChoice.CardinalityCount);
        Assert.Equal(choice.CandidateCardIds, restoredChoice.CandidateCardIds);
    }

    [Fact]
    public void ExplicitFailToFindShufflesZeroKeepsCardHiddenAndTakesFalseArm()
    {
        HiddenSearchFixture fixture = CreateBranchFixture("explicit-fail", includeCandidate: true);
        GameState prompt = Begin(fixture);
        LegalAction fail = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => IsCardSelection(action, "hidden-search", []));

        ApplyResult result = Apply(prompt, fixture.Catalog, fail);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        BehaviorCardsShuffledEventPayload shuffle = Assert.Single(
            result.Events,
            @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled).Payload
            as BehaviorCardsShuffledEventPayload
            ?? throw new Xunit.Sdk.XunitException("Expected a typed shuffle event.");
        Assert.Equal(0, shuffle.CardCount);
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.BehaviorCardRevealed);
        Assert.Equal(8, result.State.GetPlayer(fixture.Runner).Credits);
        Assert.Contains(fixture.CandidateCard, result.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.False(result.State.GetCard(fixture.CandidateCard).FaceUp);
        Assert.Empty(StateInvariantValidator.Collect(result.State, fixture.Catalog));
    }

    [Fact]
    public void EmptySearchAutoFinalizesAndShufflesZeroWithoutPrompting()
    {
        HiddenSearchFixture fixture = CreateBranchFixture("auto-empty", includeCandidate: false);
        HiddenSearchFixture gatedFixture = BeginWithGate(fixture);
        GameState prompt = Begin(gatedFixture);
        LegalAction gate = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["go"]));

        ApplyResult result = Apply(prompt, gatedFixture.Catalog, gate);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        BehaviorCardsShuffledEventPayload shuffle = Assert.Single(
            result.Events,
            @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled).Payload
            as BehaviorCardsShuffledEventPayload
            ?? throw new Xunit.Sdk.XunitException("Expected a typed shuffle event.");
        Assert.Equal(0, shuffle.CardCount);
        Assert.DoesNotContain(result.Events, @event => @event.Kind == EngineEventKinds.BehaviorCardRevealed);
        Assert.Equal(8, result.State.GetPlayer(fixture.Runner).Credits);
        Assert.Empty(StateInvariantValidator.Collect(result.State, gatedFixture.Catalog));
    }

    [Fact]
    public void FoundSearchShufflesBeforeRevealThenMovesCardAndTakesTrueArm()
    {
        CardDefinitionId sourceDefinition = new("test:hidden-search-found-source");
        CardDefinitionId candidateDefinition = new("test:hidden-search-found-candidate");
        CardInstanceId sourceCard = new("card:hidden-search-found-source");
        CardInstanceId candidateCard = new("card:hidden-search-found-candidate");
        BehaviorCardFilter filter = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        BehaviorTarget target = BehaviorTarget.CardInZone(BehaviorZone.Stack, filter);
        BehaviorBranchSpec branch = new(
            BehaviorCondition.FoundCard(),
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.AddToHand,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Grip),
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 7),
            ],
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)]);
        BehaviorCatalog catalog = BuildCatalog(
            sourceDefinition,
            candidateDefinition,
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Search,
                    argument: "hidden-search",
                    sourceZone: BehaviorZone.Stack,
                    filter: filter,
                    target: target),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(
                    BehaviorInstructionKind.Reveal,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
                new BehaviorInstruction(BehaviorInstructionKind.Branch, branch: branch),
            ]);
        GameState state = CreateState(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceCard, runner),
            catalog);
        LegalAction found = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => IsCardSelection(action, "hidden-search", [candidateCard]));

        ApplyResult result = Apply(prompt, catalog, found);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        int shuffleIndex = result.Events.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled);
        int revealIndex = result.Events.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.BehaviorCardRevealed);
        int moveIndex = result.Events.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.CardMoved
                && @event.Payload is CardMovedEventPayload payload
                && payload.CardId == candidateCard);
        Assert.True(shuffleIndex >= 0);
        Assert.True(revealIndex > shuffleIndex);
        Assert.True(moveIndex > revealIndex);
        Assert.Equal(12, result.State.GetPlayer(runner).Credits);
        Assert.Contains(candidateCard, result.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(candidateCard, result.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Empty(StateInvariantValidator.Collect(result.State, catalog));
    }

    [Fact]
    public void OrdinaryCardChoiceDoesNotSatisfyFoundCardAndTakesFalseArm()
    {
        CardDefinitionId sourceDefinition = new("test:hidden-search-ordinary-choice-source");
        CardDefinitionId candidateDefinition = new("test:hidden-search-ordinary-choice-candidate");
        CardInstanceId sourceCard = new("card:hidden-search-ordinary-choice-source");
        CardInstanceId candidateCard = new("card:hidden-search-ordinary-choice-candidate");
        BehaviorChoiceSpec choice = new(
            "ordinary-choice",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.Exactly(1),
            target: BehaviorTarget.CardInZone(BehaviorZone.Stack, BehaviorCardFilter.Any));
        BehaviorBranchSpec branch = new(
            BehaviorCondition.FoundCard(),
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 7)],
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.AddToHand,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Grip),
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3),
            ]);
        BehaviorCatalog catalog = BuildCatalog(
            sourceDefinition,
            candidateDefinition,
            [
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                new BehaviorInstruction(BehaviorInstructionKind.Branch, branch: branch),
            ]);
        GameState state = CreateState(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceCard, runner),
            catalog);
        LegalAction selected = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => IsCardSelection(action, "ordinary-choice", [candidateCard]));

        ApplyResult result = Apply(prompt, catalog, selected);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Null(result.State.PendingDecision);
        Assert.Empty(result.State.ResolutionStack);
        Assert.Equal(8, result.State.GetPlayer(runner).Credits);
        Assert.Contains(candidateCard, result.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.DoesNotContain(candidateCard, result.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.Empty(StateInvariantValidator.Collect(result.State, catalog));
    }

    [Fact]
    public void ForgedConstrainedSearchCardinalityIsRejectedByInvariantAndApply()
    {
        HiddenSearchFixture fixture = CreateBranchFixture("forged-cardinality", includeCandidate: true);
        GameState prompt = Begin(fixture);
        BehaviorResolutionFrame frame = Assert.IsType<BehaviorResolutionFrame>(prompt.ResolutionStack.Single());
        BehaviorChoiceMaterialization choice = Assert.IsType<BehaviorChoiceMaterialization>(frame.Choice);
        BehaviorChoiceMaterialization forgedChoice = new(
            choice.Key,
            choice.Owner,
            choice.Mode,
            BehaviorChoiceCardinalityKind.Exactly,
            1,
            choice.OptionKeys,
            choice.CandidateCardIds,
            choice.SelectedCardIds);
        GameState forged = ReplaceResolutionStack(prompt, [frame.With(choice: forgedChoice)]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(forged, fixture.Catalog);
        Assert.Contains(violations, violation =>
            violation.Code == "resolution.behavior-search-cardinality-mismatch");
        string hash = DeterministicHash.ForState(forged);
        LegalAction legal = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => IsCardSelection(action, choice.Key, [fixture.CandidateCard]));
        ApplyResult rejected = Apply(
            forged,
            fixture.Catalog,
            legal);
        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Equal(hash, DeterministicHash.ForState(rejected.State));
        Assert.Empty(rejected.Events);
    }

    [Fact]
    public void EmptySearchClearsPriorSearchAndCanonicalChosenBindings()
    {
        CardDefinitionId sourceDefinition = new("test:hidden-search-clear-source");
        CardDefinitionId candidateDefinition = new("test:hidden-search-clear-candidate");
        CardInstanceId sourceCard = new("card:hidden-search-clear-source");
        CardInstanceId candidateCard = new("card:hidden-search-clear-candidate");
        BehaviorCardFilter constrainedFilter = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        BehaviorTarget anyTarget = BehaviorTarget.CardInZone(BehaviorZone.Stack, BehaviorCardFilter.Any);
        BehaviorTarget constrainedTarget = BehaviorTarget.CardInZone(BehaviorZone.Stack, constrainedFilter);
        BehaviorChoiceSpec gate = new(
            "pause",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("go"), new BehaviorChoiceOption("stop")]);
        BehaviorBranchSpec branch = new(
            BehaviorCondition.FoundCard(),
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 7)],
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.AddToHand,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Grip),
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3),
            ]);
        BehaviorCatalog catalog = BuildCatalog(
            sourceDefinition,
            candidateDefinition,
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.Search,
                    argument: "lookup",
                    sourceZone: BehaviorZone.Stack,
                    filter: BehaviorCardFilter.Any,
                    target: anyTarget),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(
                    BehaviorInstructionKind.Search,
                    argument: "lookup",
                    sourceZone: BehaviorZone.Stack,
                    filter: constrainedFilter,
                    target: constrainedTarget),
                new BehaviorInstruction(
                    BehaviorInstructionKind.ShuffleIntoDeck,
                    target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                    sourceZone: BehaviorZone.Stack,
                    destinationZone: BehaviorZone.Stack),
                new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: gate),
                new BehaviorInstruction(BehaviorInstructionKind.Branch, branch: branch),
            ]);
        GameState state = CreateState(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceCard, runner),
            catalog);
        LegalAction firstSelection = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => IsCardSelection(action, "lookup", [candidateCard]));

        ApplyResult afterFirst = Apply(prompt, catalog, firstSelection);

        Assert.True(afterFirst.Accepted, afterFirst.Rejection?.Message);
        PendingDecision secondPending = Assert.IsType<PendingDecision>(afterFirst.State.PendingDecision);
        LegalAction secondFail = Assert.Single(
            secondPending.LegalActions,
            action => IsCardSelection(action, "lookup", []));
        ApplyResult afterSecond = Apply(afterFirst.State, catalog, secondFail);

        Assert.True(afterSecond.Accepted, afterSecond.Rejection?.Message);
        Assert.Contains(
            afterSecond.Events,
            @event => @event.Kind == EngineEventKinds.BehaviorCardsShuffled
                && @event.Payload is BehaviorCardsShuffledEventPayload payload
                && payload.CardCount == 0);
        BehaviorResolutionFrame paused = Assert.IsType<BehaviorResolutionFrame>(afterSecond.State.ResolutionStack.Single());
        Assert.NotNull(afterSecond.State.PendingDecision);
        Assert.DoesNotContain(paused.Bindings, binding => binding.Key == "lookup");
        Assert.DoesNotContain(paused.Bindings, binding => binding.Key == "chosen");

        LegalAction pause = Assert.Single(
            afterSecond.State.PendingDecision!.LegalActions,
            action => action.Parameters is BehaviorChoiceParameters parameters
                && parameters.OptionKeys.SequenceEqual(["go"]));
        ApplyResult result = Apply(afterSecond.State, catalog, pause);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(8, result.State.GetPlayer(runner).Credits);
        Assert.Contains(candidateCard, result.State.GetZone(ZoneKind.RunnerStack).CardIds);
        Assert.DoesNotContain(candidateCard, result.State.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Empty(StateInvariantValidator.Collect(result.State, catalog));
    }

    [Fact]
    public void EmptyNonSearchChoiceDoesNotAuthorizeDeckShuffle()
    {
        CardDefinitionId sourceDefinition = new("test:hidden-search-non-search-empty-source");
        CardDefinitionId candidateDefinition = new("test:hidden-search-non-search-empty-candidate");
        CardInstanceId sourceCard = new("card:hidden-search-non-search-empty-source");
        CardInstanceId candidateCard = new("card:hidden-search-non-search-empty-candidate");
        BehaviorChoiceSpec choice = new(
            "empty-choice",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mandatory,
            BehaviorChoiceCardinality.UpTo(1),
            target: BehaviorTarget.CardInZone(
                BehaviorZone.Stack,
                BehaviorCardFilter.Any));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder("test:hidden-search-non-search-empty")
            .Add(new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: choice),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.ShuffleIntoDeck,
                        target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                        sourceZone: BehaviorZone.Stack,
                        destinationZone: BehaviorZone.Stack),
                ]))
            .Build();
        GameState state = CreateState(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        GameState prompt = GameEngine.BeginBehaviorForTesting(
            state,
            new BehaviorInvocationContext(sourceCard, runner),
            catalog);
        LegalAction empty = Assert.Single(
            prompt.PendingDecision!.LegalActions,
            action => IsCardSelection(action, "empty-choice", []));

        ApplyResult result = Apply(prompt, catalog, empty);

        Assert.False(result.Accepted);
        Assert.Equal(ApplyRejectionCode.ResolutionFailure, result.Rejection?.Code);
        Assert.Empty(result.Events);
        Assert.Same(prompt, result.State);
        Assert.Contains(candidateCard, result.State.GetZone(ZoneKind.RunnerStack).CardIds);
    }

    private static HiddenSearchFixture CreateBranchFixture(string suffix, bool includeCandidate)
    {
        CardDefinitionId sourceDefinition = new($"test:hidden-search-{suffix}-source");
        CardDefinitionId candidateDefinition = new($"test:hidden-search-{suffix}-candidate");
        CardInstanceId sourceCard = new($"card:hidden-search-{suffix}-source");
        CardInstanceId candidateCard = new($"card:hidden-search-{suffix}-candidate");
        BehaviorCardFilter filter = new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);
        BehaviorTarget target = BehaviorTarget.CardInZone(BehaviorZone.Stack, filter);
        BehaviorBranchSpec branch = new(
            BehaviorCondition.FoundCard(),
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 7)],
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)]);
        BehaviorInstruction[] instructions =
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Search,
                argument: "hidden-search",
                sourceZone: BehaviorZone.Stack,
                filter: filter,
                target: target),
            new BehaviorInstruction(
                BehaviorInstructionKind.ShuffleIntoDeck,
                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard),
                sourceZone: BehaviorZone.Stack,
                destinationZone: BehaviorZone.Stack),
            new BehaviorInstruction(
                BehaviorInstructionKind.Reveal,
                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
            new BehaviorInstruction(BehaviorInstructionKind.Branch, branch: branch),
        ];
        BehaviorCatalog catalog = BuildCatalog(sourceDefinition, candidateDefinition, instructions);
        GameState state = CreateState(
            catalog,
            (sourceCard, sourceDefinition, ZoneKind.RunnerGrip, PlayerSide.Runner),
            includeCandidate
                ? (candidateCard, candidateDefinition, ZoneKind.RunnerStack, PlayerSide.Runner)
                : (new CardInstanceId($"card:hidden-search-{suffix}-other"), new CardDefinitionId($"test:hidden-search-{suffix}-other"), ZoneKind.RunnerStack, PlayerSide.Runner));
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        return new HiddenSearchFixture(catalog, state, runner, candidateCard);
    }

    private static GameState Begin(HiddenSearchFixture fixture) =>
        GameEngine.BeginBehaviorForTesting(
            fixture.State,
            new BehaviorInvocationContext(
                fixture.State.Cards.Single(card => card.OwnerSide == PlayerSide.Runner && card.ZoneId == fixture.State.GetZone(ZoneKind.RunnerGrip).Id).Id,
                fixture.Runner),
            fixture.Catalog);

    private static HiddenSearchFixture BeginWithGate(HiddenSearchFixture fixture)
    {
        CardDefinitionId sourceDefinition = fixture.State.Cards
            .Single(card => card.OwnerSide == PlayerSide.Runner
                && card.ZoneId == fixture.State.GetZone(ZoneKind.RunnerGrip).Id)
            .DefinitionId;
        BehaviorChoiceSpec gate = new(
            "gate",
            BehaviorChooser.Runner,
            BehaviorChoiceMode.Mode,
            BehaviorChoiceCardinality.Exactly(1),
            [new BehaviorChoiceOption("go"), new BehaviorChoiceOption("stop")]);
        CardBehaviorDefinition existing = fixture.Catalog.GenericSpecs.Single(spec => spec.DefinitionId == sourceDefinition);
        BehaviorInstruction[] gateInstructions =
        [
            new BehaviorInstruction(BehaviorInstructionKind.Choose, choice: gate),
        ];
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(fixture.Catalog.Version)
            .Add(new CardBehaviorDefinition(
                existing.DefinitionId,
                existing.Controller,
                existing.Costs,
                existing.Conditions,
                existing.Targets,
                gateInstructions
                    .Concat(existing.Instructions)
                    .ToArray(),
                existing.StaticModifiers,
                existing.Triggers,
                existing.ExtensionHandlerId))
            .AddIcebreaker(fixture.Catalog.IcebreakerSpecs.Single())
            .Build();
        GameState state = ReplaceCatalogPins(fixture.State, catalog);
        return new HiddenSearchFixture(catalog, state, fixture.Runner, fixture.CandidateCard);
    }

    private static BehaviorCatalog BuildCatalog(
        CardDefinitionId sourceDefinition,
        CardDefinitionId candidateDefinition,
        IReadOnlyList<BehaviorInstruction> instructions)
    {
        return new BehaviorCatalogBuilder("test:hidden-search")
            .Add(new CardBehaviorDefinition(sourceDefinition, PlayerSide.Runner, instructions: instructions))
            .AddIcebreaker(new IcebreakerBehaviorSpec(
                candidateDefinition,
                baseStrength: 1,
                interfaceType: IceType.Barrier,
                breakCost: 1,
                breakLimit: 1,
                pumpCost: 1,
                pumpStrength: 1))
            .Build();
    }

    private static bool IsCardSelection(
        LegalAction action,
        string key,
        IReadOnlyList<CardInstanceId> cardIds) =>
        action.Parameters is BehaviorChoiceParameters parameters
        && parameters.ChoiceKey == key
        && parameters.OptionKeys.Count == 0
        && parameters.CardIds.SequenceEqual(cardIds);

    private static ApplyResult Apply(GameState state, BehaviorCatalog catalog, LegalAction action) =>
        GameEngine.Apply(
            state,
            new ActionSubmission(
                state.PendingDecision!.Owner,
                state.PendingDecision.Id,
                state.PendingDecision.Revision,
                action.Id),
            catalog);

    private static GameState CreateState(
        BehaviorCatalog catalog,
        params (CardInstanceId Id, CardDefinitionId DefinitionId, ZoneKind Zone, PlayerSide Owner)[] placements)
    {
        GameState baseline = GameState.CreateNew(
            seed: 4096,
            behaviorRegistryVersion: catalog.Version,
            behaviorCatalogFingerprint: catalog.Fingerprint);
        CardInstanceState[] cards = placements
            .Select(placement => new CardInstanceState(
                placement.Id,
                placement.DefinitionId,
                baseline.GetZone(placement.Zone).Id,
                placement.Owner,
                faceUp: placement.Zone is ZoneKind.RunnerRig or ZoneKind.RunnerPlayArea
                    or ZoneKind.CorpPlayArea or ZoneKind.ServerIce or ZoneKind.ServerRoot))
            .ToArray();
        ZoneState[] zones = baseline.Zones
            .Select(zone =>
            {
                CardInstanceId[] additions = placements
                    .Where(placement => placement.Zone == zone.Kind)
                    .Select(placement => placement.Id)
                    .ToArray();
                return additions.Length == 0
                    ? zone
                    : new ZoneState(
                        zone.Id,
                        zone.Kind,
                        zone.OwnerSide,
                        zone.ServerId,
                        zone.CardIds.Concat(additions).ToArray());
            })
            .ToArray();
        return new GameState(
            baseline.SchemaVersion,
            baseline.GameId,
            baseline.RulesVersion,
            baseline.CardDataVersion,
            baseline.Players,
            cards,
            zones,
            baseline.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, turnNumber: 1),
            baseline.Outcome,
            baseline.RandomState,
            baseline.Allocator,
            baseline.DecisionRevision,
            baseline.EventSequence,
            baseline.ResolutionStack,
            baseline.PendingDecision,
            baseline.ActionSequence,
            baseline.InitialSeed,
            baseline.RunState,
            baseline.RulesState,
            catalog.Version,
            catalog.Fingerprint);
    }

    private static GameState ReplaceCatalogPins(GameState state, BehaviorCatalog catalog) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        state.Turn,
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        catalog.Version,
        catalog.Fingerprint);

    private static GameState ReplaceResolutionStack(
        GameState state,
        IReadOnlyList<ResolutionFrame> frames) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards,
        state.Zones,
        state.Servers,
        state.Turn,
        state.Outcome,
        state.RandomState,
        state.Allocator,
        state.DecisionRevision,
        state.EventSequence,
        frames,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint);

    private sealed record HiddenSearchFixture(
        BehaviorCatalog Catalog,
        GameState State,
        PlayerId Runner,
        CardInstanceId CandidateCard);
}
