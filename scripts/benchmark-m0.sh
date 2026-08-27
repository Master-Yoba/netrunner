#!/usr/bin/env bash
set -euo pipefail

benchmark_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$benchmark_root/scripts/env.sh"

benchmark_samples="${M0_BENCHMARK_SAMPLES:-10}"
benchmark_output_dir="$benchmark_root/artifacts/benchmarks"
benchmark_results="$benchmark_output_dir/m0-loop-results.tsv"
mkdir -p "$benchmark_output_dir" "$NETRUNNER_GODOT_DATA"

if ! [[ "$benchmark_samples" =~ ^[1-9][0-9]*$ ]]; then
  echo "M0_BENCHMARK_SAMPLES must be a positive integer."
  exit 1
fi

printf 'benchmark\tmilliseconds\n' > "$benchmark_results"

measure()
{
  local label="$1"
  shift
  local started finished elapsed
  started="$(date +%s%N)"
  "$@" >/dev/null
  finished="$(date +%s%N)"
  elapsed=$(((finished - started) / 1000000))
  printf '%s\t%s\n' "$label" "$elapsed" >> "$benchmark_results"
  printf '  %-24s %6d ms\n' "$label" "$elapsed"
}

godot_observe_loop()
{
  dotnet build \
    "$benchmark_root/src/Netrunner.Godot/Netrunner.Godot.csproj" \
    --no-restore >/dev/null
  XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
    XDG_CONFIG_HOME="$NETRUNNER_GODOT_DATA" \
    "$GODOT4" --headless --path "$benchmark_root/src/Netrunner.Godot" \
    -- --smoke-test >/dev/null
}

echo "Running $benchmark_samples samples for each M0 loop"
for ((sample = 1; sample <= benchmark_samples; sample++)); do
  echo "Sample $sample/$benchmark_samples"

  dotnet clean "$benchmark_root/Netrunner.sln" --nologo >/dev/null
  dotnet restore "$benchmark_root/Netrunner.sln" --nologo >/dev/null
  measure clean-solution-build \
    dotnet build "$benchmark_root/Netrunner.sln" --no-restore --nologo

  touch "$benchmark_root/src/Netrunner.Core/FoundationEventBatch.cs"
  measure focused-core-test \
    dotnet test \
    "$benchmark_root/tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj" \
    --no-restore --nologo

  touch "$benchmark_root/tests/Netrunner.Cards.Tests/CardDefinitionTests.cs"
  measure focused-card-test \
    dotnet test \
    "$benchmark_root/tests/Netrunner.Cards.Tests/Netrunner.Cards.Tests.csproj" \
    --no-restore --nologo

  touch "$benchmark_root/src/Netrunner.Godot/scenes/Main.cs"
  measure godot-edit-to-observe godot_observe_loop
done

echo
echo "Summary (milliseconds)"
printf '%-24s %10s %10s %10s\n' benchmark mean p50 p95
for label in clean-solution-build focused-core-test focused-card-test godot-edit-to-observe; do
  mapfile -t values < <(awk -F '\t' -v label="$label" '$1 == label { print $2 }' "$benchmark_results" | sort -n)
  count="${#values[@]}"
  p50_index=$(((count - 1) / 2))
  p95_index=$((((95 * count + 99) / 100) - 1))
  mean="$(awk -F '\t' -v label="$label" '$1 == label { sum += $2; count++ } END { printf "%.1f", sum / count }' "$benchmark_results")"
  printf '%-24s %10s %10s %10s\n' \
    "$label" "$mean" "${values[$p50_index]}" "${values[$p95_index]}"
done

echo "Raw results: $benchmark_results"
