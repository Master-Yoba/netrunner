#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/update-card-data.sh --commit <40-hex-sha> --retrieval-date YYYY-MM-DD

The command downloads the official repository at one immutable commit, creates
local-only metadata for each official pack (System Gateway and Elevation),
validates each offline, and replaces
data/vendor/{system-gateway,elevation}/{cards.json,manifest.json,SOURCE_COPYRIGHT.md}.
EOF
}

die() {
  echo "update-card-data: $*" >&2
  exit 2
}

if [[ "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

commit_sha=""
retrieval_date=""
while (($# > 0)); do
  case "$1" in
    --commit)
      [[ -z "$commit_sha" ]] || die "--commit may be specified only once."
      (($# >= 2)) || die "--commit requires a value."
      [[ "$2" != --* ]] || die "--commit requires a value."
      commit_sha="$2"
      shift 2
      ;;
    --retrieval-date)
      [[ -z "$retrieval_date" ]] || die "--retrieval-date may be specified only once."
      (($# >= 2)) || die "--retrieval-date requires a value."
      [[ "$2" != --* ]] || die "--retrieval-date requires a value."
      retrieval_date="$2"
      shift 2
      ;;
    *)
      die "unknown option '$1' (use --help for usage)."
      ;;
  esac
done

[[ "$commit_sha" =~ ^[0-9a-f]{40}$ ]] \
  || die "--commit must be an exact lowercase 40-hex commit SHA."
[[ "$retrieval_date" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] \
  || die "--retrieval-date must use YYYY-MM-DD."

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
source_url="https://github.com/Null-Signal-Games/netrunner-cards-json"
api_url="https://api.github.com/repos/Null-Signal-Games/netrunner-cards-json/commits/$commit_sha"
archive_url="$source_url/archive/$commit_sha.tar.gz"
tmp_parent="${TMPDIR:-/tmp}"
[[ -d "$tmp_parent" ]] || die "temporary directory '$tmp_parent' does not exist."
tmp_dir="$(mktemp -d "$tmp_parent/netrunner-card-data.XXXXXX")"
case "$tmp_dir" in
  "$tmp_parent"/netrunner-card-data.*) ;;
  *) die "mktemp returned an unexpected temporary path." ;;
esac

# Each official pack: upstream code, expected record count, local directory.
pack_codes=(sg elev)
pack_counts=(77 82)
pack_dirs=(system-gateway elevation)

data_parent="$repo_root/data/vendor"
mkdir -p "$data_parent"
[[ -d "$data_parent" && ! -L "$data_parent" ]] || die "data/vendor is not a normal directory."

stage_dirs=()
backup_dirs=()
moved=()
installed=()
install_complete=0

cleanup() {
  local status=$?
  local index
  if ((install_complete == 0)); then
    for index in "${!pack_dirs[@]}"; do
      local target="$data_parent/${pack_dirs[$index]}"
      if [[ "${installed[$index]:-0}" == 1 ]] && [[ -e "$target" || -L "$target" ]]; then
        rm -rf -- "$target"
      fi
      if [[ "${moved[$index]:-0}" == 1 && -n "${backup_dirs[$index]:-}" && -e "${backup_dirs[$index]}" ]]; then
        mv -- "${backup_dirs[$index]}" "$target" || status=1
      fi
    done
  fi
  for index in "${!stage_dirs[@]}"; do
    if [[ -d "${stage_dirs[$index]}" ]]; then
      rm -rf -- "${stage_dirs[$index]}"
    fi
  done
  for index in "${!backup_dirs[@]}"; do
    if [[ -n "${backup_dirs[$index]}" && -e "${backup_dirs[$index]}" ]]; then
      rm -rf -- "${backup_dirs[$index]}"
    fi
  done
  if [[ -d "$tmp_dir" ]]; then
    rm -rf -- "$tmp_dir"
  fi
  exit "$status"
}
trap cleanup EXIT

for index in "${!pack_dirs[@]}"; do
  stage="$(mktemp -d "$data_parent/.${pack_dirs[$index]}-stage.XXXXXX")"
  case "$stage" in
    "$data_parent"/."${pack_dirs[$index]}"-stage.*) ;;
    *) die "staging directory was created outside data/vendor." ;;
  esac
  stage_dirs[$index]="$stage"
  backup_dirs[$index]=""
  moved[$index]=0
  installed[$index]=0
done

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

curl "${curl_args[@]}" \
  --header 'Accept: application/vnd.github+json' \
  --header 'X-GitHub-Api-Version: 2022-11-28' \
  "$api_url" \
  -o "$tmp_dir/commit.json"
python3 - "$tmp_dir/commit.json" "$commit_sha" <<'PY'
import json
import sys

path, requested = sys.argv[1:]
with open(path, encoding="utf-8") as handle:
    payload = json.load(handle)
resolved = payload.get("sha")
if not isinstance(resolved, str) or resolved.lower() != requested:
    raise SystemExit(
        f"GitHub API resolved commit {resolved!r}, expected {requested!r}"
    )
PY

curl "${curl_args[@]}" "$archive_url" -o "$tmp_dir/source.tar.gz"
python3 - "$tmp_dir/source.tar.gz" "$commit_sha" <<'PY'
import posixpath
import sys
import tarfile

archive_path, commit = sys.argv[1:]
root = f"netrunner-cards-json-{commit}"
required = {
    f"{root}/COPYRIGHT.md",
    f"{root}/pack/sg.json",
    f"{root}/pack/elev.json",
    f"{root}/packs.json",
    f"{root}/cycles.json",
    f"{root}/schema/v1/card_schema.json",
}
image_suffixes = (".gif", ".jpeg", ".jpg", ".png", ".svg", ".webp")
with tarfile.open(archive_path, "r:gz") as archive:
    names = set()
    for member in archive.getmembers():
        name = member.name
        parts = name.split("/")
        if name.startswith("/") or ".." in parts or posixpath.normpath(name) != name:
            raise SystemExit(f"unsafe archive member: {name!r}")
        if name == root:
            if not member.isdir():
                raise SystemExit(f"archive root is not a directory: {name!r}")
            continue
        if not name.startswith(root + "/"):
            raise SystemExit(f"unexpected archive root: {name!r}")
        if member.isdir():
            continue
        if member.issym() or member.islnk() or not member.isfile():
            raise SystemExit(f"unsupported archive member type: {name!r}")
        lower = name.lower()
        if lower.endswith(image_suffixes) or "/art/" in lower or "/images/" in lower:
            raise SystemExit(f"artwork member is not permitted: {name!r}")
        names.add(name)
    missing = sorted(required - names)
    if missing:
        raise SystemExit(f"archive is missing required files: {', '.join(missing)}")
PY
tar --extract --gzip --file "$tmp_dir/source.tar.gz" \
  --directory "$tmp_dir" --no-same-owner --no-same-permissions
source_dir="$tmp_dir/netrunner-cards-json-$commit_sha"
[[ -d "$source_dir" ]] || die "pinned archive did not produce the expected source directory."

python3 - "$source_dir" <<'PY'
import json
import sys

source = sys.argv[1]
def read(relative):
    with open(f"{source}/{relative}", encoding="utf-8") as handle:
        return json.load(handle)

packs = read("packs.json")
schema = read("schema/v1/card_schema.json")
cycles = read("cycles.json")
items = schema.get("items", {})
properties = items.get("properties", {})
required = set(items.get("required", []))
if items.get("additionalProperties") is not False or not required:
    raise SystemExit("schema/v1/card_schema.json is not the expected strict v1 schema")
expected_packs = {
    "sg": (77, "system-gateway"),
    "elev": (82, "elevation"),
}
for pack_code, (count, cycle_code) in expected_packs.items():
    cards = read(f"pack/{pack_code}.json")
    if not isinstance(cards, list) or len(cards) != count:
        raise SystemExit(f"pack/{pack_code}.json must contain exactly {count} records, found {len(cards)}")
    pack_records = [record for record in packs if record.get("code") == pack_code]
    if len(pack_records) != 1:
        raise SystemExit(f"packs.json must contain exactly one {pack_code} record")
    pack = pack_records[0]
    if pack.get("size") != count or pack.get("cycle_code") != cycle_code:
        raise SystemExit(f"the {pack_code} pack metadata is not the expected {cycle_code} record")
    if not any(cycle.get("code") == cycle_code for cycle in cycles):
        raise SystemExit(f"cycles.json has no {cycle_code} cycle")
    seen = set()
    for index, card in enumerate(cards):
        if not isinstance(card, dict):
            raise SystemExit(f"{pack_code} cards[{index}] is not an object")
        unknown = set(card) - set(properties)
        missing = required - set(card)
        if unknown:
            raise SystemExit(f"{pack_code} cards[{index}] has unknown fields: {sorted(unknown)}")
        if missing:
            raise SystemExit(f"{pack_code} cards[{index}] is missing required fields: {sorted(missing)}")
        if card.get("pack_code") != pack_code:
            raise SystemExit(f"{pack_code} cards[{index}] is not in pack {pack_code}")
        if card.get("side_code") not in {"corp", "runner"}:
            raise SystemExit(f"{pack_code} cards[{index}] has an invalid side")
        code = card.get("code")
        if not isinstance(code, str) or len(code) != 5 or not code.isdigit() or code in seen:
            raise SystemExit(f"{pack_code} cards[{index}] has a duplicate or invalid code")
        seen.add(code)
    print(f"upstream-schema-check=pass pack={pack_code} records={count} schema=v1")
PY

dotnet_cmd="dotnet"
if [[ -x "$repo_root/.tools/dotnet/dotnet" ]]; then
  dotnet_cmd="$repo_root/.tools/dotnet/dotnet"
fi

for index in "${!pack_codes[@]}"; do
  pack="${pack_codes[$index]}"
  stage="${stage_dirs[$index]}"
  "$dotnet_cmd" run \
    --project "$repo_root/src/Netrunner.Cli" \
    --no-restore \
    -- card-data normalize \
    --upstream "$source_dir/pack/$pack.json" \
    --copyright "$source_dir/COPYRIGHT.md" \
    --commit "$commit_sha" \
    --retrieval-date "$retrieval_date" \
    --output-dir "$stage" \
    --pack "$pack" \
    --json

  for generated in cards.json manifest.json SOURCE_COPYRIGHT.md; do
    [[ -f "$stage/$generated" ]] || die "normalizer did not produce '$generated' for pack $pack."
  done
  entry_count=$(( $(find "$stage" -mindepth 1 -maxdepth 1 -print | wc -l) ))
  [[ "$entry_count" == "3" ]] || die "normalizer produced unexpected staging entries for pack $pack."

  target_dir="$data_parent/${pack_dirs[$index]}"
  if [[ -d "$target_dir" && ! -L "$target_dir" ]]; then
    while IFS= read -r -d '' entry; do
      case "$(basename -- "$entry")" in
        cards.json|manifest.json|SOURCE_COPYRIGHT.md)
          [[ -f "$entry" && ! -L "$entry" ]] \
            || die "refusing to replace '$target_dir': '$entry' is not a regular file."
          ;;
        *) die "refusing to replace '$target_dir': unexpected entry '$entry'." ;;
      esac
    done < <(find "$target_dir" -mindepth 1 -maxdepth 1 -print0)
  elif [[ -e "$target_dir" || -L "$target_dir" ]]; then
    die "refusing to replace non-directory target '$target_dir'."
  fi

  if [[ -f "$target_dir/cards.json" ]]; then
    cp -- "$target_dir/cards.json" "$tmp_dir/previous-$pack-cards.json"
    echo "--- normalized $pack card text diff (previous -> staged) ---"
    diff -u --label previous --label staged "$tmp_dir/previous-$pack-cards.json" "$stage/cards.json" || true
  else
    echo "--- normalized $pack card text diff: no previous local snapshot ---"
  fi
