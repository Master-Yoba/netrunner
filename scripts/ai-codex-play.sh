#!/usr/bin/env bash
# Codex plays the bot through the ai-mcp-game MCP server (sends the boards it sees to OpenAI). Usage:
#   scripts/ai-codex-play.sh --output <new dir> [--side corp|runner|both] [--games N] [--seed S] [--model gpt-6.1-sol]
# Games are saved like ai-ladder --save-games, one directory each, so ai-lint and the reviews read them.
# Needs the codex CLI, logged in.
set -euo pipefail

review_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$review_root/scripts/env.sh"
review_data="$review_root/data/vendor/system-gateway"
if [[ ! -f "$review_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi

# Its own build directory, apart from review and LLM runs.
review_build="$review_root/.tools/codex-play-cli"
dotnet build "$review_root/src/Netrunner.Cli" -c Release --artifacts-path "$review_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$review_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$review_data" >&2
exec dotnet "$review_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll" ai-codex-play "$@"
