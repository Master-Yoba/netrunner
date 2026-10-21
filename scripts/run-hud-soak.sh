#!/usr/bin/env bash
# Run HUD soak: plays whole Gateway games through the client with the HUD on and checks it on
# every frame of every run (see Main.RunHudSoak.cs). Usage:
#   scripts/run-hud-soak.sh [--games N] [--jobs N] [--seed S] [--max-actions N] [--output <dir>]
set -euo pipefail
soak_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$soak_root/scripts/env.sh"
games=4; jobs=4; seed=1; max_actions=300
output="$soak_root/artifacts/run-hud-soak/$(date -u +%Y%m%dT%H%M%SZ)-$$"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --games) games="$2"; shift 2 ;;
    --jobs) jobs="$2"; shift 2 ;;
    --seed) seed="$2"; shift 2 ;;
    --max-actions) max_actions="$2"; shift 2 ;;
    --output) output="$2"; shift 2 ;;
    *) echo "Unknown option $1" >&2; exit 2 ;;
  esac
done
dotnet build "$soak_root/src/Netrunner.Godot" -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official -v q >&2
mkdir -p "$output"
run_game() {
  local game="$1" log="$output/game-$1.log"
  env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" timeout 1800 "$GODOT4" --headless --path "$soak_root/src/Netrunner.Godot" \
    -- --run-hud-soak --soak-game "$game" --soak-seed "$seed" --soak-max-actions "$max_actions" --soak-output "$output/game-$game" \
    >"$log" 2>&1 || true
}
# Games are independent launches; run a few at once.
for ((game = 0; game < games; game++)); do
  run_game "$game" &
  while (( $(jobs -rp | wc -l) >= jobs )); do sleep 2; done
done
wait
failed=0
for ((game = 0; game < games; game++)); do
  log="$output/game-$game.log"
  grep -E "RUN_HUD_SOAK (PASS|FAIL|VIOLATION)" "$log" || { echo "RUN_HUD_SOAK FAIL game=$game no result, see $log"; failed=1; }
  grep -q "RUN_HUD_SOAK PASS" "$log" || failed=1
done
if (( failed )); then echo "RUN_HUD_SOAK_MATRIX FAIL games=$games output=$output"; exit 1; fi
echo "RUN_HUD_SOAK_MATRIX PASS games=$games output=$output"
