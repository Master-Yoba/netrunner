#!/usr/bin/env bash

netrunner_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
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

if [[ -z "${GODOT4:-}" && -d "$netrunner_godot_root" ]]; then
  GODOT4="$(find "$netrunner_godot_root" -maxdepth 2 -type f -name 'Godot*' -perm -u+x -print -quit)"
  export GODOT4
fi

unset netrunner_root netrunner_dotnet netrunner_godot_root netrunner_rg
