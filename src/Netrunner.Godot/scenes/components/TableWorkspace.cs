using Godot;

namespace Netrunner.GodotClient;

/// <summary>Presentation-only sizing of the board and its history rail.</summary>
[Tool]
public partial class TableWorkspace : Control
{
	private GameTableControl? _board;
	private MatchHistoryPanel? _history;
	private bool _connected;
	private float _historyGap = 6;
	private float _pinMinimumWidth = 1800;

	[Export(PropertyHint.Range, "0,40,1")]
	public float HistoryGap
	{
		get => _historyGap;
		set { _historyGap = Mathf.Clamp(value, 0, 40); PositionHistory(); }
	}

	[Export(PropertyHint.Range, "760,3840,10")]
	public float PinMinimumWidth
	{
		get => _pinMinimumWidth;
		set { _pinMinimumWidth = Mathf.Clamp(value, 760, 3840); PositionHistory(); }
	}

	public override void _Ready()
	{
		_board = GetNode<GameTableControl>("GameBoard");
		_history = GetNode<MatchHistoryPanel>("History");
		ConnectLayout();
		PositionHistory();
	}

	public override void _EnterTree()
	{
		if (_history is not null) ConnectLayout();
	}

	public override void _ExitTree()
	{
		if (!_connected || _history is null) return;
		Resized -= PositionHistory;
		_history.ExpandedChanged -= HandleModeChanged;
		_history.PinnedChanged -= HandleModeChanged;
		_history.LayoutChanged -= PositionHistory;
		_history.MinimumSizeChanged -= PositionHistory;
		_connected = false;
	}

	private void ConnectLayout()
	{
		if (_connected || _history is null) return;
		Resized += PositionHistory;
		_history.ExpandedChanged += HandleModeChanged;
		_history.PinnedChanged += HandleModeChanged;
		_history.LayoutChanged += PositionHistory;
		_history.MinimumSizeChanged += PositionHistory;
		_connected = true;
	}

	private void HandleModeChanged(bool _) => PositionHistory();

	private void PositionHistory()
	{
		if (_history is null || _board is null || !IsInstanceValid(_history) || !IsInstanceValid(_board)) return;
		float width = Mathf.Max(_history.Expanded ? _history.ExpandedWidth : _history.RailWidth,
			_history.GetCombinedMinimumSize().X);
		float reserved = _history.Expanded && _history.Pinned && Size.X >= PinMinimumWidth
			? width : _history.RailWidth;
		_board.OffsetRight = -(reserved + HistoryGap);
		_history.OffsetLeft = -width;
		_history.OffsetRight = 0;
		_history.OffsetTop = 0;
		_history.OffsetBottom = 0;
	}
}
