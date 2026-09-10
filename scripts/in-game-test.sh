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

wait_for_run() {
  expected_stage="$1"
  report="$OUTPUT/benchmark-$expected_stage.log"
  [ ! -f "$GAME_LOG" ] || mv "$GAME_LOG" "$OUTPUT/$expected_stage.previous.log"
  open 'steam://rungameid/892970'
  waited=0
  while ! pgrep -f "$PROCESS_PATTERN" >/dev/null; do
    sleep 1; waited=$((waited+1)); [ "$waited" -lt 60 ] || { echo "Valheim did not start"; return 1; }
  done
  waited=0
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
    if ! pgrep -f "$PROCESS_PATTERN" >/dev/null; then
      cp "$GAME_LOG" "$OUTPUT/$expected_stage.died.log" 2>/dev/null || true
      echo "Valheim exited before benchmark terminal report ($expected_stage)"; return 1
    fi
    if [ -f "$OUTPUT/heartbeat.txt" ]; then
      modified="$(stat -f %m "$OUTPUT/heartbeat.txt")"; now="$(date +%s)"
      [ $((now-modified)) -le 180 ] || { echo "benchmark heartbeat stalled ($expected_stage)"; return 1; }
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
set_value BenchmarkAutoExit true
set_value BenchmarkWorld KukolonyBenchmark

wait_for_run create
wait_for_run reload

if [ "${BENCHMARK_SCREENSHOTS:-true}" = "true" ]; then
  expected='colony-panel.png colony-structures.png colony-members.png colony-members-page-2.png colony-member-detail.png colony-member-remove-confirm.png colony-jobs.png colony-job-config.png colony-structure-picker.png colony-preset-application.png colony-picker.png'
  for image in $expected; do [ -s "$OUTPUT/$image" ] || { echo "missing screenshot: $image"; exit 1; }; done
fi
mkdir -p /Users/kuku/Desktop/kukolony
cp -f "$OUTPUT"/*.png "$OUTPUT"/*.log "$OUTPUT"/*.json /Users/kuku/Desktop/kukolony/ 2>/dev/null || true
printf '%s\n' "$OUTPUT" > /Users/kuku/Desktop/kukolony/latest-benchmark-path.txt
echo "BENCHMARK RESULT: PASS $OUTPUT"
