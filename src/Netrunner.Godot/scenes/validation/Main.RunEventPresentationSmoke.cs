using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Detached Slice 4.5 UI proof. It sends typed Match view values through the
/// real action, server-card, and run-focus controls without constructing or
/// mutating authoritative Core state.
/// </summary>
public partial class Main
{
	private async Task RunSlice45UiSmokeAsync()
	{
		bool prominentOptions = false;
		bool actingPlayer = false;
		bool issuedOrder = false;
		bool typedCost = false;
		bool typedFallback = false;
		bool hiddenSafeInstalledCard = false;
		bool activeRestriction = false;
		bool legalBioroid = false;
		bool armedMayfly = false;
		try
		{
			_board.Visible = true;
			CardViewModel encounteredIce = new(
				"card:slice45-encounter-ice",
				"synthetic:slice45-encounter-ice",
				"Synthetic ICE",
				"ICE",
				IsKnown: true,
				Rezzed: true,
				AdvancementCounters: 0,
				Cost: 4,
				Strength: 4,
				AgendaPoints: null,
				TrashCost: null,
				Subtypes: ["Sentry"],
				RulesText: string.Empty);
			EncounterViewModel encounter = new(
				"encounter:slice45-ui",
				encounteredIce,
				RunEncounterStatus.Initialized,
				[new SubroutineViewModel(0, Broken: false, Resolved: false)]);
			RunFocusViewModel run = new(
				"run:slice45-ui",
				"server:remote",
				PlayerSide.Runner,
				RunCrPhase.Encounter,
				RunTimingStep.EncounterPaidWindow,
				CurrentIceIndex: 0,
				CurrentIce: encounteredIce,
				Successful: false,
				Unsuccessful: false,
				EndReason: null,
				Priority: null,
				Encounter: encounter,
				Access: new AccessViewModel(
					Candidates: Array.Empty<CardViewModel?>(),
					RandomAccessLimit: 0,
					HiddenCandidateSource: null,
					Chosen: Array.Empty<CardViewModel?>(),
					Accessed: Array.Empty<CardViewModel?>(),
					State: RunAccessState.NotStarted),
				RunCredits: 0,
				InitiatingSource: null);

			LegalActionViewModel payCredits = new(
				new ActionId("action:slice45-pay"),
				ActionKind.ResolveIceSubroutine,
				"description is not a presentation source",
				new IceSubroutineChoiceParameters(
					"frame:slice45-ui",
					IceSubroutineChoiceKind.PayCredits,
					creditCost: 5,
					hostedCredits: 2));
			LegalActionViewModel takeTag = new(
				new ActionId("action:slice45-take-tag"),
				ActionKind.ResolveIceSubroutine,
				"description is not a fallback source",
				new IceSubroutineChoiceParameters(
					"frame:slice45-ui",
					IceSubroutineChoiceKind.TakeTag));
			MatchViewModel runnerView = CreateSlice45View(
				run,
				PlayerSide.Runner,
				[payCredits, takeTag],
				includeCues: true);
			MatchSnapshot runnerSnapshot = CreateSlice45Snapshot(runnerView);
			RenderServers(runnerView.Servers);
			RenderFocusPresentationRegion(runnerSnapshot);
			RenderActions(runnerSnapshot);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			prominentOptions = SceneTreeContainsText(_actions, "CHOICE OPTIONS");
			actingPlayer = SceneTreeContainsText(_actions, "ACTING PLAYER RUNNER")
				&& SceneTreeContainsText(_focusPresentation, "ACTING PLAYER RUNNER");
			issuedOrder = SceneTreeContainsText(_actions, "OPTION 1")
				&& SceneTreeContainsText(_actions, "OPTION 2");
			typedCost = SceneTreeContainsText(_actions, "5 credits (hosted 2 credits + personal 3 credits)");
			typedFallback = SceneTreeContainsText(_actions, "OPTION 2")
				&& SceneTreeContainsText(_actions, "FALLBACK");
			hiddenSafeInstalledCard = SceneTreeContainsText(_servers, "UNKNOWN SLOT")
				&& SceneTreeContainsText(_servers, "Hidden information")
				&& !SceneTreeContainsText(_servers, "card:hidden-installed")
				&& !SceneTreeContainsText(_servers, "Secret installed card");
			activeRestriction = SceneTreeContainsText(
				_focusPresentation,
				"RUN RESTRICTION ACTIVE · NO STEAL / NO TRASH CORP CARDS");
			legalBioroid = SceneTreeContainsText(_focusPresentation, "BIOROID ABILITY · LEGAL");
			armedMayfly = SceneTreeContainsText(_focusPresentation, "MAYFLY RUN-END EFFECT · ARMED");

			bool passed = prominentOptions
				&& actingPlayer
				&& issuedOrder
				&& typedCost
				&& typedFallback
				&& hiddenSafeInstalledCard
				&& activeRestriction
				&& legalBioroid
				&& armedMayfly;
			GD.Print(
				$"M4_SLICE45_UI {(passed ? "PASS" : "FAIL")} "
				+ $"prominent_options={FormatSlice45Bool(prominentOptions)} "
				+ $"acting_player={FormatSlice45Bool(actingPlayer)} "
				+ $"issued_order={FormatSlice45Bool(issuedOrder)} "
				+ $"typed_cost={FormatSlice45Bool(typedCost)} "
				+ $"typed_fallback={FormatSlice45Bool(typedFallback)} "
				+ $"hidden_safe_installed_card={FormatSlice45Bool(hiddenSafeInstalledCard)} "
				+ $"active_restriction={FormatSlice45Bool(activeRestriction)} "
				+ $"legal_bioroid={FormatSlice45Bool(legalBioroid)} "
				+ $"armed_mayfly={FormatSlice45Bool(armedMayfly)}");
			GetTree().Quit(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			string error = exception.Message.Replace('\n', ' ').Replace('\r', ' ');
			GD.Print(
				$"M4_SLICE45_UI FAIL prominent_options={FormatSlice45Bool(prominentOptions)} "
				+ $"acting_player={FormatSlice45Bool(actingPlayer)} "
				+ $"issued_order={FormatSlice45Bool(issuedOrder)} "
				+ $"typed_cost={FormatSlice45Bool(typedCost)} "
				+ $"typed_fallback={FormatSlice45Bool(typedFallback)} "
				+ $"hidden_safe_installed_card={FormatSlice45Bool(hiddenSafeInstalledCard)} "
				+ $"active_restriction={FormatSlice45Bool(activeRestriction)} "
				+ $"legal_bioroid={FormatSlice45Bool(legalBioroid)} "
				+ $"armed_mayfly={FormatSlice45Bool(armedMayfly)} error={error}");
			GetTree().Quit(1);
		}
	}

	private static MatchViewModel CreateSlice45View(
		RunFocusViewModel run,
		PlayerSide owner,
		IReadOnlyList<LegalActionViewModel> actions,
		bool includeCues)
	{
		string ownerId = owner == PlayerSide.Corp ? "player:corp" : "player:runner";
		return new MatchViewModel(
			Viewer: PlayerSide.Runner,
			Corp: new PlayerViewModel("player:corp", PlayerSide.Corp, Credits: 5, Clicks: 3, AgendaPoints: 0),
			Runner: new PlayerViewModel("player:runner", PlayerSide.Runner, Credits: 8, Clicks: 4, AgendaPoints: 0),
			Servers:
			[
				new ServerViewModel(
					"server:remote",
					ServerKind.Remote,
					new CardViewModel?[] { null },
					new CardViewModel?[] { null }),
			],
			CorpHand: new ZoneViewModel(ZoneKind.CorpHand, 3, Array.Empty<CardViewModel>()),
			CorpDeck: new ZoneViewModel(ZoneKind.CorpDeck, 20, Array.Empty<CardViewModel>()),
			RunnerHand: new ZoneViewModel(ZoneKind.RunnerGrip, 2, Array.Empty<CardViewModel>()),
			RunnerDeck: new ZoneViewModel(ZoneKind.RunnerStack, 30, Array.Empty<CardViewModel>()),
			RunnerRig: Array.Empty<CardViewModel>(),
			CorpScore: Array.Empty<CardViewModel>(),
			RunnerScore: Array.Empty<CardViewModel>(),
			Decision: new DecisionViewModel(
				"decision:slice45-ui",
				1,
				new PlayerId(ownerId),
				owner,
				DecisionKind.IceSubroutineChoice,
				"Choose one issued ICE subroutine resolution."),
			Run: run,
			Outcome: new OutcomeViewModel(GameOutcomeKind.Ongoing, Winner: null, Reason: null),
			LegalActions: actions)
		{
			RunCues = includeCues
				? new RunCueViewModel(
					RunRestrictionActive: true,
					BioroidAbilityReady: true,
					MayflyBreakArmed: true)
				: RunCueViewModel.Unavailable,
		};
	}

	private static MatchSnapshot CreateSlice45Snapshot(MatchViewModel view) => new(
		Sequence: 1,
		Observation: new Observation(
			PlayerSide.Runner,
			string.Empty,
			Array.Empty<LegalAction>(),
			new Dictionary<string, object?>(),
			new Dictionary<string, object?>()),
		View: view,
		Events: Array.Empty<EngineEvent>(),
		Feedback: null,
		IsOpponentThinking: false,
		RunOutcome: null);

	private static string FormatSlice45Bool(bool value) => value ? "true" : "false";
}
