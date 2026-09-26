#!/usr/bin/env bash
# Drives one build of the app through the things a player actually does, on
# an already-booted simulator, and records what happened at every stage.
#
#   usage: ios-smoke-test.sh <path/to/BlackjackApp.Maui.app> <label>
#   env:   DEVICE_ID, BUNDLE_ID  (exported by the workflow's boot step)
#
# Stages, in order:
#   1. Install & launch     fresh install, app process comes up
#   2. idb can see the UI   one hit-test answered, or nothing below means anything
#   3. Start menu           daily-reward popup closed if it opened; PLAY showing
#   4. Open mode picker     PLAY -> RulesPage
#   5. Start a game         RulesPage PLAY -> table (the reported freeze)
#   6. Play a round         All In, DEAL, answer insurance / Stand until DEAL returns
#   7. Exit to menu         table menu -> Exit to Menu -> confirm -> start menu
# and then, whatever happened above:
#   8. Crash reports        any crash report the app left during this run
#   9. Log scan             UIKit layout-loop reports, unhandled .NET exceptions
#
# Stages 3-7 depend on each other, so once one fails the rest are recorded as
# skipped rather than run against the wrong screen. 8 and 9 always run.
#
# Everything lands in simulator-results/<label>/: a screenshot per stage, a
# video of the whole run, the app's os_log and stdout, crash reports, and
# summary.md (also appended to the GitHub job summary). Exits non-zero if any
# stage failed.

set -uo pipefail

APP_PATH="$1"
LABEL="$2"
: "${DEVICE_ID:?DEVICE_ID must be set}"
: "${BUNDLE_ID:?BUNDLE_ID must be set}"

HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="simulator-results/$LABEL"
mkdir -p "$OUT"

# Candidate x positions for controls that aren't full-width (the table's
# buttons and chips, alert buttons). iPhone 16 is 393pt wide.
GRID_XS="50,120,196,270,340"

ui() { python3 "$HERE/idb_find.py" --udid "$DEVICE_ID" "$@"; }
shot() { xcrun simctl io "$DEVICE_ID" screenshot "$OUT/$1.png" >/dev/null 2>&1 || true; }
app_running() { xcrun simctl spawn "$DEVICE_ID" launchctl list 2>/dev/null | grep -q "$BUNDLE_ID"; }

# Explains a missing screen: frozen (no hit-test answer) vs. just somewhere else.
diagnose() {
  if ui responsive --timeout 15 >/dev/null 2>&1; then
    echo "$1 (app still responds - it's on some other screen; see the screenshot)"
  else
    echo "$1 - and the app no longer answers hit-tests: FROZEN"
  fi
}

SUMMARY=()
FAILED=0
CHAIN_OK=1
DETAIL=""
LOG_PID=""; VIDEO_PID=""; APP_PID=""

record() {  # record STAGE STATUS DETAIL
  SUMMARY+=("| $1 | $2 | ${3//|/\\|} |")
  echo "[$LABEL] $1: $2 - $3"
  case "$2" in
    FAIL) FAILED=1; echo "::error title=iOS $LABEL - $1::$3" ;;
    WARN) echo "::warning title=iOS $LABEL - $1::$3" ;;
  esac
}

# run_stage NAME FUNCTION [always] - chained unless "always" is given.
run_stage() {
  local name="$1" fn="$2" mode="${3:-chained}" status
  if [ "$mode" != always ] && [ "$CHAIN_OK" -ne 1 ]; then
    record "$name" SKIP "an earlier stage failed"
    return
  fi
  echo "::group::[$LABEL] $name"
  DETAIL=""
  "$fn"
  case $? in
    0) status=PASS ;;
    2) status=WARN ;;
    *) status=FAIL; [ "$mode" != always ] && CHAIN_OK=0 ;;
  esac
  echo "::endgroup::"
  record "$name" "$status" "$DETAIL"
}

# ---------------------------------------------------------------- stages ---

