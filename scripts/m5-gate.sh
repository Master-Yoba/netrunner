#!/usr/bin/env bash
set -euo pipefail
gate_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$gate_root/scripts/env.sh"
python3 "$gate_root/scripts/freeze-m5-gate.py"
python3 "$gate_root/scripts/test-m5-gate.py"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$gate_root/data/vendor/system-gateway"
# Release: the benchmarks run optimized code; their results don't depend on the build configuration.
dotnet build "$gate_root/src/Netrunner.Cli" -c Release --no-restore -m:1 --disable-build-servers >&2
gate_output="${NETRUNNER_M5_GATE_OUTPUT:-$gate_root/artifacts/m5/gate-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
python3 "$gate_root/scripts/m5_gate.py" --output "$gate_output" "$@"
