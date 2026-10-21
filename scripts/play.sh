#!/usr/bin/env bash
set -euo pipefail
play_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$play_root/scripts/env.sh"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$play_root/data/vendor/system-gateway"
export NETRUNNER_CARD_BACK_DIRECTORY="$play_root/.tools/card-backs"
if [[ ! -f "$NetrunnerCardDataInputRoot/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 1
fi
play_diagnostics_root="$NETRUNNER_GODOT_DATA/diagnostics"
mkdir -p "$play_diagnostics_root"
play_launch_dir="$(mktemp -d "$play_diagnostics_root/launch.XXXXXX")"
play_output_log="$play_launch_dir/play-output.log"
godot_log_file="$play_launch_dir/godot.log"
export NETRUNNER_LAUNCH_DIAGNOSTICS="$play_launch_dir"
export NETRUNNER_LAUNCH_WORKING_DIRECTORY="$PWD"
echo "PLAY_DIAGNOSTICS directory=$play_launch_dir"
echo "PLAY_DIAGNOSTICS output=$play_output_log godot=$godot_log_file"

set +e
{
  dotnet build "$play_root/src/Netrunner.Godot"
  play_build_status=$?
  if (( play_build_status != 0 )); then
    exit "$play_build_status"
  fi
  netrunner_import_godot_assets "$play_root/src/Netrunner.Godot" "$play_launch_dir/godot-import.log" || exit 1
  # NETRUNNER_GODOT_ARGS passes engine options, for example --resolution 2560x1440.
  # The ${array[@]+...} form keeps an empty array safe under set -u in macOS bash 3.2.
  play_godot_args=()
  if [[ -n "${NETRUNNER_GODOT_ARGS:-}" ]]; then
    read -r -a play_godot_args <<< "$NETRUNNER_GODOT_ARGS"
  fi
  netrunner_pin_card_art_directory
  XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" "$GODOT4" \
    --path "$play_root/src/Netrunner.Godot" \
    --log-file "$godot_log_file" \
    ${play_godot_args[@]+"${play_godot_args[@]}"} \
    -- "$@"
} 2>&1 | tee "$play_output_log"
play_status="${PIPESTATUS[0]}"
set -e
exit "$play_status"
