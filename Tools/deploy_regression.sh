#!/usr/bin/env bash
# deploy_regression.sh — GOLDEN-MASTER regression test for Tools/deploy_convert.py.
#
# WHY: deploy_convert.py + rig_anim.py are SHARED across every deploy-convert model (the m114 howitzers on the
# LEGACY path, the T-62 on the CONTRACT path, ...). A change made for one model can silently break another — which
# is exactly what happened when the T-62 "engine contract" regressed the m114 three ways (invisible / microscopic /
# crossed legs), with nothing to catch it. This automates the diagnostic that finally cracked it: re-run the tool
# on each model's recorded args and diff the bone output against a known-good golden.
#
# USAGE (from the repo root, needs Blender + git-bash):
#   bash Tools/deploy_regression.sh            # CHECK: re-convert every model, diff vs Tools/deploy_golden/<res>.txt
#   bash Tools/deploy_regression.sh --capture  # (RE)CAPTURE goldens from the CURRENT tool — do this ONLY when every
#                                              #  model is verified working in-game, and review the git diff.
#
# WORKFLOW: run the CHECK before committing ANY deploy_convert.py / rig_anim.py change. A FAIL means that change
# altered a model's converted rig. If the change is intentional for that model (and re-verified in-game), re-capture
# its golden; if it's an unintended regression on ANOTHER model, that's the bug — fix it before shipping.
#
# The repro args come from each Assets/FactorySource/<res>/deploy_converted.args.txt (written by the baker):
#   source | srcMtime | toolMtime | start | end | strip | ready | legScale | barrelScale | rs | re | step | mag | arcR | return | slamDeg | slamSettle
# We ignore the two mtime cache-key fields and pass the 14 real args straight to deploy_convert.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BLENDER="${BLENDER:-C:/Program Files/Blender Foundation/Blender 5.1/blender.exe}"
CONVERT="$ROOT/Tools/deploy_convert.py"
DUMP="$ROOT/Tools/deploy_bonedump.py"
GOLD="$ROOT/Tools/deploy_golden"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
mkdir -p "$GOLD"
capture=0; [ "${1:-}" = "--capture" ] && capture=1
[ -x "$BLENDER" ] || { echo "Blender not found at '$BLENDER' (set BLENDER=... to override)"; exit 2; }
pass=0; fail=0; miss=0

for a in "$ROOT"/Assets/FactorySource/*/deploy_converted.args.txt; do
  [ -f "$a" ] || continue
  res="$(basename "$(dirname "$a")")"
  mapfile -t F < <(tr '|' '\n' < "$a")
  src="${F[0]}"
  args=("${F[@]:3}")                                   # drop source + the two mtime cache keys
  if [ ! -f "$src" ]; then echo "SKIP $res (source missing: $src)"; continue; fi
  "$BLENDER" --background --python "$CONVERT" -- "$src" "$TMP/$res.glb" "${args[@]}" >"$TMP/$res.convlog" 2>&1
  if [ ! -f "$TMP/$res.glb" ] || ! grep -q "DEPLOY wrote:" "$TMP/$res.convlog"; then
    echo "FAIL $res — deploy_convert did not complete (see log):"; tail -5 "$TMP/$res.convlog"; fail=$((fail+1)); continue
  fi
  "$BLENDER" --background --python "$DUMP" -- "$TMP/$res.glb" 2>/dev/null \
    | grep -E "^(ARMATURE|BONES|FRAMES|f[0-9])" > "$TMP/$res.dump"

  if [ "$capture" = 1 ]; then
    cp "$TMP/$res.dump" "$GOLD/$res.txt"
    echo "CAPTURED $res  ($(head -1 "$GOLD/$res.txt" | cut -d' ' -f2), $(sed -n 2p "$GOLD/$res.txt"))"
  elif [ ! -f "$GOLD/$res.txt" ]; then
    echo "NO-GOLDEN $res (run --capture once)"; miss=$((miss+1))
  elif diff -q "$GOLD/$res.txt" "$TMP/$res.dump" >/dev/null; then
    echo "PASS $res"; pass=$((pass+1))
  else
    echo "FAIL $res — converted rig CHANGED vs golden:"
    diff "$GOLD/$res.txt" "$TMP/$res.dump" | head -24
    fail=$((fail+1))
  fi
done

[ "$capture" = 1 ] && { echo "goldens written to Tools/deploy_golden/ — review the git diff before committing"; exit 0; }
echo "=== deploy regression: $pass passed, $fail failed, $miss missing golden ==="
[ "$fail" = 0 ] && [ "$miss" = 0 ]