done

for index in "${!pack_dirs[@]}"; do
  target_dir="$data_parent/${pack_dirs[$index]}"
  if [[ -e "$target_dir" || -L "$target_dir" ]]; then
    backup="$data_parent/.${pack_dirs[$index]}-backup.$$"
    [[ ! -e "$backup" && ! -L "$backup" ]] || die "backup path already exists."
    backup_dirs[$index]="$backup"
    mv -- "$target_dir" "$backup"
    moved[$index]=1
  fi
  mv -- "${stage_dirs[$index]}" "$target_dir"
  installed[$index]=1
done

for pack in "${pack_codes[@]}"; do
  "$dotnet_cmd" run \
    --project "$repo_root/src/Netrunner.Cli" \
    --no-restore \
    -- card-data validate --root "$repo_root" --pack "$pack" --json
done
for index in "${!backup_dirs[@]}"; do
  if [[ -n "${backup_dirs[$index]}" && -e "${backup_dirs[$index]}" ]]; then
    rm -rf -- "${backup_dirs[$index]}"
    backup_dirs[$index]=""
  fi
done
install_complete=1

for index in "${!pack_codes[@]}"; do
  target_dir="$data_parent/${pack_dirs[$index]}"
  echo "card-data-update=ok pack=${pack_codes[$index]} commit=$commit_sha retrieval-date=$retrieval_date records=${pack_counts[$index]}"
  sha256sum \
    "$target_dir/cards.json" \
    "$target_dir/manifest.json" \
    "$target_dir/SOURCE_COPYRIGHT.md"
done
