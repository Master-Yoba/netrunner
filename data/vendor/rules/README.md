# Netrunner Comprehensive Rules — local reference

A pinned, offline copy of the official [Netrunner Comprehensive Rules](https://rules.nullsignal.games/),
rendered to per-chapter Markdown plus a searchable index. This is a **local reference snapshot only**.

> **Inert by design.** These files are reference material for humans and for AI agents that need to
> look up a rule. They are **never parsed into, or consumed at runtime by, the rules engine or the
> AI runtime.** The engine's authoritative rules baseline is pinned separately in
> `BuildMetadata.RulesVersion`; treat this directory as documentation, not game data.

## What is here

| Path | Contents |
| --- | --- |
| `source.html` | The pinned rules page, **normalized** (Cloudflare's per-request email-obfuscation token decoded to the real `mailto:`) so the file — and its `sha256` — is stable across fetches. Verify it against `outputSha256` in `manifest.json`. |
| `chapters/*.md` | One Markdown file per top-level chapter, with rule text and lettered sub-clauses inlined. |
| `index.json` | Maps every rule identifier to its chapter file, 1-based line, anchor, section, and a short title. |
| `manifest.json` | Provenance: source URL, rules version, retrieval date, source sha256, and counts. |

## Provenance (current pin)

- **Source URL:** <https://rules.nullsignal.games/>
- **Rules version:** `26.03` (document title: *Netrunner Comprehensive Rules (v26.03)*)
- **Retrieved:** `2026-09-01`
- **Source sha256:** `ae93dad66bd14cd197c8bb3ddbe0df2a4d5ad14e0461682d602009fbb582f8e2` (of the normalized `source.html`)
- **Counts:** 11 chapters · 608 numbered rules · 812 lettered sub-clauses · 1420 index entries

Verify the pin at any time:

```sh
cd data/vendor/rules
sha256sum source.html   # must equal outputSha256 in manifest.json
```

> **Why normalize?** The page is served through Cloudflare, which re-encodes the
> one `mailto` link with a fresh random token on every request. The raw bytes
> therefore differ on each fetch even though the rules are unchanged. The
> renderer decodes that token back to `mailto:rules@nullsignal.games`, leaving
> every rule byte identical, so the committed `source.html` (and its sha256) is
> reproducible: fetching the same page twice and normalizing both yields the
> same sha256.

## How to read it

- **Chapters** mirror the official document's chapters 1–11 (e.g. `06-runs.md`,
  `11-appendix-timing-structures.md`). Numbered rules appear as `**6.9.1.** …` and lettered
  sub-clauses as `- a. …` directly beneath their rule.
- **`index.json`** is the fast lookup path. Each key is the document's own identifier and each value
  is `{ kind, file, anchor, title, section, line }`. To jump to a rule, open `file` at `line`
  (1-based), or use the browser anchor `#<anchor>`.

```python
import json
idx = json.load(open("data/vendor/rules/index.json"))
entry = idx["6.9.1a"]          # -> chapter file, line, anchor, section, title
```

### Identifier scheme

- **Numbered rule:** `N.N.N` (e.g. `6.9.1`), `kind: "rule"`.
- **Lettered sub-clause:** the parent rule number plus a letter (e.g. `6.9.1a`), `kind: "sub"`.
  Sub-clause blocks follow the rule they belong to in the source, so they are indexed under the most
  recent numbered rule.

### Symbols

The official rules use icon images for resources. This snapshot inlines them as the plain-text tokens
the rules themselves define (section 1.3): `{c}` credit · `{click}` click · `{MU}` μ-credit ·
`{sub}` subroutine · `{trash}` trash · `{link}` link · `{recurring}` recurring · `{interrupt}`
interrupt. If a future page uses an unknown icon, it is rendered as `{<alt-text>}`.

### Citation discrepancy (v26.03)

Some rule citations used in this repository reference **lettered sub-clauses that do not exist in
v26.03**. Those keys are absent from `index.json`; resolve them to the **parent rule** instead:

- **Absent** (resolve to parent): `10.3.5c`, `10.3.5e`–`10.3.5i` → parent `10.3.5` exists.
- **Present:** `8.5.11a`–`8.5.11c`, `8.5.13a`–`8.5.13d`.

If you add new citations, verify the exact key exists in `index.json`; if it does not, cite the
nearest existing parent number.

## Updating

The updater re-fetches the page, re-renders the artifacts into a staging directory, validates them
(counts, index resolution, source sha), and atomically replaces the generated files — preserving this
`README.md`.

```sh
./scripts/update-rules.sh --retrieval-date YYYY-MM-DD
```

Options:

- `--url URL` — rules page to fetch (default `https://rules.nullsignal.games/`).
- `--version YY.MM` — pin an explicit rules version (default: auto-detected from the page title).
- `--expected-sha <64-hex>` — require the fetched bytes to match a previously pinned `source.html`;
  aborts if upstream changed. Useful to reproduce an exact snapshot.

The pin model differs from card data: the rules page is a live document with no immutable revision,
so provenance is `(url, version, retrieval-date, source sha256)`. After an update, commit the new
`source.html`, `chapters/`, `index.json`, and `manifest.json`; `git log` then records each refresh.
Requires `python3` and `beautifulsoup4` (`pip install beautifulsoup4`).

## License

The upstream Comprehensive Rules are **copyrighted by Null Signal Games**. This snapshot is committed
to the repository by explicit project decision for local reference use; **no redistribution license is
asserted (`NOASSERTION`)**. Verify redistribution rights before any public distribution. See
`THIRD_PARTY_NOTICES.md`.
