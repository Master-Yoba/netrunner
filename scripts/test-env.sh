#!/usr/bin/env bash
set -euo pipefail

env_test_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
env_test_fixture="$(mktemp -d "${TMPDIR:-/tmp}/netrunner-env.XXXXXXXX")"
trap 'rm -rf -- "$env_test_fixture"' EXIT
mkdir -p "$env_test_fixture/scripts"
cp "$env_test_root/scripts/env.sh" "$env_test_fixture/scripts/env.sh"

check_layout() (
  local relative_path="$1"
  local initial_override="$2"
  local expected="$env_test_fixture/.tools/godot/$relative_path"
  mkdir -p "$(dirname -- "$expected")"
  cp /usr/bin/true "$expected"
  export GODOT4="$initial_override"
  source "$env_test_fixture/scripts/env.sh"
  [[ "$GODOT4" == "$expected" ]]
  [[ -f "$GODOT4" && -x "$GODOT4" ]]
  rm -- "$expected"
)

check_layout "Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64" ""
check_layout "Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64" "/missing/godot"
check_layout "Godot.app/Contents/MacOS/Godot" ""
check_layout "Godot" ""
(
  export GODOT4=/usr/bin/true
  source "$env_test_fixture/scripts/env.sh"
  [[ "$GODOT4" == /usr/bin/true ]]
)
echo "environment-discovery=PASS linux macos direct stale-override valid-override"
