#!/usr/bin/env python3
"""Check M5.0 specification coverage without official metadata or game execution."""

import hashlib
import json
import re
from collections import Counter
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def main():
    root = Path(__file__).resolve().parent.parent
    baseline = json.loads((root / "data/m5/benchmarks/contract-baseline-v1.json").read_text())
    tactics = json.loads((root / "data/m5/benchmarks/tactical-v1.json").read_text())
    cards = json.loads((root / "data/m4/batch-registry.json").read_text())["cards"]
    presets = json.loads((root / "data/m4/presets.json").read_text())["presets"]
    require(baseline["schemaVersion"] == 1 and baseline["kind"] == "m5-contract-baseline", "Wrong baseline schema")
    require(tactics["schemaVersion"] == 1 and tactics["kind"] == "m5-tactical-case-specification", "Wrong tactical schema")
    require(tactics["contentPinsFrom"] == "contract-baseline-v1.json", "Tactical content pins are not bound")
    require(baseline["repeats"] == 2 and baseline["actionCap"] == 4000, "Baseline repetition/cap changed")
    games = baseline["games"]
    require(len(games) == 20 and len({game["id"] for game in games}) == 20, "Expected 20 distinct baseline game IDs")
    preset_sides = {preset["presetId"]: preset["side"] for preset in presets if preset["playable"]}
    groups = Counter()
    triples = set()
    for game in games:
        require(preset_sides.get(game["corpPreset"]) == "corp", "Unknown Corp preset")
        require(preset_sides.get(game["runnerPreset"]) == "runner", "Unknown Runner preset")
        require(type(game["seed"]) is int and 0 <= game["seed"] < 2**64, "Invalid game seed")
        groups[(game["corpPreset"], game["runnerPreset"])] += 1
        triples.add((game["corpPreset"], game["runnerPreset"], game["seed"]))
    require(len(groups) == 4 and set(groups.values()) == {5} and len(triples) == 20, "Deck-pair/seed matrix is incomplete or repeated")
    require({game["seed"] for game in games} == {1, 42, 123, 47042, 8675309}, "Pinned seed set changed")
    require(baseline["pins"]["presetSha256"] == hashlib.sha256((root / "data/m4/presets.json").read_bytes()).hexdigest(), "Preset pin mismatch")
    for suite, count, seeds in (
        ("smoke", 20, {1, 42, 123, 47042, 8675309}),
        ("full", 1000, {5000001 + index * 104729 for index in range(250)}),
    ):
        batch = json.loads((root / f"data/m5/benchmarks/contract-{suite}-v1.json").read_text())
        require(batch["schemaVersion"] == 1 and batch["kind"] == "m5-ai-benchmark", "Wrong batch schema")
        require(batch["contentMode"] == "official", "Official batch incorrectly labels its content")
        rows = batch["games"]
        require(len(rows) == count and len({row["id"] for row in rows}) == count, "Batch row count/IDs changed")
        require(Counter((row["corpPreset"], row["runnerPreset"]) for row in rows)
                == {pair: count // 4 for pair in groups}, "Unbalanced batch pairing matrix")
        require({(row["corpPreset"], row["runnerPreset"], row["gameSeed"]) for row in rows}
                == {(corp, runner, seed) for corp, runner in groups for seed in seeds}, "Frozen held-out seeds changed")
        for key in ("rulesVersion", "cardDataVersion", "cardDataSha256", "behaviorVersion", "behaviorFingerprint",
                    "runtimeVersion", "runtimeFingerprint", "presetSha256"):
            require(batch["pins"][key] == baseline["pins"][key], f"Batch pin changed: {key}")
        require(batch["limits"]["turnPairs"] == 100 and batch["limits"]["actions"] == 20000, "Batch limits changed")
        for row in rows:
            require(row["split"] == "held-out" and row["comparisonGroup"] == "baseline", "Batch split/comparison changed")
            for side in ("corp", "runner"):
                agent = row[side]
                require(agent["side"] == side and agent["kind"] == "contract"
                        and agent["agentVersion"] == baseline["pins"]["agentVersion"]
                        and agent["policyVersion"] == baseline["pins"]["policyVersion"]
                        and agent["profileId"] == "contract-baseline" and agent.get("configuration") is None,
                        "Contract batch must not claim a future strategic policy")
                require(re.fullmatch(r"[a-f0-9]{64}", row[side + "DeckHash"]), "Missing deck hash")
    action_source = (root / "src/Netrunner.Core/ActionContracts.cs").read_text()
    enum_body = re.search(r"enum DecisionKind\s*\{([^}]+)\}", action_source).group(1)
    decisions = {item.strip() for item in enum_body.split(",") if item.strip()}
    codes = {card["code"] for card in cards}
    positions = tactics["positions"]
    require(len(positions) == 80 and len({case["id"] for case in positions}) == 80, "Expected 80 independent case IDs")
    require(Counter(case["side"] for case in positions) == {"corp": 40, "runner": 40}, "Both sides need 40 positions")
    require(len({case["preconditions"] for case in positions}) == 80, "Duplicate positions are not independent coverage")
    for case in positions:
        require(case["implementationStatus"] == "executable-core-fixture", "M5.2 requires every specified fixture to be executable")
        require(case["decisionKinds"] and set(case["decisionKinds"]) <= decisions - {"ToyTurn"}, "Unknown/non-Gateway decision")
        require(set(case["cardCodes"]) <= codes, "Unknown Gateway design")
        require(case["split"] in {"tuning", "held-out"} and type(case["critical"]) is bool, "Invalid case classification")
        require(case["title"] and case["preconditions"] and case["acceptance"], "Case lacks a meaningful predicate")
    for side in ("corp", "runner"):
        require(Counter(case["split"] for case in positions if case["side"] == side)
                == {"tuning": 24, "held-out": 16}, "Tuning/held-out split changed")
    executable = json.loads((root / "data/m5/benchmarks/tactical-contract-v1.json").read_text())
    require(executable["schemaVersion"] == 1 and executable["kind"] == "m5-tactical-benchmark"
            and executable["builderVersion"] == "gateway-tactics-v1", "Unknown tactical execution schema")
    rows = executable["positions"]
    require({row["metadata"]["id"] for row in rows} == {case["id"] for case in positions}
            and len(rows) == 80, "Executable fixture set differs from the frozen specification")
    require(len({row["initialStateHash"] for row in rows}) == 80, "Repeated initial states do not count as independent positions")
    for row in rows:
        case = next(case for case in positions if case["id"] == row["metadata"]["id"])
        require(all(row["metadata"][key] == case[key] for key in row["metadata"]), "Tactical rationale/classification drift")
        require(re.fullmatch(r"[a-f0-9]{64}", row["initialStateHash"]) and row["actionCap"] > 0, "Unpinned tactical root/bound")
        require(row["agent"]["side"] == case["side"] and row["agent"]["kind"] == "contract", "Incorrect tactical baseline policy")
    require(executable["pins"] == json.loads((root / "data/m5/benchmarks/contract-smoke-v1.json").read_text())["pins"], "Tactical catalog pins drifted")
    inventory = (root / "docs/M5_CAPABILITY_INVENTORY.md").read_text()
    mapped = re.findall(r"^\| (300\d\d) \|", inventory, re.MULTILINE)
    require(len(mapped) == 77 and set(mapped) == codes, "Inventory must map each of the 77 designs once")
    mapped_decisions = set(re.findall(r"^\| `(\w+)` \|", inventory, re.MULTILINE))
    require(mapped_decisions == decisions, "Decision inventory does not match Core")
    report = json.loads((root / "docs/evidence/m5-0/contract-baseline-v1.json").read_text())
    require(report["manifestSha256"] == hashlib.sha256((root / "data/m5/benchmarks/contract-baseline-v1.json").read_bytes()).hexdigest(), "Report is not bound to this manifest")
    require(report["pins"] == baseline["pins"], "Report/catalog pins disagree")
    require(report["complete"] and report["completedAttempts"] == 40, "Incomplete baseline evidence")
    require(Counter((result["id"], result["repeat"]) for result in report["results"])
            == Counter((game["id"], repeat) for game in games for repeat in (1, 2)), "Report cases/repeats disagree")
    attempts = {(result["id"], result["repeat"]): result for result in report["results"]}
    for game in games:
        first, second = attempts[game["id"], 1], attempts[game["id"], 2]
        for attempt in (first, second):
            require(all(attempt[key] == game[key] for key in ("seed", "corpPreset", "runnerPreset")), "Report input differs from manifest")
            require(attempt["status"] == "terminal" and attempt["replayVerified"], "Historical baseline contains a failed attempt")
        require(all(first[key] == second[key] for key in ("replaySha256", "finalStateHash", "actionCount", "corp", "runner")), "Repeated replay/counters disagree")
        require(second["deterministicRepeat"] is True, "Missing repeat proof")
    print("M5_SPEC PASS games=20 repeats=2 pairings=4 tactical_specs=80 corp=40 runner=40 designs=77 decisions=15 executable_tactics=80")


if __name__ == "__main__":
    main()
