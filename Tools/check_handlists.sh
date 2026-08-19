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

if [ "$fail" -eq 0 ]; then
  echo "PASS — hand-list gate: no UI-edited field can be silently reset by an ownership rebase."
fi
exit "$fail"
