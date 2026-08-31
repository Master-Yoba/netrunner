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
		if (focus.Mode == FocusRegionMode.Outcome && focus.Outcome is { } completed)
		{
			_focusPresentation.AddChild(MakeLabel("REPRESENTATIVE SLICE COMPLETE", 17, Accent));
			_focusPresentation.AddChild(MakeWrappedLabel(
				$"{completed.Summary}\nThe live run has been cleaned up by Core. This screen is a presentation-safe outcome summary.",
				13,
				Colors.White));
			_smokeOutcomePresentationObserved |= _smokeTest && _focusPresentation.GetChildCount() > 0;
			return;
		}

		if (focus.Mode == FocusRegionMode.Empty || focus.Run is not { } run)
		{
			_focusPresentation.AddChild(MakeLabel(
				"RUN FOCUS · select a legal action to continue",
				13,
				Muted));
			return;
		}

		if (focus.Mode == FocusRegionMode.Encounter && focus.Encounter is { } encounter)
		{
			_smokeEncounterFocusObserved |= _smokeTest;
			RenderEncounterFocusRegion(snapshot, run, encounter);
			return;
		}

		if (focus.Mode == FocusRegionMode.Access)
		{
			_smokeAccessFocusObserved |= _smokeTest;
			RenderAccessFocusRegion(snapshot, run);
			return;
		}

		if (focus.Mode == FocusRegionMode.PaidWindow && focus.Priority is { } priority)
		{
			RenderPaidWindowFocusRegion(priority);
			return;
		}

		_focusPresentation.AddChild(MakeLabel(
			$"RUN STATUS · {run.Phase} · {run.Step} · run pool {run.RunCredits}",
			15,
			Warning));
		_focusPresentation.AddChild(MakeWrappedLabel(
			"The current timing step is owned by Core. Legal actions below are the only actions that can mutate the run.",
			12,
			Muted));
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

		HBoxContainer encounterRow = new();
		encounterRow.AddThemeConstantOverride("separation", 10);
		if (encounter.Ice is { } ice)
		{
			TextCardControl iceCard = MakeCard(ice);
			iceCard.CustomMinimumSize = new Vector2(172, 122);
			encounterRow.AddChild(iceCard);
		}

		VBoxContainer state = new();
		state.AddThemeConstantOverride("separation", 3);
		state.AddChild(MakeLabel(
			$"ICE STRENGTH · {(encounter.EffectiveStrength is { } strength ? strength.ToString(CultureInfo.InvariantCulture) : "UNKNOWN")}",
			14,
			Colors.White));
		state.AddChild(MakeLabel(
			$"SUBROUTINES · {encounter.Subroutines.Count} total · {encounter.RemainingSubroutines} active",
			12,
			Accent));
		foreach (EncounterBreakerViewModel breaker in encounter.Breakers)
		{
			string pumpCue = breaker.EffectiveStrength == breaker.PrintedStrength
				? string.Empty
				: $" · PRINTED {breaker.PrintedStrength} + ENCOUNTER PUMP";
			state.AddChild(MakeLabel(
				$"BREAKER · {breaker.Title} · CURRENT STR {breaker.EffectiveStrength}{pumpCue}",
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
				.Where(action => action.Kind is ActionKind.PumpBreaker or ActionKind.BreakSubroutines)
				.Select(action => $"{action.Kind}: {action.Description}"));
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

	private void RenderAccessFocusRegion(MatchSnapshot snapshot, RunFocusViewModel run)
	{
		AccessViewModel access = run.Access;
		_focusPresentation.AddChild(MakeLabel(
			$"ACCESS FOCUS · {run.AttackedServerId} · {access.State.ToString().ToUpperInvariant()}",
			17,
			Accent));
		_focusPresentation.AddChild(MakeLabel(
			$"Candidates: {access.Candidates.Count} · chosen: {access.Chosen.Count} · accessed: {access.Accessed.Count}"
				+ (access.RandomAccessLimit > 0 ? $" · random access limit {access.RandomAccessLimit}" : string.Empty),
			12,
			Colors.White));

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
