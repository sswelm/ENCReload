#!/usr/bin/env bash
# check.sh — the fast pre-push gate for ENCReload (the MOD). Wired as the pre-push hook (Tools/git-hooks/pre-push);
# also runnable by hand:  bash Tools/check.sh
#
# 2026-08-24 — THIS REPO NO LONGER OWNS THE EDITOR TOOLS. They moved to the HumankindAssetFramework repo's editor/
# and are consumed here as a Unity package (Packages/manifest.json -> file:../../HumankindAssetFramework/editor).
# So the three guards that used to run here moved WITH their source and now run in that repo's tools/check.sh:
#
#   editor_compile_check.sh   the editor compiles (Roslyn vs Unity's reference assemblies) — needs licensed Unity
#   check_handlists.sh        ownership-rebase hand-lists (the silent-Save-reset class)
#   check_schema_parity.sh    baker ModelDef vs the plugin's parse — no longer cross-repo, both halves live there
#
# A guard belongs with the source it guards; running them here would mean reaching across a repo boundary to check
# files this repo no longer contains. If you change the tools, push HumankindAssetFramework — its gate covers them.
#
# What is left here is the MOD's own content: game data, the pack, skins, sounds, and the Blender/model-prep
# scripts under Tools/. Deliberately NOT in this gate (too slow / need Unity or Blender): deploy_regression.sh
# (the Blender golden-master) and the in-editor Feature Test.
set -uo pipefail
cd "$(dirname "$0")/.." && ROOT="$(pwd)" || exit 2
fail=0
run() {  # run <label> <command...>
  local label="$1"; shift
  printf '\n=== %s ===\n' "$label"
  if "$@"; then printf '[PASS] %s\n' "$label"; else printf '[FAIL] %s\n' "$label"; fail=1; fi
}

# 1) the package this project depends on must actually resolve. A file: dependency is a path on THIS machine, so a
#    moved or missing sibling checkout breaks every HAF window with a Package Manager error and nothing else would
#    say so before a push. Cheap, and it is the one coupling the move introduced.
run "HAF tools package resolves" bash -c '
  m="$0/Packages/manifest.json"
  dep=$(grep -o "\"com.sswelm.haf-authoring\"[[:space:]]*:[[:space:]]*\"file:[^\"]*\"" "$m" | sed "s/.*file://; s/\"$//")
  [ -n "$dep" ] || { echo "no com.sswelm.haf-authoring file: dependency in Packages/manifest.json"; exit 1; }
  target="$0/Packages/$dep"
  [ -f "$target/package.json" ] || { echo "package not found at: $target (expected package.json)"; exit 1; }
  echo "resolves -> $(cd "$target" && pwd)"
' "$ROOT"

printf '\n========================================\n'
if [ "$fail" -eq 0 ]; then printf 'CHECK: PASS — safe to push.\n'; else printf 'CHECK: FAIL — fix the [FAIL] step(s) above before pushing (or, only in a real emergency, git push --no-verify).\n'; fi
exit "$fail"
