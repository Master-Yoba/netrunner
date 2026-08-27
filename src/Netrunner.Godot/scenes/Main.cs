using Godot;
using Netrunner.Core;

namespace Netrunner.GodotClient;

public partial class Main : Control
{
    public override void _Ready()
    {
        Label status = GetNode<Label>("Center/Status");
        status.Text = $"Netrunner Solo\nFoundation ready · Rules {BuildMetadata.RulesVersion}";
    }
}

