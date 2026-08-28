#!/usr/bin/env bash
set -euo pipefail

verify_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$verify_root/scripts/env.sh"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is unavailable. Run ./scripts/bootstrap.sh first."
  exit 1
fi

if [[ -z "${GODOT4:-}" || ! -x "$GODOT4" ]]; then
  echo "Godot .NET is unavailable. Run ./scripts/bootstrap.sh first."
  exit 1
fi

MSBUILDDISABLENODEREUSE=1 dotnet restore "$verify_root/Netrunner.sln" -m:1
MSBUILDDISABLENODEREUSE=1 dotnet build "$verify_root/Netrunner.sln" --no-restore -m:1
MSBUILDDISABLENODEREUSE=1 dotnet test "$verify_root/Netrunner.sln" --no-build -m:1
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- self-test --seed 42
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- doctor --json
card_data_dir="$verify_root/data/vendor/system-gateway"
card_data_cards="$card_data_dir/cards.json"
card_data_manifest="$card_data_dir/manifest.json"
card_data_copyright="$card_data_dir/SOURCE_COPYRIGHT.md"
card_data_present=0
for card_data_file in "$card_data_cards" "$card_data_manifest" "$card_data_copyright"; do
  if [[ -e "$card_data_file" ]]; then
    card_data_present=$((card_data_present + 1))
  fi
done
if ((card_data_present == 0)); then
  echo "card-data=skipped (local System Gateway snapshot absent; no network fetch)"
elif ((card_data_present == 3)) && [[ -f "$card_data_cards" && -f "$card_data_manifest" && -f "$card_data_copyright" ]]; then
  dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
    card-data validate --root "$verify_root" --json
else
  echo "card-data validation failed: local System Gateway snapshot is partial." >&2
  exit 1
fi
m1_replay_dir="$(mktemp -d "${TMPDIR:-/tmp}/netrunner-m1.XXXXXX")"
if [[ -z "$m1_replay_dir" || ! -d "$m1_replay_dir" ]]; then
  echo "Unable to create the M1 replay verification directory." >&2
  exit 1
fi
trap 'rm -rf -- "$m1_replay_dir"' EXIT
for m1_goal in corp-score runner-steal deck-out; do
  dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
    play --seed 42 --goal "$m1_goal" --json
done
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
  play --seed 42 --goal corp-score --json --replay-out "$m1_replay_dir/corp-score.json"
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
  replay "$m1_replay_dir/corp-score.json" --json
XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --editor --path "$verify_root/src/Netrunner.Godot" --quit-after 60
XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" -- --smoke-test
"$verify_root/scripts/check-secrets.sh"

echo "Development environment verified."
