#!/usr/bin/env python3
"""Check M5.0 specification coverage without official metadata or game execution."""

import json
import hashlib
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
    action_source = (root / "src/Netrunner.Core/ActionContracts.cs").read_text()
    enum_body = re.search(r"enum DecisionKind\s*\{([^}]+)\}", action_source).group(1)
    decisions = {item.strip() for item in enum_body.split(",") if item.strip()}
    codes = {card["code"] for card in cards}
    positions = tactics["positions"]
    require(len(positions) == 80 and len({case["id"] for case in positions}) == 80, "Expected 80 independent case IDs")
    require(Counter(case["side"] for case in positions) == {"corp": 40, "runner": 40}, "Both sides need 40 positions")
    require(len({case["preconditions"] for case in positions}) == 80, "Duplicate positions are not independent coverage")
    for case in positions:
        require(case["implementationStatus"] == "specified-not-executable", "M5.0 specifications must not claim execution")
        require(case["decisionKinds"] and set(case["decisionKinds"]) <= decisions - {"ToyTurn"}, "Unknown/non-Gateway decision")
        require(set(case["cardCodes"]) <= codes, "Unknown Gateway design")
        require(case["split"] in {"tuning", "held-out"} and type(case["critical"]) is bool, "Invalid case classification")
        require(case["title"] and case["preconditions"] and case["acceptance"], "Case lacks a meaningful predicate")
    for side in ("corp", "runner"):
        require(Counter(case["split"] for case in positions if case["side"] == side)
                == {"tuning": 24, "held-out": 16}, "Tuning/held-out split changed")
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
    print("M5_SPEC PASS games=20 repeats=2 pairings=4 tactical_specs=80 corp=40 runner=40 designs=77 decisions=15 executable_tactics=0")


if __name__ == "__main__":
    main()
