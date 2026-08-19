#!/usr/bin/env bash
# check.sh — the one fast pre-push gate for the HAF editor (ENCReload). Runs the quick guards so a push can't land
# editor scripts that don't compile or a drifted registry schema. Wired as the pre-push hook (Tools/git-hooks/pre-push);
# also runnable by hand:  bash Tools/check.sh
#
# Deliberately NOT here (too slow / need Unity or Blender): deploy_regression.sh (Blender golden-master) and the
# in-editor Feature Test. This is the sub-minute gate.
set -uo pipefail
cd "$(dirname "$0")/.." && ROOT="$(pwd)" || exit 2
fail=0
run() {  # run <label> <command...>
  local label="$1"; shift
  printf '\n=== %s ===\n' "$label"
  if "$@"; then printf '[PASS] %s\n' "$label"; else printf '[FAIL] %s\n' "$label"; fail=1; fi
}

# 1) editor scripts compile — Roslyn against Unity's reference assemblies (Unity itself only compiles when focused).
run "editor scripts compile (Roslyn)" bash "$ROOT/Tools/editor_compile_check.sh"

# 2) registry schema parity — the editor's ModelDef/RegistryFile vs the plugin's Newtonsoft + regex parse (needs the
#    HumankindAssetFramework plugin checkout; the script self-reports MISSING and exits 2 if it can't find it).
run "registry schema parity" bash "$ROOT/Tools/check_schema_parity.sh"

# 3) hand-list gate — every field a window's UI edits must be on that window's ownership-rebase re-apply list,
#    or Save silently resets it (the combatZ class, drill-caught 2026-08-19; audit made permanent here).
run "hand-list gate (ownership rebases)" bash "$ROOT/Tools/check_handlists.sh"

printf '\n========================================\n'
if [ "$fail" -eq 0 ]; then printf 'CHECK: PASS — safe to push.\n'; else printf 'CHECK: FAIL — fix the [FAIL] step(s) above before pushing (or, only in a real emergency, git push --no-verify).\n'; fi
exit "$fail"
