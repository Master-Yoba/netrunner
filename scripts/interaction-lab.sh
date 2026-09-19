#!/usr/bin/env bash
set -euo pipefail
lab_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$lab_root/scripts/env.sh"
dotnet build "$lab_root/src/Netrunner.Godot"
XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" "$GODOT4" --path "$lab_root/src/Netrunner.Godot" -- --interaction-lab "$@"
