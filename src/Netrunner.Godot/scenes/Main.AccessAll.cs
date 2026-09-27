using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Access all of Archives in one go (playtest, 27 September). The engine issues
/// one breach choice per card, so the client submits those issued choices for
/// the player: the next candidate, and any access with a single action
/// (continue or a forced steal). It stops at any real choice, such as trashing
/// for a cost, and only acts once playback has settled, so every access is shown.
/// </summary>
public partial class Main
{
	private string? _accessAllRunId;

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
		addButton($"Access all remaining ({remaining})", () =>
		{
			_accessAllRunId = snapshot.View.Run!.Id;
			ContinueAccessAll();
		});
	}

	/// <summary>Called every frame; submits the next issued choice while access-all is on.</summary>
	private void ContinueAccessAll()
	{
		if (_accessAllRunId is not { } runId || _currentSnapshot is not { } snapshot) return;
		MatchViewModel view = snapshot.View;
		if (view.Run is not { EndReason: null } run || run.Id != runId || snapshot.IsTerminal)
		{
			_accessAllRunId = null;
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
			_accessAllRunId = null;
			return;
		}
		SubmitAction(decision, next);
	}
}
