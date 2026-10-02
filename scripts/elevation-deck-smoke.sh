#!/usr/bin/env bash
# EL.8: plays every Gateway + Elevation deck through the client, as the human
# seat of its side, with the M4 full-match smoke (the contract agent clicks for
# the human; the latest bot is the opponent). Usage:
#   scripts/elevation-deck-smoke.sh [--jobs N] [--output <new dir>]
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$root/scripts/env.sh"
jobs=2
output="$root/artifacts/elevation-deck-smoke/$(date -u +%Y%m%dT%H%M%SZ)"
while (($#)); do
  case "$1" in
    --jobs) jobs="$2"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    *) echo "Usage: scripts/elevation-deck-smoke.sh [--jobs N] [--output <new dir>]" >&2; exit 2 ;;
  esac
done
if [[ -e "$output" ]]; then echo "--output must name a new directory." >&2; exit 2; fi
mkdir -p "$output"

corps=(elevation-leo-bioroid-wall elevation-poetri-trendsetters elevation-auco-clone-vats elevation-untaian-silk-road
  elevation-nebula-headliners elevation-synapse-fast-news elevation-bangun-open-house elevation-zwicky-shell-companies)
runners=(elevation-ryo-phoenix-fire elevation-topan-street-gang elevation-barry-tri-maf elevation-muslihat-market-runs
  elevation-dewi-shadow-play elevation-magdalene-cryptarchitect)
cases=()
for index in "${!corps[@]}"; do cases+=("corp ${corps[$index]}~${runners[$((index % ${#runners[@]}))]}"); done
for index in "${!runners[@]}"; do cases+=("runner ${corps[$(((index + 3) % ${#corps[@]}))]}~${runners[$index]}"); done

run_case() {
  local side="$1" pairing="$2" log="$output/$1-${2//\~/--}.log"
  if timeout 420 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" NETRUNNER_CARD_ART_DIRECTORY="$output/missing-art" \
    "$GODOT4" --headless --path "$root/src/Netrunner.Godot" -- "--m4-full-smoke-$side" --preset "$pairing" --seed 47042 \
    >"$log" 2>&1 && grep -Fq "M4_FULL_SMOKE PASS" "$log"; then
    echo "elevation-deck-smoke $side $pairing PASS"
  else
    echo "elevation-deck-smoke $side $pairing FAIL ($log)"
    return 1
  fi
}
export -f run_case
export output GODOT4 NETRUNNER_GODOT_DATA root
status=0
printf '%s\n' "${cases[@]}" | xargs -P "$jobs" -L 1 bash -c 'run_case "$0" "$1"' || status=1
if ((status == 0)); then echo "ELEVATION_DECK_SMOKE PASS decks=14 output=$output"; else echo "ELEVATION_DECK_SMOKE FAIL output=$output"; fi
exit $status
