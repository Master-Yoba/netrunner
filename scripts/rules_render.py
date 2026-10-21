#!/usr/bin/env python3
"""Render the pinned Netrunner Comprehensive Rules snapshot into reference
artifacts.

Input:
  --source      the pinned rules page bytes (data/vendor/rules/source.html)
  --out-dir     where to write chapters/, index.json, and manifest.json
  --url         upstream rules URL (recorded in the manifest)
  --version     rules version, e.g. 26.03
  --retrieval-date  ISO date the snapshot was fetched

Output (all inert reference material; never parsed into, or consumed at
runtime by, the engine or AI):
  source.html             normalized snapshot bytes (Cloudflare email tokens
                          decoded to mailto: so the file and its sha256 are
                          stable across fetches)
  chapters/<NN>-<slug>.md   one file per top-level chapter
  index.json                rule id -> {kind, file, anchor, title, section, line}
  manifest.json             provenance (url, version, date, sha256, counts)

The index is keyed by the document's own identifiers: numbered rules (e.g.
"6.9.1", kind "rule") and their lettered sub-clauses (e.g. "6.9.1a", kind
"sub"). A sub-clause block (ol.SubRules) is a sibling of, and follows, the rule
it belongs to, so it is attributed to the most recent numbered rule.

Requires: python3 and beautifulsoup4 (pip install beautifulsoup4).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

try:
    from bs4 import BeautifulSoup, NavigableString
except Exception:  # pragma: no cover - dependency guard
    sys.stderr.write(
        "rules_render: beautifulsoup4 is required to render the rules "
        "(pip install beautifulsoup4).\n"
    )
    raise

SKIP_TAGS = {"svg", "script", "style", "nav", "header", "aside", "noscript"}
CONTAINER_TAGS = {"div", "section", "article", "main", "body", "figure", "blockquote"}


def clean(text: str) -> str:
    return re.sub(r"\s+", " ", text or "").strip()


def slugify(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")


def norm_number(value: str) -> str:
    """'6.9.1.' -> '6.9.1' (strip trailing periods/whitespace)."""
    return (value or "").strip().rstrip(".").strip()


# Cloudflare "Email Address Obfuscation" re-encodes the page's mailto link with
# a fresh random XOR key on every request, so the raw HTML bytes differ on each
# fetch even though the visible rules (and the email) are constant. Decode each
# token back to its plaintext mailto: so the committed snapshot and its sha256
# are stable across fetches.
CF_EMAIL_RE = re.compile(r"/cdn-cgi/l/email-protection#([0-9a-f]+)")


def decode_cf_email(token: str) -> str:
    key = int(token[:2], 16)
    body = bytes.fromhex(token[2:])
    return "".join(chr(b ^ key) for b in body)


def normalize_source(text: str) -> str:
    """Replace every Cloudflare email-obfuscation href with its decoded
    mailto: value, leaving all other bytes untouched."""
    return CF_EMAIL_RE.sub(lambda m: "mailto:" + decode_cf_email(m.group(1)), text)


class Renderer:
    def __init__(self, url: str, version: str, retrieval_date: str, title: str):
        self.url = url
        self.version = version
        self.retrieval_date = retrieval_date
        self.source_title = title
        self.chapters: list[dict] = []
        self.active: dict | None = None
        self.section: str | None = None
        # The most recent numbered rule; a sibling SubRules block indexes its
        # lettered sub-clauses under this number (e.g. 6.9.1 + "a" -> 6.9.1a).
        self.current_rule: str | None = None

    # -- emission -----------------------------------------------------------
    def emit(self, line: str) -> None:
        assert self.active is not None, "no active chapter for output"
        self.active["lines"].append(line)

    def on_h1(self, h1) -> None:
        order = len(self.chapters) + 1
        anchor = h1.get("id") or ""
        link = h1.find("a", class_="RuleLink")
        title = clean(link.get_text(" ", strip=True)) if link else clean(h1.get_text(" ", strip=True))
        slug = slugify(anchor.removeprefix("chpt_")) or slugify(title)
        self.active = {
            "n": order,
            "anchor": anchor,
            "title": title,
            "file": f"chapters/{order:02d}-{slug}.md",
            "lines": [],
            "rules": [],
        }
        self.chapters.append(self.active)
        self.section = None
        # A new chapter cannot inherit sub-clause attribution from the previous
        # one; if it ever began with a SubRules block, those entries must not be
        # indexed under the last rule of the prior chapter.
        self.current_rule = None
        self.emit(f"# {title}")
        self.emit("")
        self.emit(
            f"_Netrunner Comprehensive Rules v{self.version} "
            f'\u00b7 [source]({self.url}#{anchor}) \u00b7 retrieved {self.retrieval_date}_'
        )
        self.emit("")

    def on_heading(self, heading) -> None:
        # Use the same inline flattening as rule text: get_text(" ") would
        # insert spaces at inline element boundaries (e.g. "…Turn (</a><a>Section
        # 5.6</a>)" -> "…Turn ( Section 5.6 )"), diverging from the source text.
        text = inline_text(heading)
        if not text:
            return
        self.section = text
        self.emit("")
        self.emit(f"## {text}")
        self.emit("")

    def on_paragraph(self, p) -> None:
        text = inline_text(p)
        if text:
            self.emit("")
            self.emit(text)
            self.emit("")

    def on_list(self, ol, depth: int) -> None:
        indent = "  " * depth
        # Iterate every direct child in document order, not just <li>: the
        # source also places whole blocks (Example sub-sections, Snippet
        # paragraphs) inside <ol> as siblings of the <li> items, and browsers
        # keep them nested there per the HTML spec.
        for child in ol.children:
            if isinstance(child, NavigableString):
                if str(child).strip():
                    self.emit(clean(str(child)))
                continue
            if child.name in ("ol", "ul"):
                self.on_list(child, depth)
            elif child.name == "li":
                cls = child.get("class") or []
                if "Rule" in cls and "SubRule" not in cls:
                    self.on_rule(child)
                elif "SubRule" in cls:
                    self.on_subrule(child, depth)
                else:
                    text = inline_text(child)
                    if text:
                        self.emit(f"{indent}- {text}")
                    for sub in child.find_all(["ol", "ul"], recursive=False):
                        self.on_list(sub, depth + 1)
            elif child.name == "p":
                self.on_paragraph(child)
            else:
                # Descend into wrappers and unknown blocks so no content is lost.
                self.render(child)
        self.emit("")

    def on_subrule(self, li, depth: int) -> None:
        """A lettered sub-clause (a., b., …). It belongs to the most recent
        numbered rule and is indexed as "<rule><letter>" (e.g. 6.9.1a)."""
        indent = "  " * depth
        link = li.find("a", class_="RuleLink")
        letter = norm_number(link.get_text(strip=True)) if link else ""
        rule_text = li.find("span", class_="RuleText")
        body = clean(inline_text(rule_text)) if rule_text else ""
        if not body:
            body = inline_text(li)
            if letter and body.startswith(letter):
                body = body[len(letter):].lstrip().lstrip(".").lstrip()
        self.emit(f"{indent}- {letter}. {body}".rstrip())
        if self.current_rule and letter and self.active is not None:
            self.active["rules"].append(
                {
                    "number": f"{self.current_rule}{letter}",
                    "kind": "sub",
                    "anchor": li.get("id"),
                    "title": body[:90],
                    "section": self.section,
                    "line": len(self.active["lines"]),
                }
            )
        self.emit("")
        for sub in li.find_all(["ol", "ul"], recursive=False):
            self.on_list(sub, depth + 1)

    def on_rule(self, li) -> None:
        link = li.find("a", class_="RuleLink")
        number = norm_number(link.get_text(strip=True)) if link else ""
        raw_number = clean(link.get_text(strip=True)) if link else ""
        rule_text = li.find("span", class_="RuleText")
        body = clean(inline_text(rule_text)) if rule_text else ""
        if not body:
            body = inline_text(li)
            if raw_number and body.startswith(raw_number):
                body = body[len(raw_number):].strip()
        self.emit(f"**{raw_number}** {body}".rstrip())
        if self.active is not None and number:
            # The rule line is the last element now, so its 1-based line number is
            # len(lines); record before the trailing blank line is appended.
            self.current_rule = number
            self.active["rules"].append(
                {
                    "number": number,
                    "kind": "rule",
                    "anchor": li.get("id"),
                    "title": body[:90],
                    "section": self.section,
                    "line": len(self.active["lines"]),
                }
            )
        self.emit("")
        for sub in li.find_all(["ol", "ul"], recursive=False):
            self.on_list(sub, 1)

    # -- driver -------------------------------------------------------------
    def render(self, container) -> None:
        for child in container.children:
            if isinstance(child, NavigableString):
                continue
            name = child.name
            if name in SKIP_TAGS:
                continue
            if (child.get("id") or "") == "RulesToc":
                continue  # table of contents is redundant with index.json
            if name == "h1":
                self.on_h1(child)
            elif name in ("h2", "h3", "h4"):
                self.on_heading(child)
            elif name in ("ol", "ul"):
                self.on_list(child, 0)
            elif name == "p":
                self.on_paragraph(child)
            else:
                # Descend into wrappers and unknown blocks so no content is lost.
                self.render(child)

    # -- outputs ------------------------------------------------------------
    def write(self, out_dir: Path, source_bytes: bytes) -> dict:
        chapters_dir = out_dir / "chapters"
        chapters_dir.mkdir(parents=True, exist_ok=True)

        index: dict[str, dict] = {}
        for chapter in self.chapters:
            path = out_dir / chapter["file"]
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("\n".join(chapter["lines"]).rstrip() + "\n", encoding="utf-8")
            for rule in chapter["rules"]:
                index[rule["number"]] = {
                    "kind": rule.get("kind", "rule"),
                    "file": chapter["file"],
                    "anchor": rule["anchor"],
                    "title": rule["title"],
                    "section": rule["section"],
                    "line": rule["line"],
                }

        ordered = {k: index[k] for k in natural_sort_keys(index)}
        index_path = out_dir / "index.json"
        index_path.write_text(json.dumps(ordered, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

        # Persist the normalized snapshot bytes so the committed source.html (and
        # its sha256) is reproducible across fetches; see normalize_source().
        (out_dir / "source.html").write_bytes(source_bytes)
        sha256 = hashlib.sha256(source_bytes).hexdigest()
        all_entries = [r for c in self.chapters for r in c["rules"]]
        rule_count = sum(1 for r in all_entries if r.get("kind", "rule") == "rule")
        sub_rule_count = sum(1 for r in all_entries if r.get("kind", "rule") == "sub")
        manifest = {
            "schemaVersion": 1,
            "kind": "comprehensive-rules",
            "sourceUrl": self.url,
            "upstreamUrl": self.url,
            "rulesVersion": self.version,
            "sourceTitle": self.source_title,
            "retrievalDate": self.retrieval_date,
            "outputSha256": sha256,
            "chapterCount": len(self.chapters),
            "ruleCount": rule_count,
            "subRuleCount": sub_rule_count,
            "licenseIdentity": "NOASSERTION",
            "licenseNote": (
                "Committed to the repository by explicit project decision as a local "
                "reference snapshot. The upstream rules are copyrighted by Null Signal "
                "Games; no redistribution license is asserted (NOASSERTION). Verify "
                "redistribution rights before any public distribution."
            ),
            "files": {"source": "source.html", "index": "index.json", "chapters": "chapters/"},
        }
        (out_dir / "manifest.json").write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
        )
        return manifest


INLINE_SKIP = {"svg", "script", "style"}


# Plain-text tokens the rules themselves define for the symbol images
# (section 1.3 "Symbols"); alt -> token, falling back to "{alt}".
SYMBOL_PLAINTEXT = {
    "credit": "{c}",
    "click": "{click}",
    "recurring": "{recurring}",
    "link": "{link}",
    "mu": "{MU}",
    "sub": "{sub}",
    "trash": "{trash}",
    "interrupt": "{interrupt}",
    "trashcost": "{trash}",
}


def inline_text(el) -> str:
    """Flatten inline text, dropping nested lists/icons and inlining symbol
    images as their plain-text tokens. Parts are concatenated directly (the
    source keeps its own spacing) then whitespace is collapsed."""
    parts: list[str] = []

    def walk(node, in_list: bool) -> None:
        for child in node.children:
            if isinstance(child, NavigableString):
                if not in_list:
                    parts.append(str(child))
            else:
                name = child.name
                if name in INLINE_SKIP:
                    continue
                if name in ("ol", "ul"):
                    walk(child, True)
                elif name == "a" and "RuleAnchor" in (child.get("class") or []):
                    continue
                elif name == "img":
                    if not in_list:
                        alt = child.get("alt") or ""
                        if alt:
                            parts.append(SYMBOL_PLAINTEXT.get(alt, "{%s}" % alt))
                else:
                    walk(child, in_list)

    walk(el, False)
    return clean("".join(parts))


def natural_sort_keys(mapping: dict) -> list[str]:
    def key(number: str):
        parts = re.split(r"(\d+)", number)
        return [int(p) if p.isdigit() else p for p in parts]

    return sorted(mapping, key=key)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--out-dir", required=True)
    parser.add_argument("--url", default="https://rules.nullsignal.games/")
    parser.add_argument("--version", default="26.03")
    parser.add_argument("--retrieval-date", required=True)
    args = parser.parse_args(argv)

    source_path = Path(args.source)
    if not source_path.is_file():
        parser.error(f"source not found: {source_path}")
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    # Normalize the fetched bytes (Cloudflare email-obfuscation tokens) so the
    # committed snapshot is deterministic; parse from the normalized text.
    normalized = normalize_source(source_path.read_text(encoding="utf-8"))
    source_bytes = normalized.encode("utf-8")
    soup = BeautifulSoup(normalized, "html.parser")
    content = soup.find("div", id="RulesContent")
    if content is None:
        parser.error("source has no div#RulesContent; the rules page layout changed")
    title = clean(soup.title.get_text(strip=True)) if soup.title else "Netrunner Comprehensive Rules"

    # The chapter content lives in <main>; skip the title block and the TOC.
    root = content.find("main") or content

    renderer = Renderer(args.url, args.version, args.retrieval_date, title)
    renderer.render(root)
    manifest = renderer.write(out_dir, source_bytes)

    print(
        f"rules-render=ok chapters={manifest['chapterCount']} "
        f"rules={manifest['ruleCount']} sha256={manifest['outputSha256']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
