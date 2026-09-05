using System.Globalization;
using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Independently renderable run-focus region. This extraction is behavior
/// neutral: Main still owns composition and action submission, while this
/// partial owns only the focus panel's presentation tree.
/// </summary>
public partial class Main
{
	private void RenderFocusPresentationRegion(MatchSnapshot snapshot)
	{
		ClearChildren(_focusPresentation);
		FocusRegionState focus = FocusRegionPresenter.Present(snapshot);
		RenderSlice46PresentationCues(snapshot);
		if (focus.Mode == FocusRegionMode.Outcome && focus.GameOutcome is { IsTerminal: true } outcome)
		{
			_focusPresentation.AddChild(MakeLabel("MATCH OUTCOME", 17, Accent));
			_focusPresentation.AddChild(MakeWrappedLabel(
				$"Winner · {outcome.Winner}\nReason · {outcome.Reason}\nThe complete outcome was supplied by Core.",
				13,
				Colors.White));
			_smokeOutcomePresentationObserved |= _fullMatchSmoke && _focusPresentation.GetChildCount() > 0;
			AddNewMatchButton();
			return;
		}

		if (focus.Mode == FocusRegionMode.Outcome && focus.Outcome is { } completed)
		{
			_focusPresentation.AddChild(MakeLabel("REPRESENTATIVE SLICE COMPLETE", 17, Accent));
			_focusPresentation.AddChild(MakeWrappedLabel(
				$"{completed.Summary}\nThe live run has been cleaned up by Core. This screen is a presentation-safe outcome summary.",
				13,
				Colors.White));
			_smokeOutcomePresentationObserved |= _smokeTest && _focusPresentation.GetChildCount() > 0;
			AddNewMatchButton();
			return;
		}

		if (focus.Mode == FocusRegionMode.Empty || focus.Run is not { } run)
		{
			RenderSlice47DecisionFocus(snapshot);
			_focusPresentation.AddChild(MakeLabel(
				"RUN FOCUS · select a legal action to continue",
				13,
				Muted));
			return;
		}

		RenderSlice45RunCues(snapshot.View.Slice45Cues);

		if (focus.Mode == FocusRegionMode.Encounter && focus.Encounter is { } encounter)
		{
			_smokeEncounterFocusObserved |= _smokeTest;
			RenderEncounterFocusRegion(snapshot, run, encounter);
			RenderSlice45DecisionFocus(snapshot.View);
			RenderSlice47DecisionFocus(snapshot);
			return;
		}

		if (focus.Mode == FocusRegionMode.Access)
		{
			_smokeAccessFocusObserved |= _smokeTest;
			RenderAccessFocusRegion(snapshot, run);
			RenderSlice47DecisionFocus(snapshot);
			return;
		}

		if (focus.Mode == FocusRegionMode.PaidWindow && focus.Priority is { } priority)
		{
			RenderPaidWindowFocusRegion(priority);
			RenderSlice47DecisionFocus(snapshot);
			return;
		}

		_focusPresentation.AddChild(MakeLabel(
			$"RUN STATUS · {run.Phase} · {run.Step} · run pool {run.RunCredits}",
			15,
			Warning));
		AddBreachAccessModifierCues(run);
		RenderSlice45DecisionFocus(snapshot.View);
		RenderSlice47DecisionFocus(snapshot);
		_focusPresentation.AddChild(MakeWrappedLabel(
			"The current timing step is owned by Core. Legal actions below are the only actions that can mutate the run.",
			12,
			Muted));
	}

