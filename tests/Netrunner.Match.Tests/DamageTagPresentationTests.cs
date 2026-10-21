using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class DamageTagPresentationTests
{
    [Fact]
    public void MapsPublicTagsAndCoreDamageWithoutInventingMissingState()
    {
        Observation baseline = BaselineObservation();
        var publicState = new Dictionary<string, object?>(baseline.PublicState);
        var baselinePlayers = (IReadOnlyDictionary<string, object?>)publicState["players"]!;
        var players = new Dictionary<string, object?>(baselinePlayers);
        var corp = new Dictionary<string, object?>(
            (IReadOnlyDictionary<string, object?>)baselinePlayers["corp"]!);
        var runner = new Dictionary<string, object?>(
            (IReadOnlyDictionary<string, object?>)baselinePlayers["runner"]!);
        runner["tags"] = 2;
        runner["coreDamage"] = 1;
        players["corp"] = corp;
        players["runner"] = runner;
        publicState["players"] = players;

        MatchViewModel mapped = ObservationViewMapper.Build(
            baseline with { PublicState = publicState },
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());

        Assert.Null(mapped.Corp.Tags);
        Assert.Equal(2, mapped.Runner.Tags);
        Assert.Equal(1, mapped.Runner.CoreDamage);
        Assert.True(mapped.Runner.IsTagged);
        Assert.Equal("TAGS · 2 · TAGGED", mapped.Runner.TagSummary);
        Assert.Equal("TAGS · unavailable", mapped.Corp.TagSummary);
    }

    [Fact]
    public void ProjectsDamageInstructionAndConditionalTermsOnlyFromObservation()
    {
        Observation baseline = BaselineObservation();
        var publicState = new Dictionary<string, object?>(baseline.PublicState)
        {
            ["pendingDamage"] = new Dictionary<string, object?>
            {
                ["kind"] = "Net",
                ["amount"] = 2,
                ["targetId"] = "player:runner",
                ["sourceCardId"] = "card:karuna",
                ["flatlineRisk"] = true,
            },
            ["conditionalValues"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["key"] = "neurospike-x",
                    ["valueKind"] = "Strength",
                    ["baseValue"] = 0,
                    ["effectiveValue"] = 3,
                    ["terms"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["source"] = "Variable",
                            ["operation"] = "Add",
                            ["delta"] = 3,
                            ["reasonKey"] = "scored-agenda-points-this-turn",
                            ["quantity"] = 3,
                            ["variableKey"] = "scored-agenda-points",
                        },
                    },
                },
            },
        };

        MatchViewModel mapped = ObservationViewMapper.Build(
            baseline with { PublicState = publicState },
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());

        DamagePresentationViewModel damage = Assert.Single(mapped.PendingDamage);
        Assert.Equal(DamageKind.Net, damage.Kind);
        Assert.Equal(2, damage.Amount);
        Assert.Equal("card:karuna", damage.SourceCardId);
        Assert.True(damage.IsFlatlineRisk);
        Assert.Contains("FLATLINE RISK", damage.Summary, StringComparison.Ordinal);

        ConditionalValueViewModel value = Assert.Single(mapped.ConditionalValues);
        Assert.Equal("neurospike-x", value.Key);
        Assert.Equal(3, value.EffectiveValue);
        Assert.Equal("scored agenda points this turn", value.Terms.Single().ReasonLabel);
        Assert.Contains("scored agenda points this turn", value.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentsTagGainAndRemovalAndDamageEventsWithTypedDetails()
    {
        string gain = EventFeedRegionPresenter.Describe(new EngineEvent(
            1,
            EngineEventKinds.TagsAdjusted,
            new TagsAdjustedEventPayload(
                new PlayerId("player:runner"),
                1,
                1)));
        string removal = EventFeedRegionPresenter.Describe(new EngineEvent(
            2,
            EngineEventKinds.TagsAdjusted,
            new TagsAdjustedEventPayload(
                new PlayerId("player:runner"),
                -1,
                0)));
        string damage = EventFeedRegionPresenter.Describe(new EngineEvent(
            3,
            EngineEventKinds.DamageDealt,
            new DamageDealtEventPayload(
                new PlayerId("player:runner"),
                DamageKind.Net,
                2,
                1,
                0,
                flatlined: false,
                sourceCardId: new CardInstanceId("card:diviner"),
                flatlineRisk: false,
                trashedCardPrintedCost: 3,
                conditionalEndRun: true)));

        Assert.Contains("TAG GAIN", gain, StringComparison.Ordinal);
        Assert.Contains("TAGGED", gain, StringComparison.Ordinal);
        Assert.Contains("TAG REMOVAL", removal, StringComparison.Ordinal);
        Assert.Contains("CLEAR", removal, StringComparison.Ordinal);
        Assert.Contains("DAMAGE · NET · 2", damage, StringComparison.Ordinal);
        Assert.Contains("TARGET player:runner", damage, StringComparison.Ordinal);
        Assert.Contains("1 CARD(S) TRASHED", damage, StringComparison.Ordinal);
        Assert.Contains("SOURCE card:diviner", damage, StringComparison.Ordinal);
        Assert.Contains("TRASHED CARD PRINTED COST 3", damage, StringComparison.Ordinal);
        Assert.Contains("CONDITIONAL END RUN", damage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsOnlyTheExplicitSlice46ObservationFieldNames()
    {
        var publicState = new Dictionary<string, object?>
        {
            ["pendingDamage"] = new Dictionary<string, object?>
            {
                ["kind"] = "Net",
                ["amount"] = 2,
                ["target"] = "player:runner",
            },
            ["conditionalValues"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["key"] = "neurospike-x",
                    ["valueKind"] = "Damage",
                    ["baseValue"] = 0,
                    ["effectiveValue"] = 1,
                    ["terms"] = Array.Empty<object>(),
                },
            },
        };

        Assert.Empty(DamageTagObservationProjector.ReadDamageInstructions(publicState));
        Assert.Single(DamageTagObservationProjector.ReadConditionalValues(publicState));
    }

    [Fact]
    public void MarksTypedUnlessPaymentAlternativesAndTagRemovalCosts()
    {
        MatchViewModel view = BaselineView() with
        {
            Decision = new DecisionViewModel(
                "decision:slice46",
                1,
                new PlayerId("player:runner"),
                PlayerSide.Runner,
                DecisionKind.BehaviorChoice,
                "Choose unless pay."),
            Run = BaseRun(),
            LegalActions =
            [
                new LegalActionViewModel(
                    new ActionId("action:pay"),
                    ActionKind.ChooseBehavior,
                    "ignored description",
                    new BehaviorChoiceParameters(
                        "slice46-unless",
                        ["pay"],
                        finalizeSelection: true))
                {
                    BehaviorChoice = new BehaviorChoiceOptionViewModel(
                        "slice46-unless",
                        "pay",
                        BehaviorChoiceMode.Mode,
                        BehaviorChoiceOptionSemantic.Normal,
                        [
                            new BehaviorCostViewModel(
                                BehaviorCostKind.Credits,
                                8,
                                null,
                                null),
                        ]),
                },
                new LegalActionViewModel(
                    new ActionId("action:decline"),
                    ActionKind.ChooseBehavior,
                    "also ignored",
                    new BehaviorChoiceParameters(
                        "slice46-unless",
                        ["decline"],
                        finalizeSelection: true))
                {
                    BehaviorChoice = new BehaviorChoiceOptionViewModel(
                        "slice46-unless",
                        "decline",
                        BehaviorChoiceMode.Mode,
                        BehaviorChoiceOptionSemantic.Fallback,
                        []),
                },
            ],
        };

        UnlessChoiceViewModel decision = UnlessChoicePresenter.Present(view);

        Assert.True(decision.HasUnlessChoice);
        Assert.Equal("UNLESS PAY · OPTION · pay · NORMAL · COST · 8 credits", decision.Options[0].PresentationLabel);
        Assert.Contains("UNLESS FALLBACK", decision.Options[1].PresentationLabel, StringComparison.Ordinal);

        LegalActionViewModel removeTag = new(
            new ActionId("action:remove-tag"),
            ActionKind.RemoveTag,
            "ignored",
            new RemoveTagParameters(1, 2));
        Assert.Equal(
            "REMOVE TAG · COST · 1 click + 2 credits",
            UnlessChoicePresenter.Label(removeTag));
    }

    [Fact]
    public void IncludesTagActionsInTheTurnActionSurfaceAndTargetsTaggedResources()
    {
        MatchViewModel view = BaselineView() with
        {
            Decision = new DecisionViewModel(
                "decision:tag-actions",
                1,
                new PlayerId("player:runner"),
                PlayerSide.Runner,
                DecisionKind.TurnAction,
                "Choose a turn action."),
            LegalActions =
            [
                new LegalActionViewModel(
                    new ActionId("action:remove-tag"),
                    ActionKind.RemoveTag,
                    "Remove 1 tag",
                    new RemoveTagParameters(1, 2)),
                new LegalActionViewModel(
                    new ActionId("action:trash-tagged-resource"),
                    ActionKind.TrashTaggedResource,
                    "Trash tagged resource",
                    new TrashTaggedResourceParameters(
                        new CardInstanceId("card:tagged-resource"),
                        1,
                        2)),
            ],
        };

        ActionsRegionState actions = ActionsRegionPresenter.Present(Snapshot(view, []));

        Assert.Contains(actions.DisplayActions, action => action.Kind == ActionKind.RemoveTag);
        LegalActionViewModel taggedResource = Assert.Single(
            actions.DisplayActions,
            action => action.Kind == ActionKind.TrashTaggedResource);
        Assert.Equal("card:tagged-resource", ActionsRegionPresenter.GetTargetCardId(taggedResource.Parameters));
    }

    [Fact]
    public void ExposesJackOutCueOnlyOnDamagePublication()
    {
        MatchViewModel view = BaselineView() with
        {
            Decision = new DecisionViewModel(
                "decision:slice46",
                1,
                new PlayerId("player:runner"),
                PlayerSide.Runner,
                DecisionKind.IceSubroutineChoice,
                "Choose a run action."),
            LegalActions =
            [
                new LegalActionViewModel(
                    new ActionId("action:jack-out"),
                    ActionKind.ResolveIceSubroutine,
                    "ignored",
                    new IceSubroutineChoiceParameters(
                        "frame:slice46",
                        IceSubroutineChoiceKind.JackOut)),
            ],
            Run = BaseRun(),
        };
        MatchSnapshot beforeDamage = Snapshot(view, []);
        MatchSnapshot afterDamage = Snapshot(
            view,
            [
                new EngineEvent(
                    1,
                    EngineEventKinds.DamageDealt,
                    new DamageDealtEventPayload(
                        new PlayerId("player:runner"),
                        DamageKind.Net,
                        2,
                        0,
                        0,
                        flatlined: false)),
            ]);

        Assert.False(UnlessChoicePresenter.Present(beforeDamage).JackOutAfterDamage);
        Assert.True(UnlessChoicePresenter.Present(afterDamage).JackOutAfterDamage);
    }

    private static Observation BaselineObservation() =>
        ObservationBuilder.Build(
            M2ScenarioSetup.Build(M2ScenarioProvider.LoadRepresentative()),
            PlayerSide.Runner);

    private static MatchViewModel BaselineView() =>
        ObservationViewMapper.Build(
            BaselineObservation(),
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());

    private static RunFocusViewModel BaseRun() => new(
        "run:slice46",
        "server:remote",
        PlayerSide.Runner,
        RunCrPhase.Encounter,
        RunTimingStep.EncounterPaidWindow,
        CurrentIceIndex: 0,
        CurrentIce: null,
        Successful: false,
        Unsuccessful: false,
        EndReason: null,
        Priority: null,
        Encounter: null,
        Access: new AccessViewModel(
            Candidates: Array.Empty<CardViewModel?>(),
            RandomAccessLimit: 0,
            HiddenCandidateSource: null,
            Chosen: Array.Empty<CardViewModel?>(),
            Accessed: Array.Empty<CardViewModel?>(),
            State: RunAccessState.NotStarted),
        RunCredits: 0,
        InitiatingSource: null);

    private static MatchSnapshot Snapshot(
        MatchViewModel view,
        IReadOnlyList<EngineEvent> events) =>
        new(
            Sequence: 1,
            Observation: BaselineObservation(),
            View: view,
            Events: events,
            Feedback: null,
            IsOpponentThinking: false);
}
