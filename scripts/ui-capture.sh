#!/usr/bin/env bash
# Scripted UI capture for design review: plays an official match through the
# real client and saves frame bursts plus contact sheets at chosen events.
#   scripts/ui-capture.sh [--capture-seed 42] [--capture-side runner|corp]
#     [--capture-preset basic|expanded] [--capture-events run-initiated,agenda-scored]
#     [--capture-bursts 4] [--capture-frames 12] [--capture-interval-ms 150]
#     [--capture-max-actions 800] [--capture-pace-ms 500] [--capture-output <new dir>]
# Needs a display. On a headless Linux box, run it under Xvfb (for example
# `xvfb-run -s "-screen 0 1920x1080x24" scripts/ui-capture.sh`); Godot's
# --headless mode renders nothing and cannot capture frames.
# Frames can contain official card art: keep them local, never commit them.
set -euo pipefail

capture_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" && "$(uname -s)" == "Linux" ]]; then
  echo "ui-capture needs a display; run it under xvfb-run or set DISPLAY." >&2
  exit 2
fi

capture_has_output=0
for capture_argument in "$@"; do
  if [[ "$capture_argument" == --capture-output ]]; then capture_has_output=1; fi
done
capture_arguments=("$@")
if ((capture_has_output == 0)); then
  capture_arguments+=(--capture-output "$capture_root/artifacts/ui-capture/$(date -u +%Y%m%dT%H%M%SZ)-$$")
fi

exec "$capture_root/scripts/play.sh" "${capture_arguments[@]}"
