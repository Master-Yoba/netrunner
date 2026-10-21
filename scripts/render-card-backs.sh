#!/usr/bin/env bash
set -euo pipefail
# Rasterizes the original card backs in tools/card-backs/*.svg into the
# committed res://card-backs PNGs with Godot's own SVG renderer.
render_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$render_root/scripts/env.sh"

render_project="$(mktemp -d)"
trap 'rm -rf "$render_project"' EXIT
cat >"$render_project/project.godot" <<'EOF'
config_version=5
EOF
cat >"$render_project/render.gd" <<'EOF'
extends SceneTree

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	for i in range(0, args.size(), 2):
		var image := Image.new()
		if image.load_svg_from_string(FileAccess.get_file_as_string(args[i])) != OK \
				or image.save_png(args[i + 1]) != OK:
			printerr("Could not render %s" % args[i])
			quit(1)
			return
		print("%s -> %s (%dx%d)" % [args[i], args[i + 1], image.get_width(), image.get_height()])
	quit()
EOF

args=()
for side in corp runner; do
  args+=("$render_root/tools/card-backs/$side.svg" "$render_root/src/Netrunner.Godot/card-backs/$side.png")
done
"$GODOT4" --headless --path "$render_project" --script "$render_project/render.gd" -- "${args[@]}"
