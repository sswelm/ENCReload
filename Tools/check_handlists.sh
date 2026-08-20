#!/usr/bin/env bash
# check_handlists.sh — THE HAND-LIST GATE (2026-08-19). The ownership-rebase hand-lists are the editor's one
# recurring silent-reset factory: a field the Factory/Lab UI edits but the window's ownership list doesn't
# re-apply is thrown away on every Save (combatZ was drill-caught being reset to 0 the day it landed; the
# 2026-08-19 audit then verified the lists mechanically — this gate makes that audit PERMANENT instead of
# once). Mechanics mirror the audit exactly:
#   UI-edited fields  = every `cur.<field> = …EditorGUILayout/GUILayout…` assignment in the window
#   ownership list    = every `cur.<field> = <snapshot>.<field>` re-apply inside the window's rebase function
# A UI field missing from its list FAILS the gate, named. Fields set outside GUILayout calls (e.g. Browse's
# animUnitFix auto-guess, which is deliberately Lab-owned) are naturally out of scope — same rule the audit used.
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2

FACT=Assets/Scripts/Editor/ModelFactoryWindow.cs
LAB=Assets/Scripts/Editor/AnimationLabWindow.cs

ui_fields() {  # ui_fields <file> — fields the window's controls write into the form
  grep -oE 'cur\.[a-zA-Z0-9_]+ = [^;]*(EditorGUILayout|GUILayout)\.[A-Za-z]+' "$1" \
    | sed 's/cur\.\([a-zA-Z0-9_]*\).*/\1/' | sort -u
}
list_fields() {  # list_fields <file> <rebase-fn-signature> <snapshot-var> — fields the rebase re-applies
  sed -n "/$2/,/^    }/p" "$1" \
    | grep -oE "cur\.[a-zA-Z0-9_]+ = $3\.[a-zA-Z0-9_]+" \
    | sed 's/cur\.\([a-zA-Z0-9_]*\).*/\1/' | sort -u
}

fail=0
check() {  # check <label> <file> <rebase-fn> <snapshot-var>
  local ui covered missing
  ui=$(ui_fields "$2"); covered=$(list_fields "$2" "$3" "$4")
  if [ -z "$covered" ]; then
    echo "FAIL — $1: could not extract the ownership list (rebase function renamed? update this gate)."
    fail=1; return
  fi
  missing=$(comm -23 <(echo "$ui") <(echo "$covered"))
  if [ -n "$missing" ]; then
    echo "FAIL — $1: UI-edited field(s) MISSING from the ownership list (silently RESET on every Save):"
    echo "$missing" | sed 's/^/         /'
    echo "         Fix: add each to the re-apply block in $3 (see its MAINTENANCE TRAP comment)."
    fail=1
  else
    echo "ok   — $1: all $(echo "$ui" | grep -c .) UI-edited field(s) covered by the $(echo "$covered" | grep -c .)-field ownership list"
  fi
}

check "Factory"       "$FACT" "void RebaseLabOwnedOnRegistry" "form"
check "Animation Lab" "$LAB"  "void RebaseOnRegistry"         "mine"

# ── Vehicle Lab RECIPE round-trip (2026-08-20) — the same disease, different organ: a Recipe DTO field that
# SaveRecipe doesn't write or LoadRecipeFromPath doesn't restore is silently lost/leaked across a save-load
# cycle (the canoe's wave config vanished exactly this way and took GLB forensics to recover). Every field
# declared in `class Recipe` must appear as an LHS in SaveRecipe's initializer AND be read as `r.<field>` in
# LoadRecipeFromPath. ──
VLAB=Assets/Scripts/Editor/VehicleLabWindow.cs

recipe_fields() {  # every public field declared in the Recipe DTO
  sed -n '/\[Serializable\] class Recipe/,/^    }/p' "$VLAB" \
    | grep -v '^\s*//' | tr ';' '\n' \
    | grep -oE 'public [A-Za-z0-9_<>]+ [a-zA-Z0-9_, ]+' \
    | sed -E 's/public [A-Za-z0-9_<>]+ //' | tr ',' '\n' | sed 's/ //g' | grep -v '^$' | sort -u
}
save_fields() {  # LHS names in SaveRecipe's `new Recipe { … }` initializer
  sed -n '/var r = new Recipe/,/^        };/p' "$VLAB" \
    | grep -v '^\s*//' | grep -oE '(^|[{, ])[a-zA-Z0-9_]+ =' \
    | grep -oE '[a-zA-Z0-9_]+' | grep -v '^=$' | sort -u
}
load_fields() {  # every `r.<field>` the loader reads
  sed -n '/void LoadRecipeFromPath/,/^    }/p' "$VLAB" \
    | grep -v '^\s*//' | grep -oE '\br\.[a-zA-Z0-9_]+' | sed 's/^r\.//' | sort -u
}

rf=$(recipe_fields); sf=$(save_fields); lf=$(load_fields)
if [ -z "$rf" ] || [ -z "$sf" ] || [ -z "$lf" ]; then
  echo "FAIL — Vehicle Lab recipe: could not extract the DTO/save/load lists (structure changed? update this gate)."
  fail=1
else
  notsaved=$(comm -23 <(echo "$rf") <(echo "$sf"))
  notloaded=$(comm -23 <(echo "$rf") <(echo "$lf"))
  if [ -n "$notsaved" ] || [ -n "$notloaded" ]; then
    [ -n "$notsaved" ]  && { echo "FAIL — Vehicle Lab recipe: DTO field(s) NOT written by SaveRecipe (lost on save):";    echo "$notsaved"  | sed 's/^/         /'; }
    [ -n "$notloaded" ] && { echo "FAIL — Vehicle Lab recipe: DTO field(s) NOT restored by LoadRecipeFromPath (leak between models):"; echo "$notloaded" | sed 's/^/         /'; }
    fail=1
  else
    echo "ok   — Vehicle Lab recipe: all $(echo "$rf" | grep -c .) DTO field(s) round-trip through Save and Load"
  fi
fi

if [ "$fail" -eq 0 ]; then
  echo "PASS — hand-list gate: no UI-edited field can be silently reset by an ownership rebase, and every recipe field round-trips."
fi
exit "$fail"
