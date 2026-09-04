using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class Ui404PresentationTests
{
    [Fact]
    public void MapperRetainsDetachedTypedCountersAndDerivesAbilityCuesFromIssuedFacts()
    {
        List<object?> rawCounters =
        [
            new Dictionary<string, object?>
            {
                ["counterName"] = "credit",
                ["purpose"] = "hostedCredit",
                ["count"] = 3,
                ["spendRestriction"] = "cardEffectOnly",
            },
            new Dictionary<string, object?>
            {
                ["counterName"] = "virus",
                ["purpose"] = "virus",
                ["count"] = 2,
                ["spendRestriction"] = null,
            },
            new Dictionary<string, object?>
            {
                ["counterName"] = "power",
                ["purpose"] = "generic",
                ["count"] = 1,
                ["spendRestriction"] = null,
            },
        ];
        AbilityActionParameters ability = Ability(
            new CardInstanceId("card:ready"),
            "ready-ability");
        Observation observation = ObservationWithRunnerCards(
            [
                Card("card:ready"),
                Card("card:used", rawCounters),
                Card("card:plain"),
            ],
            [new LegalAction(
                new ActionId("action:ready"),
                ActionKind.UseAbility,
                "Use ability",
                ability)],
            usedAbilityKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:used",
                    ["abilityKey"] = "used-ability",
                },
            ]);

        MatchViewModel view = ObservationViewMapper.Build(observation);
        CardViewModel ready = Assert.Single(view.RunnerRig, card => card.Id == "card:ready");
        CardViewModel used = Assert.Single(view.RunnerRig, card => card.Id == "card:used");
        CardViewModel plain = Assert.Single(view.RunnerRig, card => card.Id == "card:plain");

        Assert.Equal(AbilityCueLabels.Ready, ready.AbilityCue);
        Assert.Equal(AbilityCueLabels.Used, used.AbilityCue);
        Assert.Null(plain.AbilityCue);
        Assert.Single(view.UsedAbilityKeys);
        Assert.Equal("card:used", view.UsedAbilityKeys[0].SourceCardId);
        Assert.Equal("used-ability", view.UsedAbilityKeys[0].AbilityKey);
        Assert.NotSame(rawCounters, used.TypedCounters);
        Assert.Equal(
            ["HOSTED CREDIT ×3", "VIRUS ×2", "POWER ×1"],
            used.CounterChips);
        Assert.Equal(CardCounterPurpose.HostedCredit, used.TypedCounters[0].Purpose);
        Assert.Equal(HostedCreditSpendRestriction.CardEffectOnly, used.TypedCounters[0].SpendRestriction);

        rawCounters.Clear();
        Assert.Equal(3, used.TypedCounters.Count);
    }

    [Fact]
    public void MapperDoesNotTreatLastTurnUsesAsCurrentAbilityUsage()
    {
        Observation observation = ObservationWithRunnerCards(
            [Card("card:old")],
            usedAbilityKeys: [],
            lastTurnUsedAbilityKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:old",
                    ["abilityKey"] = "old-ability",
                },
            ]);

        MatchViewModel view = ObservationViewMapper.Build(observation);

        Assert.Empty(view.UsedAbilityKeys);
        Assert.Null(view.RunnerRig[0].AbilityCue);
    }

    [Fact]
    public void MapperAppliesCuesAcrossServerAndRunCardGraph()
    {
        Observation baseObservation = ObservationWithRunnerCards(
            [Card("card:plain")],
            [new LegalAction(
                new ActionId("action:source"),
                ActionKind.UseAbility,
                "Use ability",
                Ability(new CardInstanceId("card:root"), "root-ability"))],
            usedAbilityKeys:
            [
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:accessed",
                    ["abilityKey"] = "access-ability",
                },
                new Dictionary<string, object?>
                {
                    ["sourceCardId"] = "card:source",
                    ["abilityKey"] = "source-ability",
                },
            ]);
        var publicState = new Dictionary<string, object?>(baseObservation.PublicState)
        {
            ["servers"] = new object?[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "server:remote",
                    ["kind"] = "remote",
                    ["ice"] = Array.Empty<object?>(),
                    ["root"] = new object?[] { Card("card:root") },
                },
            },
            ["run"] = new Dictionary<string, object?>
            {
                ["id"] = "run:test",
                ["attackedServer"] = "server:remote",
                ["initiatingSide"] = "runner",
                ["phase"] = "approach",
                ["step"] = "approachPaidWindow",
                ["currentIceIndex"] = 0,
                ["currentIce"] = null,
                ["successful"] = false,
                ["unsuccessful"] = false,
                ["endReason"] = null,
                ["priority"] = null,
                ["encounter"] = null,
                ["breach"] = new Dictionary<string, object?>
                {
                    ["candidates"] = new object?[] { Card("card:accessed") },
                    ["randomAccessLimit"] = 1,
                    ["hiddenCandidateSource"] = null,
                    ["chosen"] = Array.Empty<object?>(),
                    ["accessed"] = new object?[] { Card("card:accessed") },
                    ["accessState"] = "inProgress",
                },
                ["runCredits"] = 0,
                ["initiatingSource"] = Card("card:source"),
                ["lingeringEffects"] = Array.Empty<object?>(),
            },
        };

        MatchViewModel view = ObservationViewMapper.Build(baseObservation with { PublicState = publicState });

        Assert.Equal(AbilityCueLabels.Ready, view.Servers[0].Root[0]!.AbilityCue);
        Assert.Equal(AbilityCueLabels.Used, view.Run!.Access.Candidates[0]!.AbilityCue);
        Assert.Equal(AbilityCueLabels.Used, view.Run.Access.Accessed[0]!.AbilityCue);
        Assert.Equal(AbilityCueLabels.Used, view.Run.InitiatingSource!.AbilityCue);
    }

    [Fact]
    public void MapperRejectsMalformedTypedCountersAndUnknownEnums()
    {
        Observation invalidPurpose = ObservationWithRunnerCards(
            [Card(
                "card:invalid-purpose",
                [new Dictionary<string, object?>
                {
                    ["counterName"] = "future",
                    ["purpose"] = "future-purpose",
                    ["count"] = 1,
                    ["spendRestriction"] = null,
                }])]);
        Assert.Throws<FormatException>(() => ObservationViewMapper.Build(invalidPurpose));

        Observation invalidCount = ObservationWithRunnerCards(
            [Card(
                "card:invalid-count",
                [new Dictionary<string, object?>
                {
                    ["counterName"] = "power",
                    ["purpose"] = "generic",
                    ["count"] = 0,
                    ["spendRestriction"] = null,
                }])]);
        Assert.Throws<FormatException>(() => ObservationViewMapper.Build(invalidCount));

        Observation invalidRestriction = ObservationWithRunnerCards(
            [Card(
                "card:invalid-restriction",
                [new Dictionary<string, object?>
                {
                    ["counterName"] = "virus",
                    ["purpose"] = "virus",
                    ["count"] = 1,
                    ["spendRestriction"] = "abilityCost",
                }])]);
        Assert.Throws<FormatException>(() => ObservationViewMapper.Build(invalidRestriction));
    }

    [Fact]
    public void ActionPresenterExplainsOnlyFrozenInstallCleanupIds()
    {
        LegalActionViewModel required = new(
            new ActionId("action:required"),
            ActionKind.InstallCard,
            "Install selected card",
            new InstallCardParameters(
                new CardInstanceId("card:new"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 3,
                candidateReplacementCardIds: [new CardInstanceId("card:console")],
                requiredReplacementCardIds: [new CardInstanceId("card:console")],
                selectedReplacementCardIds: [new CardInstanceId("card:console")]));
        LegalActionViewModel candidatesOnly = new(
            new ActionId("action:candidates"),
            ActionKind.InstallCard,
            "Install selected card",
            new InstallCardParameters(
                new CardInstanceId("card:new"),
                InstallDestinationKind.RunnerRig,
                serverId: null,
                createNewRemote: false,
                creditCost: 3,
                candidateReplacementCardIds: [new CardInstanceId("card:maybe")]));

        string requiredText = ActionsRegionPresenter.Describe(required).Description;
        string candidatesText = ActionsRegionPresenter.Describe(candidatesOnly).Description;

        Assert.Contains("CONSOLE/UNIQUE CLEANUP", requiredText, StringComparison.Ordinal);
        Assert.Contains("card:console", requiredText, StringComparison.Ordinal);
        Assert.Equal(candidatesOnly.Description, candidatesText);
    }

    [Fact]
    public void EventFeedDescribesHostedPoolAndKeepsGenericMovementTyped()
    {
        EngineEvent hosted = new(
            1,
            EngineEventKinds.HostedCreditPoolChanged,
            new HostedCreditPoolChangedEventPayload(
                HostedCreditPoolOperation.Take,
                new CardInstanceId("card:pool"),
                -3,
                0,
                HostedCreditSpendRestriction.CardEffectOnly,
                emptied: true,
                recipient: new PlayerId("player:runner")));
        EngineEvent moved = new(
            2,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId("card:pool"),
                new ZoneId("zone:rig"),
                new ZoneId("zone:removed")));
        EngineEvent removed = new(
            3,
            EngineEventKinds.CardMoved,
            new CardMovedEventPayload(
                new CardInstanceId("card:rfg"),
                new ZoneId("zone:rig"),
                new ZoneId("zone:rfg"),
                ZoneKind.RunnerRig,
                ZoneKind.RunnerRemovedFromGame));

        string hostedText = EventFeedRegionPresenter.Describe(hosted);
        string movedText = EventFeedRegionPresenter.Describe(moved);
        string removedText = EventFeedRegionPresenter.Describe(removed);

        Assert.Contains("HOSTED CREDIT TAKE", hostedText, StringComparison.Ordinal);
        Assert.Contains("EMPTY", hostedText, StringComparison.Ordinal);
        Assert.Contains("zone:rig", movedText, StringComparison.Ordinal);
        Assert.Contains("zone:removed", movedText, StringComparison.Ordinal);
        Assert.Contains("Removed from game", removedText, StringComparison.Ordinal);
        Assert.Contains("card:rfg", removedText, StringComparison.Ordinal);
    }

    private static AbilityActionParameters Ability(CardInstanceId sourceCardId, string key) =>
        new(
            sourceCardId,
            new CardDefinitionId("30014"),
            key,
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            [],
            [],
            new AbilityUsageSnapshot(BehaviorAbilityUsageLimit.OncePerTurn, 0),
            new string('a', 64),
            new string('b', 64),
            new ZoneId("zone:rig"),
            ZoneKind.RunnerRig);

    private static Dictionary<string, object?> Card(
        string id,
        IReadOnlyList<object?>? counters = null)
    {
        var card = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["definitionId"] = "unknown:card",
            ["rezzed"] = false,
            ["advancementCounters"] = 0,
        };
        if (counters is not null)
        {
            card["counters"] = counters;
        }

        return card;
    }

    private static Observation ObservationWithRunnerCards(
        IReadOnlyList<Dictionary<string, object?>> cards,
        IReadOnlyList<LegalAction>? legalActions = null,
        IReadOnlyList<Dictionary<string, object?>>? usedAbilityKeys = null,
        IReadOnlyList<Dictionary<string, object?>>? lastTurnUsedAbilityKeys = null)
    {
        Dictionary<string, object?> thisTurn = new()
        {
            ["side"] = "runner",
            ["turnNumber"] = 1,
            ["usedAbilityKeys"] = usedAbilityKeys?.Cast<object?>().ToArray() ?? Array.Empty<object?>(),
        };
        Dictionary<string, object?> lastTurn = new()
        {
            ["side"] = "runner",
            ["turnNumber"] = 0,
            ["usedAbilityKeys"] = lastTurnUsedAbilityKeys?.Cast<object?>().ToArray() ?? Array.Empty<object?>(),
        };
        return new Observation(
            PlayerSide.Runner,
            "",
            legalActions ?? [],
            new Dictionary<string, object?>
            {
                ["turn"] = new Dictionary<string, object?>
                {
                    ["activeSide"] = "runner",
                    ["phase"] = "runner",
                    ["turnNumber"] = 1,
                },
                ["turnFacts"] = new Dictionary<string, object?>
                {
                    ["corp"] = new Dictionary<string, object?>
                    {
                        ["thisTurn"] = null,
                        ["lastTurn"] = null,
                    },
                    ["runner"] = new Dictionary<string, object?>
                    {
                        ["thisTurn"] = thisTurn,
                        ["lastTurn"] = lastTurn,
                    },
                },
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
                ["servers"] = Array.Empty<object?>(),
                ["runnerRig"] = cards.Cast<object?>().ToArray(),
                ["corpScore"] = Array.Empty<object?>(),
                ["runnerScore"] = Array.Empty<object?>(),
                ["outcome"] = new Dictionary<string, object?>
                {
                    ["kind"] = "ongoing",
                    ["winner"] = null,
                    ["reason"] = null,
                },
            },
            new Dictionary<string, object?>
            {
                ["grip"] = Array.Empty<object?>(),
                ["stack"] = Array.Empty<object?>(),
            });
    }

    private static Dictionary<string, object?> Player(string id) => new()
    {
        ["id"] = id,
        ["credits"] = 5,
        ["clicks"] = 4,
        ["agendaPoints"] = 0,
    };
}
