#!/usr/bin/env bash
set -euo pipefail

verify_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$verify_root/scripts/env.sh"
bash "$verify_root/scripts/test-env.sh"

m4_gate=0
if [[ "${1:-}" == "--m4" && $# == 1 ]]; then
  m4_gate=1
elif (($# != 0)); then
  echo "Usage: ./scripts/verify.sh [--m4]" >&2
  exit 1
fi
if ((m4_gate == 1)); then
  for required_content in cards.json manifest.json SOURCE_COPYRIGHT.md; do
    if [[ ! -f "$verify_root/data/vendor/system-gateway/$required_content" ]]; then
      echo "M4 gate requires authenticated official content; missing $required_content." >&2
      echo "Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
      exit 1
    fi
  done
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is unavailable. Run ./scripts/bootstrap.sh first."
  exit 1
fi

if [[ -z "${GODOT4:-}" || ! -x "$GODOT4" ]]; then
  echo "Godot .NET is unavailable. Run ./scripts/bootstrap.sh first."
  exit 1
fi

# macOS does not ship GNU timeout. Keep the content-lane gallery bounded on
# both macOS and Linux without making a package-manager utility a prerequisite.
run_with_timeout() {
  local limit_seconds="$1"
  shift
  "$@" &
  local command_pid=$!
  local timer_pid
  (
    sleep "$limit_seconds"
    if kill -0 "$command_pid" >/dev/null 2>&1; then
      echo "Command exceeded ${limit_seconds}s; terminating $*" >&2
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

MSBUILDDISABLENODEREUSE=1 dotnet restore "$verify_root/Netrunner.sln" -m:1
MSBUILDDISABLENODEREUSE=1 dotnet build "$verify_root/Netrunner.sln" --no-restore -m:1
MSBUILDDISABLENODEREUSE=1 dotnet test "$verify_root/Netrunner.sln" --no-build -m:1
dotnet format "$verify_root/Netrunner.sln" --no-restore --verify-no-changes
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- self-test --seed 42
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- doctor --json
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- full-match --seed 42 --json
card_data_dir="$verify_root/data/vendor/system-gateway"
card_data_cards="$card_data_dir/cards.json"
card_data_manifest="$card_data_dir/manifest.json"
card_data_copyright="$card_data_dir/SOURCE_COPYRIGHT.md"
card_data_present=0
card_data_content_enabled=0
official_content_properties=(
  "-p:NetrunnerContentEnabled=true"
  "-p:NetrunnerCardDataMode=official"
  "-p:NetrunnerCardDataInputRoot=$card_data_dir"
)
for card_data_file in "$card_data_cards" "$card_data_manifest" "$card_data_copyright"; do
  if [[ -e "$card_data_file" ]]; then
    card_data_present=$((card_data_present + 1))
  fi
done
if ((card_data_present == 0)); then
  echo "card-data=synthetic (local System Gateway snapshot absent; no network fetch)"
  echo "Provision official content explicitly with:"
  echo "  ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28"
elif ((card_data_present == 3)) && [[ -f "$card_data_cards" && -f "$card_data_manifest" && -f "$card_data_copyright" ]]; then
  dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
    card-data validate --root "$verify_root" --json
  dotnet build "$verify_root/src/Netrunner.Cards/Netrunner.Cards.csproj" \
    --no-restore --nologo -m:1 \
    "${official_content_properties[@]}"
  dotnet build "$verify_root/src/Netrunner.Match/Netrunner.Match.csproj" \
    --no-restore --nologo -m:1 \
    "${official_content_properties[@]}"
  dotnet build "$verify_root/src/Netrunner.Cli/Netrunner.Cli.csproj" \
    --no-restore --nologo -m:1 \
    "${official_content_properties[@]}"
  dotnet build "$verify_root/src/Netrunner.Godot/Netrunner.Godot.csproj" \
    --no-restore --nologo -m:1 \
    "${official_content_properties[@]}"
  # The initial solution test run uses synthetic embedded data. Execute the
  # CLI's official-content tests against an explicitly provisioned test output.
  dotnet test "$verify_root/tests/Netrunner.Cli.Tests/Netrunner.Cli.Tests.csproj" \
    --no-restore --nologo -m:1 \
    --filter FullyQualifiedName~FullMatchCommand \
    "${official_content_properties[@]}"
  dotnet test "$verify_root/tests/Netrunner.Cards.Tests/Netrunner.Cards.Tests.csproj" \
    --no-restore --nologo -m:1 \
    --filter FullyQualifiedName~OfficialPresetRevalidationTests \
    "${official_content_properties[@]}"
  card_data_content_enabled=1
else
  echo "card-data validation failed: local System Gateway snapshot is partial." >&2
  exit 1
fi
verify_temp_root="${TMPDIR:-/tmp}"
verify_temp_root="${verify_temp_root%/}"
coverage_report_dir="$(mktemp -d "$verify_temp_root/netrunner-gateway-coverage.XXXXXX")"
if [[ -z "$coverage_report_dir" || ! -d "$coverage_report_dir" ]]; then
  echo "Unable to create the Gateway coverage report directory." >&2
  exit 1
fi
m1_replay_dir=""
trap 'rm -rf -- "$coverage_report_dir"; if [[ -n "$m1_replay_dir" ]]; then rm -rf -- "$m1_replay_dir"; fi' EXIT
missing_card_data_root="$coverage_report_dir/missing-source"
missing_card_data_log="$coverage_report_dir/missing-input.log"
missing_card_data_expected="DATA-400: normalized card data is missing at '$missing_card_data_root/cards.json'. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28."
if dotnet build "$verify_root/src/Netrunner.Cards/Netrunner.Cards.csproj" \
  --no-restore --nologo -m:1 \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataInputRoot="$missing_card_data_root" \
  >"$missing_card_data_log" 2>&1; then
  echo "DATA-400 missing-input contract unexpectedly succeeded." >&2
  exit 1
fi
if ! grep -Fq "$missing_card_data_expected" "$missing_card_data_log"; then
  echo "DATA-400 missing-input diagnostic changed unexpectedly:" >&2
  sed -n '1,80p' "$missing_card_data_log" >&2
  exit 1
fi
echo "card-data=missing-input diagnostic=PASS"
official_without_opt_in_log="$coverage_report_dir/official-without-opt-in.log"
official_without_opt_in_expected="DATA-400: official card content is opt-in; set NetrunnerContentEnabled=true"
if dotnet build "$verify_root/src/Netrunner.Cards/Netrunner.Cards.csproj" \
  --no-restore --nologo -m:1 \
  -p:NetrunnerCardDataMode=official \
  >"$official_without_opt_in_log" 2>&1; then
  echo "DATA-400 official-without-opt-in contract unexpectedly succeeded." >&2
  exit 1
fi
if ! grep -Fq "$official_without_opt_in_expected" "$official_without_opt_in_log"; then
  echo "DATA-400 official-without-opt-in diagnostic changed unexpectedly:" >&2
  sed -n '1,80p' "$official_without_opt_in_log" >&2
  exit 1
fi
echo "card-data=official-without-opt-in diagnostic=PASS"
coverage_catalog="$verify_root/src/Netrunner.Cards/bin/Debug/net8.0/Netrunner.Cards.dll"
coverage_tests="$verify_root/tests/Netrunner.Cards.Tests/bin/Debug/net8.0/Netrunner.Cards.Tests.dll"
coverage_cards_arg=()
if [[ -f "$card_data_cards" ]]; then
  coverage_cards_arg=(--cards "$card_data_cards")
fi
dotnet run --project "$verify_root/tools/Netrunner.CardCoverage" --no-build --no-restore -- \
  --root "$verify_root" \
  --catalog "$coverage_catalog" \
  --tests "$coverage_tests" \
  "${coverage_cards_arg[@]}" \
  --markdown "$coverage_report_dir/gateway-coverage.md" \
  --json "$coverage_report_dir/gateway-coverage.json"
if ! diff -u "$verify_root/docs/generated/gateway-coverage.md" \
  "$coverage_report_dir/gateway-coverage.md"; then
  echo "Gateway Markdown coverage report is stale. Regenerate it with Netrunner.CardCoverage." >&2
  exit 1
fi
if ! diff -u "$verify_root/docs/generated/gateway-coverage.json" \
  "$coverage_report_dir/gateway-coverage.json"; then
  echo "Gateway JSON coverage report is stale. Regenerate it with Netrunner.CardCoverage." >&2
  exit 1
fi
python3 - "$coverage_report_dir/gateway-coverage.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as report_file:
    report = json.load(report_file)
expected = {"total": 77, "registered": 77, "tested": 77,
            "fullTextComplete": 77, "partial": 0, "unregistered": 0}
if report["counts"] != expected or report["diagnostics"]:
    raise SystemExit("M4 coverage requires 77 complete, reviewed, tested designs with no diagnostics.")
print("m4-coverage=PASS registered=77 tested=77 complete=77 diagnostics=0")
PY
m1_replay_dir="$(mktemp -d "$verify_temp_root/netrunner-m1.XXXXXX")"
if [[ -z "$m1_replay_dir" || ! -d "$m1_replay_dir" ]]; then
  echo "Unable to create the M1 replay verification directory." >&2
  exit 1
fi
for m1_goal in corp-score runner-steal deck-out; do
  dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
    play --seed 42 --goal "$m1_goal" --json
done
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
  play --seed 42 --goal corp-score --json --replay-out "$m1_replay_dir/corp-score.json"
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
  replay "$m1_replay_dir/corp-score.json" --json
dotnet run --project "$verify_root/src/Netrunner.Cli" --no-build --no-restore -- \
  m2-proof "$verify_root/data/m2/representative-run.json" --json
if ((card_data_content_enabled == 1)); then
  # Godot may trigger a managed rebuild during import/startup. Keep its child
  # builds on the same content lane as the explicitly built assemblies.
  export NetrunnerContentEnabled=true
  export NetrunnerCardDataMode=official
  export NetrunnerCardDataInputRoot="$card_data_dir"
  NetrunnerContentEnabled=true NetrunnerCardDataMode=official \
    NetrunnerCardDataInputRoot="$card_data_dir" \
    XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
    "$GODOT4" --headless --editor --path "$verify_root/src/Netrunner.Godot" --quit-after 60
else
  XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
    "$GODOT4" --headless --editor --path "$verify_root/src/Netrunner.Godot" --quit-after 60
fi
if ((card_data_content_enabled == 1)); then
  gallery_smoke_log="$coverage_report_dir/gallery-smoke.log"
  gallery_expected="M4_CARD_GALLERY PASS cards=77 mode=official"
  if ! run_with_timeout 30 env \
    NetrunnerContentEnabled=true \
    NetrunnerCardDataMode=official \
    NetrunnerCardDataInputRoot="$card_data_dir" \
    XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
    "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
    -- --m4-card-gallery-smoke >"$gallery_smoke_log" 2>&1; then
    echo "M4 card gallery smoke failed or timed out; see $gallery_smoke_log." >&2
    sed -n '1,120p' "$gallery_smoke_log" >&2
    exit 1
  fi
  if ! grep -Fq "$gallery_expected" "$gallery_smoke_log" \
    || grep -Fq "M4_CARD_GALLERY FAIL" "$gallery_smoke_log"; then
    echo "M4 card gallery smoke did not prove the official 77-card payload; see $gallery_smoke_log." >&2
    sed -n '1,120p' "$gallery_smoke_log" >&2
    exit 1
  fi
  echo "m4-card-gallery-smoke=PASS cards=77 mode=official"
fi
slice43_ui_smoke_log="$coverage_report_dir/slice43-ui-smoke.log"
slice43_ui_expected="M4_SLICE43_UI PASS card_in_scene_tree=true ready_cue=true ready_chips=true used_cue=true used_chips=true ability_affordance=true rfg_removed=true"
if ! run_with_timeout 30 env \
  XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-slice43-ui-smoke >"$slice43_ui_smoke_log" 2>&1; then
  echo "M4 Slice 4.3 UI smoke failed or timed out; see $slice43_ui_smoke_log." >&2
  sed -n '1,120p' "$slice43_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "$slice43_ui_expected" "$slice43_ui_smoke_log" \
  || grep -Fq "M4_SLICE43_UI FAIL" "$slice43_ui_smoke_log"; then
  echo "M4 Slice 4.3 UI smoke did not prove the client affordance path; see $slice43_ui_smoke_log." >&2
  sed -n '1,120p' "$slice43_ui_smoke_log" >&2
  exit 1
fi
echo "m4-slice43-ui-smoke=PASS card_in_scene_tree=true ready_cue=true ready_chips=true used_cue=true used_chips=true ability_affordance=true rfg_removed=true"
slice44_ui_smoke_log="$coverage_report_dir/slice44-ui-smoke.log"
slice44_ui_expected="M4_SLICE44_UI PASS strength_reason=true modified_cost=true used_first_x_chip=true breach_note=true"
if ! run_with_timeout 30 env \
  XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-slice44-ui-smoke >"$slice44_ui_smoke_log" 2>&1; then
  echo "M4 Slice 4.4 UI smoke failed or timed out; see $slice44_ui_smoke_log." >&2
  sed -n '1,120p' "$slice44_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "$slice44_ui_expected" "$slice44_ui_smoke_log" \
  || grep -Fq "M4_SLICE44_UI FAIL" "$slice44_ui_smoke_log"; then
  echo "M4 Slice 4.4 UI smoke did not prove the typed presentation path; see $slice44_ui_smoke_log." >&2
  sed -n '1,120p' "$slice44_ui_smoke_log" >&2
  exit 1
fi
echo "m4-slice44-ui-smoke=PASS strength_reason=true modified_cost=true used_first_x_chip=true breach_note=true"
slice45_ui_smoke_log="$coverage_report_dir/slice45-ui-smoke.log"
slice45_ui_expected="M4_SLICE45_UI PASS prominent_options=true acting_player=true issued_order=true typed_cost=true typed_fallback=true hidden_safe_installed_card=true active_restriction=true legal_bioroid=true armed_mayfly=true"
if ! run_with_timeout 30 env \
  XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-slice45-ui-smoke >"$slice45_ui_smoke_log" 2>&1; then
  echo "M4 Slice 4.5 UI smoke failed or timed out; see $slice45_ui_smoke_log." >&2
  sed -n '1,160p' "$slice45_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "$slice45_ui_expected" "$slice45_ui_smoke_log" \
  || grep -Fq "M4_SLICE45_UI FAIL" "$slice45_ui_smoke_log"; then
  echo "M4 Slice 4.5 UI smoke did not prove the typed decision/cue path; see $slice45_ui_smoke_log." >&2
  sed -n '1,160p' "$slice45_ui_smoke_log" >&2
  exit 1
fi
echo "m4-slice45-ui-smoke=PASS prominent_options=true acting_player=true issued_order=true typed_cost=true typed_fallback=true hidden_safe_installed_card=true active_restriction=true legal_bioroid=true armed_mayfly=true"
slice46_ui_smoke_log="$coverage_report_dir/slice46-ui-smoke.log"
if ! run_with_timeout 30 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-slice46-ui-smoke >"$slice46_ui_smoke_log" 2>&1; then
  sed -n '1,160p' "$slice46_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "M4_SLICE46_UI PASS damage=true tag_chip=true tag_feed=true unless=true conditional_value=true no_early_jack_out=true jack_out_after_damage=true" "$slice46_ui_smoke_log" \
  || grep -Fq "M4_SLICE46_UI FAIL" "$slice46_ui_smoke_log"; then
  sed -n '1,160p' "$slice46_ui_smoke_log" >&2
  exit 1
fi
echo "m4-slice46-ui-smoke=PASS"
slice47_ui_smoke_log="$coverage_report_dir/slice47-ui-smoke.log"
if ! run_with_timeout 30 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-slice47-ui-smoke >"$slice47_ui_smoke_log" 2>&1; then
  sed -n '1,160p' "$slice47_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "M4_SLICE47_UI PASS setup_choices=true identities=true nbn_choices=true access_count=true swap_preview=true hosted_trojan=true derez=true outcome_new_match=true" "$slice47_ui_smoke_log" \
  || grep -Fq "M4_SLICE47_UI FAIL" "$slice47_ui_smoke_log"; then
  sed -n '1,160p' "$slice47_ui_smoke_log" >&2
  exit 1
fi
echo "m4-slice47-ui-smoke=PASS"
slice48_ux_smoke_log="$coverage_report_dir/slice48-ux-smoke.log"
slice48_content_args=()
if ((card_data_content_enabled == 1)); then
  slice48_content_args=(--require-official-content)
fi
if ! run_with_timeout 90 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-slice48-ux-smoke "${slice48_content_args[@]}" >"$slice48_ux_smoke_log" 2>&1; then
  cat "$slice48_ux_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "M4_SLICE48_UX PASS" "$slice48_ux_smoke_log" \
  || grep -Fq "M4_SLICE48_UX FAIL" "$slice48_ux_smoke_log"; then
  cat "$slice48_ux_smoke_log" >&2
  exit 1
fi
cat "$slice48_ux_smoke_log"
m49_ui_smoke_log="$coverage_report_dir/m49-ui-smoke.log"
m49_ui_content_args=("${slice48_content_args[@]}")
m49_ui_content_env=()
card_back_dir="$verify_root/.tools/card-backs"
if ((card_data_content_enabled == 1)); then
  m49_ui_content_env=(
    NetrunnerContentEnabled=true
    NetrunnerCardDataMode=official
    NetrunnerCardDataInputRoot="$card_data_dir"
  )
  # Prefer the provisioned local copy; CI falls back to the committed
  # res://card-backs defaults.
  if [[ -d "$card_back_dir" ]]; then
    m49_ui_content_env+=(NETRUNNER_CARD_BACK_DIRECTORY="$card_back_dir")
  fi
fi
if ! run_with_timeout 120 env "${m49_ui_content_env[@]}" XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-9-ui-smoke "${m49_ui_content_args[@]}" >"$m49_ui_smoke_log" 2>&1; then
  echo "M4.9 UI smoke failed or timed out; see $m49_ui_smoke_log." >&2
  cat "$m49_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Eq "M49_UI PASS .*dense=true history=true" "$m49_ui_smoke_log" \
  || grep -Eq "M49_UI FAIL|SCRIPT ERROR|ERROR:" "$m49_ui_smoke_log"; then
  echo "M4.9 UI smoke did not prove the dense table/history path; see $m49_ui_smoke_log." >&2
  cat "$m49_ui_smoke_log" >&2
  exit 1
fi
cat "$m49_ui_smoke_log"
echo "m4-9-ui-smoke=PASS"
m410_ui_smoke_log="$coverage_report_dir/m410-editor-ui-smoke.log"
if ! run_with_timeout 60 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  res://scenes/UiEditorSmoke.tscn >"$m410_ui_smoke_log" 2>&1; then
  echo "M4.10 editor UI smoke failed or timed out; see $m410_ui_smoke_log." >&2
  cat "$m410_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "M410_EDITOR_UI PASS" "$m410_ui_smoke_log" \
  || grep -Eq "M410_EDITOR_UI FAIL|SCRIPT ERROR|ERROR:" "$m410_ui_smoke_log"; then
  echo "M4.10 editor UI smoke did not prove appearance binding and preview isolation." >&2
  cat "$m410_ui_smoke_log" >&2
  exit 1
fi
cat "$m410_ui_smoke_log"
echo "m4-10-editor-ui-smoke=PASS"
m411_ui_smoke_log="$coverage_report_dir/m411-interaction-smoke.log"
if ! run_with_timeout 120 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m4-11-ui-smoke >"$m411_ui_smoke_log" 2>&1; then
  echo "M4.11 interaction smoke failed; see $m411_ui_smoke_log." >&2
  cat "$m411_ui_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "M411_INTERACTION PASS" "$m411_ui_smoke_log" \
  || ! grep -Fq "M412_BOARD_ACTIONS PASS" "$m411_ui_smoke_log" \
  || ! grep -Fq "M412_DRAW_MOTION PASS" "$m411_ui_smoke_log" \
  || grep -Eq "M411_INTERACTION FAIL|SCRIPT ERROR|ERROR:" "$m411_ui_smoke_log"; then
  cat "$m411_ui_smoke_log" >&2
  exit 1
fi
cat "$m411_ui_smoke_log"
echo "m4-11-interaction-smoke=PASS"
echo "m4-12-board-actions-smoke=PASS"
latency_smoke_log="$coverage_report_dir/latency-smoke.log"
if ! run_with_timeout 45 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
  -- --m3-responsiveness-smoke >"$latency_smoke_log" 2>&1; then
  sed -n '1,160p' "$latency_smoke_log" >&2
  exit 1
fi
if ! grep -Fq "M3_LATENCY_SMOKE PASS" "$latency_smoke_log" \
  || grep -Fq "M3_LATENCY_SMOKE FAIL" "$latency_smoke_log"; then
  sed -n '1,160p' "$latency_smoke_log" >&2
  exit 1
fi
echo "m3-responsiveness-smoke=PASS"
for full_match_side in corp runner; do
  full_match_log="$coverage_report_dir/full-match-$full_match_side.log"
  if ! run_with_timeout 45 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
    "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
    -- "--full-match-smoke-$full_match_side" >"$full_match_log" 2>&1; then
    sed -n '1,160p' "$full_match_log" >&2
    exit 1
  fi
  if ! grep -Fq "FULL_MATCH_SMOKE PASS" "$full_match_log" \
    || grep -Fq "FULL_MATCH_SMOKE FAIL" "$full_match_log"; then
    sed -n '1,160p' "$full_match_log" >&2
    exit 1
  fi
  echo "full-match-$full_match_side=PASS setup=true mulligan=true turn_pair=true identities=true terminal=true"
done
if ((card_data_content_enabled == 1)); then
  for m4_preset in basic expanded; do
    for m4_human_side in corp runner; do
      m4_full_smoke_log="$coverage_report_dir/m4-full-$m4_preset-$m4_human_side.log"
      if ! run_with_timeout 210 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
        "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" \
        -- "--m4-full-smoke-$m4_human_side" --preset "$m4_preset" --seed 47042 \
        >"$m4_full_smoke_log" 2>&1; then
        cat "$m4_full_smoke_log" >&2
        exit 1
      fi
      if ! grep -Fq "M4_FULL_SMOKE PASS" "$m4_full_smoke_log" \
        || ! grep -Fq "preset=$m4_preset seed=47042" "$m4_full_smoke_log" \
        || ! grep -Fq "reload=true fresh_setup=true" "$m4_full_smoke_log" \
        || grep -Fq "M4_FULL_SMOKE FAIL" "$m4_full_smoke_log"; then
        cat "$m4_full_smoke_log" >&2
        exit 1
      fi
      cat "$m4_full_smoke_log"
    done
  done
fi
run_with_timeout 30 env XDG_DATA_HOME="$NETRUNNER_GODOT_DATA" \
  "$GODOT4" --headless --path "$verify_root/src/Netrunner.Godot" -- --smoke-test
"$verify_root/scripts/check-secrets.sh"

if ((m4_gate == 1)); then
  echo "M4_GATE PASS content=official coverage=77/77"
elif ((card_data_content_enabled == 0)); then
  echo "M4_GATE NOT_RUN official content absent; synthetic regression only."
fi
echo "Development environment verified."
