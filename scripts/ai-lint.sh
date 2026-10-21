#!/usr/bin/env bash
# Oddity lints (Wave 4.5, P1) over self-play games saved by the ladder. Usage:
#   scripts/ai-ladder.sh --entrants standard-w4a,standard --workers 12 --save-games artifacts/ai-lint/<corpus>
#   scripts/ai-lint.sh artifacts/ai-lint/<corpus> [--workers N] [--examples N] [--output <new file>] [--json]
# Needs the provisioned System Gateway snapshot and never downloads it.
set -euo pipefail

lint_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$lint_root/scripts/env.sh"
lint_data="$lint_root/data/vendor/system-gateway"
if [[ ! -f "$lint_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi

# Its own build directory, so a lint run never replaces a running ladder's build.
lint_build="$lint_root/.tools/lint-cli"
dotnet build "$lint_root/src/Netrunner.Cli" -c Release --artifacts-path "$lint_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$lint_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$lint_data" >&2
exec dotnet "$lint_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" ai-lint "$@"
