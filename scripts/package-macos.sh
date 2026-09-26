#!/usr/bin/env bash
set -euo pipefail

package_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$package_root/scripts/env.sh"

# Prefer the repository-local .NET editor binary. env.sh deliberately keeps a
# caller-provided GODOT4 value, which is useful for development but can be a
# stale Linux path when this script is run from a long-lived shell.
local_godot="$package_root/.tools/godot/Godot.app/Contents/MacOS/Godot"
if [[ -x "$local_godot" ]]; then
  godot4="$local_godot"
else
  godot4="${GODOT4:-}"
fi

if [[ -z "$godot4" || ! -x "$godot4" ]]; then
  echo "Godot 4.7.2 .NET editor binary is unavailable; set GODOT4 or install it under .tools/godot/Godot.app." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo ".NET SDK is unavailable; run scripts/bootstrap.sh or install the SDK." >&2
  exit 1
fi

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "package-macos.sh must run on macOS; found $(uname -s)." >&2
  exit 1
fi

package_output="$package_root/artifacts/macos/NetrunnerSolo.app"
export_log="$package_root/artifacts/macos/export.log"
fast_smoke_log="$package_root/artifacts/macos/smoke-fast.log"
latency_smoke_log="$package_root/artifacts/macos/smoke-latency.log"
mkdir -p "$(dirname -- "$package_output")" "$NETRUNNER_GODOT_DATA"

# Build the managed project first so a C# compiler failure stops packaging
# before an old app bundle can be mistaken for a fresh export. Godot repeats
# the managed build as part of --export-release and is the authoritative
# pack/native-bundle step below.
echo "Building the managed macOS client"
MSBUILDDISABLENODEREUSE=1 dotnet build \
  "$package_root/src/Netrunner.Godot/Netrunner.Godot.csproj" \
  --configuration Release --no-restore -m:1

if [[ -e "$package_output" ]]; then
  # This is a generated, gitignored artifact. Removing the exact bundle
  # prevents an export failure from falling through to an older app.
  rm -rf -- "$package_output"
fi

echo "Exporting macOS app to $package_output"
if ! XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$godot4" --headless --path "$package_root/src/Netrunner.Godot" \
  --export-release "macOS" "$package_output" 2>&1 | tee "$export_log"; then
  echo "Godot macOS export failed; see $export_log." >&2
  exit 1
fi

# Godot's .NET export hook can report a managed publish failure while the
# native exporter still returns success. Treat that diagnostic as fatal even
# when a bundle directory happened to be created.
if grep -Eq 'ERROR: Export \.NET Project|ERROR: Project export .*failed|ERROR: .*([Mm][Ss][Bb]uild|dotnet)|Export failed' "$export_log"; then
  echo "Godot reported a .NET/export failure; see $export_log." >&2
  exit 1
fi

if [[ ! -d "$package_output" || ! -f "$package_output/Contents/Info.plist" ]]; then
  echo "Godot did not produce a macOS app bundle at $package_output." >&2
  exit 1
fi

# Godot derives the executable name from application/name, so it may contain
# spaces. Read the bundle metadata instead of duplicating that name here.
bundle_executable="$(/usr/libexec/PlistBuddy \
  -c 'Print :CFBundleExecutable' "$package_output/Contents/Info.plist" \
  2>/dev/null || true)"
if [[ -z "$bundle_executable" ]]; then
  bundle_executable="$(find "$package_output/Contents/MacOS" -maxdepth 1 \
    -type f -perm -u+x -print -quit 2>/dev/null || true)"
  bundle_executable="${bundle_executable##*/}"
fi
package_executable="$package_output/Contents/MacOS/$bundle_executable"
if [[ -z "$bundle_executable" || ! -x "$package_executable" ]]; then
  echo "macOS app bundle has no executable in Contents/MacOS: $package_output." >&2
  exit 1
fi

# macOS does not ship GNU timeout. Keep the smoke bounded without requiring a
# package-manager dependency; the timer is cancelled when the app exits.
run_with_timeout() {
  local limit_seconds
  limit_seconds="$(netrunner_scaled_seconds "$1")"
  shift
  "$@" &
  local command_pid=$!
  local timer_pid
  (
    sleep "$limit_seconds"
    if kill -0 "$command_pid" >/dev/null 2>&1; then
      echo "Packaged smoke exceeded ${limit_seconds}s; terminating $*" >&2
      kill -TERM "$command_pid" >/dev/null 2>&1 || true
      sleep 2
      kill -KILL "$command_pid" >/dev/null 2>&1 || true
    fi
  ) &
  timer_pid=$!

  local command_status=0
  wait "$command_pid" || command_status=$?
  kill "$timer_pid" >/dev/null 2>&1 || true
  wait "$timer_pid" >/dev/null 2>&1 || true
  return "$command_status"
}

verify_smoke_log() {
  local log_file="$1"
  local marker="$2"
  if ! grep -Eq "${marker} PASS" "$log_file"; then
    echo "Packaged smoke did not report '${marker} PASS'; see $log_file." >&2
    return 1
  fi

  # Keep diagnostics actionable without treating benign Godot startup notices
  # (for example, certificate probing on an offline host) as gameplay failure.
  if grep -Eq 'M3_(SMOKE|LATENCY_SMOKE) FAIL|ERROR: .*([Uu]nhandled|[Ee]xception|[Mm][Ss][Bb]uild|\.NET|[Ee]xport)' "$log_file"; then
    echo "Packaged smoke log contains a fatal diagnostic; see $log_file." >&2
    return 1
  fi
}

echo "Running packaged full-slice M3 smoke (30s bound)"
if ! XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  run_with_timeout 30 "$package_executable" --headless -- --smoke-test \
  2>&1 | tee "$fast_smoke_log"; then
  echo "Packaged full-slice macOS smoke failed; see $fast_smoke_log." >&2
  exit 1
fi
if ! verify_smoke_log "$fast_smoke_log" "M3_SMOKE"; then
  exit 1
fi

echo "Running packaged ten-second responsiveness smoke (30s bound)"
if ! XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  run_with_timeout 30 "$package_executable" --headless -- --m3-responsiveness-smoke \
  2>&1 | tee "$latency_smoke_log"; then
  echo "Packaged ten-second macOS smoke failed; see $latency_smoke_log." >&2
  exit 1
fi
if ! verify_smoke_log "$latency_smoke_log" "M3_LATENCY_SMOKE"; then
  exit 1
fi

echo "macOS package verified at $package_output"
echo "Smoke logs: $fast_smoke_log and $latency_smoke_log"
