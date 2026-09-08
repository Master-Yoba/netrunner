using Godot;
using Netrunner.Core;
using Netrunner.Match;

namespace Netrunner.GodotClient;

/// <summary>
/// Presentation-only card sample host for the Godot editor.
///
/// The preview creates synthetic viewer-safe projections and never constructs
/// a coordinator, reads a match trace, resolves hidden metadata, or writes a
/// save. Exported values are opt-in sample state so a clean checkout can show
/// the card contract without official card data or local artwork.
/// </summary>
[Tool]
public partial class CardViewPreview : Control
{
	private bool _sampleKnown = true;
	private PlayerSide _sampleBackSide = PlayerSide.Corp;
	private bool _sampleIce;
	private bool _sampleActionable;
	private bool _sampleSelected;
	private bool _sampleTarget;
	private bool _sampleHovered;
	private CardAppearance? _appearance;
	private TableCardControl? _card;
	private Control? _previewHost;
	private Label? _caption;
	private bool _previewDirty = true;
	private bool _lastHovered;
	private static readonly string[] PreviewSubtypes = ["Connection", "Virtual"];

	[ExportGroup("Synthetic card sample")]
	[Export]
	public bool SampleKnown
	{
		get => _sampleKnown;
		set => SetPreviewValue(ref _sampleKnown, value);
	}

	[Export]
	public PlayerSide SampleBackSide
	{
		get => _sampleBackSide;
		set => SetPreviewValue(ref _sampleBackSide, value);
	}

	[Export]
	public bool SampleIce
	{
		get => _sampleIce;
		set => SetPreviewValue(ref _sampleIce, value);
	}

	[Export]
	public bool SampleActionable
	{
		get => _sampleActionable;
		set => SetPreviewValue(ref _sampleActionable, value);
	}

	[Export]
	public bool SampleSelected
	{
		get => _sampleSelected;
		set => SetPreviewValue(ref _sampleSelected, value);
	}

	[Export]
	public bool SampleTarget
	{
		get => _sampleTarget;
		set => SetPreviewValue(ref _sampleTarget, value);
	}

	[Export]
	public bool SampleHovered
	{
		get => _sampleHovered;
		set => SetPreviewValue(ref _sampleHovered, value);
	}

	[ExportGroup("Shared appearance")]
	[Export]
	public CardAppearance Appearance
	{
		get => _appearance ??= LoadDefaultAppearance();
		set
		{
			_appearance = value ?? LoadDefaultAppearance();
			_previewDirty = true;
		}
	}

	public override void _EnterTree()
	{
		base._EnterTree();
		BindPreviewNodes();
		_previewDirty = true;
	}

	public override void _Ready()
	{
		BindPreviewNodes();
		if (_previewHost is null)
		{
			GD.PushWarning($"{nameof(CardViewPreview)} expects a PreviewHost child.");
			return;
		}

		SetProcess(true);
		_previewDirty = true;
		RefreshPreview();
	}

	public override void _Process(double delta)
	{
		if (_previewDirty && IsInsideTree())
		{
			RefreshPreview();
		}
	}

	public override void _ExitTree()
	{
		// The host owns this runtime-only instance. Keep references while the
		// scene is detached so re-entry reuses the same card instead of spawning
		// a second child; a freed child is recreated by EnsureCard.
		base._ExitTree();
	}

	private void BindPreviewNodes()
	{
		_previewHost = GetNodeOrNull<Control>("PreviewHost");
		_caption = GetNodeOrNull<Label>("PreviewCaption");
	}

	private void SetPreviewValue<T>(ref T field, T value)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return;
		}

		field = value;
		_previewDirty = true;
	}

	private void RefreshPreview()
	{
		_previewDirty = false;
		if (_previewHost is null || !IsInstanceValid(_previewHost))
		{
			return;
		}

		EnsureCard();
		if (_card is null)
		{
			return;
		}

		_card.Appearance = Appearance;
		_card.SetBackSide(_sampleBackSide);
		_card.SetIce(_sampleIce);
		_card.SetCard(_sampleKnown ? CreateKnownSample(_sampleIce) : CreateHiddenSample());
		_card.SetTargetCandidate(_sampleTarget);
		_card.SetSelected(_sampleSelected);
		_card.SetActionableSource(_sampleActionable);
		_card.MouseFilter = Control.MouseFilterEnum.Ignore;
		_card.FocusMode = Control.FocusModeEnum.None;

		if (_sampleHovered != _lastHovered)
		{
			_card.EmitSignal(_sampleHovered
				? Control.SignalName.MouseEntered
				: Control.SignalName.MouseExited);
			_lastHovered = _sampleHovered;
		}

		if (_caption is not null)
		{
			_caption.Text = _sampleKnown
				? "SYNTHETIC CARD · KNOWN"
				: $"SYNTHETIC CARD · {(_sampleBackSide == PlayerSide.Corp ? "CORP" : "RUNNER")} BACK";
		}
	}

	private void EnsureCard()
	{
		if (_card is not null && IsInstanceValid(_card))
		{
			return;
		}

		_card = TableCardControl.Create();
		_previewHost!.AddChild(_card);
	}

	private static CardViewModel CreateKnownSample(bool ice) => new(
		"preview:synthetic-known",
		null,
		"Synthetic Asset: Long Editor Preview Title",
		ice ? "ICE" : "Asset",
		true,
		false,
		3,
		2,
		ice ? 5 : null,
		null,
		null,
		PreviewSubtypes,
		"<strong>Install</strong> this synthetic card for a long rules preview. "
		+ "The editor sample contains no authoritative game state.",
		"Shaper");

	private static CardViewModel CreateHiddenSample() => new(
		"preview:synthetic-hidden",
		null,
		"Hidden card",
		string.Empty,
		false,
		false,
		0,
		null,
		null,
		null,
		null,
		Array.Empty<string>(),
		string.Empty);

	private static CardAppearance LoadDefaultAppearance()
	{
		CardAppearance? appearance = GD.Load<CardAppearance>(TableCardControl.DefaultAppearancePath);
		if (appearance is not null)
		{
			return appearance;
		}

		GD.PushError($"Shared card appearance resource is missing: {TableCardControl.DefaultAppearancePath}");
		return new CardAppearance();
	}
}
