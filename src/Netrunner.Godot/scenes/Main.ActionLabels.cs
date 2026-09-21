using Netrunner.Match;

namespace Netrunner.GodotClient;

public partial class Main
{
	private string TableActionLabel(LegalActionViewModel action) => FriendlyText(ActionLabelPresenter.Label(
		action, _currentSnapshot?.View.Decision?.Kind, ActionCardName, _gameTable.GetServerDisplayName,
		_currentSnapshot?.View.LegalActions));

	private string ActionCardName(string id)
	{
		if (_currentSnapshot?.View is not { } view) return "face-down card";
		CardViewModel[] cards = BoardChoiceCards(view);
		CardViewModel? card = cards.FirstOrDefault(candidate => candidate.Id == id);
		string title = card is { IsKnown: true } ? card.Title : "face-down card";
		if (card is { IsKnown: true } && cards.Count(candidate => candidate.IsKnown && candidate.Title == title) == 1)
			return title;
		string? location = Position(view.RunnerHand.VisibleCards, "Grip") ?? Position(view.CorpHand.VisibleCards, "HQ hand")
			?? Position(view.RunnerRig, "Rig") ?? Position(view.RunnerHeap, "Heap") ?? Position(view.CorpDiscard, "Archives")
			?? Position(view.RunnerScore, "Runner score") ?? Position(view.CorpScore, "Corp score");
		foreach (ServerViewModel server in view.Servers)
		{
			string name = _gameTable.GetServerDisplayName(server.Id);
			location ??= Position(server.Ice, name + " ICE") ?? Position(server.Root, name + " root");
			if (server.IcePositions.FirstOrDefault(ice => ice.CardId == id) is { } ice)
				location ??= $"{name} ICE {ice.Index + 1}";
		}
		if (view.CardPositions.FirstOrDefault(position => position.CardId == id) is { } installed)
			location ??= $"{_gameTable.GetServerDisplayName(installed.ServerId)} {(installed.Zone == Netrunner.Core.ZoneKind.ServerIce ? "ICE" : "root")} {installed.Index + 1}";
		return location is null ? title : $"{title} ({location})";

		string? Position(IEnumerable<CardViewModel?> zone, string name)
		{
			int position = 0;
			foreach (CardViewModel? candidate in zone)
			{
				position++;
				if (candidate?.Id == id) return $"{name} {position}";
			}
			return null;
		}
	}
}
