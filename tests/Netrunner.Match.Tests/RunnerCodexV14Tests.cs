using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>
/// Codex reviews of Runner v13, 1 October, in saved ladder games of the current bots:
/// - it declined Zahya Sadeghi's income after accessing cards on HQ, though broke (the trigger scored 0);
/// - it ran HQ through a rezzed Diviner holding only odd-cost cards, where the run ends for certain;
/// - it ran HQ with no credits through a rezzed Ansel 1.0, which trashes a program and forbids stealing.
/// Reviews of Runner v14's losses to Codex: it left the remote alone with the Corp one agenda from winning, and never
/// installed Fermenter, whose counters it didn't count as income, and ran an Archives that could gain it nothing.
/// </summary>
public sealed class RunnerCodexV14Tests
{
    [Fact]
    public async Task ZahyaSIncomeIsTaken()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // v13 scored using and declining the same, 0, so its choice was a coin flip (it declined in the game).
        LegalAction chosen = await ChooseAtAsync("codex-runner-zahya", RunnerHeuristicPolicy.V14, 103);
        Assert.True(chosen.Kind == ActionKind.UseTrigger, $"v14 chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V13, true)]
    [InlineData(RunnerHeuristicPolicy.V14, false)]
    public async Task NoRunIntoADivinerThatEndsTheRunForCertain(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("codex-runner-futile-runs", version, 186);
        Assert.True(runs == chosen.Parameters is RunServerParameters { ServerId.Value: "server:4" }, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V13, true)]
    [InlineData(RunnerHeuristicPolicy.V14, false)]
    public async Task NoRunPastAnAnselThatForbidsStealing(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        LegalAction chosen = await ChooseAtAsync("codex-runner-futile-runs", version, 821);
        Assert.True(runs == chosen.Parameters is RunServerParameters { ServerId.Value: "server:4" }, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V14, "codex-runner-match-point-027", 545, false)]
    [InlineData(RunnerHeuristicPolicy.V15, "codex-runner-match-point-027", 545, true)]
    public async Task TheRemoteIsContestedAtTheCorpSMatchPoint(string version, string fixture, long sequence, bool contests)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October): the Corp on 5 points with Send a Message on four counters in a remote, which v14
        // left alone for fear of a lethal trap; the Corp scored it to win.
        (LegalAction chosen, GameState state) = await ChooseInAsync(fixture, version, sequence);
        bool remote = chosen.Parameters is RunServerParameters run && state.Servers.Single(server => server.Id == run.ServerId).Kind == ServerKind.Remote;
        Assert.True(contests == remote, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V14, false)]
    [InlineData(RunnerHeuristicPolicy.V15, true)]
    public async Task FermenterIsInstalledForItsGrowingIncome(string version, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October): with 1 credit and Fermenter in the grip, v14 clicked for credits all turn.
        (LegalAction chosen, GameState state) = await ChooseInAsync("codex-runner-fermenter", version, 115);
        bool fermenter = chosen.Parameters is InstallCardParameters install
            && CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(install.CardId).DefinitionId.Value).Title == "Fermenter";
        Assert.True(installs == fermenter, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V14, true)]
    [InlineData(RunnerHeuristicPolicy.V15, false)]
    public async Task NoRunOnAnArchivesThatCanGainNothing(string version, bool runs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October): Archives held only operations the Runner had seen, and the run's one reward
        // was the install discount on a second Carmen.
        LegalAction chosen = await ChooseAtAsync("codex-runner-archives", version, 443);
        Assert.True(runs == chosen.Parameters is RunServerParameters { ServerId.Value: "server:10" }, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V15, "codex-runner-unique-reinstall", 281, "Verbal Plasticity", true)]
    [InlineData(RunnerHeuristicPolicy.V16, "codex-runner-unique-reinstall", 281, "Verbal Plasticity", false)]
    [InlineData(RunnerHeuristicPolicy.V17, "codex-runner-unique-reinstall", 281, "Verbal Plasticity", false)]
    [InlineData(RunnerHeuristicPolicy.V18, "codex-runner-unique-reinstall", 281, "Verbal Plasticity", false)]
    [InlineData(RunnerHeuristicPolicy.V15, "codex-runner-docklands", 379, "Docklands Pass", false)]
    [InlineData(RunnerHeuristicPolicy.V16, "codex-runner-docklands", 379, "Docklands Pass", true)]
    public async Task InstallsThatAddSomething(string version, string fixture, long sequence, string title, bool installs)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // Games against Codex (1 October, batch 3): a second Verbal Plasticity over the first, and Docklands Pass kept in
        // the grip for an HQ run.
        (LegalAction chosen, GameState state) = await ChooseInAsync(fixture, version, sequence);
        bool installed = chosen.Parameters is InstallCardParameters install
            && CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(install.CardId).DefinitionId.Value).Title == title;
        Assert.True(installs == installed, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Fact]
    public async Task NoInstallOverItsOwnCopy()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): Pantograph's trigger installed a second Pantograph over the first. v16
        // scored it level with DZMZ Optimizer, so its pick was a coin flip; v17 never picks it.
        (LegalAction chosen, GameState state) = await ChooseInAsync("codex-runner-console-reinstall", RunnerHeuristicPolicy.V18, 488);
        bool pantograph = chosen.Parameters is BehaviorChoiceParameters choice && choice.CardIds.Any(id =>
            CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(id).DefinitionId.Value).Title == "Pantograph");
        Assert.False(pantograph, $"v18 chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V15, true)]
    [InlineData(RunnerHeuristicPolicy.V16, false)]
    public async Task CreativeCommissionWaitsForTheLastClick(string version, bool plays)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 3): played with two clicks left, losing one.
        (LegalAction chosen, GameState state) = await ChooseInAsync("codex-runner-commission", version, 30);
        bool commission = chosen.Parameters is PlayCardParameters play
            && CardDataResource.LoadEmbedded().Cards.Single(card => card.Code == state.GetCard(play.CardId).DefinitionId.Value).Title == "Creative Commission";
        Assert.True(plays == commission, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V18, false)]
    [InlineData(RunnerHeuristicPolicy.V19, true)]
    public async Task AHandWithEconomyAndATutorIsKept(string version, bool keeps)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): Sure Gamble, Jailbreak, Mutual Favor and Pennyshaver went back for a
        // missing breaker that Mutual Favor finds.
        LegalAction chosen = await ChooseAtAsync("codex-runner-economy", version, 2);
        bool kept = chosen.Parameters is NumberChoiceParameters { Value: 0 };
        Assert.True(keeps == kept, $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    [Theory]
    [InlineData(RunnerHeuristicPolicy.V18, false)]
    [InlineData(RunnerHeuristicPolicy.V19, true)]
    public async Task PennyshaverIsCashedOutOverABasicCredit(string version, bool cashes)
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        // A game against Codex (1 October, batch 4): a basic credit over Pennyshaver's 2.
        LegalAction chosen = await ChooseAtAsync("codex-runner-economy", version, 219);
        Assert.True(cashes == (chosen.Kind == ActionKind.UseAbility), $"{version} chose {chosen.Kind} {chosen.Description}.");
    }

    private static async Task<LegalAction> ChooseAtAsync(string fixture, string version, long sequence) =>
        (await ChooseInAsync(fixture, version, sequence)).Chosen;

    /// <summary>The Runner version's choice at the recorded decision <paramref name="sequence"/>, having watched the game to it.</summary>
    private static async Task<(LegalAction Chosen, GameState State)> ChooseInAsync(string fixture, string version, long sequence)
    {
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "Fixtures", "saved-games", fixture)));
        GameState state = saved.Replay.InitialSnapshot.ToState();
        OfflineAgent agent = OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp, "standard", version);
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            if (recorded.Sequence == sequence)
            {
                PendingDecision pending = state.PendingDecision!;
                ActionId id = await agent.ChooseActionAsync(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime));
                return (pending.LegalActions.Single(action => action.Id == id), state);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        throw new InvalidOperationException($"The game has no action {sequence}.");
    }
}
