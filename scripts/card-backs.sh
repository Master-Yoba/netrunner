#!/usr/bin/env bash
set -euo pipefail
# Provisions the official Null Signal Games card backs into the ignored
# .tools/card-backs directory, which overrides the committed original backs
# (scripts/play.sh and scripts/verify.sh point NETRUNNER_CARD_BACK_DIRECTORY
# there). The NSG visual-assets license does not cover card backs, so they
# stay local-only, like card art.
backs_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
backs_url="https://chiriboga.cronbach.com/images/images.zip"
backs_dir="${NETRUNNER_CARD_BACK_DIRECTORY:-$backs_root/.tools/card-backs}"
declare -A backs_members=(
  [corp.png]="images/Corp_back.png 1b9ac73029558ff7f695d483941c71acbf02a3f791fb2fda7d3c8b7b5cf7c155"
  [runner.png]="images/Runner_back.png 7ca7086e60bd70dbd5be799ad0a5c876860e0a82d5e0ed569c2862682474b797"
)

backs_have_all() {
  local name member sha
  for name in "${!backs_members[@]}"; do
    read -r member sha <<<"${backs_members[$name]}"
    [[ -f "$backs_dir/$name" ]] && echo "$sha  $backs_dir/$name" | sha256sum --check --status || return 1
  done
}

if [[ "${1:-}" != "--refresh" ]] && backs_have_all; then
  echo "NSG card backs already present in $backs_dir."
  exit 0
fi

backs_tmp="$(mktemp -d)"
trap 'rm -rf "$backs_tmp"' EXIT
echo "Downloading $backs_url (about 150 MB) for the two card backs..."
curl -fL --retry 3 --progress-bar -o "$backs_tmp/images.zip" "$backs_url"
mkdir -p "$backs_dir"
for name in "${!backs_members[@]}"; do
  read -r member sha <<<"${backs_members[$name]}"
  unzip -p "$backs_tmp/images.zip" "$member" >"$backs_tmp/$name"
  if ! echo "$sha  $backs_tmp/$name" | sha256sum --check --status; then
    echo "$member does not match the pinned SHA256 $sha; upstream changed." >&2
    exit 1
  fi
  mv "$backs_tmp/$name" "$backs_dir/$name"
done
echo "Installed NSG card backs into $backs_dir."
