#!/usr/bin/env python3
"""Freeze the existing seed corpus and M5.3/5.4 profiles; never silently replace a manifest."""

import argparse
import copy
import hashlib
import json
from pathlib import Path

from m5_gate import digest

ROOT = Path(__file__).resolve().parents[1]
MANIFESTS = ROOT / "data/m5/benchmarks"
PREVIOUS_COMMIT = "de4e1cf"


def expected():
    def read(name):
        return json.loads((MANIFESTS / name).read_text())

    sources = {}
    profiles = {}
    for side, version in (("corp", "v2"), ("runner", "v1")):
        for profile in ("standard", "easy"):
            name = f"{side}-{profile}-smoke-{version}.json"
            path = MANIFESTS / name
            spec = copy.deepcopy(read(name)["games"][0][side])
            spec["policySeed"] = spec["configuration"]["policySeed"] = 0
            profiles[f"{side}-{profile}"] = spec
            sources[name] = hashlib.sha256(path.read_bytes()).hexdigest()

    def bind(game, side, profile):
        spec = copy.deepcopy(profiles[f"{side}-{profile}"])
        spec["policySeed"] = spec["configuration"]["policySeed"] = game[side]["policySeed"]
        return spec

    def self_play(seed, name, profile):
        document = copy.deepcopy(seed)
        document["suiteId"] = name
        for game in document["games"]:
            for side in ("corp", "runner"):
                game[side] = bind(game, side, profile)
            game["comparisonGroup"] = "offline-self-play"
        return document

    full = read("contract-full-v1.json")
    smoke = read("contract-smoke-v1.json")
    standard = self_play(full, "m5-standard-v1", "standard")
    quick = self_play(smoke, "m5-standard-smoke-v1", "standard")
    easy = self_play(full, "m5-easy-v1", "easy")
    easy["games"] = [game for game in easy["games"] if int(game["id"].rsplit("-", 1)[1]) <= 25]

    def comparison(previous):
        document = copy.deepcopy(smoke)
        document["suiteId"] = "m5-compare-previous-v1" if previous else "m5-compare-contract-v1"
        document["games"] = []
        for seed in smoke["games"]:
            for candidate in ("corp", "runner"):
                game = copy.deepcopy(seed)
                for side in ("corp", "runner"):
                    if side == candidate or previous:
                        game[side] = bind(seed, side, "standard")
                game["comparisonGroup"] = f"candidate-{candidate}"
                game["id"] += "-" + game["comparisonGroup"]
                document["games"].append(game)
        return document

    documents = {"standard-v1.json": standard, "standard-smoke-v1.json": quick, "easy-v1.json": easy,
                 "compare-contract-v1.json": comparison(False), "compare-previous-v1.json": comparison(True)}
    freeze = {"schemaVersion": 1, "kind": "m5-profile-freeze", "previousPolicyCommit": PREVIOUS_COMMIT,
              "strategicPolicyChanged": False, "sourceFileSha256": sources, "profilesWithSeedZero": profiles,
              "manifestCanonicalHashes": {name: digest(document) for name, document in documents.items()}}
    documents["frozen-profiles-v1.json"] = freeze
    return documents


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="Create absent manifests; existing files must match exactly.")
    args = parser.parse_args()
    for name, document in expected().items():
        path = MANIFESTS / name
        if path.exists():
            if json.loads(path.read_text()) != document:
                raise ValueError(f"Frozen manifest drift: {name}. Create a new version; do not rewrite held-out seeds or policies.")
        elif args.write:
            with path.open("x") as output:
                json.dump(document, output, separators=(",", ":"))
                output.write("\n")
        else:
            raise ValueError(f"Missing frozen manifest: {name}")
    print("M5_FREEZE PASS standard=1000 easy=100 smoke=20 comparisons=40+40 strategic_policy_changed=false")


if __name__ == "__main__":
    main()
