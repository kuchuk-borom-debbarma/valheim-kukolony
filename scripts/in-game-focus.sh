#!/bin/sh
# One slice of the acceptance run, in game.
#
# The full run builds a settlement, hauls, tidies, rests, travels, screenshots every
# screen, then saves and relaunches to prove persistence. That is right for proving a
# release and wrong for iterating on one feature: most of an hour, of which the part
# under test is two minutes. This runs one slice against a bare colony, one launch, no
# reload, no screenshots.
#
#   scripts/in-game-focus.sh chop
#   scripts/in-game-focus.sh travel
#   scripts/in-game-focus.sh tend
#
# The checks themselves are shared with the full run rather than copied, so a focused
# pass means the same thing the full one would mean about those checks.
set -eu

# A failing run must fail the command, even when its output is piped somewhere - a
# pipeline reports the last stage's status, so `run.sh | tail` returns tail's success
# and a red report reads as green.
( set -o pipefail 2>/dev/null ) && set -o pipefail

FOCUS="${1:-chop}"
ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
GAME="/Users/kuku/Library/Application Support/Steam/steamapps/common/Valheim"
CONFIG="$GAME/BepInEx/config/com.kuku.kukolony.cfg"
GAME_LOG="$GAME/BepInEx/LogOutput.log"
RUN_ID="focus-$FOCUS-$(date +%Y%m%d-%H%M%S)"
OUTPUT="$GAME/BepInEx/kukolony-benchmarks/$RUN_ID"
BACKUP="$OUTPUT/config.original"
PROCESS_PATTERN='/Valheim/valheim.app/Contents/MacOS/Valheim'

mkdir -p "$OUTPUT"
cp "$CONFIG" "$BACKUP"

# The config is the player's own. Put it back whatever happens, including a Ctrl-C -
# leaving BenchmarkMode on would make the next ordinary launch wipe a world.
restore() {
  cp "$BACKUP" "$CONFIG"
  pkill -f "$PROCESS_PATTERN" 2>/dev/null || true
}
trap restore EXIT INT TERM

set_value() {
  if grep -q "^$1 =" "$CONFIG"; then sed -i '' -e "s|^$1 =.*|$1 = $2|" "$CONFIG"
  else printf '\n%s = %s\n' "$1" "$2" >> "$CONFIG"; fi
  grep -Fqx "$1 = $2" "$CONFIG" || { echo "could not set $1"; exit 1; }
}

dotnet run --project "$ROOT/Kukolony.DeterministicTests/Kukolony.DeterministicTests.csproj"
dotnet build "$ROOT/Kukolony.sln" -c Debug

set_value BenchmarkMode true
set_value BenchmarkAutoBoot true
set_value BenchmarkFocus "$FOCUS"
set_value BenchmarkRunId "$RUN_ID"
set_value BenchmarkOutputPath "$OUTPUT"
# Photographs are the half of "it works" the assertions cannot reach: a villager that
# fells a tree by sliding backwards through it passes every check in the suite. Kept on
# for a focused run because there are only a couple of frames and they are the point.
set_value BenchmarkScreenshots "${BENCHMARK_SCREENSHOTS:-true}"
set_value BenchmarkAutoExit true
set_value BenchmarkWorld KukolonyHaulTest

# Every run starts from an empty world: eight runs of leftover colonies and chests once
# made later runs fail at things earlier ones had passed, scattered across unrelated
# checks. The .fwl2 carries the name and seed and is kept, so the terrain is identical
# from one run to the next; everything else is the object database, which has to go.
WORLDS="$HOME/Library/Application Support/IronGate/Valheim/worlds_local"
if [ -d "$WORLDS/KukolonyHaulTest" ]; then
  find "$WORLDS/KukolonyHaulTest" -type f ! -name '*.fwl2' -delete
  echo "benchmark world emptied, seed kept"
fi

# Nothing launches while the last one is still leaving: Steam accepts a launch request
# for a game it already considers running and quietly does nothing with it.
leaving=0
while pgrep -f "$PROCESS_PATTERN" >/dev/null; do
  [ "$leaving" -ne 0 ] || echo "waiting for a previous run to exit"
  leaving=$((leaving+1))
  if [ "$leaving" -gt 120 ]; then
    echo "previous run would not exit; stopping it"
    pkill -KILL -f "$PROCESS_PATTERN" 2>/dev/null || true
    sleep 5
    break
  fi
  sleep 1
done

[ ! -f "$GAME_LOG" ] || mv "$GAME_LOG" "$OUTPUT/previous.log"
echo "launching Valheim for the '$FOCUS' checks"
open 'steam://rungameid/892970'

waited=0
while ! pgrep -f "$PROCESS_PATTERN" >/dev/null; do
  sleep 1; waited=$((waited+1))
  [ "$waited" -lt 60 ] || { echo "Valheim did not start"; exit 1; }
done

# A focused run is minutes, so the patience here is a fraction of the full harness's.
# The heartbeat is what distinguishes a slow check from a dead coroutine: the controller
# writes it from Update, which keeps running whatever the checks are doing.
report="$OUTPUT/benchmark-focus.log"
waited=0
while [ ! -f "$report" ]; do
  if ! pgrep -f "$PROCESS_PATTERN" >/dev/null; then
    [ -f "$report" ] || { echo "Valheim exited without writing a report"; exit 1; }
    break
  fi

  if [ -f "$OUTPUT/heartbeat.txt" ]; then
    modified="$(stat -f %m "$OUTPUT/heartbeat.txt")"; now="$(date +%s)"
    if [ $((now-modified)) -gt 300 ]; then
      echo "benchmark heartbeat stalled"
      # Usually a crashed coroutine rather than a hang: an exception inside a nested
      # check kills the enumerator and the only symptom is the heartbeat stopping.
      grep -B 1 -A 6 "Exception" "$GAME_LOG" 2>/dev/null | tail -20
      exit 1
    fi
  fi

  sleep 2; waited=$((waited+2))
  [ "$waited" -lt 900 ] || { echo "focused run timed out"; exit 1; }
done

sleep 2
cp -f "$GAME_LOG" "$OUTPUT/game.log" 2>/dev/null || true
echo
sed -n '/Colony acceptance run/,$p' "$report" 2>/dev/null || cat "$report"

grep -q "BENCHMARK TERMINAL focus PASS" "$report" || { echo; echo "FOCUSED RUN FAILED"; exit 1; }
# find rather than ls: a slice that takes no photographs is not a failure, and with
# pipefail a glob that matches nothing would kill a run that had just passed everything.
shots="$(find "$OUTPUT" -maxdepth 1 -name '*.png' | wc -l | tr -d ' ')"
if [ "$shots" != "0" ]; then
  mkdir -p /Users/kuku/Desktop/kukolony
  cp -f "$OUTPUT"/*.png /Users/kuku/Desktop/kukolony/ 2>/dev/null || true
  echo "$shots screenshot(s) copied to ~/Desktop/kukolony"
fi

echo
echo "focused run passed: $OUTPUT"