stage_install_and_launch() {
  xcrun simctl terminate "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true
  # Uninstalling wipes saved data too, so every pass is a true first launch:
  # starting balance, no saved round, daily reward waiting.
  xcrun simctl uninstall "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true
  if ! xcrun simctl install "$DEVICE_ID" "$APP_PATH"; then
    DETAIL="simctl install failed for $APP_PATH"; return 1
  fi

  # UIKit's own layout-feedback-loop detector - it reports a view whose
  # layout keeps re-entering, whether or not the app's logging works. Its
  # output is what the log-scan stage looks for.
  xcrun simctl spawn "$DEVICE_ID" defaults write "$BUNDLE_ID" \
    UIViewLayoutFeedbackLoopDebuggingThreshold 100 >/dev/null 2>&1 || true

  xcrun simctl spawn "$DEVICE_ID" log stream --level debug --style compact \
    --predicate 'processImagePath CONTAINS "BlackjackApp"' > "$OUT/app-oslog.log" 2>&1 &
  LOG_PID=$!

  xcrun simctl io "$DEVICE_ID" recordVideo --codec=h264 --force "$OUT/run.mp4" > "$OUT/video.log" 2>&1 &
  VIDEO_PID=$!

  # The app's own Console output (its [table] trace) doesn't reach os_log on
  # iOS, so it's captured separately from the process's stdout.
  xcrun simctl launch --console-pty "$DEVICE_ID" "$BUNDLE_ID" > "$OUT/app-stdout.log" 2>&1 &
  APP_PID=$!

  for _ in $(seq 1 20); do
    if app_running; then DETAIL="app process is up"; return 0; fi
    sleep 1
  done

  # --console-pty is fragile on a headless runner; retry without stdout capture.
  kill "$APP_PID" 2>/dev/null || true
  APP_PID=""
  xcrun simctl launch "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true
  for _ in $(seq 1 15); do
    if app_running; then DETAIL="app process is up (plain launch - no stdout capture)"; return 0; fi
    sleep 1
  done
  DETAIL="app process never appeared - crashed on launch? see Crash reports below"
  return 1
}

stage_idb_sees_ui() {
  sleep 5   # let the first screen draw
  shot 01-launched
  local out
  out=$(ui probe 196 400 2>&1)
  local rc=$?
  echo "$out"
  if [ $rc -eq 0 ]; then
    DETAIL="$(echo "$out" | tail -n 1)"
    return 0
  fi
  DETAIL="idb can't read this simulator's UI: $(echo "$out" | head -n 1 | cut -c1-300)"
  return 1
}

stage_start_menu() {
  # A first launch opens the daily-reward popup over the start menu about a
  # quarter-second after it appears (GameMenuPage.OnAppearing). CLAIM only
  # exists on that popup, so finding it is the tell.
  if ui find CLAIM --timeout 10; then
    shot 02-daily-reward
    # Close sits below CLAIM, possibly under the fold of the popup's ScrollView.
    ui swipe 196 700 196 150 || true
    sleep 1
    if ! ui find Close --tap --timeout 8; then
      shot 02-daily-reward-stuck
      DETAIL="the daily-reward popup opened but its Close button wasn't found"
      return 1
    fi
    sleep 1
  fi
  if ui find PLAY --timeout 15; then
    shot 03-start-menu
    DETAIL="start menu is showing PLAY"
    return 0
  fi
  shot 03-start-menu
  DETAIL="$(diagnose "PLAY never appeared on the start menu")"
  return 1
}

stage_open_mode_picker() {
  if ! ui find PLAY --tap --timeout 5; then
    DETAIL="PLAY disappeared before it could be tapped"; return 1
  fi
  # "How to Play" is RulesPage's heading - only there.
  if ui find "How to Play" --timeout 12; then
    shot 04-mode-picker
    DETAIL="mode picker opened"
    return 0
  fi
  shot 04-mode-picker
  DETAIL="$(diagnose "tapped PLAY, but the mode picker never appeared")"
  return 1
}

