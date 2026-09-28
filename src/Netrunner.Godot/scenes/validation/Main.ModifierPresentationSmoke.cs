using Godot;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Dedicated Slice 4.4 client proof. It feeds detached typed Match values
/// through the real card, action, and focus controls; no authoritative match
/// state is created or mutated by this path.
/// </summary>
public partial class Main
{
	private async Task RunSlice44UiSmokeAsync()
	{
		bool strengthReason = false;
		bool modifiedCost = false;
		bool usedFirstXChip = false;
		bool breachNote = false;
		TextCardControl? cardControl = null;
		Button? actionButton = null;
		try
		{
			_board.Visible = true;

			ValueQuote strengthQuote = new(
				BehaviorValueKind.Strength,
				baseValue: 3,
				effectiveValue: 5,
				terms:
				[
					new ValueCalculationTerm(
						BehaviorValueSourceKind.PerInstalledCard,
						BehaviorValueOperation.Add,
						delta: 2,
						reasonKey: "echelon-installed-icebreakers",
						sourceDefinitionId: new CardDefinitionId("definition:echelon"),
						sourceCardId: new CardInstanceId("card:echelon"),
						quantity: 2,
						unitDelta: 1),
				]);
			StrengthBreakdownViewModel breakdown = StrengthBreakdownViewModel.From(strengthQuote);
			CardViewModel card = new(
				"card:slice44-ui",
				"synthetic:slice44-ui",
				"Synthetic ICE",
				"ICE",
				IsKnown: true,
				Rezzed: true,
				AdvancementCounters: 0,
				Cost: 3,
				Strength: 3,
				AgendaPoints: null,
				TrashCost: null,
				Subtypes: ["Barrier"],
				RulesText: string.Empty)
			{
				FirstXUses =
				[
					new FirstXUseViewModel(
						"card:slice44-ui",
						"dzmz-first-program-install",
						1),
				],
			};
			cardControl = new TextCardControl();
			cardControl.SetCard(card);
			cardControl.SetEncounterStrength(breakdown);
			_runnerRig.AddChild(cardControl);

			CostQuote costQuote = new(
				baseCost: 2,
				[
					new CostAdjustment(
						BehaviorCostModifierKind.RunnerInstallCreditCost,
						-2,
						"carmen-successful-run-install",
						new CardDefinitionId("definition:carmen"),
						new CardInstanceId("card:carmen")),
				]);
			InstallCardParameters install = new(
				new CardInstanceId("card:slice44-ui"),
				InstallDestinationKind.RunnerRig,
				serverId: null,
				createNewRemote: false,
				creditCost: costQuote.EffectiveCost,
				costQuote: costQuote);
			LegalActionViewModel action = new(
				new ActionId("action:slice44-ui"),
				ActionKind.InstallCard,
				"Install card",
				install);
			LegalActionViewModel presented = ActionsRegionPresenter.Describe(action);
			actionButton = new Button
			{
				Text = presented.Description,
				Alignment = HorizontalAlignment.Left,
			};
			_actions.AddChild(actionButton);

			MatchViewModel baseline = ObservationViewMapper.Build(
				ObservationBuilder.Build(
					M2ScenarioSetup.Build(M2ScenarioProvider.LoadRepresentative()),
					PlayerSide.Runner),
				CardMetadataResolvers.Default,
				GatewayBehaviorCatalog.Create());
			RunFocusViewModel run = new(
				"run:slice44-ui",
				"server:headquarters",
				PlayerSide.Runner,
				RunCrPhase.Success,
				RunTimingStep.BreachCandidateSelection,
				CurrentIceIndex: -1,
				CurrentIce: null,
				Successful: true,
				Unsuccessful: false,
				EndReason: null,
				Priority: null,
				Encounter: null,
				Access: new AccessViewModel(
					Candidates: Array.Empty<CardViewModel?>(),
					RandomAccessLimit: 2,
					HiddenCandidateSource: BreachHiddenCandidateSource.CorpHand,
					Chosen: Array.Empty<CardViewModel?>(),
					Accessed: Array.Empty<CardViewModel?>(),
					State: RunAccessState.InProgress),
				RunCredits: 0,
				InitiatingSource: null)
			{
				BreachAccessModifiers =
				[
					new RunBreachAccessModifierViewModel(
						null,
						"server:headquarters",
						1,
						"docklands-pass-first-hq-breach",
						RunBreachAccessModifierSourceKind.FirstHeadquartersBreach),
				],
			};
			RenderFocusPresentationRegion(
				new MatchSnapshot(
					Sequence: 1,
					Observation: new Observation(
						PlayerSide.Runner,
						string.Empty,
						Array.Empty<LegalAction>(),
						new Dictionary<string, object?>(),
						new Dictionary<string, object?>()),
					View: baseline with { Run = run },
					Events: Array.Empty<EngineEvent>(),
					Feedback: null,
					IsOpponentThinking: false,
					RunOutcome: null));

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			strengthReason = cardControl.IsInsideTree()
				&& cardControl.DisplayedStats.Contains(
					"STR 3 +2 (per installed icebreaker: 2)",
					StringComparison.Ordinal);
			modifiedCost = actionButton.IsInsideTree()
				&& actionButton.Text.Contains(
					"install 2 → 0 (successful run this turn)",
					StringComparison.Ordinal);
			usedFirstXChip = cardControl.IsInsideTree()
				&& string.Equals(
					cardControl.DisplayedFirstXUseCue,
					FirstXUseLabels.UsedThisTurn,
					StringComparison.Ordinal);
			breachNote = SceneTreeContainsText(
				_focusPresentation,
				"+1 first HQ breach this turn");

			bool passed = strengthReason && modifiedCost && usedFirstXChip && breachNote;
			GD.Print(
				$"M4_SLICE44_UI {(passed ? "PASS" : "FAIL")} "
				+ $"strength_reason={FormatSlice44Bool(strengthReason)} "
				+ $"modified_cost={FormatSlice44Bool(modifiedCost)} "
				+ $"used_first_x_chip={FormatSlice44Bool(usedFirstXChip)} "
				+ $"breach_note={FormatSlice44Bool(breachNote)}");
			cardControl.QueueFree();
			actionButton.QueueFree();
			QuitCleanly(passed ? 0 : 1);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.ToString());
			string error = exception.Message.Replace('\n', ' ').Replace('\r', ' ');
			GD.Print(
				$"M4_SLICE44_UI FAIL strength_reason={FormatSlice44Bool(strengthReason)} "
				+ $"modified_cost={FormatSlice44Bool(modifiedCost)} "
				+ $"used_first_x_chip={FormatSlice44Bool(usedFirstXChip)} "
				+ $"breach_note={FormatSlice44Bool(breachNote)} error={error}");
			if (cardControl is not null && IsInstanceValid(cardControl))
			{
				cardControl.QueueFree();
			}

			if (actionButton is not null && IsInstanceValid(actionButton))
			{
				actionButton.QueueFree();
			}

			QuitCleanly(1);
		}
	}

	private static string FormatSlice44Bool(bool value) => value ? "true" : "false";

	private static bool SceneTreeContainsText(Node root, string text)
	{
		foreach (Node child in root.GetChildren())
		{
			if (child is Label label && label.Text.Contains(text, StringComparison.Ordinal))
			{
				return true;
			}

			if (child is Button button && button.Text.Contains(text, StringComparison.Ordinal))
			{
				return true;
			}

			if (SceneTreeContainsText(child, text))
			{
				return true;
			}
		}

		return false;
	}
}
