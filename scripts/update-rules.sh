#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/update-rules.sh --retrieval-date YYYY-MM-DD
                               [--url URL] [--version 26.03] [--expected-sha <64-hex>]

Refreshes the local Comprehensive Rules reference. It downloads the pinned rules
page over HTTPS, renders per-chapter Markdown plus a number/sub-clause index and
a provenance manifest into a staging directory, validates the output, and
atomically replaces the generated files in data/vendor/rules/
(source.html, chapters/, index.json, manifest.json). Committed reference files
that are not generated, such as README.md, are preserved.

Unlike the card-data updater (pinned to an immutable commit), the rules page is
a live document with no immutable revision, and Cloudflare re-encodes its
mailto link with a fresh random token on every request, so the raw bytes are not
stable across fetches. The renderer decodes those tokens (leaving all rules
content untouched), so provenance is recorded as (url, rules version, retrieval
date, and the sha256 of the normalized source bytes). The committed source.html
plus manifest.json is the pin: its sha256 can be verified at any time. Pass
--expected-sha to require the normalized source to match a previously pinned
snapshot before installing.
EOF
}

die() {
  echo "update-rules: $*" >&2
  exit 2
}

if [[ "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

retrieval_date=""
url="https://rules.nullsignal.games/"
version=""
expected_sha=""
while (($# > 0)); do
  case "$1" in
    --retrieval-date)
      [[ -z "$retrieval_date" ]] || die "--retrieval-date may be specified only once."
      (($# >= 2)) || die "--retrieval-date requires a value."
      [[ "$2" != --* ]] || die "--retrieval-date requires a value."
      retrieval_date="$2"
      shift 2
      ;;
    --url)
      [[ -z "${url:-}" || "$url" == "https://rules.nullsignal.games/" ]] \
        || die "--url may be specified only once."
      (($# >= 2)) || die "--url requires a value."
      [[ "$2" != --* ]] || die "--url requires a value."
      url="$2"
      shift 2
      ;;
    --version)
      [[ -z "$version" ]] || die "--version may be specified only once."
      (($# >= 2)) || die "--version requires a value."
      [[ "$2" != --* ]] || die "--version requires a value."
      version="$2"
      shift 2
      ;;
    --expected-sha)
      [[ -z "$expected_sha" ]] || die "--expected-sha may be specified only once."
      (($# >= 2)) || die "--expected-sha requires a value."
      [[ "$2" != --* ]] || die "--expected-sha requires a value."
      expected_sha="$2"
      shift 2
      ;;
    *)
      die "unknown option '$1' (use --help for usage)."
      ;;
  esac
done

[[ -n "$retrieval_date" ]] || die "--retrieval-date is required (use --help for usage)."
[[ "$retrieval_date" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] \
  || die "--retrieval-date must use YYYY-MM-DD."
[[ "$url" =~ ^https://[^[:space:]]+$ ]] \
  || die "--url must be an https URL."
if [[ -n "$expected_sha" ]]; then
  [[ "$expected_sha" =~ ^[0-9a-f]{64}$ ]] \
    || die "--expected-sha must be an exact lowercase 64-hex sha256."
fi

python3 - "$retrieval_date" <<'PY'
from datetime import date
import sys

value = sys.argv[1]
try:
    parsed = date.fromisoformat(value)
except ValueError as error:
    raise SystemExit(f"invalid retrieval date: {error}")
if parsed.isoformat() != value:
    raise SystemExit("retrieval date must be canonical YYYY-MM-DD")
PY

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
render_script="$repo_root/scripts/rules_render.py"
[[ -f "$render_script" ]] || die "rules renderer not found at $render_script."
command -v python3 >/dev/null 2>&1 || die "python3 is required."

data_parent="$repo_root/data/vendor"
target_dir="$data_parent/rules"
mkdir -p "$data_parent"
[[ -d "$data_parent" && ! -L "$data_parent" ]] || die "data/vendor is not a normal directory."
stage_dir="$(mktemp -d "$data_parent/.rules-stage.XXXXXX")"
case "$stage_dir" in
  "$data_parent"/.rules-stage.*) ;;
  *) die "staging directory was created outside data/vendor." ;;
esac

# Files the renderer produces and that this command replaces on install.
GENERATED_NAMES="source.html index.json manifest.json chapters"

backup_dir=""
target_moved=0
target_installed=0
install_complete=0

cleanup() {
  local status=$?
  if ((install_complete == 0 && (target_moved == 1 || target_installed == 1))); then
    if ((target_installed == 1)) && [[ -e "$target_dir" || -L "$target_dir" ]]; then
      rm -rf -- "$target_dir"
    fi
    if [[ -n "$backup_dir" && -e "$backup_dir" ]]; then
      mv -- "$backup_dir" "$target_dir" || status=1
    fi
  fi
  if [[ -d "$stage_dir" ]]; then
    rm -rf -- "$stage_dir"
  fi
  if [[ -n "$backup_dir" && -e "$backup_dir" ]]; then
    rm -rf -- "$backup_dir"
  fi
  exit "$status"
}
trap cleanup EXIT

curl_args=(
  --fail
  --silent
  --show-error
  --location
  --proto '=https'
  --tlsv1.2
  --retry 3
  --retry-delay 1
)

echo "update-rules: fetching $url"
curl "${curl_args[@]}" "$url" -o "$stage_dir/source.html"
[[ -s "$stage_dir/source.html" ]] || die "downloaded source.html is empty."

# Determine the rules version: explicit --version wins, else detect from the page.
if [[ -z "$version" ]]; then
  version="$(python3 - "$stage_dir/source.html" <<'PY'
import re
import sys
from pathlib import Path

text = Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace")
match = re.search(r"<title>[^<]*?v(\d{2}\.\d{2})", text) or re.search(r"\(v(\d{2}\.\d{2})\)", text)
if not match:
    raise SystemExit("could not detect the rules version from the page; pass --version")
print(match.group(1))
PY
)"
fi
[[ "$version" =~ ^[0-9]{2}\.[0-9]{2}$ ]] \
  || die "rules version '$version' is not the expected YY.MM form."

python3 "$render_script" \
  --source "$stage_dir/source.html" \
  --out-dir "$stage_dir" \
  --url "$url" \
  --version "$version" \
  --retrieval-date "$retrieval_date"

# The renderer rewrote stage/source.html in normalized form (Cloudflare email
# tokens decoded), which is the stable snapshot id; --expected-sha pins that.
if [[ -n "$expected_sha" ]]; then
  actual_sha="$(sha256sum "$stage_dir/source.html" | awk '{print $1}')"
  [[ "$actual_sha" == "$expected_sha" ]] \
    || die "normalized source sha256 ${actual_sha} does not match --expected-sha ${expected_sha}; the upstream page changed. Re-run without --expected-sha to accept the new version."
fi

python3 - "$stage_dir" <<'PY'
import hashlib
import json
import sys
from pathlib import Path

stage = Path(sys.argv[1])
manifest = json.loads((stage / "manifest.json").read_text(encoding="utf-8"))
index = json.loads((stage / "index.json").read_text(encoding="utf-8"))

sha = hashlib.sha256((stage / "source.html").read_bytes()).hexdigest()
if sha != manifest["outputSha256"]:
    raise SystemExit("manifest outputSha256 does not match the staged source.html")
if manifest["chapterCount"] < 1 or manifest["ruleCount"] < 1:
    raise SystemExit("rendered manifest has no chapters or rules")
if len(index) != manifest["ruleCount"] + manifest["subRuleCount"]:
    raise SystemExit("index size does not equal ruleCount + subRuleCount")
for number, entry in index.items():
    path = stage / entry["file"]
    lines = path.read_text(encoding="utf-8").splitlines()
    line_no = entry["line"]
    if not (1 <= line_no <= len(lines)):
        raise SystemExit(f"index entry {number} points outside {entry['file']}")
    line = lines[line_no - 1]
    if entry["kind"] == "rule":
        if not (f"**{number}.**" in line or line.strip().startswith(f"**{number}**")):
            raise SystemExit(f"index entry {number} does not match its line")
    else:
        if not line.strip().lstrip("- ").strip().startswith(number[-1] + ". "):
            raise SystemExit(f"index entry {number} does not match its line")
print(
    "rules-validate=pass chapters={} rules={} sub-rules={}".format(
        manifest["chapterCount"], manifest["ruleCount"], manifest["subRuleCount"]
    )
)
PY

for generated in source.html index.json manifest.json chapters; do
  [[ -e "$stage_dir/$generated" ]] || die "renderer did not produce '$generated'."
done
[[ -d "$stage_dir/chapters" ]] || die "staged chapters/ is not a directory."

# Preserve committed, non-generated files (e.g. README.md) by copying them into
# the staged directory so the atomic swap keeps them.
if [[ -d "$target_dir" ]]; then
  while IFS= read -r -d '' entry; do
    base="$(basename -- "$entry")"
    case "$base" in
      source.html|index.json|manifest.json|chapters) : ;;
      *) cp -a -- "$entry" "$stage_dir/" ;;
    esac
  done < <(find "$target_dir" -mindepth 1 -maxdepth 1 -print0)
fi

if [[ -d "$target_dir" && ! -L "$target_dir" ]]; then
  while IFS= read -r -d '' entry; do
    case "$(basename -- "$entry")" in
      source.html|index.json|manifest.json|chapters|README.md)
        [[ -e "$entry" ]] || die "refusing to replace '$target_dir': missing '$entry'."
        ;;
      *) die "refusing to replace '$target_dir': unexpected entry '$entry'." ;;
    esac
  done < <(find "$target_dir" -mindepth 1 -maxdepth 1 -print0)
elif [[ -e "$target_dir" || -L "$target_dir" ]]; then
  die "refusing to replace non-directory target '$target_dir'."
fi

if [[ -e "$target_dir" || -L "$target_dir" ]]; then
  backup_dir="$data_parent/.rules-backup.$$"
  [[ ! -e "$backup_dir" && ! -L "$backup_dir" ]] || die "backup path already exists."
  mv -- "$target_dir" "$backup_dir"
  target_moved=1
fi
mv -- "$stage_dir" "$target_dir"
target_installed=1
stage_dir=""

if [[ -n "$backup_dir" && -e "$backup_dir" ]]; then
  rm -rf -- "$backup_dir"
  backup_dir=""
fi
install_complete=1

echo "rules-update=ok version=$version retrieval-date=$retrieval_date"
sha256sum \
  "$target_dir/source.html" \
  "$target_dir/index.json" \
  "$target_dir/manifest.json"
