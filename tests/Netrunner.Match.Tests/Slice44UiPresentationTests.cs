using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class Slice44UiPresentationTests
{
    [Fact]
    public void StrengthBreakdownUsesOrderedRealizedTermsForIceAndBreaker()
    {
        ValueQuote iceQuote = new(
            BehaviorValueKind.Strength,
            baseValue: 2,
            effectiveValue: 4,
            terms:
            [
                new ValueCalculationTerm(
                    BehaviorValueSourceKind.Static,
                    BehaviorValueOperation.Add,
                    2,
                    "ice-strength-protecting-remote"),
            ]);
        ValueQuote breakerQuote = new(
            BehaviorValueKind.Strength,
            baseValue: 0,
            effectiveValue: 2,
            terms:
            [
                new ValueCalculationTerm(
                    BehaviorValueSourceKind.PerInstalledCard,
                    BehaviorValueOperation.Add,
                    2,
                    "echelon-installed-icebreakers",
                    sourceDefinitionId: new CardDefinitionId("30025"),
                    sourceCardId: new CardInstanceId("card:echelon"),
                    quantity: 2,
                    unitDelta: 1),
            ]);

        Assert.Equal(
            "STR 2 +2 (protecting remote)",
            StrengthBreakdownViewModel.From(iceQuote).Summary);
        Assert.Equal(
            "STR 0 +2 (per installed icebreaker: 2)",
            StrengthBreakdownViewModel.From(breakerQuote).Summary);

        ValueQuote ordered = new(
            BehaviorValueKind.Strength,
            baseValue: 3,
            effectiveValue: 5,
            terms:
            [
                new ValueCalculationTerm(
                    BehaviorValueSourceKind.Static,
                    BehaviorValueOperation.Add,
                    1,
                    "z-reason"),
                new ValueCalculationTerm(
                    BehaviorValueSourceKind.Static,
                    BehaviorValueOperation.Add,
                    1,
                    "a-reason"),
            ]);
        Assert.Equal(
            "STR 3 +1 (a-reason) +1 (z-reason)",
            StrengthBreakdownViewModel.From(ordered).Summary);
    }

    [Fact]
    public void MapperProjectsStrengthBreakdownsFromLiveAndJsonObservationShapes()
    {
        Observation live = ObservationWithEncounter(
            firstXUseKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:echelon",
                    ["key"] = "dzmz-first-program-install",
                    ["count"] = 1,
                },
            ]);

        MatchViewModel liveView = ObservationViewMapper.Build(
            live,
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());
        EncounterViewModel encounter = Assert.IsType<EncounterViewModel>(liveView.Run!.Encounter);
        Assert.Equal("STR 2 +2 (protecting remote)", encounter.StrengthSummary);
        EncounterBreakerViewModel breaker = Assert.Single(encounter.Breakers);
        Assert.Equal("STR 0 +1 (per installed icebreaker: 1)", breaker.StrengthSummary);

        Observation restored = EngineJson.Deserialize<Observation>(EngineJson.Serialize(live));
        MatchViewModel jsonView = ObservationViewMapper.Build(
            restored,
            CardMetadataResolvers.Default,
            GatewayBehaviorCatalog.Create());
        Assert.Equal(encounter.StrengthSummary, jsonView.Run!.Encounter!.StrengthSummary);
        Assert.Equal(breaker.StrengthSummary, Assert.Single(jsonView.Run.Encounter.Breakers).StrengthSummary);
        Assert.Equal(
            "STR 2 +2 (protecting remote)",
            jsonView.Run.Encounter.StrengthBreakdown!.Summary);
    }

    [Fact]
    public void CostPresenterUsesTypedBaseEffectiveAndAdjustmentOrderWithSafeFallback()
    {
        CostQuote quote = new(
            baseCost: 5,
            adjustments:
            [
                new CostAdjustment(
                    BehaviorCostModifierKind.RunnerInstallCreditCost,
                    -2,
                    "carmen-successful-run-install"),
                new CostAdjustment(
                    BehaviorCostModifierKind.RunnerInstallCreditCost,
                    -1,
                    "future-cost-reason"),
            ]);
        InstallCardParameters parameters = new(
            new CardInstanceId("card:install"),
            InstallDestinationKind.RunnerRig,
            serverId: null,
            createNewRemote: false,
            creditCost: quote.EffectiveCost,
            costQuote: quote);
        LegalActionViewModel action = new(
            new ActionId("action:install"),
            ActionKind.InstallCard,
            "Install card",
            parameters);

        Assert.Equal("install 5 → 2 (successful run this turn; future-cost-reason)", action.CostSummary);
        Assert.Contains(action.CostSummary!, ActionsRegionPresenter.Describe(action).Description, StringComparison.Ordinal);
        Assert.Equal(
            "+3 (future-value-reason)",
            new ValueCalculationTermPresentationViewModel(
                BehaviorValueSourceKind.EncounterScoped,
                BehaviorValueOperation.Add,
                3,
                "future-value-reason",
                null,
                null,
                null,
                null,
                null).Summary);
    }

    [Fact]
    public void FirstXProjectionUsesOnlyCurrentViewerFilteredFactsAndAttachesDetachedCue()
    {
        Observation observation = ObservationWithRunnerCards(
            [Card("card:used"), Card("card:old"), Card("card:plain")],
            currentFirstXUseKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:used",
                    ["key"] = "dzmz-first-program-install",
                    ["count"] = 1,
                },
            ],
            lastFirstXUseKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:old",
                    ["key"] = "old-first-x",
                    ["count"] = 1,
                },
            ]);

        MatchViewModel view = ObservationViewMapper.Build(observation);
        FirstXUseViewModel use = Assert.Single(view.UsedFirstXKeys);
        Assert.Equal("card:used", use.SourceCardId);
        Assert.Equal("dzmz-first-program-install", use.Key);
        Assert.Equal(FirstXUseLabels.UsedThisTurn, view.RunnerRig[0].FirstXUseCue);
        Assert.True(view.RunnerRig[0].IsFirstXUsedThisTurn);
        Assert.Null(view.RunnerRig[1].FirstXUseCue);
        Assert.Null(view.RunnerRig[2].FirstXUseCue);

        Observation restored = EngineJson.Deserialize<Observation>(EngineJson.Serialize(observation));
        MatchViewModel jsonView = ObservationViewMapper.Build(restored);
        Assert.Equal(view.UsedFirstXKeys, jsonView.UsedFirstXKeys);
        Assert.Equal(FirstXUseLabels.UsedThisTurn, jsonView.RunnerRig[0].FirstXUseCue);
        Assert.Null(jsonView.RunnerRig[1].FirstXUseCue);
    }

    [Fact]
    public void FirstXMalformedFactsFailClosedAndBreachNotesKeepUnknownReasonsVisible()
    {
        Observation negativeCount = ObservationWithRunnerCards(
            [Card("card:bad")],
            currentFirstXUseKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:bad",
                    ["key"] = "bad-first-x",
                    ["count"] = 0,
                },
            ]);
        Assert.Throws<FormatException>(() => ObservationViewMapper.Build(negativeCount));

        Observation duplicate = ObservationWithRunnerCards(
            [Card("card:bad")],
            currentFirstXUseKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:bad",
                    ["key"] = "bad-first-x",
                    ["count"] = 1,
                },
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:bad",
                    ["key"] = "bad-first-x",
                    ["count"] = 2,
                },
            ]);
        Assert.Throws<FormatException>(() => ObservationViewMapper.Build(duplicate));

        RunBreachAccessModifierViewModel known = new(
            null,
            "server:headquarters",
            1,
            "docklands-pass-first-hq-breach",
            RunBreachAccessModifierSourceKind.FirstHeadquartersBreach);
        RunBreachAccessModifierViewModel unknown = new(
            null,
            "server:remote",
            2,
            "future-access-reason",
            RunBreachAccessModifierSourceKind.InstalledRunSource);
        Assert.Equal("+1 first HQ breach this turn", known.Summary);
        Assert.Equal("+2 (future-access-reason)", unknown.Summary);
    }

    [Fact]
    public void ActionsPresenterExposesInstallAndRezCostLinesThroughDecisionGroups()
    {
        CostQuote installQuote = new(
            baseCost: 2,
            [new CostAdjustment(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                -2,
                "dzmz-first-program-install")]);
        LegalActionViewModel install = new(
            new ActionId("action:install-cost"),
            ActionKind.InstallCard,
            "Install DZMZ target",
            new InstallCardParameters(
                new CardInstanceId("card:program"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: installQuote.EffectiveCost,
                costQuote: installQuote));
        DecisionViewModel turnAction = new(
            "decision:turn-action",
            1,
            new PlayerId("player:runner"),
            PlayerSide.Runner,
            DecisionKind.TurnAction,
            "Choose a basic action.");
        MatchViewModel turnView = ObservationViewMapper.Build(
            ObservationWithRunnerCards([Card("card:program")])) with
        {
            Decision = turnAction,
            LegalActions = [install],
        };

        ActionsRegionState installRegion = ActionsRegionPresenter.Present(Snapshot(turnView));
        LegalActionViewModel groupedInstall = Assert.Single(installRegion.DisplayActions);
        Assert.Equal(ActionKind.InstallCard, groupedInstall.Kind);
        Assert.Contains("install 2 → 0 (DZMZ first program install this turn)", groupedInstall.Description, StringComparison.Ordinal);
        Assert.Contains(install.Id, installRegion.TargetedActions);

        CostQuote rezQuote = new(
            baseCost: 3,
            [new CostAdjustment(
                BehaviorCostModifierKind.IceRezCreditCost,
                3,
                "tread-lightly-ice-rez-cost")]);
        LegalActionViewModel rez = new(
            new ActionId("action:rez-cost"),
            ActionKind.RezCard,
            "Rez ICE",
            new RezCardParameters(
                new CardInstanceId("card:ice"),
                rezQuote.EffectiveCost,
                rezQuote));
        DecisionViewModel runTiming = turnAction with
        {
            Id = "decision:run-timing",
            Kind = DecisionKind.RunTiming,
        };
        ActionsRegionState rezRegion = ActionsRegionPresenter.Present(
            Snapshot(turnView with
            {
                Decision = runTiming,
                LegalActions = [rez],
            }));
        LegalActionViewModel groupedRez = Assert.Single(rezRegion.DisplayActions);
        Assert.Equal(ActionKind.RezCard, groupedRez.Kind);
        Assert.Contains("rez 3 → 6 (Tread Lightly this run)", groupedRez.Description, StringComparison.Ordinal);
        Assert.Contains(rez.Id, rezRegion.TargetedActions);
    }

    private static Observation ObservationWithEncounter(
        IReadOnlyList<Dictionary<string, object?>>? firstXUseKeys = null)
    {
        Dictionary<string, object?> ice = Card("card:ice", "30072", rezzed: true);
        Dictionary<string, object?> breaker = Card("card:echelon", "30025");
        Dictionary<string, object?> publicState = BasePublicState(
            [breaker],
            firstXUseKeys,
            servers:
            [
                new Dictionary<string, object?>
                {
                    ["id"] = "server:remote",
                    ["kind"] = ServerKind.Remote,
                    ["ice"] = new object?[] { ice },
                    ["root"] = Array.Empty<object?>(),
                },
            ]);
        publicState["run"] = new Dictionary<string, object?>
        {
            ["id"] = "run:test",
            ["attackedServer"] = "server:remote",
            ["initiatingSide"] = PlayerSide.Runner,
            ["phase"] = RunCrPhase.Encounter,
            ["step"] = RunTimingStep.EncounterPaidWindow,
            ["currentIceIndex"] = 0,
            ["currentIce"] = ice,
            ["successful"] = false,
            ["unsuccessful"] = false,
            ["endReason"] = null,
            ["priority"] = null,
            ["encounter"] = new Dictionary<string, object?>
            {
                ["id"] = "encounter:test",
                ["ice"] = ice,
                ["status"] = RunEncounterStatus.Initialized,
                ["subroutines"] = Array.Empty<object?>(),
                ["strengthQuote"] = new Dictionary<string, object?>
                {
                    ["valueKind"] = BehaviorValueKind.Strength,
                    ["baseValue"] = 2,
                    ["effectiveValue"] = 4,
                    ["terms"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["source"] = BehaviorValueSourceKind.Static,
                            ["operation"] = BehaviorValueOperation.Add,
                            ["delta"] = 2,
                            ["reasonKey"] = "ice-strength-protecting-remote",
                            ["sourceDefinitionId"] = null,
                            ["sourceCardId"] = null,
                        },
                    },
                },
                ["breakerStrengths"] = new object?[]
                {
                    new Dictionary<string, object?>
                    {
                        ["cardId"] = "card:echelon",
                        ["strengthQuote"] = new Dictionary<string, object?>
                        {
                            ["valueKind"] = BehaviorValueKind.Strength,
                            ["baseValue"] = 0,
                            ["effectiveValue"] = 1,
                            ["terms"] = new object?[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["source"] = BehaviorValueSourceKind.PerInstalledCard,
                                    ["operation"] = BehaviorValueOperation.Add,
                                    ["delta"] = 1,
                                    ["reasonKey"] = "echelon-installed-icebreakers",
                                    ["sourceDefinitionId"] = "30025",
                                    ["sourceCardId"] = "card:echelon",
                                    ["quantity"] = 1,
                                    ["unitDelta"] = 1,
                                },
                            },
                        },
                    },
                },
            },
            ["breach"] = Breach(),
            ["runCredits"] = 0,
            ["initiatingSource"] = null,
            ["lingeringEffects"] = Array.Empty<object?>(),
        };
        return new Observation(
            PlayerSide.Runner,
            "",
            Array.Empty<LegalAction>(),
            publicState,
            new Dictionary<string, object?>
            {
                ["grip"] = Array.Empty<object?>(),
                ["stack"] = Array.Empty<object?>(),
            });
    }

    private static Observation ObservationWithRunnerCards(
        IReadOnlyList<Dictionary<string, object?>> cards,
        IReadOnlyList<Dictionary<string, object?>>? currentFirstXUseKeys = null,
        IReadOnlyList<Dictionary<string, object?>>? lastFirstXUseKeys = null)
    {
        return new Observation(
            PlayerSide.Runner,
            "",
            Array.Empty<LegalAction>(),
            BasePublicState(
                cards,
                currentFirstXUseKeys,
                lastFirstXUseKeys: lastFirstXUseKeys),
            new Dictionary<string, object?>
            {
                ["grip"] = Array.Empty<object?>(),
                ["stack"] = Array.Empty<object?>(),
            });
    }

    private static Dictionary<string, object?> BasePublicState(
        IReadOnlyList<Dictionary<string, object?>> runnerRig,
        IReadOnlyList<Dictionary<string, object?>>? firstXUseKeys = null,
        IReadOnlyList<Dictionary<string, object?>>? lastFirstXUseKeys = null,
        IReadOnlyList<Dictionary<string, object?>>? servers = null)
    {
        Dictionary<string, object?> turnFacts = new()
        {
            ["corp"] = new Dictionary<string, object?>
            {
                ["thisTurn"] = null,
                ["lastTurn"] = null,
            },
            ["runner"] = new Dictionary<string, object?>
            {
                ["thisTurn"] = new Dictionary<string, object?>
                {
                    ["side"] = PlayerSide.Runner,
                    ["turnNumber"] = 1,
                    ["firstXUseKeys"] = firstXUseKeys?.Cast<object?>().ToArray() ?? Array.Empty<object?>(),
                },
                ["lastTurn"] = new Dictionary<string, object?>
                {
                    ["side"] = PlayerSide.Runner,
                    ["turnNumber"] = 0,
                    ["firstXUseKeys"] = lastFirstXUseKeys?.Cast<object?>().ToArray() ?? Array.Empty<object?>(),
                },
            },
        };
        return new Dictionary<string, object?>
        {
            ["turn"] = new Dictionary<string, object?>
            {
                ["activeSide"] = PlayerSide.Runner,
                ["phase"] = TurnPhase.Runner,
                ["turnNumber"] = 1,
            },
            ["turnFacts"] = turnFacts,
            ["players"] = new Dictionary<string, object?>
            {
                ["corp"] = Player("player:corp"),
                ["runner"] = Player("player:runner"),
            },
            ["hiddenZoneCounts"] = new Dictionary<string, object?>
            {
                ["corpHand"] = 0,
                ["corpDeck"] = 0,
                ["runnerGrip"] = 0,
                ["runnerStack"] = 0,
            },
            ["servers"] = servers?.Cast<object?>().ToArray() ?? Array.Empty<object?>(),
            ["runnerRig"] = runnerRig.Cast<object?>().ToArray(),
            ["corpScore"] = Array.Empty<object?>(),
            ["runnerScore"] = Array.Empty<object?>(),
            ["outcome"] = new Dictionary<string, object?>
            {
                ["kind"] = GameOutcomeKind.Ongoing,
                ["winner"] = null,
                ["reason"] = null,
            },
        };
    }

    private static Dictionary<string, object?> Breach() => new()
    {
        ["candidates"] = Array.Empty<object?>(),
        ["randomAccessLimit"] = 0,
        ["hiddenCandidateSource"] = null,
        ["chosen"] = Array.Empty<object?>(),
        ["accessed"] = Array.Empty<object?>(),
        ["accessState"] = RunAccessState.NotStarted,
    };

    private static Dictionary<string, object?> Card(
        string id,
        string definitionId = "unknown:card",
        bool rezzed = false) => new()
        {
            ["id"] = id,
            ["definitionId"] = definitionId,
            ["rezzed"] = rezzed,
            ["advancementCounters"] = 0,
        };

    private static Dictionary<string, object?> Player(string id) => new()
    {
        ["id"] = id,
        ["credits"] = 5,
        ["clicks"] = 4,
        ["agendaPoints"] = 0,
    };

    private static MatchSnapshot Snapshot(MatchViewModel view) => new(
        Sequence: 1,
        Observation: new Observation(
            PlayerSide.Runner,
            view.Decision?.Id ?? string.Empty,
            Array.Empty<LegalAction>(),
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>()),
        View: view,
        Events: Array.Empty<EngineEvent>(),
        Feedback: null,
        IsOpponentThinking: false,
        RunOutcome: null);
}
