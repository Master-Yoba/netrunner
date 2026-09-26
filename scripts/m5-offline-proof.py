#!/usr/bin/env python3
"""macOS exported-client proof using preinstalled content/art; never provisions or fetches."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(path):
    with path.open("rb") as stream:
        digest = hashlib.sha256()
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
        return digest.hexdigest()


def policy(denied=()):
    return "(version 1)\n(allow default)\n(deny network*)\n" + "".join(
        f"(deny file-read* file-write* (subpath {json.dumps(str(path))}))\n" for path in denied)


def scaled(seconds):
    """Scale a wall-time limit by NETRUNNER_TIMEOUT_SCALE (1 to 20, default 1)."""
    try:
        scale = float(os.environ.get("NETRUNNER_TIMEOUT_SCALE", "1"))
    except ValueError:
        scale = 1.0
    return seconds * (scale if 1 <= scale <= 20 else 1.0)


def run(command, output, name, environment, sandbox, timeout=600):
    log = output / f"{name}.log"
    with log.open("w") as stream:
        completed = subprocess.run(["/usr/bin/sandbox-exec", "-p", sandbox, *command],
                                   cwd=output, env=environment, stdout=stream, stderr=subprocess.STDOUT, timeout=scaled(timeout))
    require(completed.returncode == 0, f"{name} failed ({completed.returncode}); see {log}")
    return log


def isolated_environment():
    environment = {key: value for key, value in os.environ.items()
                   if not key.startswith("NETRUNNER_") and key not in ("DOTNET_ROOT", "XDG_DATA_HOME")}
    environment["DOTNET_EnableDiagnostics"] = "0"
    return environment


def denied_paths():
    return [ROOT, Path.home() / "Library/Application Support/NetrunnerSolo",
            Path.home() / "Library/Application Support/Godot/app_userdata/Netrunner Solo"]


def save_receipt(directory, name):
    matches = []
    for path in directory.glob("*.solo.json"):
        try:
            document = json.loads(path.read_text())
        except (ValueError, OSError):
            continue  # The smoke deliberately leaves a corrupt browser entry.
        if document.get("metadata", {}).get("name") == name:
            snapshot = document["payload"]["snapshot"]
            matches.append({"actionSequence": snapshot["actionSequence"], "outcome": snapshot["outcome"],
                            **{key: document[key] for key in ("engineStateHash", "policyStateHash", "integrityHash")}})
    require(len(matches) == 1, f"Expected one verified smoke save named {name}.")
    return matches[0]


def record_portable_boundaries(output, summary):
    for row in summary["cases"]:
        case = output / f"{row['artwork']}-{row['humanSide']}-{row['preset']}"
        saved = save_receipt(case / "save/saves", "M5 process restart")
        moved = save_receipt(case / "resume/saves", "M5 process restart")
        require(saved == moved and saved["outcome"]["kind"] == "ongoing", "Moved restart checkpoint differs or was terminal.")
        terminal = save_receipt(case / "resume/saves", "M5 terminal")
        require(terminal["outcome"]["kind"] == "won", "Resumed match did not reach a terminal save.")
        row.update(restart=saved, terminal=terminal)


def render_proof(output):
    """Actual renderer captures supplement the completed headless restart matrix."""
    require(not output.is_relative_to(ROOT), "The rendered proof must remain outside the checkout.")
    summary_path = output / "offline-summary.json"
    summary = json.loads(summary_path.read_text())
    require(summary["passed"], "Complete the offline restart matrix before the renderer supplement.")
    record_portable_boundaries(output, summary)
    bundle = output / "NetrunnerSolo.app"
    for relative, expected in summary["bundleFileSha256"].items():
        require(sha256(bundle / relative) == expected, "Exported bundle changed after the offline restart proof.")
    with (bundle / "Contents/Info.plist").open("rb") as stream:
        executable = bundle / "Contents/MacOS" / plistlib.load(stream)["CFBundleExecutable"]
    summary["renderingVerified"] = False
    summary["renderedCases"] = []
    summary_path.write_text(json.dumps(summary, indent=2) + "\n")
    for art_mode, side, preset, width in (("missing", "runner", "basic", 1920), ("restored", "corp", "expanded", 2560)):
        name = f"rendered-{art_mode}-{side}-{preset}"
        profile = output / name
        profile.mkdir(exist_ok=False)
        environment = dict(isolated_environment(), NETRUNNER_SAVE_DIRECTORY=str(profile / "saves"),
                           NETRUNNER_LAUNCH_DIAGNOSTICS=str(profile / "diagnostics"),
                           NETRUNNER_M5_SCREENSHOT_DIRECTORY=str(profile / "screenshots"),
                           NETRUNNER_CARD_ART_DIRECTORY=str(output / ("restored-art" if art_mode == "restored" else "missing-art")))
        command = [str(executable), "--log-file", str(profile / "engine.log"), "--", "--m5-client-smoke",
                   "--phase", "save", "--side", side, "--preset", preset, "--difficulty", "standard",
                   "--offline-proof", "--offline-art", art_mode, "--developer"]
        log = run(command, output, name, environment, policy(denied_paths()))
        content = log.read_text()
        require("M5_CLIENT SAVE_READY" in content and f"M5_OFFLINE_ASSETS PASS artwork={art_mode}" in content,
                f"Renderer did not finish {name}; see {log}")
        captures = {}
        for screen in ("setup", "settings", "browser", "paused"):
            path = profile / "screenshots" / f"{screen}-{width}.png"
            require(path.read_bytes().startswith(b"\x89PNG\r\n\x1a\n"), f"Missing renderer capture: {path}")
            captures[path.name] = sha256(path)
        summary["renderedCases"].append({"artwork": art_mode, "side": side, "preset": preset,
                                         "resolution": f"{width}x{width * 9 // 16}", "logSha256": sha256(log),
                                         "screenshotSha256": captures, "passed": True})
        summary_path.write_text(json.dumps(summary, indent=2) + "\n")
        print(f"M5_OFFLINE_RENDER PASS case={name} captures={len(captures)}", flush=True)
    summary["renderingVerified"] = True
    summary_path.write_text(json.dumps(summary, indent=2) + "\n")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, default=ROOT / "artifacts/macos/NetrunnerSolo.app")
    parser.add_argument("--art-cache", type=Path, help="An already validated local 77-card cache.")
    parser.add_argument("--output", type=Path, help="New directory outside the checkout; defaults to a private temporary directory.")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--render-proof", type=Path, help="Add real renderer captures to a completed proof directory.")
    args = parser.parse_args()
    require(sys.platform == "darwin", "This proof uses macOS sandbox-exec; provision and export first.")
    if args.render_proof:
        return render_proof(args.render_proof.resolve())
    require(args.bundle.is_dir() and args.art_cache and args.art_cache.is_dir(), "The release bundle and existing artwork cache are required.")
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="netrunner-m5-offline-")).resolve()
    require(not output.is_relative_to(ROOT), "The offline package must run outside the checkout.")
    if args.output:
        output.mkdir(parents=True, exist_ok=False)
    (output / "PRIVATE-ARTIFACTS.txt").write_text("Local artwork, private saves and logs. Share only offline-summary.json.\n")
    environment = isolated_environment()
    archive = output / "card-art.zip"
    artwork = output / "restored-art"
    cli = [args.dotnet, str(ROOT / "src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll"), "card-art"]
    archive_logs = []
    for name, command in (("pack", [*cli, "pack", "--cache-dir", str(args.art_cache.resolve()), "--output", str(archive)]),
                          ("unpack", [*cli, "unpack", "--input", str(archive), "--cache-dir", str(artwork)]),
                          ("validate", [*cli, "validate", "--cache-dir", str(artwork)])):
        log = run(command, output, name, environment, policy())
        require(f"card-art {name}=PASS cards=77" in log.read_text(), f"Artwork {name} did not verify all cards.")
        archive_logs.append(log)
    bundle = output / "NetrunnerSolo.app"
    subprocess.run(["/usr/bin/ditto", str(args.bundle.resolve()), str(bundle)], check=True)
    with (bundle / "Contents/Info.plist").open("rb") as stream:
        executable = bundle / "Contents/MacOS" / plistlib.load(stream)["CFBundleExecutable"]
    denied = denied_paths()
    sandbox = policy(denied)
    (output / "offline.sb").write_text(sandbox)
    # Prove the sandbox denies networking, not merely that the client happens
    # to avoid requests. Loopback binding never contacts an external service.
    probe = """import errno, os, socket, sys
