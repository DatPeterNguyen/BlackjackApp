#!/usr/bin/env bash
# Builds one iOS Simulator copy of the app, checks what came out, and
# packages it the way Appetize and the smoke test expect it.
#
#   usage: build-ios-sim.sh <Configuration> <name> [extra dotnet build args...]
#   e.g.   build-ios-sim.sh Release release-no-ads -p:Ads=false --no-incremental
#
# Produces:
#   dist/BlackjackApp-ios-simulator-<name>.zip  the .app, zipped with ditto
#   logs/build-<name>.binlog                    full MSBuild log (open in
#                                               MSBuild Structured Log Viewer)
#   logs/warnings-<name>.log                    every build warning
# and adds a row to the job summary.
#
# Why ditto: a zip of the .app has to contain the .app folder itself, keep
# the main executable's +x bit, and keep the symlinks inside frameworks.
# Uploading the .app folder straight to actions/upload-artifact does none of
# those - artifacts don't preserve permissions, and a folder upload stores
# its contents without the folder around them.
set -euo pipefail

CONFIG="$1"
NAME="$2"
shift 2

PROJECT="BlackjackApp.Maui/BlackjackApp.Maui.csproj"
RID="iossimulator-arm64"
APP="BlackjackApp.Maui/bin/$CONFIG/net10.0-ios/$RID/BlackjackApp.Maui.app"
mkdir -p dist logs

# Stale output from an earlier build in this job must not be mistaken for
# this one's (the Release and release-no-ads builds share a folder).
rm -rf "$APP"

echo "::group::dotnet build ($NAME)"
dotnet build "$PROJECT" -f net10.0-ios -c "$CONFIG" \
  -p:RuntimeIdentifier="$RID" \
  -p:BuildIpa=false \
  -bl:"logs/build-$NAME.binlog" \
  -fl1 "-flp1:warningsonly;logfile=logs/warnings-$NAME.log" \
  "$@"
echo "::endgroup::"

if [ ! -d "$APP" ]; then
  echo "::error::The build reported success but there is no .app at $APP"
  exit 1
fi

# What actually came out.
EXE="$APP/BlackjackApp.Maui"
PLIST="$APP/Info.plist"
ARCHS=$(lipo -archs "$EXE" 2>/dev/null || echo "unreadable")
MIN_IOS=$(/usr/libexec/PlistBuddy -c 'Print :MinimumOSVersion' "$PLIST" 2>/dev/null || echo "?")
BUNDLE=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$PLIST" 2>/dev/null || echo "?")
SIZE=$(du -sh "$APP" | cut -f1)
[ -x "$EXE" ] || echo "::warning::$NAME: the app's main executable isn't marked executable"

# (grep finds nothing in a clean build, which under pipefail is a failure.)
WARNINGS=$( (grep ': warning ' "logs/warnings-$NAME.log" 2>/dev/null || true) | sort -u | wc -l | tr -d ' ')
TOP=$( (sort -u "logs/warnings-$NAME.log" 2>/dev/null | grep -o 'warning [A-Z]\{2,\}[0-9]\{3,\}' || true) \
       | sort | uniq -c | sort -rn | head -5 | awk '{printf "%s x%s, ", $3, $1}' | sed 's/, $//')
[ -n "$TOP" ] || TOP="-"

ZIP="dist/BlackjackApp-ios-simulator-$NAME.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"

echo "Built $NAME: $BUNDLE, $ARCHS, iOS $MIN_IOS+, $SIZE, $WARNINGS warning(s) -> $ZIP"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  echo "| $NAME | $CONFIG | $ARCHS | $MIN_IOS | $SIZE | $WARNINGS | $TOP |" >> "$GITHUB_STEP_SUMMARY"
fi
