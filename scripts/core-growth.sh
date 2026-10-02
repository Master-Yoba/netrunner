#!/usr/bin/env bash
# E6 Core-growth report (docs/ELEVATION_PLAN.md): builds the official CLI, dumps each registered card's engine
# vocabulary, and runs scripts/core-growth.py with the given arguments. Usage:
#   scripts/core-growth.sh --registry data/m4/batch-registry.json --range slice-4.7=ade5111^..fa99e86 [--json]
#   scripts/core-growth.sh --registry data/m4/batch-registry.json --registry data/elevation/batch-registry.json \
#     --range slice-EL.3=BASE..HEAD
# Needs the provisioned official card data.
set -euo pipefail

growth_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$growth_root/scripts/env.sh"
growth_data="$growth_root/data/vendor/system-gateway"
if [[ ! -f "$growth_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi
growth_build="$growth_root/.tools/core-growth-cli"
dotnet build "$growth_root/src/Netrunner.Cli" -c Release --artifacts-path "$growth_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$growth_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$growth_data" \
  "-p:NetrunnerElevationCardDataInputRoot=$growth_root/data/vendor/elevation" >&2
vocabulary="$growth_build/vocabulary.json"
dotnet "$growth_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" card-vocabulary --json > "$vocabulary"
cd "$growth_root"
exec python3 scripts/core-growth.py --vocabulary "$vocabulary" "$@"
