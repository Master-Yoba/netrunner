#!/usr/bin/env python3
"""Allowlist a shareable batch summary; never copy snapshots, decisions or errors."""

import json
import sys
from pathlib import Path


def main():
    if len(sys.argv) != 3:
        raise ValueError("Usage: summarize-ai-benchmark.py <report.json> <new-summary.json>")
    report = json.loads(Path(sys.argv[1]).read_text())
    if report["schemaVersion"] != 1 or report["kind"] not in ("m5-ai-benchmark-report", "m5-tactical-report"):
        raise ValueError("Unsupported benchmark report")
    if report["kind"] == "m5-tactical-report":
        summary = {key: report[key] for key in (
            "schemaVersion", "suiteId", "mode", "manifestHash", "pins", "buildRevision", "assemblyHashes",
            "workers", "deterministicDigest", "scores", "passed",
        )}
        summary["kind"] = "m5-tactical-summary"
        summary["verifiedReplays"] = sum(position["replayVerified"] for position in report["positions"])
        summary["positions"] = [{key: position[key] for key in (
            "id", "side", "category", "split", "critical", "initialStateHash", "passed", "completed", "actions",
            "replayVerified", "replayHash", "hiddenTwinEquivalent", "hiddenTwinReplayHash", "hiddenTwinReplayVerified",
        )} for position in report["positions"]]
        with Path(sys.argv[2]).open("x") as destination:
            json.dump(summary, destination, indent=2)
            destination.write("\n")
        return
    summary = {key: report[key] for key in (
        "schemaVersion", "suiteId", "manifestHash", "pins", "buildRevision", "assemblyHashes",
        "runtime", "operatingSystem", "architecture", "workers", "deterministicDigest", "aggregate", "strata",
    )}
    summary["kind"] = "m5-ai-benchmark-summary"
    summary["verifiedReplays"] = sum(game["result"]["replayVerified"] for game in report["games"])
    summary["nonterminalAttempts"] = [
        {"id": game["game"]["id"], **{key: game["result"][key] for key in (
            "termination", "executionTermination", "actions", "completedTurnPairs", "replayVerified",
            "finalStateHash", "replayHash",
        )}} for game in report["games"] if game["result"]["termination"] != "terminal"
    ]
    with Path(sys.argv[2]).open("x") as destination:
        json.dump(summary, destination, indent=2)
        destination.write("\n")


if __name__ == "__main__":
    main()
