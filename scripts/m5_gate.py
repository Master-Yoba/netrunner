#!/usr/bin/env python3
"""Run and validate frozen headless M5 benchmarks. Human/offline acceptance is recorded separately."""

import argparse
import collections
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFESTS = ROOT / "data/m5/benchmarks"


def canonical(value):
    """Match Core.CanonicalJson for the integer-only benchmark evidence schema.

    Utf8JsonWriter's default encoder escapes HTML-sensitive ASCII and non-ASCII
    UTF-16 units. Python's JSON encoder has a different default escape contract.
    Reject non-integer numbers rather than silently hashing rounded floats.
    """
    if isinstance(value, str):
        escaped = []
        short = {"\\": "\\\\", "\b": "\\b", "\f": "\\f", "\n": "\\n", "\r": "\\r", "\t": "\\t"}
        for character in value:
            if character in short:
                escaped.append(short[character])
            elif 32 <= ord(character) < 127 and character not in "\"&'+<>`":
                escaped.append(character)
            else:
                units = character.encode("utf-16-be")
                escaped.extend(f"\\u{int.from_bytes(units[index:index + 2], 'big'):04X}" for index in range(0, len(units), 2))
        return '"' + "".join(escaped) + '"'
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, list):
        return "[" + ",".join(canonical(item) for item in value) + "]"
    if isinstance(value, dict):
        keys = sorted(value, key=lambda key: key.encode("utf-16-be"))
        return "{" + ",".join(canonical(key) + ":" + canonical(value[key]) for key in keys) + "}"
    raise ValueError(f"Unsupported canonical evidence value: {type(value).__name__}")


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate_header(report, manifest, kind):
    require(report["schemaVersion"] == 1 and report["kind"] == kind, "Unsupported report schema/kind")
    require(report["manifestHash"] == digest(manifest), "Report is bound to a different manifest")
    require(report["pins"] == manifest["pins"] and report["suiteId"] == manifest["suiteId"], "Content or suite pins differ")
    require(bool(report["buildRevision"]) and bool(report["assemblyHashes"]), "Build evidence is missing")


def validate_games(report, manifest, minimum_terminal=None, reviewed_caps=()):
    validate_header(report, manifest, "m5-ai-benchmark-report")
    expected = {game["id"]: game for game in manifest["games"]}
    games = report["games"]
    require(len(games) == len(expected) == len(manifest["games"]), "Missing/duplicate benchmark rows")
    require([game["game"]["id"] for game in games] == sorted(expected), "Report rows must be complete and ordered")
    terminal = corp = runner = 0
    caps = []
    for row in games:
        game, result = row["game"], row["result"]
        require(game == expected[game["id"]], "A game seed, deck, profile or comparison group changed")
        require(result["replayVerified"] is True, "An accepted-action replay was not verified")
        require(row.get("error") is None and row.get("failurePath") is None, "A game retained an execution failure")
        termination = result["termination"]
        require(termination == result["executionTermination"], "Execution and replay classifications disagree")
        require(termination in ("terminal", "turnCap"), f"Failed termination: {termination}")
        if termination == "terminal":
            require(result["outcome"]["kind"] == "won" and result["outcome"]["winner"] in ("corp", "runner"), "False terminal classification")
            terminal += 1
            corp += result["outcome"]["winner"] == "corp"
            runner += result["outcome"]["winner"] == "runner"
        else:
            require(result["outcome"]["kind"] == "ongoing", "Turn cap has a terminal outcome")
            caps.append(game["id"])
    require(set(caps) == set(reviewed_caps), "Every turn cap requires an explicit review; unused reviews are rejected")
    require(terminal >= (len(expected) if minimum_terminal is None else minimum_terminal), "Too few actual terminal games")
    require(len(caps) <= 10, "More than ten turn caps")
    aggregate = report["aggregate"]
    require((aggregate["games"], aggregate["terminal"], aggregate["turnCaps"], aggregate["failures"], aggregate["corpWins"], aggregate["runnerWins"])
            == (len(expected), terminal, len(caps), 0, corp, runner), "Aggregate counts disagree with actual attempts")
    require(report["deterministicDigest"] == digest([{"game": row["game"], "result": row["result"]} for row in games]), "Deterministic digest differs from game results")
    return {"games": len(games), "terminal": terminal, "reviewedTurnCaps": caps, "verifiedReplays": len(games)}


def validate_tactics(report, manifest, standard=True):
    validate_header(report, manifest, "m5-tactical-report")
    require(report["mode"] == "policy-evaluation", "Fixture-oracle execution is not policy evidence")
    expected = {position["metadata"]["id"]: position for position in manifest["positions"]}
    positions = report["positions"]
    require(len(positions) == len(expected) and [row["id"] for row in positions] == sorted(expected), "Missing/duplicate tactical positions")
    for row in positions:
        specification = expected[row["id"]]
        require(all(row[key] == specification["metadata"][key] for key in ("side", "category", "split", "critical")), "Tactical acceptance metadata drifted")
        require(row["initialStateHash"] == specification["initialStateHash"], "Tactical start changed")
        require(row["completed"] and row["replayVerified"] and row.get("error") is None, "Tactical execution/replay failed")
        if specification["hiddenTwinStateHash"] is not None:
            require(row["hiddenTwinEquivalent"] and row["hiddenTwinReplayVerified"] and row.get("hiddenTwinError") is None, "Hidden-information twin failed")
    scores = []
    for side in sorted({row["side"] for row in positions}):
        rows = [row for row in positions if row["side"] == side]
        critical = [row for row in rows if row["critical"]]
        passed = sum(row["passed"] for row in rows)
        if standard:
            require(all(row["passed"] for row in critical), "A critical tactic failed")
            require(passed / len(rows) >= 0.85, "A seat missed the 85% tactical target")
        scores.append({"side": side, "passed": passed, "total": len(rows), "criticalPassed": sum(row["passed"] for row in critical),
                       "criticalTotal": len(critical), "splits": {split: {"passed": sum(row["passed"] for row in rows if row["split"] == split),
                       "total": sum(row["split"] == split for row in rows)} for split in ("tuning", "held-out")}})
    require(report["deterministicDigest"] == digest(positions), "Tactical digest differs from positions")
    return {"verifiedReplays": len(positions), "scores": scores}


