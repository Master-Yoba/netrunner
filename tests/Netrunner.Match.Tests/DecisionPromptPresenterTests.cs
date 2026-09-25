using System.Text.RegularExpressions;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class DecisionPromptPresenterTests
{
    private const string CorePrompt = "CR 6.9.3b: encounter paid window. card:17 server:4";

    [Fact]
    public void EveryDecisionKindHasPlayerWordingWithoutEngineText()
    {
        foreach (DecisionKind kind in Enum.GetValues<DecisionKind>())
        {
            foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                string prompt = DecisionPromptPresenter.Prompt(Decision(kind, side), View(side, run: null));

                AssertPlayerText(prompt);
            }
        }
    }

    [Fact]
    public void EveryRunStepHasPlayerWordingWithoutEngineText()
    {
        foreach (RunTimingStep step in Enum.GetValues<RunTimingStep>())
        {
            foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                RunFocusViewModel run = Run(step, Ice("Whitespace", known: true));
                MatchViewModel view = View(side, run);

                AssertPlayerText(DecisionPromptPresenter.Prompt(Decision(DecisionKind.RunTiming, side), view));
                AssertPlayerText(DecisionPromptPresenter.RunContext(run, _ => "Remote 1"));
            }
        }
    }

    [Fact]
    public void EncounterPromptNamesVisibleIce()
    {
        RunFocusViewModel run = Run(RunTimingStep.EncounterPaidWindow, Ice("Whitespace", known: true));

        Assert.Equal("Run on Remote 1 · encountering Whitespace", DecisionPromptPresenter.RunContext(run, _ => "Remote 1"));
        Assert.Equal("Whitespace blocks the way — break its subroutines, or let them fire.",
            DecisionPromptPresenter.Prompt(Decision(DecisionKind.RunTiming, PlayerSide.Runner), View(PlayerSide.Runner, run)));
    }

    [Fact]
    public void HiddenIceIsNeverNamed()
    {
        RunFocusViewModel run = Run(RunTimingStep.ApproachPaidWindow, Ice("Secret Title", known: false)) with
        {
            Priority = new RunPriorityViewModel(RunPriorityWindowKind.ApproachPaid, PlayerSide.Corp, 0, true, false),
        };

        string context = DecisionPromptPresenter.RunContext(run, _ => "HQ");
        string prompt = DecisionPromptPresenter.Prompt(Decision(DecisionKind.RunTiming, PlayerSide.Corp), View(PlayerSide.Corp, run));

        Assert.Equal("Run on HQ · approaching a piece of ICE", context);
        Assert.DoesNotContain("Secret Title", prompt);
        Assert.Contains("rez it, or pass", prompt);
    }

    private static void AssertPlayerText(string text)
    {
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("CR ", text);
        Assert.DoesNotContain("card:", text);
        Assert.DoesNotContain("server:", text);
        // Enum identifiers such as EncounterPaidWindow are not player language.
        Assert.DoesNotMatch(new Regex("[a-z][A-Z]"), text);
    }

    private static DecisionViewModel Decision(DecisionKind kind, PlayerSide side) => new(
        "decision:1", 1, new PlayerId(side == PlayerSide.Corp ? "player:corp" : "player:runner"), side, kind, CorePrompt);

    private static CardViewModel Ice(string title, bool known) => new(
        "card:17", known ? "definition:ice" : null, known ? title : "Unknown", "ICE", known,
        Rezzed: known, AdvancementCounters: 0, Cost: null, Strength: null, AgendaPoints: null, TrashCost: null,
        Subtypes: [], RulesText: "");

    private static RunFocusViewModel Run(RunTimingStep step, CardViewModel ice) => new(
        "run:1", "server:4", PlayerSide.Runner, RunCrPhase.Approach, step, 0, ice, false, false, null, null,
        step is RunTimingStep.EncounterPaidWindow or RunTimingStep.Encounter or RunTimingStep.EncounterInitialization
            ? new EncounterViewModel("encounter:1", ice, RunEncounterStatus.Initialized, [])
            : null,
        new AccessViewModel(Array.Empty<CardViewModel?>(), 0, null, Array.Empty<CardViewModel?>(),
            Array.Empty<CardViewModel?>(), RunAccessState.NotStarted),
        0, null);

    private static MatchViewModel View(PlayerSide viewer, RunFocusViewModel? run) => new(
        viewer,
        new PlayerViewModel("player:corp", PlayerSide.Corp, 5, 3, 0),
        new PlayerViewModel("player:runner", PlayerSide.Runner, 5, 4, 0),
        Array.Empty<ServerViewModel>(),
        new ZoneViewModel(ZoneKind.CorpHand, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.CorpDeck, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerGrip, 0, Array.Empty<CardViewModel>()),
        new ZoneViewModel(ZoneKind.RunnerStack, 0, Array.Empty<CardViewModel>()),
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        Array.Empty<CardViewModel>(),
        null,
        run,
        new OutcomeViewModel(GameOutcomeKind.Ongoing, null, null),
        Array.Empty<LegalActionViewModel>());
}
