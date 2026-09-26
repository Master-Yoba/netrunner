#!/usr/bin/env bash
set -euo pipefail

developer_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$developer_root/scripts/env.sh"
developer_output="${NETRUNNER_M5_DEVELOPER_OUTPUT:-$developer_root/artifacts/m5/developer-$(date -u +%Y%m%dT%H%M%SZ)-$$}"
mkdir -p "$developer_output"
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$developer_root/data/vendor/system-gateway"
dotnet build "$developer_root/src/Netrunner.Godot" --no-restore -m:1 --disable-build-servers >&2
python3 - "$developer_root" "$developer_output" "$GODOT4" <<'PY'
import os
import pathlib
import subprocess
import sys

root, output, godot = sys.argv[1:]
for case, arguments in (("disabled", ["--expect-disabled"]),
                        ("runner", ["--developer", "--side", "runner"]),
                        ("corp", ["--developer", "--side", "corp"])):
    directory = pathlib.Path(output, case)
    directory.mkdir(parents=True, exist_ok=True)
    environment = dict(os.environ, NETRUNNER_M5_DEVELOPER_OUTPUT=str(directory),
                       NETRUNNER_SAVE_DIRECTORY=str(directory / "saves"),
                       NETRUNNER_LAUNCH_DIAGNOSTICS=str(directory / "diagnostics"),
                       NETRUNNER_CARD_ART_DIRECTORY=str(directory / "missing-art"))
    log = directory / "smoke.log"
    command = [godot, "--headless", "--path", f"{root}/src/Netrunner.Godot", "--", "--m5-developer-smoke", *arguments]
    with log.open("w") as stream:
        result = subprocess.run(command, env=environment, stdout=stream, stderr=subprocess.STDOUT, timeout=180)
    content = log.read_text()
    if result.returncode or "M5_DEVELOPER PASS" not in content or any(error in content for error in ("M5_DEVELOPER FAIL", "SCRIPT ERROR", "ERROR:")):
        raise SystemExit(f"Developer smoke failed: {case}; see {log}\n{content[-6000:]}")
    print(f"M5_DEVELOPER_MATRIX PASS case={case}", flush=True)
print(f"M5_DEVELOPER_MATRIX PASS output={output}")
PY
