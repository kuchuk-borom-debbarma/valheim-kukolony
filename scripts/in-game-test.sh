#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
GAME="/Users/kuku/Library/Application Support/Steam/steamapps/common/Valheim"
PACK="/Users/kuku/Downloads/denikson-BepInExPack_Valheim-5.4.2350/BepInExPack_Valheim"
CONFIG="$GAME/BepInEx/config/com.kuku.kukolony.cfg"
GAME_LOG="$GAME/BepInEx/LogOutput.log"
RUN_ID="$(date +%Y%m%d-%H%M%S)"
OUTPUT="$GAME/BepInEx/kukolony-benchmarks/$RUN_ID"
BACKUP="$OUTPUT/config.original"
PROCESS_PATTERN='/Valheim/valheim.app/Contents/MacOS/Valheim'
mkdir -p "$OUTPUT"
cp "$CONFIG" "$BACKUP"

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

# A setting the config file has never seen, which the plain setter cannot place: BepInEx reads
# entries as belonging to the section above them, and this script restores the config from a
# backup afterwards - so a key the game has not yet written is never there to be edited, and an
# appended line lands outside every section and is silently ignored. Repeating the header is
# legal and puts the entry where it will actually be read.
#
# Worth the six lines: the first run of the idle watch reported threshold=300s, the default,
# because the 150 written here went somewhere BepInEx never looked.
set_value_in() {
  if grep -q "^$2 =" "$CONFIG"; then sed -i '' -e "s|^$2 =.*|$2 = $3|" "$CONFIG"
  else printf '\n[%s]\n%s = %s\n' "$1" "$2" "$3" >> "$CONFIG"; fi
  grep -Fqx "$2 = $3" "$CONFIG" || { echo "could not set $2"; exit 1; }
}

wait_for_run() {
  expected_stage="$1"
  report="$OUTPUT/benchmark-$expected_stage.log"

  # Nothing launches while the last one is still leaving.
  #
  # Steam accepts a launch request for a game it already considers running and quietly does
  # nothing with it; the wait below then finds the *dying* process, decides the game started,
  # and times out waiting for a report that was never coming - which reads as a hang.
  #
  # It belongs here rather than once at the top of the script, because the race is the
  # create-to-reload handover: the previous stage gives the game ninety seconds to exit, then
  # sends it a TERM and returns without confirming it is gone.
  leaving=0
  while pgrep -f "$PROCESS_PATTERN" >/dev/null; do
    [ "$leaving" -ne 0 ] || echo "waiting for the previous run to exit"
    leaving=$((leaving+1))
    if [ "$leaving" -gt 120 ]; then
      echo "previous run would not exit; stopping it"
      pkill -KILL -f "$PROCESS_PATTERN" 2>/dev/null || true
      sleep 5
      break
    fi
    sleep 1
  done

  [ ! -f "$GAME_LOG" ] || mv "$GAME_LOG" "$OUTPUT/$expected_stage.previous.log"
  open 'steam://rungameid/892970'
  waited=0
  while ! pgrep -f "$PROCESS_PATTERN" >/dev/null; do
    sleep 1; waited=$((waited+1)); [ "$waited" -lt 60 ] || { echo "Valheim did not start"; return 1; }
  done
  waited=0
  missing=0
  quiet=0
  while [ "$waited" -lt 900 ]; do
    if [ -s "$report" ] && grep -Fq "BENCHMARK TERMINAL $expected_stage " "$report"; then
      cp "$GAME_LOG" "$OUTPUT/$expected_stage.game.log" 2>/dev/null || true
      exit_wait=0
      while pgrep -f "$PROCESS_PATTERN" >/dev/null && [ "$exit_wait" -lt 90 ]; do sleep 1; exit_wait=$((exit_wait+1)); done
      if pgrep -f "$PROCESS_PATTERN" >/dev/null; then
        echo "benchmark reported but Valheim did not exit; closing stale process"
        pkill -TERM -f "$PROCESS_PATTERN" 2>/dev/null || true
      fi
      if grep -Fq "BENCHMARK TERMINAL $expected_stage PASS" "$report"; then
        return 0
      fi
      echo "benchmark reported failure ($expected_stage)"
      cat "$report"
      return 1
    fi
    # Steam exits and re-execs the game while it starts, so there is a window where
    # nothing matches the pattern even though the run is fine. A single miss meant a
    # failed run; only repeated misses mean the process is really gone.
    if pgrep -f "$PROCESS_PATTERN" >/dev/null; then
      missing=0
    else
      missing=$((missing+1))
      if [ "$missing" -ge 8 ]; then
        cp "$GAME_LOG" "$OUTPUT/$expected_stage.died.log" 2>/dev/null || true
        echo "Valheim exited before benchmark terminal report ($expected_stage)"; return 1
      fi
    fi

    # A hang during world load never writes a heartbeat, so the heartbeat check below
    # cannot see it and the run would sit here until the overall timeout. The game logs
    # steadily while it is alive, so silence is the signal.
    log_mtime="$(stat -f %m "$GAME_LOG" 2>/dev/null || echo 0)"
    if [ "$log_mtime" -gt 0 ]; then
      log_age=$(( $(date +%s) - log_mtime ))
      if [ "$log_age" -gt 180 ]; then quiet=$((quiet+1)); else quiet=0; fi
      if [ "$quiet" -ge 3 ]; then
        cp "$GAME_LOG" "$OUTPUT/$expected_stage.hung.log" 2>/dev/null || true
        pkill -TERM -f "$PROCESS_PATTERN" 2>/dev/null || true
        echo "Valheim stopped logging for ${log_age}s ($expected_stage); treating as hung"
        # Nothing of ours had run yet, so this cannot be the change under test.
        [ -f "$OUTPUT/heartbeat.txt" ] || return 2
        return 1
      fi
    fi

    if [ -f "$OUTPUT/heartbeat.txt" ]; then
      modified="$(stat -f %m "$OUTPUT/heartbeat.txt")"; now="$(date +%s)"
      if [ $((now-modified)) -gt 300 ]; then
        echo "benchmark heartbeat stalled ($expected_stage)"
        # A stalled heartbeat is usually a crashed coroutine rather than a true hang: an
        # exception inside a nested check kills the enumerator, the phase stops advancing, and
        # the only symptom is this. Say what threw, or the next person reads "stalled" and goes
        # looking for a deadlock that is not there.
        grep -B 1 -A 6 "Exception" "$GAME_LOG" 2>/dev/null | tail -20
        return 1
      fi
    fi
    sleep 2; waited=$((waited+2))
  done
  echo "benchmark timed out ($expected_stage)"; return 1
}