for path in sys.argv[1:]:
    try: os.stat(path)
    except OSError as error:
        assert error.errno in (errno.EPERM, errno.EACCES), (path, error)
    else: raise AssertionError('Readable old source/profile: ' + path)
try:
    with socket.socket() as connection: connection.bind(('127.0.0.1', 0))
except OSError as error:
    assert error.errno in (errno.EPERM, errno.EACCES), error
else: raise AssertionError('Network was not denied')
print('M5_SANDBOX PASS network=denied checkout=denied old_profiles=denied')
"""
    probe_log = run([sys.executable, "-c", probe, *map(str, denied)], output, "sandbox-probe", environment, sandbox)
    cases = []
    summary = {"schemaVersion": 1, "kind": "m5-offline-package-proof", "passed": False,
               "networkDenied": True, "checkoutDenied": True, "oldProfilesDenied": True,
               "scriptedClientAcceptance": True, "humanAcceptance": False,
               "archiveSha256": sha256(archive), "archiveCards": 77,
               "archiveLogSha256": {log.name: sha256(log) for log in archive_logs},
               "sandboxProbeLogSha256": sha256(probe_log), "cases": cases,
               "bundleFileSha256": {str(path.relative_to(bundle)): sha256(path) for path in sorted(bundle.rglob("*"))
                                    if path.is_file() and (path == executable or path.suffix == ".pck" or path.name.startswith("Netrunner") and path.suffix == ".dll")}}
    summary_path = output / "offline-summary.json"
    summary_path.write_text(json.dumps(summary, indent=2) + "\n")
    matrix = [("missing", "corp", "basic"), ("missing", "runner", "expanded"),
              ("restored", "corp", "expanded"), ("restored", "runner", "basic")]
    for art_mode, side, preset in matrix:
        name = f"{art_mode}-{side}-{preset}"
        case = output / name
        case.mkdir()
        row = {"artwork": art_mode, "humanSide": side, "preset": preset, "difficulty": "standard", "seed": 56142,
               "saveDirectoryMoved": True, "previousDiagnosticsDenied": True, "phases": []}
        cases.append(row)
        for phase in ("save", "resume"):
            profile = case / phase
            profile.mkdir()
            if phase == "resume":
                shutil.copytree(case / "save/saves", profile / "saves")
            phase_environment = dict(environment, NETRUNNER_SAVE_DIRECTORY=str(profile / "saves"),
                                     NETRUNNER_LAUNCH_DIAGNOSTICS=str(profile / "diagnostics"),
                                     NETRUNNER_CARD_ART_DIRECTORY=str(artwork if art_mode == "restored" else output / "missing-art"))
            phase_sandbox = policy([*denied, *([case / "save"] if phase == "resume" else [])])
            command = [str(executable), "--headless", "--log-file", str(profile / "engine.log"), "--", "--m5-client-smoke",
                       "--phase", phase, "--side", side, "--preset", preset, "--difficulty", "standard",
                       "--offline-proof", "--offline-art", art_mode, "--developer"]
            log = run(command, output, f"{name}-{phase}", phase_environment, phase_sandbox)
            content = log.read_text()
            marker = "M5_CLIENT SAVE_READY" if phase == "save" else "M5_CLIENT PASS phase=resume"
            require(marker in content and f"M5_OFFLINE_ASSETS PASS artwork={art_mode}" in content,
                    f"Offline client did not finish {name} {phase}; see {log}")
            require(not any(failure in content for failure in ("M5_CLIENT FAIL", "SCRIPT ERROR", "Unhandled exception")),
                    f"Offline client reported a failure; see {log}")
            row["phases"].append({"phase": phase, "passed": True, "logSha256": sha256(log),
                                  "markers": [line for line in content.splitlines() if line.startswith(("M5_CLIENT ", "M5_OFFLINE_ASSETS "))]})
            summary_path.write_text(json.dumps(summary, indent=2) + "\n")
            print(f"M5_OFFLINE_CASE PASS case={name} phase={phase}", flush=True)
    record_portable_boundaries(output, summary)
    summary["passed"] = True
    summary_path.write_text(json.dumps(summary, indent=2) + "\n")
    print(f"M5_OFFLINE_PROOF PASS cases={len(cases)} phases={2 * len(cases)} output={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
