#!/usr/bin/env bash
# Random-legal rules fuzzing. Usage:
#   scripts/fuzz.sh official|elevation|gateway-elevation|nsg-core-sets|developer [--games N | --games-per-pairing N] [--verify-replays-every N] [--seed S] [--workers N] [--action-cap N] [--output <new dir>] [--json]
# Official fuzzing needs the provisioned System Gateway snapshot and never downloads it.
set -euo pipefail

fuzz_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$fuzz_root/scripts/env.sh"
fuzz_content="${1:-official}"
if (($#)); then shift; fi

fuzz_build_arguments=()
case "$fuzz_content" in
  official|elevation|gateway-elevation|nsg-core-sets)
    fuzz_data="$fuzz_root/data/vendor/system-gateway"
    if [[ ! -f "$fuzz_data/cards.json" ]]; then
      echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
      exit 2
    fi
    fuzz_build_arguments=(
      "-p:NetrunnerContentEnabled=true"
      "-p:NetrunnerCardDataMode=official"
      "-p:NetrunnerCardDataInputRoot=$fuzz_data"
    )
    ;;
  developer) ;;
  *) echo "Usage: scripts/fuzz.sh official|elevation|gateway-elevation|nsg-core-sets|developer [fuzz options]" >&2; exit 2 ;;
esac

fuzz_arguments=(--content "$fuzz_content")
fuzz_has_output=0
for fuzz_argument in "$@"; do
  if [[ "$fuzz_argument" == --output ]]; then fuzz_has_output=1; fi
done
if ((fuzz_has_output == 0)); then
  fuzz_arguments+=(--output "$fuzz_root/artifacts/fuzz/$fuzz_content-$(date -u +%Y%m%dT%H%M%SZ)-$$")
fi

# Build in an isolated directory so fuzzing never replaces the solution's
# content-specific build output.
fuzz_build_lane=official
if [[ "$fuzz_content" == developer ]]; then fuzz_build_lane=developer; fi
fuzz_build="$fuzz_root/.tools/fuzz-cli/$fuzz_build_lane"
dotnet build "$fuzz_root/src/Netrunner.Cli" -c Release --artifacts-path "$fuzz_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$fuzz_build/generated/GeneratedCardData.g.cs" \
  "${fuzz_build_arguments[@]}" >&2
exec dotnet "$fuzz_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" fuzz "${fuzz_arguments[@]}" "$@"
