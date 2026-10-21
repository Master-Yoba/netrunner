#!/usr/bin/env bash
set -euo pipefail
recover_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$recover_root/scripts/env.sh"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$recover_root/data/vendor/system-gateway"
dotnet run --project "$recover_root/src/Netrunner.Cli" --no-restore -- session-recover "$@"
