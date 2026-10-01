#!/usr/bin/env bash
# P2 reviews by the Codex CLI (sends the games and card texts to OpenAI). Usage:
#   scripts/ai-codex-review.sh artifacts/ai-lint/<corpus> --side corp|runner --output <new dir> [--game ID]... [--model gpt-6.1-sol]
# Needs the codex CLI, logged in.
set -euo pipefail

review_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$review_root/scripts/env.sh"
review_data="$review_root/data/vendor/system-gateway"
if [[ ! -f "$review_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi

# Its own build directory, so a review never replaces a running game's build.
review_build="$review_root/.tools/codex-cli"
dotnet build "$review_root/src/Netrunner.Cli" -c Release --artifacts-path "$review_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$review_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$review_data" >&2
exec dotnet "$review_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" ai-codex-review "$@"
