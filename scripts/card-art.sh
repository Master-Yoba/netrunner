#!/usr/bin/env bash
set -euo pipefail
art_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$art_root/scripts/env.sh"
# Build in an isolated artifacts directory so artwork maintenance cannot
# replace the game's official-content assemblies with a synthetic CLI build.
art_build="$art_root/.tools/card-art-cli"
dotnet build "$art_root/src/Netrunner.Cli" --artifacts-path "$art_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$art_build/generated/GeneratedCardData.g.cs" >&2
exec dotnet "$art_build/bin/Netrunner.Cli/debug/Netrunner.Cli.dll" card-art "$@"
