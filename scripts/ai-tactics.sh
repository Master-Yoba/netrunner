#!/usr/bin/env bash
set -euo pipefail

tactical_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$tactical_root/scripts/env.sh"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$tactical_root/data/vendor/system-gateway"
tactical_arguments=("$tactical_root/data/m5/benchmarks/tactical-contract-v1.json")
tactical_has_output=0
for tactical_argument in "$@"; do
  if [[ "$tactical_argument" == --output ]]; then tactical_has_output=1; fi
done
if ((tactical_has_output == 0)); then
  tactical_arguments+=(--output "$tactical_root/artifacts/m5/tactics-$(date -u +%Y%m%dT%H%M%SZ)-$$")
fi
dotnet build "$tactical_root/src/Netrunner.Cli" --no-restore -m:1 --disable-build-servers >&2
dotnet "$tactical_root/src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll" ai-tactics "${tactical_arguments[@]}" "$@"
