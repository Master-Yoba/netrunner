#!/usr/bin/env bash
set -euo pipefail

templates_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$templates_root/scripts/env.sh"

templates_version="4.7.2.stable.mono"
templates_destination="$NETRUNNER_GODOT_DATA/godot/export_templates/$templates_version"
templates_url="https://downloads.godotengine.org/?flavor=stable&platform=templates&slug=mono_export_templates.tpz&version=4.7.2"

if [[ -x "$templates_destination/linux_release.x86_64" ]]; then
  echo "Godot .NET export templates are already installed at $templates_destination"
  exit 0
fi

templates_tmp="$(mktemp -d -t netrunner-templates.XXXXXXXX)"
trap 'rm -rf -- "$templates_tmp"' EXIT

echo "Downloading Godot 4.7.2 .NET export templates"
curl --fail --location --silent --show-error "$templates_url" --output "$templates_tmp/templates.tpz"
unzip -q "$templates_tmp/templates.tpz" -d "$templates_tmp/unpacked"

if [[ ! -f "$templates_tmp/unpacked/templates/linux_release.x86_64" ]]; then
  echo "The downloaded archive does not contain the expected Linux x86_64 template."
  exit 1
fi

mkdir -p "$(dirname -- "$templates_destination")"
mv "$templates_tmp/unpacked/templates" "$templates_destination"
echo "Installed export templates at $templates_destination"

