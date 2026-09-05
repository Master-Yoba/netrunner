#!/usr/bin/env bash
set -euo pipefail
play_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$play_root/scripts/env.sh"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$play_root/data/vendor/system-gateway"
if [[ ! -f "$NetrunnerCardDataInputRoot/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 1
fi
dotnet build "$play_root/src/Netrunner.Godot" --no-restore
XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" "$GODOT4" --path "$play_root/src/Netrunner.Godot" -- "$@"
