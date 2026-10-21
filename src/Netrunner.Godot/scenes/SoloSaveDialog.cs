using Godot;
using Netrunner.Cards;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>A reusable modal which receives only allowlisted save previews.</summary>
public partial class SoloSaveDialog : Control
{
	public event Action? CloseRequested;
	public event Action? RefreshRequested;
	public event Action? OpenFolderRequested;
	public event Action<string, bool>? LoadRequested;
	public event Action<string>? SaveRequested;
	private Label _title = null!;
	private Label _message = null!;
	private VBoxContainer _rows = null!;
	private ScrollContainer _scroll = null!;
	private LineEdit _name = null!;
	private Button _confirm = null!;
	private Button _refresh = null!;
	private Button _close = null!;
	private bool _saving;
	private bool _busy;
	public string Message => _message.Text;
	public LineEdit NameInput => _name;
	public Button ConfirmButton => _confirm;
	public Button CloseButton => _close;

	public override void _Ready()
	{
		_title = GetNode<Label>("%Title");
		_message = GetNode<Label>("%Message");
		_rows = GetNode<VBoxContainer>("%Rows");
		_scroll = GetNode<ScrollContainer>("%Scroll");
		_name = GetNode<LineEdit>("%SaveName");
		_confirm = GetNode<Button>("%Confirm");
		_refresh = GetNode<Button>("%Refresh");
		_close = GetNode<Button>("%Close");
		_close.Pressed += () => CloseRequested?.Invoke();
		_refresh.Pressed += () => RefreshRequested?.Invoke();
		GetNode<Button>("%OpenFolder").Pressed += () => OpenFolderRequested?.Invoke();
		_confirm.Pressed += Confirm;
		_name.TextSubmitted += _ => Confirm();
		_name.TextChanged += _ => _confirm.Disabled = _busy || string.IsNullOrWhiteSpace(_name.Text);
	}

	public void ShowSave(string suggestedName, bool quit)
	{
		ClearRows();
		_saving = true;
		GetNode<Control>("Panel/Margin/Content/Spacer").Show();
		_title.Text = quit ? "Save and quit" : "Save game";
		_confirm.Text = quit ? "Save and quit" : "Save game";
		_name.Text = suggestedName;
		_name.Visible = _confirm.Visible = true;
		_scroll.Visible = _refresh.Visible = false;
		Visible = true;
		SetBusy(false, "Save this match to play later, even without an internet connection.");
		_name.GrabFocus();
		_name.SelectAll();
	}

	public void ShowBrowser()
	{
		_saving = false;
		GetNode<Control>("Panel/Margin/Content/Spacer").Hide();
		_title.Text = "Resume a saved game";
		_name.Visible = _confirm.Visible = false;
		_scroll.Visible = _refresh.Visible = true;
		Visible = true;
		ClearRows();
		SetBusy(true, "Checking saved games…");
		_close.GrabFocus();
	}

	public void SetEntries(IReadOnlyList<SoloSaveBrowserEntry> entries)
	{
		ClearRows();
		foreach (SoloSaveBrowserEntry entry in entries)
		{
			var row = new VBoxContainer();
			_rows.AddChild(row);
			SoloSavePreview? preview = entry.Preview ?? entry.Backup;
			row.AddChild(new Label { Text = preview?.Name ?? "Unreadable save", AutowrapMode = TextServer.AutowrapMode.WordSmart });
			if (entry.Preview is { } primary) row.AddChild(Details(primary));
			if (entry.IsCompatible && entry.Preview is { } valid)
			{
				row.AddChild(new Label { Text = "Ready to load" });
				AddLoadButton(row, valid, backup: false);
			}
			else
			{
				row.AddChild(new Label { Text = entry.Error ?? "This save is unavailable.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
				if (entry.Backup is { } backup)
				{
					row.AddChild(Details(backup));
					AddLoadButton(row, backup, backup: true);
				}
			}
			row.AddChild(new HSeparator());
		}
		SetBusy(false, entries.Count == 0 ? "No saved games yet. Use the settings cog during a match to save." : "Choose a game. It will open paused, ready to continue.");
	}

	public void SetBusy(bool busy, string message)
	{
		_busy = busy;
		_message.Text = message;
		_name.Editable = !busy;
		_confirm.Disabled = busy || string.IsNullOrWhiteSpace(_name.Text);
		_refresh.Disabled = busy;
		foreach (Button button in _rows.FindChildren("*", "Button", true, false).OfType<Button>()) button.Disabled = busy;
	}

	private void Confirm()
	{
		if (_saving && !_busy && !string.IsNullOrWhiteSpace(_name.Text)) SaveRequested?.Invoke(_name.Text.Trim());
	}

	private void AddLoadButton(VBoxContainer row, SoloSavePreview preview, bool backup)
	{
		var button = new Button { Text = backup ? "Load verified backup" : "Load game", Alignment = HorizontalAlignment.Left };
		button.Pressed += () => { if (!_busy) LoadRequested?.Invoke(preview.SaveId, backup); };
		row.AddChild(button);
	}

	private static Label Details(SoloSavePreview preview) => new()
	{
		Text = $"{preview.HumanSide} · {(preview.Difficulty == "contract-baseline" ? "Contract baseline" : preview.Difficulty.ToPascalCase())} · Seed {preview.Seed}\n"
			+ $"Corp: {DeckLabel(preview.CorpDeck)} · Runner: {DeckLabel(preview.RunnerDeck)}\n"
			+ $"{(preview.IsTerminal ? "Match complete" : $"Turn {preview.Turn} · {preview.Phase}")} · {preview.UpdatedUtc.ToLocalTime():g}",
		AutowrapMode = TextServer.AutowrapMode.WordSmart,
	};

	private static string DeckLabel(string id) => id switch
	{
		"system-gateway-basic-corp" or "system-gateway-basic-runner" => "Basic",
		"system-gateway-expanded-corp" or "system-gateway-expanded-runner" => "Expanded",
		// Gateway sample decks (Wave 4 C1) carry their published names.
		_ when PlayFormats.TryGetPreset(id, out OfficialPreset preset) && preset.Name is { } name => name,
		_ => "Custom",
	};

	private void ClearRows()
	{
		foreach (Node child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
	}
}
