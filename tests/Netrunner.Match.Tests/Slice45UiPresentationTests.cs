using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class Slice45UiPresentationTests
{
    [Fact]
    public void ProjectsRunnerOwnedOptionsInEngineOrderWithTypedCostAndFallback()
    {
        MatchViewModel view = View(
            PlayerSide.Runner,
            DecisionKind.BehaviorChoice,
            [
                BehaviorAction(
                    "action:credits",
                    "description deliberately unrelated to the option",
                    "manegarm-approach-choice",
                    "pay-credits"),
                BehaviorAction(
                    "action:end",
                    "another arbitrary description",
                    "manegarm-approach-choice",
                    "end-run"),
            ],
            ApproachRun());

        Slice45DecisionViewModel projected = Slice45PresentationProjector.Present(view);

        Assert.True(projected.IsProminent);
        Assert.Equal(Slice45DecisionSurface.Approach, projected.Surface);
        Assert.Equal(PlayerSide.Runner, projected.ActingPlayer);
        Assert.Equal(
            ["pay-credits", "end-run"],
            projected.Options.Select(option => option.OptionKey));
        Assert.Equal("5 credits", projected.Options[0].CostSummary);
        Assert.Equal(BehaviorChoiceOptionSemantic.Normal, projected.Options[0].Semantic);
        Assert.Equal(BehaviorChoiceOptionSemantic.Fallback, projected.Options[1].Semantic);
        Assert.True(projected.Options[1].IsFallback);
        Assert.Contains("FALLBACK", projected.Options[1].PresentationLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("description deliberately", projected.Options[0].PresentationLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsCorpOwnedPrivateCardCostWithoutCatalogTargetLeak()
    {
        MatchViewModel view = View(
            PlayerSide.Corp,
            DecisionKind.BehaviorChoice,
            [
                BehaviorAction(
                    "action:anoetic",
                    "description does not name any card",
                    "anoetic-void-approach-choice",
                    "pay-and-trash-hq",
                    ["card:hidden-hq-1", "card:hidden-hq-2"]),
            ],
            ApproachRun());

        Slice45DecisionViewModel projected = Slice45PresentationProjector.Present(view);

        Slice45ActionPresentationViewModel option = Assert.Single(projected.Options);
        Assert.Equal(PlayerSide.Corp, option.ActingPlayer);
        Assert.Equal(
            ["card:hidden-hq-1", "card:hidden-hq-2"],
            option.VisibleCardIds);
        Assert.Equal("2 credits + 2 private cards to trash", option.CostSummary);
        Assert.DoesNotContain("headquarters", option.PresentationLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncounterChoicesAreProminentAndPreserveDescriptionIndependence()
    {
        MatchViewModel first = View(
            PlayerSide.Runner,
            DecisionKind.BehaviorChoice,
            [
                BehaviorAction(
                    "action:tag",
                    "first arbitrary description",
                    "funhouse-encounter-choice",
                    "take-tag"),
            ],
            EncounterRun());
        MatchViewModel second = View(
            PlayerSide.Runner,
            DecisionKind.BehaviorChoice,
            [
                BehaviorAction(
                    "action:tag",
                    "second arbitrary description",
                    "funhouse-encounter-choice",
                    "take-tag"),
            ],
            EncounterRun());

        Slice45ActionPresentationViewModel firstOption =
            Assert.Single(Slice45PresentationProjector.Present(first).Options);
        Slice45ActionPresentationViewModel secondOption =
            Assert.Single(Slice45PresentationProjector.Present(second).Options);

        Assert.Equal(Slice45DecisionSurface.Encounter, Slice45PresentationProjector.Present(first).Surface);
        Assert.Equal(firstOption.PresentationLabel, secondOption.PresentationLabel);
        Assert.Equal("OPTION · take-tag · NORMAL", firstOption.PresentationLabel);
        Assert.Null(firstOption.CostSummary);
    }

    [Fact]
    public void TypedRunEventTargetRemainsVisibleWithoutUsingCardDescription()
    {
        var action = new LegalActionViewModel(
            new ActionId("action:jailbreak-hq"),
            ActionKind.PlayCard,
            "description omitted by a future client",
            new PlayCardParameters(
                new CardInstanceId("card:jailbreak"),
                creditCost: 0,
                targetServerId: new ServerId("server:headquarters")));

        Assert.Equal(
            "PLAYCARD · TARGET SERVER · server:headquarters",
            Slice45PresentationProjector.Label(action));
        Assert.True(ActionsRegionPresenter.IsTargetedAction(action));
        Assert.Equal("server:headquarters", ActionsRegionPresenter.GetTargetServerId(action.Parameters));
    }

    [Fact]
    public void TypedBioroidActionUsesLossCostAndSubroutineIdentity()
    {
        var action = new LegalActionViewModel(
            new ActionId("action:bioroid"),
            ActionKind.UseBioroidAbility,
            "description omitted by a future client",
            new RunBioroidAbilityParameters(
                new CardInstanceId("card:ansel"),
                new SubroutineId("subroutine:ansel:0"),
                "bioroid-break"));

        Assert.Equal(
            "USE BIOROID · BREAK subroutine:ansel:0 · LOSE 1 CLICK",
            Slice45PresentationProjector.Label(action));
        Assert.True(ActionsRegionPresenter.IsTargetedAction(action));
        Assert.Equal("card:ansel", ActionsRegionPresenter.GetTargetCardId(action.Parameters));
    }

    [Fact]
    public void MalformedChoicePayloadFailsClosedAndDoesNotRenderPartialOptions()
    {
        MatchViewModel malformed = View(
            PlayerSide.Runner,
            DecisionKind.BehaviorChoice,
            [
                new LegalAction(
                    new ActionId("action:malformed"),
                    ActionKind.ChooseBehavior,
                    "malformed",
                    new BehaviorChoiceParameters(
                        "choice:malformed",
                        optionKeys: ["one", "two"],
                        cardIds: [])),
            ],
            ApproachRun());

        Slice45DecisionViewModel projected = Slice45PresentationProjector.Present(malformed);

        Assert.False(projected.IsAvailable);
        Assert.False(projected.IsProminent);
        Assert.Empty(projected.Options);
        Assert.Contains("malformed", projected.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingAndMalformedFutureCuesRemainUnavailable()
    {
        MatchViewModel baseline = View(
            PlayerSide.Runner,
            DecisionKind.BehaviorChoice,
            [BehaviorAction("action:choice", "description", "manegarm-approach-choice", "end-run")],
            ApproachRun());

        Assert.False(baseline.Slice45Cues.HasAnyValue);
        Assert.Null(baseline.Slice45Cues.RunRestrictionActive);
        Assert.Null(baseline.Slice45Cues.BioroidAbilityReady);
        Assert.Null(baseline.Slice45Cues.MayflyBreakArmed);

        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        Observation baselineObservation = ObservationBuilder.Build(
            M2ScenarioSetup.Build(document),
            PlayerSide.Runner);
        var malformedState = new Dictionary<string, object?>(baselineObservation.PublicState)
        {
            ["slice45Cues"] = new Dictionary<string, object?>
            {
                ["runRestrictionActive"] = "not-a-bool",
                ["bioroidAbilityReady"] = 1,
                ["mayflyBreakArmed"] = null,
            },
        };
        MatchViewModel malformed = ObservationViewMapper.Build(
            baselineObservation with { PublicState = malformedState },
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());

        Assert.False(malformed.Slice45Cues.HasAnyValue);
    }

    private static LegalAction BehaviorAction(
        string actionId,
        string description,
        string choiceKey,
        string optionKey,
        IReadOnlyList<string>? cardIds = null) =>
        new(
            new ActionId(actionId),
            ActionKind.ChooseBehavior,
            description,
            new BehaviorChoiceParameters(
                choiceKey,
                optionKeys: [optionKey],
                cardIds: (cardIds ?? []).Select(static value => new CardInstanceId(value)).ToArray()));

    private static MatchViewModel View(
        PlayerSide owner,
        DecisionKind kind,
        IReadOnlyList<LegalAction> actions,
        RunFocusViewModel run)
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        Observation baseline = ObservationBuilder.Build(
            M2ScenarioSetup.Build(document),
            owner);
        Observation observation = baseline with
        {
            LegalActions = actions,
            Decision = new ObservationDecision(
                new DecisionId("decision:slice45-ui"),
                1,
                new PlayerId(owner == PlayerSide.Runner ? "player:runner" : "player:corp"),
                owner,
                kind,
                "typed prompt"),
        };
        return ObservationViewMapper.Build(
            observation,
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create()) with
        {
            Run = run,
        };
    }

    private static RunFocusViewModel ApproachRun() => new(
        "run:slice45-ui",
        "server:remote",
        PlayerSide.Runner,
        RunCrPhase.Approach,
        RunTimingStep.ApproachServer,
        CurrentIceIndex: -1,
        CurrentIce: null,
        Successful: false,
        Unsuccessful: false,
        EndReason: null,
        Priority: null,
        Encounter: null,
        Access: EmptyAccess(),
        RunCredits: 0,
        InitiatingSource: null);

    private static RunFocusViewModel EncounterRun() => ApproachRun() with
    {
        Phase = RunCrPhase.Encounter,
        Step = RunTimingStep.EncounterPaidWindow,
        Encounter = new EncounterViewModel(
            "encounter:slice45-ui",
            null,
            RunEncounterStatus.Initialized,
            Array.Empty<SubroutineViewModel>()),
    };

    private static AccessViewModel EmptyAccess() => new(
        Array.Empty<CardViewModel?>(),
        0,
        null,
        Array.Empty<CardViewModel?>(),
        Array.Empty<CardViewModel?>(),
        RunAccessState.NotStarted);
}
