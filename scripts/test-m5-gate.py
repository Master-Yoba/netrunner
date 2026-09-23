#!/usr/bin/env python3
"""Negative controls for milestone evidence acceptance; no engine/content provisioning required."""

import copy
import json
import unittest

import m5_gate as gate


class GateTests(unittest.TestCase):
    def test_engine_canonical_encoding_vectors(self):
        # Captured from Core.CanonicalJson, including HTML and UTF-16 escapes.
        vector = {"text": "&+'<>`\"/\\\b\f\n\r\t\x00\x7fé😀", "number": 123, "zero": 0}
        self.assertEqual("ce547845a309740f9933684de540d00f5e66bdbbaeea998f19c7a9291ccad099", gate.digest(vector))
        manifest = json.loads((gate.MANIFESTS / "corp-standard-tactical-v2.json").read_text())
        self.assertEqual("51965a0808849a67ee605d9b01c1b4c958d5b7fb3df937c561fd563aea2b1e88", gate.digest(manifest))
        with self.assertRaises(ValueError):
            gate.digest({"rounded": 1.1})

    def setUp(self):
        manifest = json.loads((gate.MANIFESTS / "standard-smoke-v1.json").read_text())
        self.manifest = manifest
        self.report = {"schemaVersion": 1, "kind": "m5-ai-benchmark-report", "suiteId": manifest["suiteId"],
                       "manifestHash": gate.digest(manifest), "pins": manifest["pins"], "buildRevision": "test",
                       "assemblyHashes": {"test": "a" * 64}, "workers": 4, "games": [],
                       "aggregate": {"games": 20, "terminal": 20, "turnCaps": 0, "failures": 0, "corpWins": 20, "runnerWins": 0}}
        for game in sorted(manifest["games"], key=lambda game: game["id"]):
            self.report["games"].append({"game": copy.deepcopy(game), "error": None, "failurePath": None, "result": {
                "termination": "terminal", "executionTermination": "terminal", "replayVerified": True,
                "outcome": {"kind": "won", "winner": "corp"}, "replayHash": "a" * 64}})
        self.seal()

    def seal(self):
        self.report["deterministicDigest"] = gate.digest([{"game": row["game"], "result": row["result"]} for row in self.report["games"]])

    def test_complete_report_and_balanced_policies_pass(self):
        gate.validate_matrix(self.manifest, 20, "standard")
        self.assertEqual(20, gate.validate_games(self.report, self.manifest)["terminal"])

    def test_missing_duplicate_stale_or_changed_rows_fail(self):
        for change in (lambda report: report["games"].pop(),
                       lambda report: report["games"].__setitem__(1, report["games"][0]),
                       lambda report: report.__setitem__("manifestHash", "0" * 64),
                       lambda report: report["games"][0]["game"].__setitem__("gameSeed", 99)):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                change(report)
                with self.assertRaises(ValueError):
                    gate.validate_games(report, self.manifest)

    def test_a_failure_is_not_hidden_by_a_green_aggregate(self):
        for termination in ("illegalAction", "invariantFailure", "resolutionFailure", "replayMismatch", "watchdogTimeout", "noDecision", "actionCap"):
            with self.subTest(termination=termination):
                report = copy.deepcopy(self.report)
                report["games"][0]["result"]["termination"] = report["games"][0]["result"]["executionTermination"] = termination
                with self.assertRaises(ValueError):
                    gate.validate_games(report, self.manifest)

    def test_replay_and_outcome_must_match_terminal_claim(self):
        for field, value in (("replayVerified", False), ("outcome", {"kind": "ongoing", "winner": None})):
            report = copy.deepcopy(self.report)
            report["games"][0]["result"][field] = value
            with self.assertRaises(ValueError):
                gate.validate_games(report, self.manifest)

    def test_turn_caps_require_exact_review_and_never_count_as_terminals(self):
        row = self.report["games"][0]
        row["result"].update(termination="turnCap", executionTermination="turnCap", outcome={"kind": "ongoing", "winner": None})
        self.report["aggregate"].update(terminal=19, turnCaps=1, corpWins=19)
        self.seal()
        with self.assertRaises(ValueError):
            gate.validate_games(self.report, self.manifest, minimum_terminal=19)
        with self.assertRaises(ValueError):
            gate.validate_games(self.report, self.manifest, reviewed_caps=[row["game"]["id"]])
        self.assertEqual(19, gate.validate_games(self.report, self.manifest, minimum_terminal=19, reviewed_caps=[row["game"]["id"]])["terminal"])

    def test_worker_repeat_requires_same_build_manifest_and_results(self):
        repeat = copy.deepcopy(self.report)
        repeat["workers"] = 1
        gate.validate_repeat(self.report, repeat)
        for field, value in (("workers", 4), ("manifestHash", "other"), ("assemblyHashes", {}), ("deterministicDigest", "other")):
            mutated = copy.deepcopy(repeat)
            mutated[field] = value
            with self.assertRaises(ValueError):
                gate.validate_repeat(self.report, mutated)

    def test_tactical_oracle_and_failed_critical_position_do_not_count(self):
        manifest = json.loads((gate.MANIFESTS / "corp-standard-tactical-v2.json").read_text())
        rows = [{**{key: item["metadata"][key] for key in ("id", "side", "category", "split", "critical")},
                 "initialStateHash": item["initialStateHash"], "completed": True, "replayVerified": True,
                 "hiddenTwinEquivalent": True, "hiddenTwinReplayVerified": True, "passed": True}
                for item in sorted(manifest["positions"], key=lambda item: item["metadata"]["id"])]
        report = {"schemaVersion": 1, "kind": "m5-tactical-report", "suiteId": manifest["suiteId"],
                  "manifestHash": gate.digest(manifest), "pins": manifest["pins"], "buildRevision": "test", "assemblyHashes": {"test": "hash"},
                  "mode": "policy-evaluation", "positions": rows, "deterministicDigest": gate.digest(rows)}
        gate.validate_tactics(report, manifest)
        report["mode"] = "fixture-validation"
        with self.assertRaises(ValueError):
            gate.validate_tactics(report, manifest)
        report["mode"] = "policy-evaluation"
        next(row for row in rows if row["critical"])["passed"] = False
        report["deterministicDigest"] = gate.digest(rows)
        with self.assertRaises(ValueError):
            gate.validate_tactics(report, manifest)


if __name__ == "__main__":
    unittest.main()
