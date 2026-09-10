#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
GAME="/Users/kuku/Library/Application Support/Steam/steamapps/common/Valheim"
CONFIG="$GAME/BepInEx/config/com.kuku.kukolony.cfg"
LOG="$GAME/BepInEx/LogOutput.log"
STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="$GAME/BepInEx/kukolony-benchmarks/$STAMP"
BACKUP="$OUT/config.original"
mkdir -p "$OUT"
cp "$CONFIG" "$BACKUP"
restore() { cp "$BACKUP" "$CONFIG"; pkill -f '/Valheim/valheim.app/Contents/MacOS/Valheim' 2>/dev/null || true; }
trap restore EXIT INT TERM

set_value() { sed -i '' -e "s|^$1 =.*|$1 = $2|" "$CONFIG"; grep -Fqx "$1 = $2" "$CONFIG"; }
run_stage() {
  stage="$1"; marker="$2"; set_value BenchmarkStage "$stage"; [ ! -f "$LOG" ] || mv "$LOG" "$OUT/$stage.previous.log"
  open 'steam://rungameid/892970'
  elapsed=0
  while [ "$elapsed" -lt 900 ]; do
    if [ -f "$LOG" ] && grep -Fq "$marker" "$LOG"; then cp "$LOG" "$OUT/$stage.log"; return 0; fi
    sleep 5; elapsed=$((elapsed+5))
  done
  cp "$LOG" "$OUT/$stage.timeout.log" 2>/dev/null || true; return 1
}

dotnet run --project "$ROOT/Kukolony.DeterministicTests/Kukolony.DeterministicTests.csproj"
dotnet build "$ROOT/Kukolony.sln" -c Debug
set_value BenchmarkMode true
set_value AutoTestEnabled false
set_value AutoTestQuitWhenDone true
set_value AutoTestWorld KukolonyJobs
set_value BenchmarkOutputPath "$OUT"
set_value DebugScreenshotEnabled false
run_stage create 'RESULT: PASS'
run_stage reload 'RESULT: PASS'
run_stage ui '[Screenshot] done'
find "$OUT" -name '*.png' -size +0c | sort > "$OUT/screenshots.manifest"
expected='colony-panel.png colony-structures.png colony-members.png colony-members-page-2.png colony-member-detail.png colony-jobs.png colony-job-config.png colony-structure-picker.png colony-preset-application.png colony-picker.png'
for image in $expected; do test -s "$OUT/$image" || { echo "missing benchmark screenshot: $image"; exit 1; }; done
mkdir -p /Users/kuku/Desktop/kukolony
cp -f "$OUT"/* /Users/kuku/Desktop/kukolony/ 2>/dev/null || true
echo "BENCHMARK RESULT: PASS $OUT"
