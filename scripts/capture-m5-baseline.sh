#!/usr/bin/env bash
set -euo pipefail
baseline_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$baseline_root/scripts/env.sh"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$baseline_root/data/vendor/system-gateway"
export NETRUNNER_BASELINE_SOURCE_REVISION="$(git -C "$baseline_root" rev-parse HEAD)"
dotnet run --project "$baseline_root/tools/Netrunner.M5Baseline" -- "$@"