def validate_repeat(first, repeated):
    require(first["workers"] != repeated["workers"], "Repeat must use a different worker count")
    require(first["manifestHash"] == repeated["manifestHash"], "Repeat changed the manifest")
    require(first["assemblyHashes"] == repeated["assemblyHashes"], "Repeat changed the compiled build")
    require(first["deterministicDigest"] == repeated["deterministicDigest"], "Worker counts changed results or replays")


def validate_matrix(manifest, count, profile):
    games = manifest["games"]
    require(len(games) == count, "Wrong suite denominator")
    counts = collections.Counter((game["corpPreset"], game["runnerPreset"]) for game in games)
    required = {(f"system-gateway-{corp}-corp", f"system-gateway-{runner}-runner") for corp in ("basic", "expanded") for runner in ("basic", "expanded")}
    require(set(counts) == required and set(counts.values()) == {count // 4}, "The four preset pairings are not balanced")
    require(all(game[side]["kind"] == "offline" and game[side]["profileId"] == profile for game in games for side in ("corp", "runner")), "Wrong seat policy/profile")


def cases(full):
    result = [("smoke", "standard-smoke-v1.json", 4), ("smoke-repeat", "standard-smoke-v1.json", 1)]
    for side, version in (("corp", "v2"), ("runner", "v1")):
        result += [(f"{side}-tactics", f"{side}-standard-tactical-{version}.json", 4),
                   (f"{side}-tactics-repeat", f"{side}-standard-tactical-{version}.json", 1)]
    if full:
        result += [("standard", "standard-v1.json", 4), ("easy", "easy-v1.json", 4),
                   ("compare-contract", "compare-contract-v1.json", 4), ("compare-previous", "compare-previous-v1.json", 4)]
        result += [(f"{side}-easy-tactics", f"{side}-easy-tactical-{version}.json", 4) for side, version in (("corp", "v2"), ("runner", "v1"))]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--quick", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    output = args.output.resolve()
    require(not output.exists(), "Choose a new evidence directory; previous attempts are retained")
    output.mkdir(parents=True)
    (output / "PRIVATE-ARTIFACTS.txt").write_text("Local private replays/snapshots. Only summary files may be shared. No automatic upload.\n")
    # Freeze loaded binaries and inputs so later builds cannot alter another process in this run.
    shutil.copytree(ROOT / "src/Netrunner.Cli/bin/Debug/net8.0", output / "runner")
    shutil.copytree(MANIFESTS, output / "manifests")
    reports, errors, evidence = {}, [], {}
    for name, file_name, workers in cases(not args.quick):
        manifest_path = output / "manifests" / file_name
        manifest = json.loads(manifest_path.read_text())
        tactical = "tactical" in file_name
        command = [args.dotnet, str(output / "runner/Netrunner.Cli.dll"), "ai-tactics" if tactical else "ai-benchmark",
                   str(manifest_path), "--workers", str(workers), "--output", str(output / name)]
        try:
            with (output / f"{name}.log").open("w") as log:
                completed = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=7200 if name == "standard" else 1800)
            require(completed.returncode in (0, 1), "Benchmark process failed before producing a complete report")
            report_path = output / name / "report.json"
            report = json.loads(report_path.read_text())
            # Preserve failed-attempt counts as well as successful reports.
            subprocess.run([sys.executable, str(ROOT / "scripts/summarize-ai-benchmark.py"), str(report_path), str(output / f"{name}-summary.json")], check=True)
            if tactical:
                result = validate_tactics(report, manifest, standard="easy" not in name)
            else:
                if name in ("smoke", "smoke-repeat", "standard", "easy"):
                    validate_matrix(manifest, {"standard": 1000, "easy": 100}.get(name, 20), "easy" if name == "easy" else "standard")
                result = validate_games(report, manifest, minimum_terminal=990 if name == "standard" else None)
            if reports:
                require(report["assemblyHashes"] == next(iter(reports.values()))["assemblyHashes"], "Compiled build changed during the gate")
            reports[name] = report
            evidence[name] = {**result, "manifestHash": report["manifestHash"], "deterministicDigest": report["deterministicDigest"],
                              "reportSha256": hashlib.sha256(report_path.read_bytes()).hexdigest(), "workers": workers}
            print(f"M5_BENCHMARK_CASE PASS case={name}", flush=True)
        except (ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
            errors.append(f"{name}: {error}")
            print(f"M5_BENCHMARK_CASE FAIL case={name} detail={error}", flush=True)
    for name in ("smoke", "corp-tactics", "runner-tactics"):
        try:
            validate_repeat(reports[name], reports[name + "-repeat"])
        except (ValueError, KeyError) as error:
            errors.append(f"{name} repeat: {error}")
    summary = {"schemaVersion": 1, "kind": "m5-headless-gate-summary", "mode": "quick" if args.quick else "full",
               "passed": not errors, "cases": evidence, "errors": errors,
               "scope": "Headless legality, tactics, comparisons and worker determinism; offline package, performance and human acceptance are separate."}
    (output / "gate-summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(f"M5_BENCHMARK_GATE {'FAIL' if errors else 'PASS'} output={output}")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
