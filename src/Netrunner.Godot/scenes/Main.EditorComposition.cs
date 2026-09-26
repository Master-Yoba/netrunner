using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

/// <summary>Production scene binding; diagnostic modes retain their legacy shell.</summary>
public partial class Main
{
	private void BindProductionShell()
	{
		_layout = GetNode<VBoxContainer>("Margin/Layout");
		Control header = _layout.GetNode<Control>("Header");
		_status = header.GetNode<Label>("Status");
		_phase = header.GetNode<Label>("Phase");
		Control setup = _layout.GetNode<Control>("MatchSetup");
		_startPanel = setup.GetNode<VBoxContainer>("%SetupContent");
		_corpPreset = setup.GetNode<OptionButton>("%CorpPreset");
		_runnerPreset = setup.GetNode<OptionButton>("%RunnerPreset");
		_corpPreset.Clear();
		_runnerPreset.Clear();
		PopulatePresetButton(_corpPreset, PlayerSide.Corp, _corpPresetIds);
		PopulatePresetButton(_runnerPreset, PlayerSide.Runner, _runnerPresetIds);
		_humanSide = setup.GetNode<OptionButton>("%HumanSide");
		_seedInput = setup.GetNode<LineEdit>("%SeedInput");
		_startButton = setup.GetNode<Button>("%StartMatch");
		bool available = HasAuthenticatedOfficialContent();
		_startButton.Text = available ? "Start selected official match" : "Official match unavailable";
		_startButton.Disabled = !available;
		_startButton.Pressed += BeginMatch;
		_galleryButton = setup.GetNode<Button>("%OpenGallery");
		_galleryButton.Visible = DeveloperToolsEnabled;
		_galleryButton.Disabled = !_metadataSnapshot.IsOfficial;
		_galleryButton.Pressed += ShowCardGallery;
		if (!_metadataSnapshot.IsOfficial)
			_galleryButton.TooltipText = "Provision the authenticated 77-card snapshot for the dev gallery.";
		setup.GetNode<Button>("%InteractionLab").Pressed += OpenInteractionLab;
		BuildCardTable();
		BuildCardZoomOverlay();
		BindOfflineSetup(setup);
		ApplyAccessibilityFocusOrder(_startPanel);
		ApplyAccessibilityFocusOrder(_board);
	}
}
