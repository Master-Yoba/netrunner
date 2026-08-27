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
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build -- self-test --seed 42
"$GODOT4" --headless --editor --path "$verify_root/src/Netrunner.Godot" --quit

echo "Development environment verified."
