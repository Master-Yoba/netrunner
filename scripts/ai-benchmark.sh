#!/usr/bin/env bash
set -euo pipefail

benchmark_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$benchmark_root/scripts/env.sh"
benchmark_suite="${1:-smoke}"
if (($#)); then shift; fi
case "$benchmark_suite" in
  smoke|full)
    export NetrunnerContentEnabled=true
    export NetrunnerCardDataMode=official
    export NetrunnerCardDataInputRoot="$benchmark_root/data/vendor/system-gateway"
    benchmark_manifest="$benchmark_root/data/m5/benchmarks/contract-$benchmark_suite-v1.json"
    ;;
  developer)
    export NetrunnerContentEnabled=false
    export NetrunnerCardDataMode=synthetic
    benchmark_manifest="$benchmark_root/data/m5/benchmarks/contract-developer-v1.json"
    ;;
  *) echo "Usage: scripts/ai-benchmark.sh smoke|full|developer [--output <new directory>] [--workers 1..64] [--json]" >&2; exit 2 ;;
esac

benchmark_arguments=("$benchmark_manifest")
benchmark_has_output=0
for benchmark_argument in "$@"; do
  if [[ "$benchmark_argument" == --output ]]; then benchmark_has_output=1; fi
done
if ((benchmark_has_output == 0)); then
  benchmark_arguments+=(--output "$benchmark_root/artifacts/m5/$benchmark_suite-$(date -u +%Y%m%dT%H%M%SZ)-$$")
fi

# Build against already provisioned local content. No restore or download is
# hidden inside execution, and the manifest pins must match the resulting DLL.
dotnet build "$benchmark_root/src/Netrunner.Cli" --no-restore -m:1 --disable-build-servers >&2
dotnet "$benchmark_root/src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll" \
  ai-benchmark "${benchmark_arguments[@]}" "$@"
