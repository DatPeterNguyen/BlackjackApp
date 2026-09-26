#!/usr/bin/env bash
# Installs each iOS Simulator build in <apps-dir> on the booted simulator and
# runs .maestro/smoke.yaml against it.
#
#   usage: smoke-test-ios.sh <apps-dir>
#   env:   DEVICE_ID, BUNDLE_ID
#
# Per build, in smoke-results/<name>/:
#   maestro.txt       every step Maestro ran, and the one it failed on
#   report.xml        the same as a JUnit report
#   *.png             the flow's own screenshots, plus at-failure.png
#   run.mp4           video of the whole run
#   app.log           the app's system log
#   hang-stacks.txt   on failure only: a 5-second sample of every thread in
#                     the app. If it froze, the main thread's stack shows
#                     what it's stuck in - the one thing no log has told us.
#   maestro-debug/    Maestro's own logs, for when Maestro itself misbehaves
#
# Exits non-zero if any build failed.
set -uo pipefail

APPS_DIR="$1"
: "${DEVICE_ID:?DEVICE_ID must be set}"
: "${BUNDLE_ID:?BUNDLE_ID must be set}"
FLOW="$PWD/.maestro/smoke.yaml"
RESULTS="$PWD/smoke-results"
mkdir -p "$RESULTS"
FAILED=0

strip_ansi() { sed $'s/\x1b\\[[0-9;]*[A-Za-z]//g'; }

summary() { [ -n "${GITHUB_STEP_SUMMARY:-}" ] && echo "$1" >> "$GITHUB_STEP_SUMMARY"; }
summary "### Smoke test (Maestro, iPhone 16 simulator)"
summary ""
summary "| Build | Result | Where it stopped |"
summary "|---|---|---|"

# Release first: it's the build closest to what a player runs, and the
# no-ads build right after it is the comparison that matters most.
for NAME in release release-no-ads debug; do
  ZIP="$APPS_DIR/BlackjackApp-ios-simulator-$NAME.zip"
  [ -f "$ZIP" ] || { echo "::warning::No $NAME build to test"; continue; }
  OUT="$RESULTS/$NAME"
  mkdir -p "$OUT"
  echo "::group::Smoke test: $NAME"

  UNZIP="$RUNNER_TEMP/app-$NAME"
  rm -rf "$UNZIP" && mkdir -p "$UNZIP"
  ditto -x -k "$ZIP" "$UNZIP"
  APP=$(find "$UNZIP" -maxdepth 1 -name '*.app' | head -1)

  xcrun simctl terminate "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true
  xcrun simctl uninstall "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true
  if ! xcrun simctl install "$DEVICE_ID" "$APP"; then
    summary "| $NAME | :x: FAIL | couldn't install the app on the simulator |"
    FAILED=1
    echo "::endgroup::"
    continue
  fi

  xcrun simctl spawn "$DEVICE_ID" log stream --level info --style compact \
    --predicate 'processImagePath CONTAINS "BlackjackApp"' > "$OUT/app.log" 2>&1 &
  LOG_PID=$!
  xcrun simctl io "$DEVICE_ID" recordVideo --codec=h264 --force "$OUT/run.mp4" > /dev/null 2>&1 &
  VIDEO_PID=$!

  # Run from $OUT so the flow's takeScreenshot files land there.
  ( cd "$OUT" && maestro test "$FLOW" \
      --format junit --output "$OUT/report.xml" \
      --debug-output "$OUT/maestro-debug" ) > "$OUT/maestro.raw" 2>&1
  RC=$?
  strip_ansi < "$OUT/maestro.raw" > "$OUT/maestro.txt"

  if [ $RC -ne 0 ]; then
    FAILED=1
    xcrun simctl io "$DEVICE_ID" screenshot "$OUT/at-failure.png" >/dev/null 2>&1 || true
    # Simulator apps are ordinary processes on the Mac, so the Mac's own
    # profiler can sample them.
    APP_PROC=$(pgrep -f "BlackjackApp.Maui.app/BlackjackApp.Maui" | head -1 || true)
    if [ -n "$APP_PROC" ]; then
      sample "$APP_PROC" 5 -file "$OUT/hang-stacks.txt" >/dev/null 2>&1 || true
      STATE="app still running - see hang-stacks.txt"
    else
      STATE="app process gone (crashed?)"
    fi
    # Maestro marks the step it stopped on; fall back to its last lines.
    WHERE=$(grep -m1 -iE 'FAILED|not found|Assertion|timed out|Exception' "$OUT/maestro.txt" | tr -s ' ' | cut -c1-200)
    [ -n "$WHERE" ] || WHERE=$(tail -n 3 "$OUT/maestro.txt" | tr '\n' ' ' | tr -s ' ' | cut -c1-200)
    WHERE=${WHERE//|/\\|}
    summary "| $NAME | :x: FAIL | $WHERE ($STATE) |"
    echo "::error title=Smoke test $NAME::$WHERE ($STATE)"
  else
    summary "| $NAME | :white_check_mark: PASS | played Play -> table -> a full round -> Exit to Menu |"
  fi

  kill -INT "$VIDEO_PID" 2>/dev/null || true
  for _ in $(seq 1 15); do kill -0 "$VIDEO_PID" 2>/dev/null || break; sleep 1; done
  kill "$LOG_PID" 2>/dev/null || true
  xcrun simctl terminate "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true

  cat "$OUT/maestro.txt"
  echo "::endgroup::"
done

summary ""
summary "Each build's screenshots, video, logs and (on failure) thread stacks are in the **BlackjackApp-iOS-Smoke-Test-Results** artifact."
exit "$FAILED"
