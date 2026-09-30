using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>
/// Seat orientation (roadmap U5): the local player's side of the table is at the
/// bottom. At the Corp seat the server field moves below the Runner's area and
/// each server builds outward toward the Runner: the root nearest the Corp, the
/// outermost ICE at the top, the server name on the Corp's edge.
/// </summary>
public partial class GameTableControl
{
	private PlayerSide _seat = PlayerSide.Runner;
	private bool _seatApplied;

	/// <summary>True when the Corp's server field is drawn below the Runner's area.</summary>
	public bool CorpSeatLayout => _seat == PlayerSide.Corp;

	private void ApplySeatOrientation(PlayerSide viewer)
	{
		if (_seatApplied && viewer == _seat) return;
		_seat = viewer;
		_seatApplied = true;
		Control? corp = GetNodeOrNull<Control>("Surface/Margins/Layout/ServerField");
		Control? runner = GetNodeOrNull<Control>("Surface/Margins/Layout/RunnerField");
		if (corp is null || runner is null) return;
		if (CorpSeatLayout) _tableLayout.MoveChild(runner, 0);
		else _tableLayout.MoveChild(corp, 0);
		foreach (ServerLaneParts lane in _serverLanes.Values)
		{
			ApplyLaneOrientation(lane);
		}
	}

	/// <summary>Orders a lane's rows for the current seat: root side toward the lane's owner.</summary>
	private void ApplyLaneOrientation(ServerLaneParts lane)
	{
		if (lane.Header.GetParent() is not VBoxContainer content) return;
		Control[] rows = CorpSeatLayout
			? [lane.IceCaption, lane.IceCards, lane.RootCaption, lane.RootCards, lane.RunMarker, lane.Header]
			: [lane.Header, lane.RunMarker, lane.RootCaption, lane.RootCards, lane.IceCaption, lane.IceCards];
		for (int index = 0; index < rows.Length; index++)
		{
			content.MoveChild(rows[index], index);
		}
		content.Alignment = CorpSeatLayout ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;
	}

	/// <summary>
	/// Puts a server's ICE rows in presentation order. <paramref name="innerFirst"/>
	/// lists them innermost first; at the Runner seat that is top to bottom, at the
	/// Corp seat bottom to top.
	/// </summary>
	private void OrderIceRows(VBoxContainer iceCards, List<Control> innerFirst)
	{
		int count = innerFirst.Count;
		for (int index = 0; index < count; index++)
		{
			Control row = innerFirst[CorpSeatLayout ? count - 1 - index : index];
			if (row.GetParent() == iceCards) iceCards.MoveChild(row, index);
		}
	}
}
