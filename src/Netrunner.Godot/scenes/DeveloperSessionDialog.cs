using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>Instantiated only by an explicitly enabled development launch.</summary>
public partial class DeveloperSessionDialog : Control
{
	public event Action? CloseRequested;
	public event Action? PauseRequested;
	public event Action? ImportRequested;
	public event Action<bool>? ExportRequested;
	public event Action<bool>? RevealChanged;
	public event Action<DeveloperAdvanceRequest>? AdvanceRequested;
	private OptionButton _actions = null!;
	private readonly List<ActionId> _issuedActions = [];
	private Label _status = null!;
	private TextEdit _inspector = null!;
	private bool _busy;
	private bool _hasSession;
	public bool RevealEnabled => GetNode<CheckButton>("%Reveal").ButtonPressed;
	public string Status => _status.Text;

	public override void _Ready()
	{
		_actions = GetNode<OptionButton>("%Actions");
		_status = GetNode<Label>("%Status");
		_inspector = GetNode<TextEdit>("%Inspector");
		GetNode<Button>("%Close").Pressed += () => CloseRequested?.Invoke();
		GetNode<Button>("%Pause").Pressed += () => PauseRequested?.Invoke();
		GetNode<Button>("%Import").Pressed += () => ImportRequested?.Invoke();
		GetNode<Button>("%Export").Pressed += () => ExportRequested?.Invoke(false);
		GetNode<Button>("%ExportRaw").Pressed += () => ExportRequested?.Invoke(true);
		GetNode<Button>("%Step").Pressed += () => AdvanceRequested?.Invoke(new(DeveloperAdvanceTarget.ActionLimit, 1,
			HumanAction: _actions.Selected > 0 ? _issuedActions[_actions.Selected - 1] : null));
		GetNode<Button>("%ToHuman").Pressed += () => AdvanceRequested?.Invoke(new(DeveloperAdvanceTarget.HumanDecision, Limit));
		GetNode<Button>("%ToTurn").Pressed += () => AdvanceRequested?.Invoke(new(DeveloperAdvanceTarget.Turn, Limit,
			(int)GetNode<SpinBox>("%Turn").Value));
		GetNode<Button>("%ToOutcome").Pressed += () => AdvanceRequested?.Invoke(new(DeveloperAdvanceTarget.Outcome, Limit));
		GetNode<CheckButton>("%Reveal").Toggled += enabled =>
		{
			_inspector.Visible = enabled;
			if (!enabled) _inspector.Text = "";
			RevealChanged?.Invoke(enabled);
		};
	}

	private int Limit => (int)GetNode<SpinBox>("%Limit").Value;

	public void SetSession(MatchSnapshot? snapshot)
	{
		_hasSession = snapshot is not null;
		_issuedActions.Clear();
		_actions.Clear();
		_actions.AddItem("Automatic action");
		if (snapshot is not null)
		{
			foreach (LegalAction action in snapshot.Observation.LegalActions)
			{
				_issuedActions.Add(action.Id);
				_actions.AddItem($"{action.Description} · {action.Id.Value}");
			}
			GetNode<SpinBox>("%Turn").Value = (snapshot.View.Turn.TurnNumber ?? 0) + 1;
		}
		_actions.Select(0);
		RefreshButtons();
	}

	public void SetBusy(bool busy, string status)
	{
		_busy = busy;
		_status.Text = status;
		RefreshButtons();
	}

	public void SetInspection(DeveloperInspection inspection) => _inspector.Text =
		$"{inspection.Origin} · action {inspection.ActionSequence}\n"
		+ string.Join('\n', inspection.Cards.Select(card => $"{card.Zone} · {card.Id} · {card.Title} ({card.DefinitionId}) · {(card.FaceUp ? "face up" : "hidden")}"));

	private void RefreshButtons()
	{
		foreach (string name in new[] { "Step", "ToHuman", "ToTurn", "ToOutcome", "Export", "ExportRaw" })
			GetNode<Button>("%" + name).Disabled = _busy || !_hasSession;
		GetNode<Button>("%Import").Disabled = _busy;
		GetNode<Button>("%Pause").Disabled = !_hasSession;
		_actions.Disabled = _busy || !_hasSession;
	}
}
