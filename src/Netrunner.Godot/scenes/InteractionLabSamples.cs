using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Isolated presentation fixtures, not a rules engine or saved match.</summary>
public static partial class InteractionLabSamples
{
	public static readonly string[] Names = ["Runner: event", "Runner: run event", "Runner: rig install", "Runner: hosted install",
		"Corp: operation", "Corp: root install", "Corp: ICE install", "Corp: new remote", "Corp: replacement",
		"Runner: memory cleanup", "Runner: card choice", "Runner: non-spatial choice", "Dense board: scrolling", "Runner: pile card choice", "Runner: multiple targets", "Runner: hand and board actions", "Corp: hand and board actions", "Runner: discard from hand", "Corp: discard from hand", "Runner: icebreakers"];
	public static readonly string[] Instructions = [
		"Drag the event from hand into the board. It should play immediately.",
		"Drag onto a central server, or into general space then click a highlighted server.",
		"Drag the program into the rig. Dropping it in a server should return it to hand.",
		"Drag the program onto the highlighted ICE host. The rig is not a valid destination.",
		"Drag the operation into the board to play it.",
		"Drag the upgrade to an existing server root. ICE regions are separate targets.",
		"Drag ICE to the outermost ICE area of an existing server.",
		"Drag the asset to New remote at the right of the server row. Scroll if needed.",
		"Drag the asset into Remote 1, then select the required replacement and finish installing.",
		"Drag the program into the rig, then choose a program to trash for memory.",
		"Play the event, then choose its highlighted card target on the board.",
		"Play the event, then choose a non-spatial option in the contextual panel.",
		"Drag a hand card while scrolling near board edges. Escape, right-click, or focus loss cancels.",
		"Play the event, then select the displayed card from the heap choice browser.",
		"Play the event, click a highlighted program, then explicitly choose one target pair.",
		"Hover the hand. Click credits or Stack, browse Archives, or choose Run and select a server. Reset to repeat.",
		"Hover the hand. Click credits or R&D, or click Archives to browse. Reset to repeat.",
		"Drag two cards from hand to Heap. Escape or an invalid drop cancels the gesture. Reset to repeat.",
		"Drag two cards from hand to Archives. Selected cards wait for the complete discard choice. Reset to repeat.",
		"Click Practice Fracter in the rig. Boost its strength, choose Break, and select subroutines beside the ICE. Choose a payment; Escape cancels. Reset to repeat."];

	public static MatchViewModel Create(int scenario, long revision)
	{
		if (scenario == 19) return IcebreakerEncounter(revision);
		PlayerSide side = scenario is >= 4 and <= 8 or 16 or 18 ? PlayerSide.Corp : PlayerSide.Runner;
		var view = UiPreviewSamples.Board(side, scenario == 12 ? UiPreviewSamples.Density.Dense : UiPreviewSamples.Density.Normal);
		if (scenario is 17 or 18)
		{
			CardViewModel[] discardHand = Enumerable.Range(0, 7).Select(i => UiPreviewSamples.Known($"lab-discard-{i}",
				side == PlayerSide.Corp ? "Operation" : "Event", $"Practice discard {i + 1}")).ToArray();
			view = side == PlayerSide.Corp ? view with { CorpHand = new(ZoneKind.CorpHand, 7, discardHand) }
				: view with { RunnerHand = new(ZoneKind.RunnerGrip, 7, discardHand) };
			return view with
			{
				Decision = Decision(side, revision, DecisionKind.TurnDiscard, "Choose 2 cards to discard"),
				LegalActions = discardHand.Select(card => Action("discard-" + card.Id, ActionKind.ChooseCard, new CardChoiceParameters(new(card.Id)), revision)).ToArray(),
				ForcedDiscard = new(side, "Choose 2 cards to discard", new(side, 7, 5, null)),
				CorpDiscard = [],
				CorpDiscardCount = 0,
				RunnerHeap = [],
			};
		}
		view = view with { RunnerRig = view.RunnerRig.Select((card, index) => card with { Title = $"Practice program {index + 1}" }).ToArray() };
		string type = scenario switch { 2 or 3 or 9 => "Program", 5 => "Upgrade", 6 => "ICE", 7 or 8 => "Asset", 4 or 16 => "Operation", _ => "Event" };
		CardViewModel card = UiPreviewSamples.Known("lab-hand", type, "Practice " + type);
		CardViewModel[] hand = scenario == 12 ? [card, .. view.RunnerHand.VisibleCards] : [card];
		view = side == PlayerSide.Runner ? view with { RunnerHand = new(ZoneKind.RunnerGrip, hand.Length, hand) }
			: view with { CorpHand = new(ZoneKind.CorpHand, hand.Length, hand) };
		var actions = new List<LegalActionViewModel>();
		if (scenario >= 15)
		{
			CardViewModel[] practiceHand = Enumerable.Range(0, 5).Select(i => UiPreviewSamples.Known($"lab-fan-{i}", type, $"Practice card {i + 1}")).ToArray();
			view = side == PlayerSide.Runner ? view with { RunnerHand = new(ZoneKind.RunnerGrip, 5, practiceHand) }
				: view with { CorpHand = new(ZoneKind.CorpHand, 5, practiceHand) };
			actions.Add(Action("credit", ActionKind.GainCredit, new NoActionParameters(), revision));
			actions.Add(Action("draw", ActionKind.DrawCard, new NoActionParameters(), revision));
			if (side == PlayerSide.Runner)
				foreach (var server in view.Servers.Where(server => server.Id != "preview-remote-1"))
					actions.Add(Action("run-" + server.Id, ActionKind.StartRun, new RunServerParameters(new(server.Id)), revision));
		}
		if (scenario is 0 or 4 or 10 or 11 or 12 or 13 or 14) actions.Add(Action("play", ActionKind.PlayCard, new PlayCardParameters(new(card.Id), 3), revision));
		else if (scenario == 1)
			foreach (var server in view.Servers) actions.Add(Action("play-" + server.Id, ActionKind.PlayCard, new PlayCardParameters(new(card.Id), 3, new(server.Id)), revision));
		else if (scenario is 2 or 9) actions.Add(Action("rig", ActionKind.InstallCard, new InstallCardParameters(new(card.Id), InstallDestinationKind.RunnerRig, null, false, 3), revision));
		else if (scenario == 3) actions.Add(Action("host", ActionKind.InstallCard, new InstallCardParameters(new(card.Id), InstallDestinationKind.RunnerRig, null, false, 3,
			hostCardId: new("preview-ice-Archives")), revision));
		else if (scenario is 5 or 6)
			foreach (var server in view.Servers) actions.Add(Action("install-" + server.Id, ActionKind.InstallCard,
				new InstallCardParameters(new(card.Id), scenario == 6 ? InstallDestinationKind.ServerIce : InstallDestinationKind.ServerRoot, new(server.Id), false, 3), revision));
		else if (scenario == 7) actions.Add(Action("remote", ActionKind.InstallCard, new InstallCardParameters(new(card.Id), InstallDestinationKind.ServerRoot, null, true, 3), revision));
		else if (scenario == 8) actions.Add(Action("replace", ActionKind.InstallCard, new InstallCardParameters(new(card.Id), InstallDestinationKind.ServerRoot,
			new("preview-remote-0"), false, 3, [new("preview-root-0")], [new("preview-root-0")], [], false), revision));
		return view with { Decision = Decision(side, revision, DecisionKind.TurnAction, Instructions[scenario]), LegalActions = actions };
	}

