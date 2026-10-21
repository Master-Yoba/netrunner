using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class ActionLabelPresenterTests
{
    private static string Card(string id) => id switch
    {
        "card:1" => "Cleaver",
        "card:2" => "Carmen",
        "card:3" => "Spin Doctor",
        "card:4" => "Jailbreak",
        _ => "face-down card",
    };
    private static string Server(string id) => id == "server:1" ? "HQ" : "R&D";
    private static LegalActionViewModel Action(ActionKind kind, ActionParameters parameters) =>
        new(new("action:1"), kind, "Unhelpful generic description", parameters);
    private static string Label(LegalActionViewModel action, DecisionKind? decision = null,
        IReadOnlyList<LegalActionViewModel>? peers = null) =>
        ActionLabelPresenter.Label(action, decision, Card, Server, peers);

    [Fact]
    public void RunEventLabelsKeepCardCostAndDistinctServerTargets()
    {
        var hq = Action(ActionKind.PlayCard, new PlayCardParameters(new("card:4"), 0, new("server:1")));
        var rd = Action(ActionKind.PlayCard, new PlayCardParameters(new("card:4"), 0, new("server:2")));
        Assert.Equal("Play Jailbreak → Run HQ · 0 credits", Label(hq));
        Assert.Equal("Play Jailbreak → Run R&D · 0 credits", Label(rd));
    }

    [Fact]
    public void DiscardAndMemoryChoicesNameTheCardAndConsequence()
    {
        var first = Action(ActionKind.ChooseCard, new CardChoiceParameters(new("card:1")));
        var second = Action(ActionKind.ChooseCard, new CardChoiceParameters(new("card:2")));
        Assert.Equal("Discard Cleaver", Label(first, DecisionKind.TurnDiscard));
        Assert.Equal("Discard Carmen", Label(second, DecisionKind.TurnDiscard));
        Assert.Equal("Trash to free memory: Cleaver", Label(first, DecisionKind.MemoryCleanup));
    }

    [Fact]
    public void InstallLabelsPreserveHostAndServerZone()
    {
        var hosted = Action(ActionKind.InstallCard, new InstallCardParameters(new("card:1"),
            InstallDestinationKind.RunnerRig, null, false, 3, hostCardId: new("card:2")));
        Assert.Contains("Host: Carmen", Label(hosted), StringComparison.Ordinal);
        var ice = Action(ActionKind.InstallCard, new InstallCardParameters(new("card:1"),
            InstallDestinationKind.ServerIce, new("server:1"), false, 2));
        Assert.Contains("HQ · ICE", Label(ice), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wildcat-strike-effect", "gain", "Runner gains 6 credits")]
    [InlineData("wildcat-strike-effect", "draw", "Runner draws 4 cards")]
    [InlineData("predictive-planogram-single-effect", "gain", "Corp gains 3 credits")]
    [InlineData("predictive-planogram-both-order", "draw-first", "Corp draws 3 cards; then Corp gains 3 credits")]
    [InlineData("public-trail-tag-or-pay", "take-tag", "Runner takes 1 tag")]
    public void ModeOptionsExplainAmountRecipientAndOrderFromTheCatalog(string key, string option, string expected)
    {
        var parameters = new BehaviorChoiceParameters(key, option);
        var action = Action(ActionKind.ChooseBehavior, parameters) with
        {
            BehaviorChoice = EncounterChoicePresenter.ResolveChoiceMetadata(parameters, GatewayBehaviorCatalog.Create()),
        };
        Assert.Equal(expected, Label(action));
    }

    [Fact]
    public void CardSelectionLabelsSeparateAddingFinishingAndEmptySelection()
    {
        var add = Action(ActionKind.ChooseBehavior, new BehaviorChoiceParameters("selection", cardIds: [new("card:1")], finalizeSelection: false));
        var finish = Action(ActionKind.ChooseBehavior, new BehaviorChoiceParameters("selection", cardIds: [new("card:2")]));
        var empty = Action(ActionKind.ChooseBehavior, new BehaviorChoiceParameters("selection"));
        Assert.Equal("Add to selection: Cleaver", Label(add));
        Assert.Equal("Finish selection: Carmen", Label(finish, peers: [add, finish]));
        Assert.Equal("Continue without selecting cards", Label(empty, peers: [add, empty]));
        Assert.Equal("Continue without selecting cards", Label(empty));
    }

    [Fact]
    public void AbilityShowsSourceTargetsAndEveryAdditionalCost()
    {
        var ability = new AbilityActionParameters(new("card:3"), new("30053"), "spin-doctor-archives",
            BehaviorAbilityTiming.AnyPaidWindow, BehaviorAbilityActivation.PaidAbility,
            [new(BehaviorCostKind.RemoveSelfFromGame), new(BehaviorCostKind.Credits, 2)],
            [new("card:1")], new(BehaviorAbilityUsageLimit.OncePerTurn, 0), new string('a', 64), new string('b', 64),
            new("zone:1"), ZoneKind.ServerRoot);
        string label = Label(Action(ActionKind.UseAbility, ability));
        Assert.Contains("Spin Doctor", label, StringComparison.Ordinal);
        Assert.Contains("Target: Cleaver", label, StringComparison.Ordinal);
        Assert.Contains("Remove Spin Doctor from game + 2 credits", label, StringComparison.Ordinal);
        Assert.Contains("Once per turn", label, StringComparison.Ordinal);
        Assert.DoesNotContain("spin-doctor-archives", label, StringComparison.Ordinal);
    }

    [Fact]
    public void TriggerLabelsUseVisibleSourceAndNeverExposeInternalInvocationIds()
    {
        var use = Action(ActionKind.UseTrigger, new TriggerChoiceParameters(new("trigger:private"))) with
        { Source = new("card:3", "Spin Doctor", "spin-doctor-rez") };
        Assert.Contains("Spin Doctor", Label(use), StringComparison.Ordinal);
        Assert.DoesNotContain("trigger:", Label(use), StringComparison.Ordinal);
        Assert.Equal("Use triggered ability", Label(use with { Source = null }));
    }

    [Theory]
    [InlineData(ActionKind.UseTrigger)]
    [InlineData(ActionKind.DeclineTrigger)]
    [InlineData(ActionKind.OrderTriggers)]
    [InlineData(ActionKind.UsePrevention)]
    [InlineData(ActionKind.UseReplacement)]
    public void SameTitleTriggerAndInterruptSourcesUseDisambiguatedCardNames(ActionKind kind)
    {
        ActionParameters parameters = kind switch
        {
            ActionKind.OrderTriggers => new TriggerOrderParameters(new("trigger:1")),
            ActionKind.UsePrevention => new PreventionChoiceParameters(new("interrupt:1"), 1),
            ActionKind.UseReplacement => new ReplacementChoiceParameters(new("interrupt:1")),
            _ => new TriggerChoiceParameters(new("trigger:1")),
        };
        LegalActionViewModel first = Action(kind, parameters) with { Source = new("copy-a", "Daily Casts", "payout") };
        LegalActionViewModel second = first with { Source = first.Source! with { CardId = "copy-b" } };
        string Name(string id) => id == "copy-a" ? "Daily Casts (Rig 1)" : "Daily Casts (Rig 2)";
        Assert.Contains("Daily Casts (Rig 1)", ActionLabelPresenter.Label(first, null, Name, Server), StringComparison.Ordinal);
        Assert.Contains("Daily Casts (Rig 2)", ActionLabelPresenter.Label(second, null, Name, Server), StringComparison.Ordinal);
    }

    [Fact]
    public void PaidAbilityTagCostRemovesExistingTags()
    {
        var ability = new AbilityActionParameters(new("card:1"), new("test:tags"), "spend-tags",
            BehaviorAbilityTiming.AnyPaidWindow, BehaviorAbilityActivation.PaidAbility,
            [new(BehaviorCostKind.Tags, 2)], [], new(BehaviorAbilityUsageLimit.Unlimited, 0),
            new string('a', 64), new string('b', 64), new("zone:rig"), ZoneKind.RunnerRig);
        Assert.Contains("Cost: Remove 2 tags", Label(Action(ActionKind.UseAbility, ability)), StringComparison.Ordinal);
    }

    // VP-ACCESS-PAID-ABILITY (PR #154's fourth review): a cost of the accessed card's printed cost reads as what it resolved to.
    [Fact]
    public void PaidAbilityAccessedCardCostReadsAsItsResolvedCredits()
    {
        var ability = new AbilityActionParameters(new("card:1"), new("test:lampades"), "trash-accessed",
            BehaviorAbilityTiming.AnyPaidWindow, BehaviorAbilityActivation.PaidAbility,
            [new(BehaviorCostKind.Credits, 1), new(BehaviorCostKind.Credits, 0, accessedCardPrintedCost: true)], [],
            new(BehaviorAbilityUsageLimit.Unlimited, 0), new string('a', 64), new string('b', 64), new("zone:rig"), ZoneKind.RunnerRig,
            resolvedCreditCost: 4);
        Assert.Contains("Cost: 1 credit + 3 credits", Label(Action(ActionKind.UseAbility, ability)), StringComparison.Ordinal);
    }

    [Fact]
    public void MapperRetainsKnownTriggerAndReplacementSourcesWithoutChangingIssuedParameters()
    {
        Observation baseline = ObservationBuilder.Build(GameState.CreateNew(219), PlayerSide.Runner);
        var parameters = new ReplacementChoiceParameters(new("interrupt:1"));
        Observation observation = baseline with
        {
            LegalActions = [new(new("action:1"), ActionKind.UseReplacement, "Use replacement", parameters)],
        };
        LegalActionViewModel action = Assert.Single(ObservationViewMapper.Build(observation,
            CardMetadataResolvers.Default, GatewayBehaviorCatalog.Create(), handSizeProjection: null,
            presentationContext: MatchPresentationContext.Empty with
            { ActionSources = [new(new("action:1"), new("card:1"), new("test:known"), "first-draw", 2)] }).LegalActions);
        Assert.NotNull(action.Source);
        Assert.Equal("Draw 2 cards instead", action.Source.EffectLabel);
        Assert.Same(parameters, action.Parameters);
        Assert.Contains("Draw 2 cards instead", Label(action), StringComparison.Ordinal);
    }

    [Fact]
    public void MapperDescribesPaidAbilityFromItsCatalogDeclaration()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        Assert.True(catalog.TryGet(GatewayBatch2CardCodes.TeleworkContract, out CardBehaviorDefinition? definition));
        BehaviorPaidAbility declared = Assert.Single(definition.PaidAbilities);
        var parameters = new AbilityActionParameters(new("card:3"), definition.DefinitionId, declared.Key,
            declared.Timing, declared.Activation, declared.Costs, [],
            new(BehaviorAbilityUsageLimit.OncePerTurn, 0), new string('a', 64), new string('b', 64),
            new("zone:1"), ZoneKind.RunnerRig);
        Observation observation = ObservationBuilder.Build(GameState.CreateNew(219), PlayerSide.Runner) with
        {
            LegalActions = [new(new("action:1"), ActionKind.UseAbility, "Use ability", parameters)],
        };
        LegalActionViewModel action = Assert.Single(ObservationViewMapper.Build(observation,
            CardMetadataResolvers.Default, catalog).LegalActions);
        Assert.Equal("Take 3 hosted credits", action.EffectLabel);
        Assert.Contains("Cost: 1 click · Once per turn", Label(action), StringComparison.Ordinal);
        Assert.Same(parameters, action.Parameters);
    }

    [Fact]
    public void AdditionalTrashCostNamesItsCardsAndUnknownTargetsStayHidden()
    {
        var ability = new AbilityActionParameters(new("card:3"), new("30007"), "carnivore-access",
            BehaviorAbilityTiming.BreachAccessPaidWindow, BehaviorAbilityActivation.PaidAbility,
            [new(BehaviorCostKind.Additional, 2, additionalKey: "trash-cards", additionalKind: BehaviorAdditionalCostKind.TrashCards,
                additionalTarget: BehaviorTarget.CardsInZone(BehaviorZone.Grip, new BehaviorCardFilter(ownerSide: PlayerSide.Runner)))],
            [new("card:hidden")], new(BehaviorAbilityUsageLimit.OncePerTurn, 0), new string('a', 64), new string('b', 64),
            new("zone:1"), ZoneKind.RunnerRig, [new(0, "trash-cards", [new("card:1"), new("card:2")])]);
        string label = Label(Action(ActionKind.UseAbility, ability));
        Assert.Contains("Cost: Trash Cleaver, Carmen", label, StringComparison.Ordinal);
        Assert.Contains("Target: face-down card", label, StringComparison.Ordinal);
        Assert.DoesNotContain("card:hidden", label, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessContinuationDoesNotPromiseToSkipAnAutomaticSteal()
    {
        Assert.Equal("Continue access", Label(Action(ActionKind.NoAccessAction, new NoActionParameters())));
    }

    [Fact]
    public void BreakerPaymentsKeepTheExactSplitAndActionIsUnchanged()
    {
        var action = Action(ActionKind.BreakSubroutines, new RunBreakParameters(new("card:1"), 2, hostedCredits: 1)) with
        { Description = "Break 2 subroutines with card:1 (1 hosted + 1 personal credit)" };
        string before = EngineJson.Serialize(action.Parameters);
        Assert.Equal(action.Description, Label(action));
        Assert.Equal(before, EngineJson.Serialize(action.Parameters));
    }

    [Fact]
    public void StandardCostsAndScoringRewardsStayVisible()
    {
        Assert.Equal("Purge all virus counters · 3 clicks", Label(Action(ActionKind.PurgeVirusCounters, new PurgeVirusCountersParameters(3))));
        Assert.Equal("Advance Cleaver · 1 click + 2 credits", Label(Action(ActionKind.AdvanceCard, new AdvanceCardParameters(new("card:1"), 2))));
        Assert.Equal("Score Cleaver · 2 agenda points", Label(Action(ActionKind.ScoreAgenda, new ScoreAgendaParameters(new("card:1"), 2, 3))));
    }
}
