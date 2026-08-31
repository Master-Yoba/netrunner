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
		GODOT4="$(find "$netrunner_godot_root" -maxdepth 6 -type f -name 'Godot' -perm -u+x -print -quit)"
	fi
	export GODOT4
fi

unset netrunner_root netrunner_dotnet netrunner_godot_root netrunner_rg netrunner_godot_candidate