stage_start_game() {
  # RulesPage's own PLAY is below three long rule sections, so scroll it into
  # view. Over-scrolling just stops at the bottom.
  ui swipe 196 700 196 120 || true
  ui swipe 196 700 196 120 || true
  sleep 1
  shot 05-mode-picker-bottom
  # That PLAY shares a row with a narrower Close on its right, so x=100 is
  # inside it; x=196 would be close to the edge between them.
  if ! ui find PLAY --xs 100 --tap --timeout 8; then
    DETAIL="the mode picker's PLAY button wasn't found"; return 1
  fi
  # DEAL only exists on the table, and it's showing before the first round.
  if ui find DEAL --xs "$GRID_XS" --y0 250 --timeout 20; then
    shot 06-table
    DETAIL="the table opened (DEAL is showing)"
    return 0
  fi
  shot 06-table
  DETAIL="$(diagnose "pressed the mode picker's PLAY, but the table never appeared")"
  return 1
}

stage_play_round() {
  # All In is one tap with a text label; placing a chip on a hand takes a
  # chip tap plus a tap on a hand slot, which has no label to find it by.
  if ! ui find "All In" --xs "$GRID_XS" --y0 250 --tap --timeout 8; then
    DETAIL="the All In button wasn't found"; return 1
  fi
  sleep 1
  local found label x y
  if ! found=$(ui find DEAL --xs "$GRID_XS" --y0 250 --timeout 5); then
    DETAIL="DEAL wasn't found to start the round"; return 1
  fi
  IFS=$'\t' read -r _ label x y _ <<< "$found"
  ui tap "$x" "$y"
  shot 07-deal-tapped

  # DEAL hides for the whole round and comes back when it's over. Wait for it
  # to go first - otherwise the DEAL that's still on screen for a moment
  # after the tap would look like "round over" straight away.
  ui gone DEAL --at "$x" "$y" --timeout 10
  case $? in
    0) ;;
    2) shot 07-deal-frozen; DETAIL="app stopped answering right after DEAL: FROZEN while dealing"; return 1 ;;
    *) shot 07-deal-ignored; DETAIL="tapped DEAL but no round started (bet rejected? see the screenshot)"; return 1 ;;
  esac

  # Then answer whatever the table asks until DEAL is back: insurance when
  # the dealer shows an Ace, Stand on the hand, No Thanks on the
  # out-of-chips offer if All In lost everything. A natural blackjack ends
  # the round with no decision at all.
  local decisions=0
  while :; do
    if ! found=$(ui find Stand "NO INSURANCE" "No Thanks" DEAL --xs "$GRID_XS" --y0 200 --timeout 30); then
      shot 08-round-stuck
      DETAIL="$(diagnose "mid-round, none of Stand / NO INSURANCE / DEAL showed up for 30s")"
      return 1
    fi
    IFS=$'\t' read -r _ label x y _ <<< "$found"
    if [ "$label" = DEAL ]; then
      shot 09-round-over
      DETAIL="dealt and played a full round ($decisions decision(s)) and the table is ready for the next one"
      return 0
    fi
    decisions=$((decisions + 1))
    if [ "$decisions" -gt 6 ]; then
      DETAIL="answered $decisions prompts and the round still hasn't ended"; return 1
    fi
    ui tap "$x" "$y"
    # Don't find the same button again while it's on its way out.
    ui gone "$label" --at "$x" "$y" --timeout 10
    if [ $? -eq 2 ]; then
      shot 08-round-frozen
      DETAIL="app stopped answering after tapping $label: FROZEN mid-round"
      return 1
    fi
  done
}

stage_exit_to_menu() {
  # The gear in the table's top corner. Its label comes from
  # SemanticProperties.Description in MainPage.xaml.
  if ! ui find "Table menu" --xs "$GRID_XS" --y0 40 --y1 320 --tap --timeout 8; then
    DETAIL="the table's menu button wasn't found"; return 1
  fi
  if ! ui find "Exit to Menu" --tap --timeout 10; then
    shot 10-menu
    DETAIL="$(diagnose "the menu didn't show Exit to Menu")"
    return 1
  fi
  sleep 1
  shot 10-exit-confirm
  # The confirmation alert has its own "Exit to Menu", beside Cancel.
  if ! ui find "Exit to Menu" --xs "$GRID_XS" --timeout 8 --tap; then
    DETAIL="the Exit to Menu confirmation wasn't found"; return 1
  fi
  if ui find PLAY --timeout 15; then
    shot 11-back-at-start
    DETAIL="left the game and landed back on the start menu"
    return 0
  fi
  shot 11-back-at-start
  DETAIL="$(diagnose "confirmed Exit to Menu, but the start menu never came back")"
  return 1
}