	private void RenderSlice46PresentationCues(MatchSnapshot snapshot)
	{
		MatchViewModel view = snapshot.View;
		if (view.PendingDamage.Count > 0)
		{
			_focusPresentation.AddChild(MakeLabel("SLICE 4.6 DAMAGE", 14, Warning));
			foreach (DamagePresentationViewModel damage in view.PendingDamage)
			{
				_focusPresentation.AddChild(MakeWrappedLabel(damage.Summary, 11, damage.IsFlatlineRisk ? Warning : Colors.White));
			}
		}

		if (view.ConditionalValues.Count > 0)
		{
			_focusPresentation.AddChild(MakeLabel("CONDITIONAL VALUE · CORE QUOTE", 13, Accent));
			foreach (ConditionalValueViewModel value in view.ConditionalValues)
			{
				_focusPresentation.AddChild(MakeWrappedLabel(value.Summary, 11, Colors.White));
			}
		}

		Slice46DecisionViewModel decision = Slice46PresentationProjector.Present(snapshot);
		if (decision.HasUnlessChoice)
		{
			_focusPresentation.AddChild(MakeLabel("UNLESS CHOICE · CORE ISSUED ALTERNATIVES", 13, Accent));
			if (decision.Prompt is { } prompt && !string.IsNullOrWhiteSpace(prompt))
			{
				_focusPresentation.AddChild(MakeWrappedLabel(prompt, 11, Colors.White));
			}

			foreach (Slice46ActionPresentationViewModel option in decision.Options)
			{
				_focusPresentation.AddChild(MakeWrappedLabel(
					option.OrderedPresentationLabel,
					11,
					option.IsFallback ? Warning : Colors.White));
			}
		}

		if (decision.JackOutAfterDamage)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				"KARUNĀ DAMAGE RESOLVED · JACK-OUT CHOICE ISSUED",
				13,
				Accent));
		}
	}

	private void AddNewMatchButton()
	{
		var button = new Button
		{
			Text = "New match",
			CustomMinimumSize = new Vector2(180, 32),
		};
		button.Pressed += () => GetTree().ReloadCurrentScene();
		_focusPresentation.AddChild(button);
	}

	private void RenderPaidWindowFocusRegion(RunPriorityViewModel priority)
	{
		_focusPresentation.AddChild(MakeLabel(
			$"PAID WINDOW · {priority.Window} · PRIORITY {priority.Holder.ToString().ToUpperInvariant()}",
			17,
			Accent));
		_focusPresentation.AddChild(MakeWrappedLabel(
			$"Consecutive passes: {priority.ConsecutivePasses} · "
			+ $"Corp may rez approached ICE: {(priority.CorpMayRezApproachedIce ? "YES" : "NO")} · "
			+ $"non-ICE rez: {(priority.CorpMayRezNonIce ? "YES" : "NO")}",
			12,
			Colors.White));
		_focusPresentation.AddChild(MakeWrappedLabel(
			"PASS / CONTINUE remain explicit below. The priority holder is shown as text, not colour alone.",
			12,
			Muted));
	}

	private void RenderEncounterFocusRegion(
		MatchSnapshot snapshot,
		RunFocusViewModel run,
		EncounterViewModel encounter)
	{
		string iceTitle = encounter.Ice?.Title ?? run.CurrentIce?.Title ?? "Unknown ICE";
		_focusPresentation.AddChild(MakeLabel(
			$"ENCOUNTER FOCUS · {iceTitle} · {encounter.Status.ToString().ToUpperInvariant()}",
			17,
			Warning));
		AddBreachAccessModifierCues(run);

		HBoxContainer encounterRow = new();
		encounterRow.AddThemeConstantOverride("separation", 10);
		if (encounter.Ice is { } ice)
		{
			TextCardControl iceCard = MakeCard(ice);
			iceCard.CustomMinimumSize = new Vector2(172, 122);
			iceCard.SetEncounterStrength(encounter.StrengthBreakdown);
			encounterRow.AddChild(iceCard);
		}

		VBoxContainer state = new();
		state.AddThemeConstantOverride("separation", 3);
		state.AddChild(MakeLabel(
			$"ICE STRENGTH · {encounter.StrengthSummary ?? (encounter.EffectiveStrength is { } strength ? strength.ToString(CultureInfo.InvariantCulture) : "UNKNOWN")}",
			14,
			Colors.White));
		state.AddChild(MakeLabel(
			$"SUBROUTINES · {encounter.Subroutines.Count} total · {encounter.RemainingSubroutines} active",
			12,
			Accent));
		foreach (EncounterBreakerViewModel breaker in encounter.Breakers)
		{
			state.AddChild(MakeLabel(
				$"BREAKER · {breaker.Title} · {breaker.StrengthSummary ?? $"CURRENT STR {breaker.EffectiveStrength}"}",
				11,
				encounter.EffectiveStrength is { } iceStrength
					&& breaker.EffectiveStrength < iceStrength ? Warning : Accent));
		}

		foreach (SubroutineViewModel subroutine in encounter.Subroutines)
		{
			string status = subroutine.Broken
				? "[BROKEN]"
				: subroutine.Resolved ? "[RESOLVED]" : "[ACTIVE]";
			state.AddChild(MakeLabel(
				$"{status} Subroutine {subroutine.Ordinal}",
				11,
				subroutine.Broken || subroutine.Resolved ? Muted : Colors.White));
		}

		encounterRow.AddChild(state);
		_focusPresentation.AddChild(encounterRow);
		AddRunPoolCueRegion(run, "hosted run credit(s) available for pump and break costs");
		string breakerCue = string.Join(
			" · ",
			snapshot.View.LegalActions
				.Where(action => action.Kind is ActionKind.PumpBreaker
					or ActionKind.BreakSubroutines
					or ActionKind.UseBioroidAbility)
				.Select(Slice45PresentationProjector.Label));
		_focusPresentation.AddChild(MakeWrappedLabel(
			string.IsNullOrWhiteSpace(breakerCue)
				? "BREAKER OPTIONS · no breaker action is currently issued"
				: $"BREAKER OPTIONS · {breakerCue}",
			12,
			Colors.White));
		if (run.Priority is { } priority)
		{
			AddPriorityCueRegion(priority);
		}
	}

	private void RenderSlice45DecisionFocus(MatchViewModel view)
	{
		Slice45DecisionViewModel decision = Slice45PresentationProjector.Present(view);
		if (!decision.IsProminent)
		{
			return;
		}

		_focusPresentation.AddChild(MakeLabel(
			$"SLICE 4.5 {decision.Surface.ToString().ToUpperInvariant()} DECISION · "
			+ $"ACTING PLAYER {decision.ActingPlayer!.Value.ToString().ToUpperInvariant()}",
			14,
			Accent));
		if (decision.Prompt is { } prompt && !string.IsNullOrWhiteSpace(prompt))
		{
			_focusPresentation.AddChild(MakeWrappedLabel(prompt, 12, Colors.White));
		}

		foreach (Slice45ActionPresentationViewModel option in decision.Options)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				option.OrderedPresentationLabel,
				11,
				option.IsFallback ? Warning : Colors.White));
		}
	}

	private void RenderSlice45RunCues(Slice45CueViewModel cues)
	{
		if (cues.NoStealOrTrashActive == true)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				"RUN RESTRICTION ACTIVE · NO STEAL / NO TRASH CORP CARDS · UNTIL RUN ENDS",
				12,
				Warning));
		}

		if (cues.BioroidAbilityLegal == true)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				"BIOROID ABILITY · LEGAL",
				12,
				Accent));
		}

		if (cues.MayflyRunEndArmed == true)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				"MAYFLY RUN-END EFFECT · ARMED",
				12,
				Accent));
		}
	}

	private void RenderSlice47DecisionFocus(MatchSnapshot snapshot)
	{
		Slice47DecisionViewModel decision = Slice47PresentationProjector.Present(snapshot);
		if (decision.ZahyaAccess.IsKnown)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				decision.ZahyaAccess.Summary + " · supplied by Core",
				12,
				Accent));
		}

		foreach (IceSwapPreviewViewModel preview in decision.SwapPreviews)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(preview.Summary, 11, Warning));
		}

		if (!decision.IsProminent)
		{
			return;
		}

		_focusPresentation.AddChild(MakeLabel(
			$"SLICE 4.7 DECISION · ACTING PLAYER {decision.ActingPlayer!.Value.ToString().ToUpperInvariant()}",
			14,
			Accent));
		if (decision.Prompt is { } prompt && !string.IsNullOrWhiteSpace(prompt))
		{
			_focusPresentation.AddChild(MakeWrappedLabel(prompt, 11, Colors.White));
		}

		foreach (Slice47ActionPresentationViewModel option in decision.Options)
		{
			string label = option.SemanticLabel is { } semantic
				? $"{semantic} · {option.PresentationLabel}"
				: option.PresentationLabel;
			_focusPresentation.AddChild(MakeWrappedLabel(label, 11, option.IsControllerChoice ? Warning : Colors.White));
		}
	}

	private void RenderAccessFocusRegion(MatchSnapshot snapshot, RunFocusViewModel run)
	{
		AccessViewModel access = run.Access;
		_focusPresentation.AddChild(MakeLabel(
			$"ACCESS FOCUS · {run.AttackedServerId} · {access.State.ToString().ToUpperInvariant()}",
			17,
			Accent));
		AddBreachAccessModifierCues(run);
		_focusPresentation.AddChild(MakeLabel(
			$"Candidates: {access.Candidates.Count} · chosen: {access.Chosen.Count} · accessed: {access.Accessed.Count}"
				+ (access.RandomAccessLimit > 0 ? $" · random access limit {access.RandomAccessLimit}" : string.Empty),
			12,
			Colors.White));
		_focusPresentation.AddChild(MakeWrappedLabel(access.AccessOccurrenceSummary, 12, Accent));

		if (access.Candidates.Count > 0)
		{
			_focusPresentation.AddChild(MakeLabel("CANDIDATE TARGETS · click a card or choose a legal action below", 11, Warning));
			HBoxContainer candidates = new();
			candidates.AddThemeConstantOverride("separation", 7);
			foreach (CardViewModel? candidate in access.Candidates)
			{
				TextCardControl card = MakeCard(candidate);
				card.CustomMinimumSize = new Vector2(140, 100);
				candidates.AddChild(card);
			}
			_focusPresentation.AddChild(candidates);
		}
		else if (access.HiddenCandidateSource is { } source)
		{
			_focusPresentation.AddChild(MakeWrappedLabel(
				$"HIDDEN CANDIDATE · Core will draw from {source} after the Runner confirms the legal choice.",
				12,
				Warning));
		}

		if (access.Accessed.Count > 0)
		{
			_focusPresentation.AddChild(MakeLabel("ACCESSED CARD · costs and choices are shown below", 11, Warning));
			HBoxContainer accessed = new();
			foreach (CardViewModel? cardModel in access.Accessed)
			{
				TextCardControl card = MakeCard(cardModel);
				card.CustomMinimumSize = new Vector2(140, 100);
				accessed.AddChild(card);
			}
			_focusPresentation.AddChild(accessed);
		}

		AddRunPoolCueRegion(run, "hosted run credit(s) available for trash costs");
		IEnumerable<LegalActionViewModel> accessActions = snapshot.View.LegalActions
			.Where(ActionsRegionPresenter.IsAccessAction);
		string actionCue = string.Join(
			" · ",
			accessActions.Select(action => $"{action.Kind.ToString().ToUpperInvariant()}: {action.Description}"));
		_focusPresentation.AddChild(MakeWrappedLabel(
			string.IsNullOrWhiteSpace(actionCue)
				? "ACCESS ACTIONS · select a candidate to continue"
				: $"ACCESS ACTIONS · {actionCue}",
			12,
			Colors.White));
	}

	private void AddBreachAccessModifierCues(RunFocusViewModel run)
	{
		if (run.BreachAccessNotes.Count == 0)
		{
			return;
		}

		_focusPresentation.AddChild(MakeWrappedLabel(
			$"BREACH ACCESS · {string.Join(" · ", run.BreachAccessNotes)}",
			11,
			Warning));
	}

	private void AddRunPoolCueRegion(RunFocusViewModel run, string usage)
	{
		if (run.RunCredits > 0)
		{
			_focusPresentation.AddChild(MakeLabel(
				$"RUN POOL · {run.RunCredits} {usage} · expires at run end",
				11,
				Warning));
		}
	}

	private void AddPriorityCueRegion(RunPriorityViewModel priority)
	{
		_focusPresentation.AddChild(MakeWrappedLabel(
			$"PAID WINDOW · {priority.Window} · priority {priority.Holder} · passes {priority.ConsecutivePasses} · PASS / CONTINUE below",
			11,
			Muted));
	}
}
