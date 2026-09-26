#!/usr/bin/env bash

netrunner_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-$0}")/.." && pwd)"
if [[ ! -f "$netrunner_root/scripts/env.sh" && -f "$PWD/scripts/env.sh" ]]; then
  netrunner_root="$PWD"
fi
netrunner_dotnet="$netrunner_root/.tools/dotnet"
netrunner_godot_root="$netrunner_root/.tools/godot"
netrunner_rg="$netrunner_root/.tools/ripgrep"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export NUGET_XMLDOC_MODE=skip
export NETRUNNER_GODOT_DATA="$netrunner_root/.tools/godot-data"

if [[ -d "$netrunner_dotnet" ]]; then
  export DOTNET_ROOT="$netrunner_dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi

if [[ -x "$netrunner_rg/rg" ]]; then
  export PATH="$netrunner_rg:$PATH"
fi

if [[ ( -z "${GODOT4:-}" || ! -x "$GODOT4" ) && -d "$netrunner_godot_root" ]]; then
	GODOT4=""
	# A macOS download is an app bundle, so its executable is several levels
	# below .tools/godot. Keep the direct candidates readable, then fall back
	# to a bounded search for Linux-style and future bundle layouts.
	for netrunner_godot_candidate in \
		"$netrunner_godot_root/Godot" \
		"$netrunner_godot_root/Godot.app/Contents/MacOS/Godot"; do
		if [[ -x "$netrunner_godot_candidate" ]]; then
			GODOT4="$netrunner_godot_candidate"
			break
		fi
	done
	if [[ -z "${GODOT4:-}" ]]; then
		GODOT4="$(find "$netrunner_godot_root" -maxdepth 6 -type f \( -name 'Godot' -o -name 'Godot_v*_mono_linux.*' \) -perm -u+x -print -quit)"
	fi
	export GODOT4
fi

# Wall-time limits for smokes and gates are tuned to the reference hardware.
# NETRUNNER_TIMEOUT_SCALE (1 to 20, default 1) stretches them on slower
# machines; the Godot client applies the same rule to its own limits.
netrunner_scaled_seconds() {
	awk -v scale="${NETRUNNER_TIMEOUT_SCALE:-1}" -v seconds="$1" \
		'BEGIN { if (scale + 0 < 1 || scale + 0 > 20) scale = 1; value = seconds * scale; printf "%d", (value == int(value)) ? value : int(value) + 1 }'
}

unset netrunner_root netrunner_dotnet netrunner_godot_root netrunner_rg netrunner_godot_candidate