stage_crash_reports() {
  local reports
  reports=$(find "$HOME/Library/Logs/DiagnosticReports" -type f -newer "$OUT/.started" \
              -iname '*BlackjackApp*' 2>/dev/null || true)
  if [ -n "$reports" ]; then
    mkdir -p "$OUT/crash-reports"
    while IFS= read -r f; do cp "$f" "$OUT/crash-reports/" 2>/dev/null || true; done <<< "$reports"
    DETAIL="$(echo "$reports" | wc -l | tr -d ' ') crash report(s) written during this run - see crash-reports/"
    return 1
  fi
  if ! app_running; then
    DETAIL="no crash report, but the app process is gone"
    return 2
  fi
  DETAIL="no crash reports; app still running"
  return 0
}

stage_log_scan() {
  local loops exceptions notes=()
  loops=$(grep -ci 'feedback loop' "$OUT/app-oslog.log" 2>/dev/null || true)
  exceptions=$(grep -ciE 'unhandled exception|System\.[A-Za-z]+Exception' "$OUT/app-stdout.log" "$OUT/app-oslog.log" 2>/dev/null \
               | awk -F: '{s+=$NF} END {print s+0}')
  [ "${loops:-0}" -gt 0 ] && notes+=("UIKit reported a layout feedback loop $loops time(s)")
  [ "${exceptions:-0}" -gt 0 ] && notes+=("$exceptions .NET exception line(s) in the logs")
  if [ ${#notes[@]} -gt 0 ]; then
    DETAIL="$(IFS='; '; echo "${notes[*]}")"
    return 2
  fi
  DETAIL="no layout-loop reports or .NET exceptions in the logs"
  return 0
}

# ------------------------------------------------------------------- run ---

touch "$OUT/.started"

run_stage "Install & launch"   stage_install_and_launch
run_stage "idb can see the UI" stage_idb_sees_ui
run_stage "Start menu"         stage_start_menu
run_stage "Open mode picker"   stage_open_mode_picker
run_stage "Start a game"       stage_start_game
run_stage "Play a round"       stage_play_round
run_stage "Exit to menu"       stage_exit_to_menu

shot 99-final
run_stage "Crash reports"      stage_crash_reports always

# Stop recording and logging before scanning the logs, so they're complete.
[ -n "$VIDEO_PID" ] && kill -INT "$VIDEO_PID" 2>/dev/null
for _ in $(seq 1 15); do
  { [ -z "$VIDEO_PID" ] || ! kill -0 "$VIDEO_PID" 2>/dev/null; } && break
  sleep 1
done
[ -n "$LOG_PID" ] && kill "$LOG_PID" 2>/dev/null
[ -n "$APP_PID" ] && kill "$APP_PID" 2>/dev/null
xcrun simctl terminate "$DEVICE_ID" "$BUNDLE_ID" >/dev/null 2>&1 || true

run_stage "Log scan"           stage_log_scan always

{
  echo "### iOS smoke test - $LABEL build"
  echo
  echo "| Stage | Result | Detail |"
  echo "|---|---|---|"
  printf '%s\n' "${SUMMARY[@]}"
  echo
  echo "Screenshots, a video of the run (run.mp4), and logs are in the BlackjackApp-iOS-Smoke-Test artifact under $LABEL/."
} > "$OUT/summary.md"
cat "$OUT/summary.md"
[ -n "${GITHUB_STEP_SUMMARY:-}" ] && cat "$OUT/summary.md" >> "$GITHUB_STEP_SUMMARY"

exit "$FAILED"
