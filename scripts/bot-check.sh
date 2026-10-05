#!/usr/bin/env bash
# The fast check for a bot or Match change, run per change: formatting, the quick M5 cases (smoke and
# tactics, with their worker-count repeats), and the official-data AI, Match and CLI suites side by side.
# The full gates (make verify, make m5-gate, make ai-tactics-verify) still run once per batch, before a push.
set -euo pipefail

check_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$check_root/scripts/env.sh"
logs="$check_root/artifacts/bot-check/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$logs"

dotnet format "$check_root/Netrunner.sln" --verify-no-changes
echo "bot-check format=PASS"

# The quick M5 gate builds and freezes its own copy of the CLI first, so the official build below can't change it.
# make gates sets BOT_CHECK_SKIP_M5_QUICK=1: its full m5-gate already ran these cases.
if [[ "${BOT_CHECK_SKIP_M5_QUICK:-0}" == 1 ]]; then
  echo "bot-check m5-quick=SKIP (the full m5-gate covers it)"
elif bash "$check_root/scripts/m5-gate.sh" --quick >"$logs/m5-quick.log" 2>&1; then
  echo "bot-check m5-quick=PASS"
else
  echo "bot-check m5-quick=FAIL ($logs/m5-quick.log)"
  exit 1
fi

official=(-p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official)
dotnet build "$check_root/Netrunner.sln" "${official[@]}" -m:1 --disable-build-servers >"$logs/build.log" 2>&1 \
  || { echo "bot-check build=FAIL ($logs/build.log)"; exit 1; }
pids=()
for project in AI Match Cli; do
  dotnet test "$check_root/tests/Netrunner.$project.Tests" "${official[@]}" --no-build >"$logs/$project.log" 2>&1 &
  pids+=("$!")
done
status=0
for index in "${!pids[@]}"; do
  project=$(echo AI Match Cli | cut -d' ' -f$((index + 1)))
  if wait "${pids[$index]}"; then
    echo "bot-check $project=PASS $(grep -E '^(Passed|Failed)!' "$logs/$project.log" | tail -1)"
  else
    echo "bot-check $project=FAIL $(grep -E '^(Passed|Failed)!' "$logs/$project.log" | tail -1) ($logs/$project.log)"
    status=1
  fi
done

# Bot plan S2.3, the per-change suites (opt in with BOT_CHECK_SUITES=1): the regret suite's held-out roots and the puzzle
# sets for the latest versions (or BOT_CHECK_VERSIONS), and the Oddity lints over the saved games in BOT_CHECK_LINT_GAMES.
if [[ "${BOT_CHECK_SUITES:-0}" == 1 ]]; then
  suites_build="$check_root/.tools/bot-check-cli"
  if ! dotnet build "$check_root/src/Netrunner.Cli" -c Release --artifacts-path "$suites_build" -m:1 --disable-build-servers \
    "-p:NetrunnerCardDataGeneratedFile=$suites_build/generated/GeneratedCardData.g.cs" "${official[@]}" \
    "-p:NetrunnerCardDataInputRoot=$check_root/data/vendor/system-gateway" >"$logs/suites-build.log" 2>&1; then
    echo "bot-check suites=FAIL (build: $logs/suites-build.log)"
    exit 1
  fi
  cli=(dotnet "$suites_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll")
  if "${cli[@]}" ai-regret score "$check_root/data/ai/regret/gateway-elevation" --version "${BOT_CHECK_VERSIONS:-latest}" --split held-out \
    --workers 12 --json >"$logs/regret.json" 2>"$logs/regret.err"; then
    python3 - "$logs/regret.json" <<'PY'
import json, sys
report = json.load(open(sys.argv[1]))["report"]
print("bot-check regret (box, held-out) " + " ".join(
    f"{entry['version']}={entry['summaries'][0]['meanRegret']:.4f} [{entry['summaries'][0]['lower']:.4f}, {entry['summaries'][0]['upper']:.4f}]"
    for entry in report))
PY
  else
    echo "bot-check regret=FAIL ($logs/regret.err)"
    status=1
  fi
  for pool in gateway-elevation nsg-core-sets; do
    if "${cli[@]}" ai-puzzles run "$check_root/data/ai/puzzles/$pool" --workers 12 >"$logs/puzzles-$pool.log" 2>&1; then
      echo "bot-check puzzles $pool $(head -1 "$logs/puzzles-$pool.log" | grep -o 'passed=[0-9]*/[0-9]*')"
    else
      echo "bot-check puzzles $pool=FAIL ($logs/puzzles-$pool.log)"
      status=1
    fi
  done
  if [[ -d "${BOT_CHECK_LINT_GAMES:-}" ]]; then
    "${cli[@]}" ai-lint "$BOT_CHECK_LINT_GAMES" --workers 12 >"$logs/lint.log" 2>&1 \
      && echo "bot-check lint $(head -1 "$logs/lint.log" | awk '{split($2, g, "="); split($3, f, "="); printf "games=%s findings=%s (%.1f per 1,000 games)", g[2], f[2], 1000 * f[2] / g[2]}')" \
      || { echo "bot-check lint=FAIL ($logs/lint.log)"; status=1; }
  else
    echo "bot-check lint=SKIP (set BOT_CHECK_LINT_GAMES to a saved ladder run)"
  fi
fi
exit "$status"
