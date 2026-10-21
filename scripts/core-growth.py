#!/usr/bin/env python3
"""E6 Core-growth report: how much a card batch grew the engine, and which of its cards needed it.

Usage:
  scripts/core-growth.py --vocabulary VOCAB.json --registry REGISTRY.json [--registry REGISTRY.json]...
      [--batch ID]... [--range ID=BASE..HEAD]...
      [--json] [--markdown OUT.md]

For each batch (a `batches[].id` in the registry, with its card codes) and its git range BASE..HEAD:
  - Core files and lines the range changed under src/Netrunner.Core;
  - Core vocabulary the range added: enum members ("Type.Member") and type names ("type:Name") declared in
    src/Netrunner.Core at HEAD but not at BASE;
  - per card, whether its behaviour (from `card-vocabulary --json`) uses any of that new vocabulary, or a bespoke
    extension handler first mentioned in Core during the range. A card that does neither needed no Core change.
The budget the E6 plan sets is on the share of a batch's cards that needed no Core change.

Without --range, a batch's range comes from its registry entry's `coreRange` ("BASE..HEAD").
"""

import argparse
import json
import re
import subprocess
import sys

CORE = "src/Netrunner.Core"
ENUM = re.compile(r"\benum\s+(\w+)\b[^{;]*\{(.*?)\}", re.S)
TYPE = re.compile(r"\b(?:class|record|struct|interface)\s+(?:struct\s+)?(\w+)")


def git(*args: str) -> str:
    return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout


def strip_comments(source: str) -> str:
    source = re.sub(r"/\*.*?\*/", "", source, flags=re.S)
    return re.sub(r"//[^\n]*", "", source)


def core_sources(ref: str) -> dict[str, str]:
    files = [path for path in git("ls-tree", "-r", "--name-only", ref, CORE).splitlines() if path.endswith(".cs")]
    return {path: git("show", f"{ref}:{path}") for path in files}


def vocabulary(sources: dict[str, str]) -> set[str]:
    found: set[str] = set()
    for source in sources.values():
        text = strip_comments(source)
        for name, body in ENUM.findall(text):
            for member in body.split(","):
                member = member.strip().split("=")[0].strip()
                member = re.sub(r"\[[^\]]*\]", "", member).strip()
                if re.fullmatch(r"\w+", member):
                    found.add(f"{name}.{member}")
        found.update(f"type:{name}" for name in TYPE.findall(text))
    return found


def core_diff(base: str, head: str) -> tuple[int, int, int]:
    files = added = removed = 0
    for line in git("diff", "--numstat", base, head, "--", CORE).splitlines():
        plus, minus, _ = line.split("\t", 2)
        files += 1
        added += int(plus) if plus.isdigit() else 0
        removed += int(minus) if minus.isdigit() else 0
    return files, added, removed


def report(batch_id: str, codes: list[str], base: str, head: str, cards: dict[str, dict]) -> dict:
    base_sources, head_sources = core_sources(base), core_sources(head)
    new_vocabulary = sorted(vocabulary(head_sources) - vocabulary(base_sources))
    new_set = set(new_vocabulary)
    base_text = "\n".join(base_sources.values())
    head_text = "\n".join(head_sources.values())
    files, added, removed = core_diff(base, head)
    rows = []
    for code in codes:
        card = cards.get(code)
        if card is None:
            rows.append({"code": code, "registered": False, "needsCore": None, "uses": []})
            continue
        uses = sorted(set(card["vocabulary"]) & new_set)
        handler = card.get("extensionHandler")
        new_handler = bool(handler) and handler in head_text and handler not in base_text
        rows.append({"code": code, "registered": True, "needsCore": bool(uses) or new_handler, "uses": uses,
                     "newHandler": handler if new_handler else None})
    registered = [row for row in rows if row["registered"]]
    free = sum(1 for row in registered if not row["needsCore"])
    return {
        "batch": batch_id, "base": base, "head": head,
        "coreFiles": files, "coreLinesAdded": added, "coreLinesRemoved": removed,
        "newVocabulary": new_vocabulary,
        "cards": len(codes), "registered": len(registered), "noCoreChange": free,
        "noCoreChangeShare": round(free / len(registered), 3) if registered else None,
        "rows": rows,
    }


def markdown(reports: list[dict]) -> str:
    lines = ["| Batch | Range | Core files | Core lines +/− | New vocabulary | Cards | No Core change |",
             "|---|---|---|---|---|---|---|"]
    for r in reports:
        share = "n/a" if r["noCoreChangeShare"] is None else f"{r['noCoreChange']}/{r['registered']} ({r['noCoreChangeShare']:.0%})"
        lines.append(f"| {r['batch']} | `{r['base']}..{r['head']}` | {r['coreFiles']} | +{r['coreLinesAdded']}/−{r['coreLinesRemoved']} "
                     f"| {len(r['newVocabulary'])} | {r['cards']} | {share} |")
    total = sum(r["registered"] for r in reports)
    free = sum(r["noCoreChange"] for r in reports)
    if total:
        lines.append(f"| **All** | | | | | {total} | **{free}/{total} ({free / total:.0%})** |")
    for r in reports:
        needing = [row for row in r["rows"] if row["needsCore"]]
        if needing:
            lines.append("")
            lines.append(f"**{r['batch']}**, cards that used vocabulary new in the batch:")
            for row in needing:
                reason = ", ".join(row["uses"][:6]) + (" …" if len(row["uses"]) > 6 else "")
                if row.get("newHandler"):
                    reason = (reason + "; " if reason else "") + f"handler {row['newHandler']}"
                lines.append(f"- {row['code']}: {reason}")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--vocabulary", required=True, help="card-vocabulary --json output")
    parser.add_argument("--registry", action="append", required=True,
                        help="a batch registry (data/m4/batch-registry.json shape); repeat for several card sets")
    parser.add_argument("--batch", action="append", default=[], help="a batch id; all batches with a range if omitted")
    parser.add_argument("--range", action="append", default=[], help="ID=BASE..HEAD, overriding the registry's coreRange")
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--markdown", help="write the markdown report here")
    args = parser.parse_args()

    cards = {card["code"]: card for card in json.load(open(args.vocabulary, encoding="utf-8"))["cards"]}
    batches = [batch for path in args.registry for batch in json.load(open(path, encoding="utf-8"))["batches"]]
    ranges = dict(item.split("=", 1) for item in args.range)
    reports = []
    for batch in batches:
        if args.batch and batch["id"] not in args.batch:
            continue
        span = ranges.get(batch["id"]) or batch.get("coreRange")
        if not span:
            continue
        base, head = span.split("..", 1)
        reports.append(report(batch["id"], batch["codes"], base, head, cards))
    if not reports:
        print("core-growth: no batch with a range", file=sys.stderr)
        return 2
    text = markdown(reports)
    if args.markdown:
        open(args.markdown, "w", encoding="utf-8").write(text)
    print(json.dumps({"schemaVersion": 1, "reports": reports}, indent=1) if args.json else text, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
