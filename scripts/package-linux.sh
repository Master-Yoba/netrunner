#!/usr/bin/env bash
set -euo pipefail

package_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$package_root/scripts/env.sh"

package_output="$package_root/artifacts/linux/NetrunnerSolo.x86_64"
mkdir -p "$(dirname -- "$package_output")" "$NETRUNNER_GODOT_DATA"

XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$package_root/src/Netrunner.Godot" \
  --export-release "Linux x86_64" "$package_output"

if [[ ! -x "$package_output" ]]; then
  echo "Godot did not produce an executable Linux package."
  exit 1
fi

XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  timeout 15 "$package_output" --headless -- --smoke-test

echo "Linux package verified at $package_output"

