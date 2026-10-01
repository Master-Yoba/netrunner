#!/usr/bin/env bash
# Runs the full gates on a commit in a separate worktree, so work can go on in this checkout meanwhile:
#   scripts/gate-worktree.sh [REF]     (default HEAD; commit the work first)
# The worktree (.tools/gate-worktree) is kept between runs, so its builds and Godot import stay warm. It
# shares this checkout's local-only inputs: the .NET SDK, Godot, ripgrep and Xvfb by symlink, and the
# official card data; its Godot user data is its own copy, so its UI smokes don't touch this checkout's.
# Logs go to artifacts/gates/<time>-<commit>/; the last line is "GATES PASS" or "GATES FAIL".
set -uo pipefail

main_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
ref="${1:-HEAD}"
commit="$(git -C "$main_root" rev-parse --verify "$ref^{commit}")" || { echo "Unknown ref '$ref'." >&2; exit 2; }
tree="$main_root/.tools/gate-worktree"
logs="$main_root/artifacts/gates/$(date -u +%Y%m%dT%H%M%SZ)-${commit:0:9}"
mkdir -p "$logs"

if [[ -d "$tree/.git" || -f "$tree/.git" ]]; then
  git -C "$tree" checkout --quiet --detach "$commit"
else
  git -C "$main_root" worktree add --quiet --detach "$tree" "$commit"
fi
# Tracked files exactly as committed; ignored build output is kept for speed.
git -C "$tree" reset --quiet --hard "$commit"
git -C "$tree" clean --quiet -fd

mkdir -p "$tree/.tools" "$tree/data/vendor"
for tool in dotnet godot ripgrep xvfb; do
  [[ -e "$main_root/.tools/$tool" ]] && ln -sfn "$main_root/.tools/$tool" "$tree/.tools/$tool"
done
ln -sfn "$main_root/data/vendor/system-gateway" "$tree/data/vendor/system-gateway"
if [[ ! -d "$tree/.tools/godot-data" && -d "$main_root/.tools/godot-data" ]]; then
  cp -a "$main_root/.tools/godot-data" "$tree/.tools/godot-data"
fi

echo "gates commit=$commit worktree=$tree logs=$logs"
status=0
step() {
  local name="$1"; shift
  local started=$SECONDS
  if (cd "$tree" && "$@") >"$logs/$name.log" 2>&1; then
    echo "gates $name=PASS $((SECONDS - started))s"
  else
    echo "gates $name=FAIL $((SECONDS - started))s ($logs/$name.log)"
    status=1
  fi
}
step verify make verify
step m5-gate make m5-gate
step tactics make ai-tactics-verify "TACTICAL_ARGS=--workers 12"
step bot-check bash scripts/bot-check.sh
if [[ $status -eq 0 ]]; then echo "GATES PASS commit=$commit"; else echo "GATES FAIL commit=$commit"; fi
exit $status
