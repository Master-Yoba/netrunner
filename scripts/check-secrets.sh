#!/usr/bin/env bash
set -euo pipefail

secrets_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$secrets_root"

secrets_pattern='(AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{20,}|-----BEGIN ([A-Z ]+ )?PRIVATE KEY-----|NETRUNNER_LLM_API_KEY[[:space:]]*=[[:space:]]*[^[:space:]#]+)'

if rg -n -I --hidden "$secrets_pattern" . \
  -g '!.git/**' \
  -g '!.tools/**' \
  -g '!artifacts/**' \
  -g '!.env.example' \
  -g '!scripts/check-secrets.sh'; then
  echo "Potential repository secret detected."
  exit 1
fi

echo "No high-confidence repository secret patterns detected."
