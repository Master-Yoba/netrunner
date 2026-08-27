#!/usr/bin/env bash
set -euo pipefail

bootstrap_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
bootstrap_tools="$bootstrap_root/.tools"
bootstrap_dotnet="$bootstrap_tools/dotnet"
bootstrap_godot="$bootstrap_tools/godot"
bootstrap_godot_version="4.7.2-stable"
bootstrap_godot_zip="Godot_v4.7.2-stable_mono_linux_x86_64.zip"
bootstrap_godot_url="https://github.com/godotengine/godot-builds/releases/download/${bootstrap_godot_version}/${bootstrap_godot_zip}"

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
  echo "This bootstrap currently supports Linux x86_64. See README.md for manual setup."
  exit 1
fi

mkdir -p "$bootstrap_tools"
bootstrap_tmp="$(mktemp -d -t netrunner-bootstrap.XXXXXXXX)"
trap 'rm -rf -- "$bootstrap_tmp"' EXIT

if [[ ! -x "$bootstrap_dotnet/dotnet" ]]; then
  echo "Installing the current .NET 10 LTS SDK into $bootstrap_dotnet"
  curl --fail --location --silent --show-error \
    https://dot.net/v1/dotnet-install.sh \
    --output "$bootstrap_tmp/dotnet-install.sh"
  bash "$bootstrap_tmp/dotnet-install.sh" \
    --channel 10.0 \
    --quality GA \
    --install-dir "$bootstrap_dotnet"
fi

if ! compgen -G "$bootstrap_dotnet/shared/Microsoft.NETCore.App/8.*" >/dev/null; then
  echo "Installing the .NET 8 runtime required by net8.0 test executables"
  if [[ ! -f "$bootstrap_tmp/dotnet-install.sh" ]]; then
    curl --fail --location --silent --show-error \
      https://dot.net/v1/dotnet-install.sh \
      --output "$bootstrap_tmp/dotnet-install.sh"
  fi
  bash "$bootstrap_tmp/dotnet-install.sh" \
    --channel 8.0 \
    --runtime dotnet \
    --install-dir "$bootstrap_dotnet"
fi

if [[ ! -d "$bootstrap_godot" ]]; then
  echo "Installing Godot ${bootstrap_godot_version} .NET into $bootstrap_godot"
  mkdir -p "$bootstrap_godot"
  curl --fail --location --silent --show-error \
    "$bootstrap_godot_url" \
    --output "$bootstrap_tmp/$bootstrap_godot_zip"
  unzip -q "$bootstrap_tmp/$bootstrap_godot_zip" -d "$bootstrap_godot"
  find "$bootstrap_godot" -maxdepth 2 -type f -name 'Godot*' -exec chmod +x {} \;
fi

source "$bootstrap_root/scripts/env.sh"

echo ".NET SDK: $(dotnet --version)"
echo "Godot: $($GODOT4 --version)"

if [[ ! -f "$bootstrap_root/Netrunner.sln" ]]; then
  dotnet new sln --name Netrunner --format sln --output "$bootstrap_root"
  dotnet sln "$bootstrap_root/Netrunner.sln" add \
    "$bootstrap_root/src/Netrunner.Core/Netrunner.Core.csproj" \
    "$bootstrap_root/src/Netrunner.Cards/Netrunner.Cards.csproj" \
    "$bootstrap_root/src/Netrunner.AI/Netrunner.AI.csproj" \
    "$bootstrap_root/src/Netrunner.LLM/Netrunner.LLM.csproj" \
    "$bootstrap_root/src/Netrunner.Cli/Netrunner.Cli.csproj" \
    "$bootstrap_root/src/Netrunner.Godot/Netrunner.Godot.csproj" \
    "$bootstrap_root/tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj" \
    "$bootstrap_root/tests/Netrunner.Cards.Tests/Netrunner.Cards.Tests.csproj" \
    "$bootstrap_root/tests/Netrunner.AI.Tests/Netrunner.AI.Tests.csproj"
fi

dotnet restore "$bootstrap_root/Netrunner.sln"

echo "Bootstrap complete. Run: source scripts/env.sh"
