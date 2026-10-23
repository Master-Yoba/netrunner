using Netrunner.Core;
using Netrunner.Match.Llm;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// The board and options as the LLM opponent reads them: every card it can act on reads apart from the others, and the
/// counters, hosts and progress it would otherwise guess from card text are on the board.
/// </summary>
public sealed class LlmBoardTextTests
{
    [Fact]
    public void TwoCopiesOfAnAgendaReadApart()
    {
        // Qwen advanced the unadvanced copy of Send a Message as the one it had scored two turns before.
        CardViewModel installed = Agenda("card:7"), scored = Agenda("card:3");
        MatchViewModel view = View(PlayerSide.Corp, [Server("remote-1", ServerKind.Remote, [], [installed])], rig: [], corpScore: [scored]);

        string board = LlmBoardText.Board(view);
        string? advance = HumanGameReview.Describer(view)(Action(new AdvanceCardParameters(new("card:7"), 0)));

        Assert.Contains("Remote 1: ICE [] root [Send a Message (face down), 0 of 5 advancements]", board, StringComparison.Ordinal);
        Assert.Contains("Score areas: Corp [Send a Message (3 points)]", board, StringComparison.Ordinal);
        Assert.Equal("Advance Send a Message in Remote 1 (0 of 5 advancements)", advance);
    }

    [Fact]
    public void HostsCountersAndAbilitiesAreNamed()
    {
        // HQ's outermost ICE is unrezzed; Core counts its position from the innermost.
        CardViewModel palisade = Card("card:5", "Palisade", "ICE") with { Rezzed = true };
        CardViewModel botulus = Card("card:20", "Botulus", "Program") with { HostCardId = "card:4", TypedCounters = [Virus(1)] };
        CardViewModel richer = Card("card:30", "Smartware Distributor", "Resource") with { TypedCounters = [Credits(2)] };
        CardViewModel poorer = Card("card:31", "Smartware Distributor", "Resource");
        CardViewModel redTeam = Card("card:32", "Red Team", "Resource") with { TypedCounters = [Credits(9)] };
        ServerViewModel hq = Server("hq", ServerKind.Headquarters, [null, palisade], []) with
        {
            IcePositions = [new("card:5", "hq", 0), new("card:4", "hq", 1)],
        };
        MatchViewModel view = View(PlayerSide.Runner, [hq], rig: [botulus, richer, poorer, redTeam], corpScore: []);
        Func<LegalAction?, string?> describe = HumanGameReview.Describer(view);

        string board = LlmBoardText.Board(view);

        Assert.Contains("HQ: ICE [unrezzed ICE hosting [Botulus (1 virus counter)], Palisade]", board, StringComparison.Ordinal);
        Assert.Contains("rig [Smartware Distributor (2 credits), Smartware Distributor, Red Team (9 credits)]", board, StringComparison.Ordinal);
        Assert.Equal("Install Tranquilizer on unrezzed ICE in HQ (ICE 1 of 2, outermost first)", describe(Action(Hosted("card:4"))));
        // An unrezzed host whose position the Runner wasn't sent.
        Assert.Equal("Install Tranquilizer on unrezzed ICE", describe(Action(Hosted("card:99"))));
        Assert.Equal("Use Red Team: run R&D", describe(Action(Ability("card:32", "red-team-run-rnd"))));
        Assert.Equal("Use Smartware Distributor (2 credits): place", describe(Action(Ability("card:30", "smartware-place"))));
        Assert.Equal("Trash Red Team (the Runner is tagged)", describe(Action(new TrashTaggedResourceParameters(new("card:32"), 1, 2))));
    }

    [Fact]
    public void ThePlayersFactsBeyondTheUsualAreOnTheBoard()
    {
        MatchViewModel view = View(PlayerSide.Runner, [], rig: [], corpScore: []) with
        {
            Corp = new("corp", PlayerSide.Corp, 5, 0, 0) { BadPublicity = 2 },
            Runner = new("runner", PlayerSide.Runner, 4, 4, 0) { CoreDamage = 1, UsedMemory = 2, MemoryLimit = 4 },
            RunnerHandSize = new(PlayerSide.Runner, 0, 4, null),
        };

        string board = LlmBoardText.Board(view);

        Assert.Contains("Corp: 5 credits, 0 clicks, 0 agenda points, 2 bad publicity;", board, StringComparison.Ordinal);
        Assert.Contains("0 tags, 1 core damage, memory 2 of 4 used, maximum hand size 4;", board, StringComparison.Ordinal);
    }

    private static CardViewModel Card(string id, string title, string type) =>
        new(id, "00000", title, type, true, false, 0, null, null, null, null, [], "");

    private static CardViewModel Agenda(string id) =>
        Card(id, "Send a Message", "Agenda") with { AgendaPoints = 3, AdvancementRequirement = 5 };

    private static CardCounterViewModel Virus(int count) => new("virus", CardCounterPurpose.Virus, count, null);

    private static CardCounterViewModel Credits(int count) => new("credit", CardCounterPurpose.HostedCredit, count, null);

    private static ServerViewModel Server(string id, ServerKind kind, CardViewModel?[] ice, CardViewModel?[] root) => new(id, kind, ice, root);

    private static InstallCardParameters Hosted(string host) =>
        new(new("card:21"), InstallDestinationKind.RunnerRig, null, false, 0, hostCardId: new(host));

    private static AbilityActionParameters Ability(string card, string key) =>
        new(new(card), new("00000"), key, BehaviorAbilityTiming.ActionPhase, BehaviorAbilityActivation.PaidAbility, [], [],
            new(BehaviorAbilityUsageLimit.Unlimited, 0), new string('0', 64), new string('0', 64), new("zone:rig"), ZoneKind.RunnerRig);

    private static LegalAction Action(ActionParameters parameters) => new(new("action:1"), ActionKind.Legacy, "", parameters);

    private static MatchViewModel View(PlayerSide viewer, ServerViewModel[] servers, CardViewModel[] rig, CardViewModel[] corpScore) =>
        new(viewer, new("corp", PlayerSide.Corp, 5, 3, 0), new("runner", PlayerSide.Runner, 4, 0, 0), servers,
            new(ZoneKind.CorpHand, 0, []), new(ZoneKind.CorpDeck, 30, []), new(ZoneKind.RunnerGrip, 0, []), new(ZoneKind.RunnerStack, 30, []),
            rig, corpScore, [], null, null, new(GameOutcomeKind.Ongoing, null, null), [])
        {
            // Tranquilizer, in the grip, has a name to install.
            RunnerHand = new(ZoneKind.RunnerGrip, 1, [Card("card:21", "Tranquilizer", "Program")]),
        };
}