	public static DecisionViewModel Decision(PlayerSide side, long revision, DecisionKind kind, string prompt) =>
		new($"lab-decision-{revision}", revision, new(side == PlayerSide.Corp ? "preview-corp" : "preview-runner"), side, kind, prompt);
	public static LegalActionViewModel Action(string suffix, ActionKind kind, ActionParameters parameters, long revision) =>
		new(new($"lab-action-{revision}-{suffix}"), kind, suffix.Replace('-', ' '), parameters);

	/// <summary>Give known practice cards artwork keys without requiring official metadata or changing scenario actions.</summary>
	public static MatchViewModel WithArtwork(MatchViewModel view, int scenario)
	{
		CardViewModel Map(CardViewModel card, bool corp = false)
		{
			if (!card.IsKnown || card.DefinitionId is not null) return card;
			string? code = card.Type switch
			{
				"Event" => scenario == 1 && card.Id == "lab-hand" ? "30028" : "30030",
				"Operation" => "30075",
				"Program" => scenario == 3 && card.Id == "lab-hand" ? "30004" : "30026",
				"ICE" => "30038",
				"Asset" => "30037",
				"Upgrade" => "30042",
				"Agenda" => "30067",
				"Hardware" => "30031",
				"Resource" => "30027",
				"Identity" => corp ? "30035" : "30019",
				_ => null,
			};
			return card with { DefinitionId = code };
		}
		CardViewModel? Slot(CardViewModel? card, bool corp = false) => card is null ? null : Map(card, corp);
		CardViewModel[] Cards(IReadOnlyList<CardViewModel> cards, bool corp = false) => cards.Select(card => Map(card, corp)).ToArray();
		ZoneViewModel Zone(ZoneViewModel zone, bool corp = false) => zone with { VisibleCards = Cards(zone.VisibleCards, corp) };
		return view with
		{
			CorpHand = Zone(view.CorpHand, true),
			CorpDeck = Zone(view.CorpDeck, true),
			RunnerHand = Zone(view.RunnerHand),
			RunnerDeck = Zone(view.RunnerDeck),
			CorpIdentity = Slot(view.CorpIdentity, true),
			RunnerIdentity = Slot(view.RunnerIdentity),
			CorpScore = Cards(view.CorpScore, true),
			RunnerScore = Cards(view.RunnerScore),
			CorpDiscard = Cards(view.CorpDiscard, true),
			RunnerHeap = Cards(view.RunnerHeap),
			RunnerRig = Cards(view.RunnerRig),
			Servers = view.Servers.Select(server => server with
			{
				Ice = server.Ice.Select(card => Slot(card, true)).ToArray(),
				Root = server.Root.Select(card => Slot(card, true)).ToArray(),
			}).ToArray(),
		};
	}
}
