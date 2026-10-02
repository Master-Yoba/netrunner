#!/usr/bin/env bash
# Claude plays the bot through the ai-mcp-game MCP server, on the same numbered deals as ai-codex-play, so the two
# outside opponents (and versions against either) compare on fixed deals. Usage:
#   scripts/ai-claude-play.sh --output <new dir> [--side corp|runner|both] [--first-game N] [--games N] [--seed S]
#     [--opponent ENTRANT] [--model claude-sonnet-5-5] [--parallel N] [--budget-usd 10]
# Each game is one `claude -p` session with only the game's MCP tools (no shell or file tools), from an empty workspace,
# capped at --budget-usd. Games are saved like ai-ladder --save-games (game-NNN/game/), with Claude's usage beside them
# (game-NNN/claude.json); a line per game and a summary go to stdout. Needs the claude CLI, logged in. It spends plan
# usage (about $0.90 a game at list price for Sonnet 5.5, 2 October 2026): measure a game first.
set -euo pipefail

play_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
source "$play_root/scripts/env.sh"
play_data="$play_root/data/vendor/system-gateway"
if [[ ! -f "$play_data/cards.json" ]]; then
  echo "Official card metadata is missing. Run ./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28" >&2
  exit 2
fi
command -v claude >/dev/null || { echo "The claude CLI is not on PATH." >&2; exit 2; }

output="" side="both" first=0 games=1 seed=1 opponent="standard" model="claude-sonnet-5-5" parallel=2 budget=10
while (($#)); do
  case "$1" in
    --output) output="$2"; shift 2 ;;
    --side) side="$2"; shift 2 ;;
    --first-game) first="$2"; shift 2 ;;
    --games) games="$2"; shift 2 ;;
    --seed) seed="$2"; shift 2 ;;
    --opponent) opponent="$2"; shift 2 ;;
    --model) model="$2"; shift 2 ;;
    --parallel) parallel="$2"; shift 2 ;;
    --budget-usd) budget="$2"; shift 2 ;;
    *) echo "Unsupported ai-claude-play argument '$1'." >&2; exit 2 ;;
  esac
done
[[ -n "$output" ]] || { echo "ai-claude-play requires --output <new directory>." >&2; exit 2; }
[[ ! -e "$output" ]] || { echo "--output must name a new directory." >&2; exit 2; }
output="$(mkdir -p "$output" && cd "$output" && pwd)"

# Its own build directory, apart from the Codex and review runs.
play_build="$play_root/.tools/claude-play-cli"
dotnet build "$play_root/src/Netrunner.Cli" -c Release --artifacts-path "$play_build" -m:1 --disable-build-servers \
  "-p:NetrunnerCardDataGeneratedFile=$play_build/generated/GeneratedCardData.g.cs" \
  -p:NetrunnerContentEnabled=true -p:NetrunnerCardDataMode=official "-p:NetrunnerCardDataInputRoot=$play_data" >&2
cli="$play_build/bin/Netrunner.Cli/release/Netrunner.Cli.dll"
dotnet_host="$(command -v dotnet)"
# The deals and the prompt come from ai-codex-play, so both outside opponents play the same games.
mapfile -t deals < <(dotnet "$cli" ai-codex-play --print-deals --side "$side" --first-game "$first" --games "$games" --opponent "$opponent")

play_one() {
  local number=$1 content=$2 seat=$3
  local dir="$output/game-$number" started code
  mkdir -p "$dir/workspace"
  python3 - "$dir" "$dotnet_host" "$cli" "$content" "$seed" "$((10#$number))" "$seat" "$opponent" "$play_root/data/vendor/rules" <<'PY'
import json, sys
directory, host, cli, content, seed, number, seat, opponent, rules = sys.argv[1:]
json.dump({"mcpServers": {"netrunner-game": {"command": host, "args": [cli, "ai-mcp-game", "--content", content, "--seed", seed,
    "--number", number, "--side", seat, "--opponent", opponent, "--rules-root", rules, "--output", directory + "/game"]}}},
    open(directory + "/mcp.json", "w"))
PY
  dotnet "$cli" ai-codex-play --print-prompt "$seat" > "$dir/prompt.txt"
  started=$(date +%s)
  set +e
  (cd "$dir/workspace" && timeout 2400 claude -p "$(cat "$dir/prompt.txt")" --model "$model" --mcp-config "$dir/mcp.json" \
    --strict-mcp-config --tools "" --allowedTools "mcp__netrunner-game" --permission-mode dontAsk --output-format json \
    --no-session-persistence --max-budget-usd "$budget" > "$dir/claude.json" 2> "$dir/stderr.log")
  code=$?
  set -e
  python3 - "$dir" "$number" "$content" "$seat" "$code" "$(( $(date +%s) - started ))" <<'PY'
import json, sys
directory, number, content, seat, code, seconds = sys.argv[1:]
def load(path):
    try: return json.load(open(path))
    except Exception: return {}
usage, result = load(directory + "/claude.json"), load(directory + "/game/result.json")
decisions = result.get("decisions", [])
by_claude = sum(1 for d in decisions if d.get("outcome") == "model")
against = sum(1 for d in decisions if d.get("outcome") == "model" and d.get("chosen") != d.get("advised"))
tokens = usage.get("usage", {})
print(f"ai-claude-play game {number} {content}: Claude {seat}, winner {result.get('winner', 'unknown')}, {len(decisions)} choices, "
      f"{by_claude} by Claude ({against} against the heuristic), {len(decisions) - by_claude} by the heuristic, "
      f"{result.get('invalidChoices', 0)} invalid, {tokens.get('cache_read_input_tokens', 0)} cache-read + "
      f"{tokens.get('cache_creation_input_tokens', 0)} cache-write + {tokens.get('input_tokens', 0)} input + "
      f"{tokens.get('output_tokens', 0)} output tokens, ${usage.get('total_cost_usd', 0):.2f}, {seconds} s, exit {code}"
      + (", error" if usage.get("is_error") else ""), flush=True)
PY
}

running=0
for deal in "${deals[@]}"; do
  read -r number content seat <<<"$deal"
  play_one "$number" "$content" "$seat" &
  running=$((running + 1))
  if ((running >= parallel)); then wait -n; running=$((running - 1)); fi
done
wait

# The summary counts only games Claude played to the end (no choice left to the heuristic).
python3 - "$output" <<'PY'
import glob, json, sys
games = wins_corp = wins_runner = corp = runner = excluded = 0
cost = 0.0
for path in sorted(glob.glob(sys.argv[1] + "/game-*/game/result.json")):
    result = json.load(open(path))
    decisions = result.get("decisions", [])
    if not decisions or any(d.get("outcome") != "model" for d in decisions) or result.get("winner") is None:
        excluded += 1
        continue
    if result.get("side") == "corp":
        runner += 1; wins_runner += result["winner"] == "the Runner"
    else:
        corp += 1; wins_corp += result["winner"] == "the Corp"
for path in glob.glob(sys.argv[1] + "/game-*/claude.json"):
    try: cost += json.load(open(path)).get("total_cost_usd", 0)
    except Exception: pass
print(f"ai-claude-play bot Corp won {wins_corp}/{corp}, bot Runner won {wins_runner}/{runner}, excluded {excluded}, cost ${cost:.2f}, output={sys.argv[1]}")
PY
