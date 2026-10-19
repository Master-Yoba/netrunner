#!/usr/bin/env bash
# EL.8: plays every deck of an Elevation-pool format through the client, as the
# human seat of its side, with the M4 full-match smoke (the contract agent clicks
# for the human; the latest bot is the opponent). The format is Gateway + Elevation
# (the default), NSG Core Sets or Startup 2026 (VP.8). Usage:
#   scripts/elevation-deck-smoke.sh [--format gateway-elevation|nsg-core-sets|startup-2026] [--jobs N] [--output <new dir>]
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$root/scripts/env.sh"
jobs=2
format=gateway-elevation
output="$root/artifacts/elevation-deck-smoke/$(date -u +%Y%m%dT%H%M%SZ)"
usage="Usage: scripts/elevation-deck-smoke.sh [--format gateway-elevation|nsg-core-sets|startup-2026] [--jobs N] [--output <new dir>]"
while (($#)); do
  case "$1" in
    --format) format="$2"; shift 2 ;;
    --jobs) jobs="$2"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    *) echo "$usage" >&2; exit 2 ;;
  esac
done
if [[ -e "$output" ]]; then echo "--output must name a new directory." >&2; exit 2; fi
mkdir -p "$output"

case "$format" in
  gateway-elevation)
    # D-EL-10: the Core Battle Box.
    corps=(elevation-box-nebula-fast-advance elevation-box-untaian-shell-game elevation-box-zwicky-supermodernism
      elevation-box-leo-glacier)
    runners=(elevation-box-topan elevation-box-muslihat elevation-box-magdalene) ;;
  nsg-core-sets)
    corps=(nsg-core-agency nsg-core-brick-stack nsg-core-brutal-efficiency nsg-core-fashion-lab nsg-core-fine-print
      nsg-core-gimbatul nsg-core-glyph-of-warding nsg-core-hidden-funds nsg-core-not-so-subtle nsg-core-peculiarity
      nsg-core-pork-chops nsg-core-quick-returns)
    runners=(nsg-core-bowel-movements nsg-core-dashing-mad nsg-core-enthusiasm nsg-core-flow-and-ebb nsg-core-prick-thyself
      nsg-core-professional-opportunities nsg-core-sabbatical nsg-core-shootin-n-lootin nsg-core-tickets-please) ;;
  startup-2026)
    # D-VP-6: real players' tournament lists.
    corps=(startup-leo-glacier startup-precision-design-campaigns startup-melies-u startup-au-co-upgrades
      startup-reality-plus-tags startup-editorial-tempo startup-zwicky-measured-response startup-built-to-last)
    runners=(startup-loup-cookbook startup-ryo-chain-reaction startup-muslihat-clean-getaway startup-zahya-runs
      startup-magdalene startup-tao-overlap) ;;
  *) echo "$usage" >&2; exit 2 ;;
esac
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
decks=$((${#corps[@]} + ${#runners[@]}))
if ((status == 0)); then echo "ELEVATION_DECK_SMOKE PASS format=$format decks=$decks output=$output"; else echo "ELEVATION_DECK_SMOKE FAIL format=$format output=$output"; fi
exit $status
