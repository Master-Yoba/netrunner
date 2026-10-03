#!/usr/bin/env bash
# Prints the fuzz and ladder contents (deck pools) a change can affect, one per line, so per-slice runs
# cover only those (AGENTS.md, verification tiers). Usage:
#   scripts/affected-pools.sh [<base-ref>]     (default origin/dev; the working tree's changes count)
# - Docs, tests, the Godot client, scripts and tools affect no pool.
# - A deck file affects its pool.
# - Card behavior specs are scoped by card: the pools whose decks hold a card whose behavior fingerprint
#   (card-fingerprints) differs between the base and this tree.
# - Anything else under src/ or data/ (the engine, bots, Match, the CLI, card data) affects every pool.
# Needs the provisioned official card data.
set -euo pipefail

scope_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$scope_root/scripts/env.sh"
base="${1:-origin/dev}"
merge_base="$(git -C "$scope_root" merge-base "$base" HEAD)"
all_pools=(official gateway elevation gateway-elevation nsg-core-sets)

declare -A pools=()
specs_changed=0
everything=0
while IFS= read -r path; do
  [[ -n "$path" ]] || continue
  case "$path" in
    docs/*|tests/*|tools/*|scripts/*|src/Netrunner.Godot/*|.github/*|*.md|AGENTS.md|Makefile) ;;
    data/decks/gateway-sample-decks.json) pools[gateway]=1 ;;
    data/decks/gateway-elevation-decks.json) pools[gateway-elevation]=1 ;;
    data/decks/nsg-core-sets-decks.json) pools[nsg-core-sets]=1 ;;
    data/decks/elevation-simulation-decks.json) pools[elevation]=1 ;;
    data/m4/presets.json) pools[official]=1 ;;
    src/Netrunner.Cards/Gateway*BehaviorSpecs.cs|src/Netrunner.Cards/Gateway*CardCodes.cs|\
    src/Netrunner.Cards/GatewayBehaviorCatalog.cs|src/Netrunner.Cards/ElevationBehaviorCatalog.cs|\
    src/Netrunner.Cards/Elevation/*) specs_changed=1 ;;
    src/*|data/*) everything=1 ;;
    *) ;;
  esac
done < <( { git -C "$scope_root" diff --name-only "$merge_base"; git -C "$scope_root" ls-files --others --exclude-standard; } | sort -u)

if ((everything == 1)); then
  printf '%s\n' "${all_pools[@]}"
  exit 0
fi

if ((specs_changed == 1)); then
  build() {
    local tree="$1" out="$2"
    dotnet build "$tree/src/Netrunner.Cli" -c Release --artifacts-path "$out" -m:1 --disable-build-servers \
      "-p:NetrunnerCardDataGeneratedFile=$out/generated/GeneratedCardData.g.cs" \
      -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official \
      "-p:NetrunnerCardDataInputRoot=$scope_root/data/vendor/system-gateway" \
      "-p:NetrunnerElevationCardDataInputRoot=$scope_root/data/vendor/elevation" >&2
  }
  tree="$scope_root/.tools/scope-worktree"
  if [[ ! -e "$tree/.git" ]]; then git -C "$scope_root" worktree add --quiet --detach "$tree" "$merge_base"; fi
  git -C "$tree" checkout --quiet --detach --force "$merge_base"
  build "$tree" "$scope_root/.tools/scope-cli-base"
  build "$scope_root" "$scope_root/.tools/scope-cli-head"
  base_json="$(mktemp)"; head_json="$(mktemp)"
  trap 'rm -f "$base_json" "$head_json"' EXIT
  if ! dotnet "$scope_root/.tools/scope-cli-base/bin/Netrunner.Cli/release/Netrunner.Cli.dll" card-fingerprints >"$base_json" 2>/dev/null; then
    # A base without card-fingerprints can't be compared card by card.
    printf '%s\n' "${all_pools[@]}"
    exit 0
  fi
  dotnet "$scope_root/.tools/scope-cli-head/bin/Netrunner.Cli/release/Netrunner.Cli.dll" card-fingerprints >"$head_json"
  while IFS= read -r pool; do pools[$pool]=1; done < <(python3 - "$base_json" "$head_json" <<'PY'
import json, sys
base, head = (json.load(open(path)) for path in sys.argv[1:3])
changed = {code for code in set(base["cards"]) | set(head["cards"])
           if base["cards"].get(code) != head["cards"].get(code)}
for pool in sorted(set(base["pools"]) | set(head["pools"])):
    codes = set(base["pools"].get(pool, [])) | set(head["pools"].get(pool, []))
    if codes & changed:
        print(pool)
PY
)
fi

for pool in "${all_pools[@]}"; do
  if [[ -n "${pools[$pool]:-}" ]]; then echo "$pool"; fi
done
