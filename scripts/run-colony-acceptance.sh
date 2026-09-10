#!/bin/sh
set -eu

ROOT="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
GAME="/Users/kuku/Library/Application Support/Steam/steamapps/common/Valheim"
PACK="/Users/kuku/Downloads/denikson-BepInExPack_Valheim-5.4.2350/BepInExPack_Valheim"
CONFIG="$GAME/BepInEx/config/com.kuku.kukolony.cfg"
LOG="$GAME/BepInEx/LogOutput.log"
STAMP="$(date +%Y%m%d-%H%M%S)"
ARTIFACTS="$ROOT/artifacts/colony-$STAMP"
BACKUP="$ARTIFACTS/config.original"
SHOTS="$ARTIFACTS/screenshots"

mkdir -p "$SHOTS"
cp "$CONFIG" "$BACKUP"
restore() {
  cp "$BACKUP" "$CONFIG"
  pkill -f '/Valheim/valheim.app/Contents/MacOS/Valheim' 2>/dev/null || true
}
trap restore EXIT INT TERM

same="$(shasum "$PACK/doorstop_libs/libdoorstop_x64.dylib" "$GAME/doorstop_libs/libdoorstop_x64.dylib" | awk '{print $1}' | uniq | wc -l | tr -d ' ')"
[ "$same" = "1" ] || { echo "Doorstop runtime does not match pinned pack"; exit 1; }

xattr -dr com.apple.quarantine "$PACK" "$GAME/doorstop_libs" 2>/dev/null || true
dotnet run --project "$ROOT/Kukolony.DeterministicTests/Kukolony.DeterministicTests.csproj"
dotnet build "$ROOT/Kukolony.sln" -c Debug

set_value() {
  key="$1"
  value="$2"
  # BepInEx keys are simple identifiers.  Use BSD sed directly: the previous
  # Perl environment interpolation silently left the user value in place on
  # macOS, causing AutoBoot to select an unrelated, very large test world.
  sed -i '' -e "s|^$key =.*|$key = $value|" "$CONFIG"
  grep -Fqx "$key = $value" "$CONFIG" || { echo "failed to pin $key"; exit 1; }
}

run_game() {
  label="$1"
  marker="$2"
  attempt=1
  while [ "$attempt" -le 2 ]; do
    pkill -f '/Valheim/valheim.app/Contents/MacOS/Valheim' 2>/dev/null || true
    [ ! -f "$LOG" ] || mv "$LOG" "$ARTIFACTS/$label.attempt-$attempt.previous.log"
    open 'steam://rungameid/892970'
    count=0
    while [ "$count" -lt 180 ]; do
      if [ -f "$LOG" ] && grep -Fq 'Kukolony 0.0.1 loaded' "$LOG" && grep -Fq "$marker" "$LOG"; then
        cp "$LOG" "$ARTIFACTS/$label.log"
        # The report is written before the self-test logs out.  Starting the
        # next Steam launch here used to kill the process while its save batch
        # was still being committed, so the supposed reload was another run 1.
        # Wait for the orderly exit before returning to the caller.
        exit_wait=0
        # The in-game fixture already grants Logout eight seconds to flush. Steam
        # occasionally leaves a headless Unity process behind afterwards; wait a
        # further bounded grace period, then close only that stale process so the
        # independent reload launch can verify the persisted data.
        while pgrep -f '/Valheim/valheim.app/Contents/MacOS/Valheim' >/dev/null && [ "$exit_wait" -lt 30 ]; do
          sleep 1
          exit_wait=$((exit_wait + 1))
        done
        if pgrep -f '/Valheim/valheim.app/Contents/MacOS/Valheim' >/dev/null; then
          echo "$label completed its report; closing stale post-save Valheim process"
          pkill -TERM -f '/Valheim/valheim.app/Contents/MacOS/Valheim' 2>/dev/null || true
          sleep 3
          pgrep -f '/Valheim/valheim.app/Contents/MacOS/Valheim' >/dev/null && pkill -KILL -f '/Valheim/valheim.app/Contents/MacOS/Valheim' 2>/dev/null || true
        fi
        return 0
      fi
      if [ "$count" -gt 24 ] && [ -f "$LOG" ]; then
        modified="$(stat -f %m "$LOG")"
        now="$(date +%s)"
        [ $((now - modified)) -le 120 ] || break
      fi
      if [ "$count" -gt 6 ] && ! pgrep -f '/Valheim/valheim.app/Contents/MacOS/Valheim' >/dev/null; then
        break
      fi
      sleep 5
      count=$((count + 1))
    done
    [ ! -f "$LOG" ] || cp "$LOG" "$ARTIFACTS/$label.attempt-$attempt.timeout.log"
    attempt=$((attempt + 1))
  done
  echo "$label timed out waiting for $marker"
  return 1
}

set_value AutoTestEnabled true
set_value AutoTestQuitWhenDone true
set_value AutoTestWorld KukolonyJobs
set_value DebugProbeEnabled false
set_value DebugScreenshotEnabled false
run_game acceptance-run1 'Colony acceptance run 1'
run_game acceptance-run2 'Colony acceptance run 2'

set_value DebugScreenshotEnabled true
set_value DebugScreenshotPath "$SHOTS"
run_game screenshots '[Screenshot] done'

grep -q 'RESULT: PASS' "$ARTIFACTS/acceptance-run1.log"
grep -q 'RESULT: PASS' "$ARTIFACTS/acceptance-run2.log"
expected='colony-panel.png colony-structures.png colony-members.png colony-members-page-2.png colony-member-detail.png colony-jobs.png colony-job-config.png colony-structure-picker.png colony-preset-application.png colony-picker.png'
for file in $expected; do
  [ -s "$SHOTS/$file" ] || { echo "missing screenshot: $file"; exit 1; }
done
DESKTOP="/Users/kuku/Desktop/kukolony"
mkdir -p "$DESKTOP"
cp -f "$SHOTS"/*.png "$DESKTOP"/
cp -f "$ARTIFACTS/acceptance-run1.log" "$ARTIFACTS/acceptance-run2.log" "$ARTIFACTS/screenshots.log" "$DESKTOP"/
printf '%s\n' "$ARTIFACTS" > "$DESKTOP/latest-artifact-path.txt"
echo "Kukolony colony acceptance passed: $ARTIFACTS"
