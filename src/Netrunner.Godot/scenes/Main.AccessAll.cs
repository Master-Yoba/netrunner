using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Access all of Archives in one go (playtest, 27 September). The engine issues
/// one breach choice per card, so the client submits those issued choices for
/// the player: the next candidate, and any access with a single action
/// (continue or a forced steal). It stops at any real choice, such as trashing
/// for a cost. Playtest, 29 September: stepping through every access flashed the
/// dock and cards, so the accesses now run without animation behind a progress bar
/// in the dock, and the history keeps every card.
/// </summary>
public partial class Main
{
	private string? _accessAllRunId;
	private int _accessAllTotal;
	private int _accessAllDone;

	private static bool IsArchivesBreachChoice(MatchViewModel view) =>
		view.Run is { EndReason: null } run
		&& view.Servers.Any(server => server.Id == run.AttackedServerId && server.Kind == ServerKind.Archives)
		&& view.Decision?.OwnerSide == view.Viewer
		&& view.LegalActions.Count(action => action.Kind == ActionKind.ChooseBreachCandidate) > 1;

	/// <summary>Adds the dock button when Archives has more than one card left to access.</summary>
	private void AddAccessAllButton(MatchSnapshot snapshot, Func<string, Action, Button> addButton)
	{
		if (_accessAllRunId is not null || !IsArchivesBreachChoice(snapshot.View)) return;
		int remaining = snapshot.View.LegalActions.Count(action => action.Kind == ActionKind.ChooseBreachCandidate);
		addButton($"Access all remaining ({remaining})", () => StartAccessAll(snapshot, remaining));
	}

	private void StartAccessAll(MatchSnapshot snapshot, int remaining)
	{
		_accessAllRunId = snapshot.View.Run!.Id;
		_accessAllTotal = remaining;
		_accessAllDone = 0;
		RenderCardTableActions(snapshot);
		ContinueAccessAll();
	}

	/// <summary>Called every frame; submits the next issued choice while access-all is on.</summary>
	private void ContinueAccessAll()
	{
		if (_accessAllRunId is not { } runId || _currentSnapshot is not { } snapshot) return;
		MatchViewModel view = snapshot.View;
		if (view.Run is not { EndReason: null } run || run.Id != runId || snapshot.IsTerminal)
		{
			FinishAccessAll(snapshot);
			return;
		}
		if (_tableAwaitingSettlement || Volatile.Read(ref _submissionInFlight) != 0 || !snapshot.AwaitingHumanDecision
			|| view.Decision is not { } decision) return;
		LegalActionViewModel? next = view.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ChooseBreachCandidate)
			?? (view.LegalActions.Count == 1 && view.LegalActions[0].Kind is ActionKind.NoAccessAction or ActionKind.StealAgenda
				or ActionKind.PassPriority ? view.LegalActions[0] : null);
		if (next is null)
		{
			// A real choice (for example trashing for a cost) is the player's.
			FinishAccessAll(snapshot);
			return;
		}
		if (next.Kind == ActionKind.ChooseBreachCandidate) _accessAllDone++;
		// No per-card playback: each access settles at once and the dock stays on the progress bar.
		_skipAnimation = true;
		_tablePlaybackWait = 0;
		_beats.Stop();
		SubmitAction(decision, next);
	}

	private void FinishAccessAll(MatchSnapshot snapshot)
	{
		if (_accessAllRunId is null) return;
		_accessAllRunId = null;
		_tableLastAction.Text = $"Accessed {_accessAllDone} {(_accessAllDone == 1 ? "card" : "cards")} in Archives";
		RenderCardTableActions(snapshot);
	}

	/// <summary>The dock while access-all runs: one progress line instead of every access's controls.</summary>
	private bool RenderAccessAllProgress()
	{
		if (_accessAllRunId is null) return false;
		_tablePrompt.Text = $"Accessing Archives… {Math.Min(_accessAllDone, _accessAllTotal)}/{_accessAllTotal}";
		var progress = new ProgressBar
		{
			Name = "AccessAllProgress",
			MinValue = 0,
			MaxValue = Math.Max(1, _accessAllTotal),
			Value = Math.Min(_accessAllDone, _accessAllTotal),
			ShowPercentage = false,
			CustomMinimumSize = new Vector2(260, 18),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		};
		_tableActions.AddChild(progress);
		return true;
	}
}
