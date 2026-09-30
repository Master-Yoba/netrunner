#!/usr/bin/env bash
# LLM reviews (Wave 4.5, P2) of self-play games saved by the ladder. Usage:
#   scripts/ai-llm-review.sh artifacts/ai-lint/<corpus> --side corp|runner --output <new dir> [--entrant E] [--games N] ...
# The endpoint comes from NETRUNNER_LLM_BASE_URL, NETRUNNER_LLM_MODEL and NETRUNNER_LLM_API_KEY, read from the
# environment or the git-ignored .env. Needs the provisioned System Gateway snapshot and never downloads it.
set -euo pipefail

review_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$review_root/scripts/env.sh"
if [[ -f "$review_root/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "$review_root/.env"
  set +a
fi
review_data="$review_root/data/vendor/system-gateway"
if [[ ! -f "$review_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi

# The lint build directory: the same official build, never a running ladder's.
review_build="$review_root/.tools/lint-cli"
dotnet build "$review_root/src/Netrunner.Cli" -c Release --artifacts-path "$review_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$review_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$review_data" >&2
exec dotnet "$review_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" ai-llm-review "$@"
