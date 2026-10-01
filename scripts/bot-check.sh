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
if bash "$check_root/scripts/m5-gate.sh" --quick >"$logs/m5-quick.log" 2>&1; then
  echo "bot-check m5-quick=PASS"
else
  echo "bot-check m5-quick=FAIL ($logs/m5-quick.log)"
  exit 1
fi

official=(-p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official)
dotnet build "$check_root/Netrunner.sln" "${official[@]}" -m:1 --disable-build-servers >"$logs/build.log" 2>&1 \
  || { echo "bot-check build=FAIL ($logs/build.log)"; exit 1; }
# Five official-data cases of this theory fail on dev too (they're tracked); left in, they'd hide a new failure.
known="FullyQualifiedName!~SessionPolicyMatrixTests.EveryNestedBoundaryRetainsEngineEventsChoicesAndPolicy"
pids=()
for project in AI Match Cli; do
  dotnet test "$check_root/tests/Netrunner.$project.Tests" "${official[@]}" --no-build --filter "$known" >"$logs/$project.log" 2>&1 &
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
exit "$status"