same="$(shasum "$PACK/doorstop_libs/libdoorstop_x64.dylib" "$GAME/doorstop_libs/libdoorstop_x64.dylib" | awk '{print $1}' | uniq | wc -l | tr -d ' ')"
[ "$same" = "1" ] || { echo "Doorstop runtime does not match pinned pack"; exit 1; }
dotnet run --project "$ROOT/Kukolony.DeterministicTests/Kukolony.DeterministicTests.csproj"
dotnet build "$ROOT/Kukolony.sln" -c Debug
set_value BenchmarkMode true
set_value BenchmarkAutoBoot true
set_value BenchmarkRunId "$RUN_ID"
set_value BenchmarkOutputPath "$OUTPUT"
set_value BenchmarkScreenshots "${BENCHMARK_SCREENSHOTS:-true}"
# A villager that finishes nothing for this long fails the run. Shorter than the default
# because a run is minutes rather than an evening, and longer than the longest wait any
# check makes on purpose - the mechanism is the same either way, only the patience differs.
set_value_in "2 - Jobs" IdleWarnSeconds 150

set_value BenchmarkAutoExit true
set_value BenchmarkWorld KukolonyHaulTest

# A hang before the benchmark starts is in world loading, not in anything being
# tested, so it is worth one retry rather than a failed run.
run_stage() {
  status=0
  wait_for_run "$1" || status=$?
  if [ "$status" -eq 0 ]; then return 0; fi
  if [ "$status" -ne 2 ]; then return 1; fi
  echo "retrying $1 once after a load hang"
  wait_for_run "$1"
}

# Every run starts from an empty world.
#
# The world was being reused, so eight runs of colonies, chests and villagers piled up in it
# and later runs began failing at things the earlier ones had passed - pieces that would not
# place because something from a previous run was already standing there, and a second colony
# claiming the structures of the first. The symptom is the worst kind: failures scattered
# across unrelated checks, none of them caused by the change being tested.
#
# The .fwl2 file carries the world's name and seed and is deliberately kept, so the terrain is
# identical from one run to the next. Everything else is the object database, which is what has
# to go. A world with a seed and no database is exactly a freshly created one.
WORLDS="$HOME/Library/Application Support/IronGate/Valheim/worlds_local"
if [ -d "$WORLDS/KukolonyHaulTest" ]; then
  find "$WORLDS/KukolonyHaulTest" -type f ! -name '*.fwl2' -delete
  echo "benchmark world emptied, seed kept"
fi

run_stage create
run_stage reload

if [ "${BENCHMARK_SCREENSHOTS:-true}" = "true" ]; then
  expected='haul-fetching.png haul-delivering.png haul-settled.png colony-villager.png colony-screen-home.png colony-screen-gallery.png colony-screen-paged.png colony-screen-picker.png colony-screen-structures.png colony-screen-structure.png colony-screen-register-nearby.png colony-screen-settings.png colony-screen-villagers.png colony-screen-villager.png'
  for image in $expected; do [ -s "$OUTPUT/$image" ] || { echo "missing screenshot: $image"; exit 1; }; done
fi
mkdir -p /Users/kuku/Desktop/kukolony
cp -f "$OUTPUT"/*.png "$OUTPUT"/*.log "$OUTPUT"/*.json /Users/kuku/Desktop/kukolony/ 2>/dev/null || true
printf '%s\n' "$OUTPUT" > /Users/kuku/Desktop/kukolony/latest-benchmark-path.txt
echo "BENCHMARK RESULT: PASS $OUTPUT"
