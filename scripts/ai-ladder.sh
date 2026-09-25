#!/usr/bin/env bash
# Head-to-head AI rating ladder on official content. Usage:
#   scripts/ai-ladder.sh [--entrants random,contract,easy,standard] [--seeds-per-pairing N]
#     [--seed S] [--bootstrap N] [--workers N] [--output <new dir>] [--json]
# Needs the provisioned System Gateway snapshot and never downloads it.
set -euo pipefail

ladder_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ladder_root/scripts/env.sh"
ladder_data="$ladder_root/data/vendor/system-gateway"
if [[ ! -f "$ladder_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi

ladder_has_output=0
for ladder_argument in "$@"; do
  if [[ "$ladder_argument" == --output ]]; then ladder_has_output=1; fi
done
ladder_arguments=(--content official)
if ((ladder_has_output == 0)); then
  ladder_arguments+=(--output "$ladder_root/artifacts/ai-ladder/$(date -u +%Y%m%dT%H%M%SZ)-$$")
fi

# Build in an isolated directory so the ladder never replaces the solution's
# content-specific build output.
ladder_build="$ladder_root/.tools/ladder-cli"
dotnet build "$ladder_root/src/Netrunner.Cli" -c Release --artifacts-path "$ladder_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$ladder_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$ladder_data" >&2
exec dotnet "$ladder_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" ai-ladder "${ladder_arguments[@]}" "$@"
