#!/usr/bin/env bash
set -euo pipefail

client_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$client_root/scripts/env.sh"
client_output="${NETRUNNER_M5_CLIENT_OUTPUT:-$client_root/artifacts/m5/client-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
mkdir -p "$client_output"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$client_root/data/vendor/system-gateway"
dotnet build "$client_root/src/Netrunner.Godot" --no-restore -m:1 --disable-build-servers >&2
python3 - "$client_root" "$client_output" "$GODOT4" "${1:-full}" <<'PY'
import concurrent.futures
import os
import pathlib
import subprocess
import sys

def scaled(seconds):
    """Scale a wall-time limit by NETRUNNER_TIMEOUT_SCALE (1 to 20, default 1)."""
    try:
        scale = float(os.environ.get("NETRUNNER_TIMEOUT_SCALE", "1"))
    except ValueError:
        scale = 1.0
    return seconds * (scale if 1 <= scale <= 20 else 1.0)

root, output, godot, mode = sys.argv[1:]
matrix = [("runner", "basic", "standard"), ("corp", "expanded", "standard")]
if mode != "--quick":
    matrix += [("corp", "basic", "standard"), ("runner", "expanded", "standard"), ("runner", "expanded", "easy")]
def run_case(row):
    side, preset, difficulty = row
    case = f"{side}-{preset}-{difficulty}"
    environment = dict(os.environ, NETRUNNER_SAVE_DIRECTORY=f"{output}/{case}/saves",
                       NETRUNNER_LAUNCH_DIAGNOSTICS=f"{output}/{case}/diagnostics",
                       NETRUNNER_CARD_ART_DIRECTORY=f"{output}/missing-art")
    for phase in ("save", "resume"):
        log = pathlib.Path(output, f"{case}-{phase}.log")
        command = [godot, "--headless", "--path", f"{root}/src/Netrunner.Godot", "--", "--m5-client-smoke",
                   "--phase", phase, "--side", side, "--preset", preset, "--difficulty", difficulty]
        with log.open("w") as stream:
            result = subprocess.run(command, env=environment, stdout=stream, stderr=subprocess.STDOUT, timeout=scaled(600))
        content = log.read_text()
        marker = "M5_CLIENT SAVE_READY" if phase == "save" else "M5_CLIENT PASS phase=resume"
        if result.returncode or marker not in content or any(error in content for error in ("M5_CLIENT FAIL", "SCRIPT ERROR", "ERROR:")):
            raise SystemExit(f"Client smoke failed: {case} {phase}; see {log}\n{content[-6000:]}")
        print(f"M5_CLIENT_MATRIX PASS case={case} phase={phase}", flush=True)
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
    list(executor.map(run_case, matrix))
print(f"M5_CLIENT_MATRIX PASS cases={len(matrix)} phases={2 * len(matrix)} output={output}")
PY
