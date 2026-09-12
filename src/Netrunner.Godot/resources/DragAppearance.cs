using Godot;

namespace Netrunner.GodotClient;

/// <summary>
/// Editor-authored values for the board's pointer drag presentation.
///
/// The resource contains presentation values only. Drag state, source cards,
/// and legal destinations remain owned by the board interaction controller.
/// </summary>
[Tool]
[GlobalClass]
public partial class DragAppearance : Resource
{
	private float _dragThreshold = 8.0f;
	private float _previewScale = 1.05f;
	private float _followSmoothing = 0.04f;
	private float _returnDuration = 0.16f;
	private float _edgeWidth = 36.0f;
	private float _scrollSpeed = 600.0f;
	private Color _legalColor = new(0.30f, 0.96f, 0.76f, 1.0f);
	private Color _currentColor = new(1.0f, 0.78f, 0.35f, 1.0f);
	private StyleBox? _legalStyle;
	private StyleBox? _currentStyle;

	[ExportGroup("Gesture")]
	[Export(PropertyHint.Range, "0,64,0.5")]
	public float DragThreshold
	{
		get => _dragThreshold;
		set => SetFloat(ref _dragThreshold, Mathf.Clamp(value, 0.0f, 64.0f));
	}

	[Export(PropertyHint.Range, "1,1.5,0.01")]
	public float PreviewScale
	{
		get => _previewScale;
		set => SetFloat(ref _previewScale, Mathf.Clamp(value, 1.0f, 1.5f));
	}

	/// <summary>Pointer-follow smoothing in seconds; zero tracks immediately.</summary>
	[Export(PropertyHint.Range, "0,0.2,0.005,suffix:s")]
	public float FollowSmoothing
	{
		get => _followSmoothing;
		set => SetFloat(ref _followSmoothing, Mathf.Clamp(value, 0.0f, 0.2f));
	}

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float ReturnDuration
	{
		get => _returnDuration;
		set => SetFloat(ref _returnDuration, Mathf.Clamp(value, 0.0f, 1.0f));
	}

	[ExportGroup("Edge scrolling")]
	[Export(PropertyHint.Range, "0,160,1")]
	public float EdgeWidth
	{
		get => _edgeWidth;
		set => SetFloat(ref _edgeWidth, Mathf.Clamp(value, 0.0f, 160.0f));
	}

	[Export(PropertyHint.Range, "0,2400,10")]
	public float ScrollSpeed
	{
		get => _scrollSpeed;
		set => SetFloat(ref _scrollSpeed, Mathf.Clamp(value, 0.0f, 2400.0f));
	}

	[ExportGroup("Destination colours")]
	[Export]
	public Color LegalColor
	{
		get => _legalColor;
		set => SetColor(ref _legalColor, value);
	}

	[Export]
	public Color CurrentColor
	{
		get => _currentColor;
		set => SetColor(ref _currentColor, value);
	}

	[ExportGroup("Destination styles")]
	[Export]
	public StyleBox? LegalStyle
	{
		get => _legalStyle;
		set => SetStyle(ref _legalStyle, value);
	}

	[Export]
	public StyleBox? CurrentStyle
	{
		get => _currentStyle;
		set => SetStyle(ref _currentStyle, value);
	}

	// These aliases keep callers descriptive without creating duplicate
	// Inspector properties for the same authored style boxes.
	public StyleBox? LegalStyleBox
	{
		get => LegalStyle;
		set => LegalStyle = value;
	}

	public StyleBox? CurrentStyleBox
	{
		get => CurrentStyle;
		set => CurrentStyle = value;
	}

	private void SetFloat(ref float field, float value)
	{
		if (Mathf.IsEqualApprox(field, value))
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private void SetColor(ref Color field, Color value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		EmitChanged();
	}

	private void SetStyle(ref StyleBox? field, StyleBox? value)
	{
		if (ReferenceEquals(field, value))
		{
			return;
		}

		field = value;
		EmitChanged();
	}
}
