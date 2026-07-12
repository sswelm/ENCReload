#!/usr/bin/env bash
# Schema parity guard for the Model Factory registry.
#
# The registry (enc_models.json) is WRITTEN by the editor baker from ModelDef (ModelRegistry.cs, JsonUtility) and READ by
# the runtime plugin into ModelEntry (UniversalInjectPatch.cs, Newtonsoft). They live in two repos and are kept in sync by
# hand. This checks the one invariant that actually breaks things: every registry key the PLUGIN reads must be a field the
# BAKER writes (plugin keys ⊆ ModelDef fields) — else the plugin silently reads a default and a feature quietly dies.
#
# It's a source-text comparison (no build coupling). Run before committing a registry-schema change.
#   Tools/check_schema_parity.sh [ENCReload_root] [ENCAccessProof_root]
set -u
RELOAD="${1:-/c/Repo/ENCReload}"
PROOF="${2:-/c/Repo/ENCAccessProof}"
DEF="$RELOAD/Assets/Scripts/Editor/ModelRegistry.cs"
PLUG="$PROOF/Patches/UniversalInjectPatch.cs"
[ -f "$DEF" ]  || { echo "MISSING: $DEF"; exit 2; }
[ -f "$PLUG" ] || { echo "MISSING: $PLUG"; exit 2; }

# Keys the plugin READS = every m["key"] access in the registry loader (JsonUtility writes each ModelDef field under its
# own name, so a plugin key must match a ModelDef field name).
plugin=$(grep -oE 'm\["[A-Za-z_][A-Za-z0-9_]*"\]' "$PLUG" \
         | sed -E 's/m\["(.*)"\]/\1/' | sort -u)

# Known plugin-ONLY keys: intentional runtime overrides the baker deliberately does NOT write — the user hand-edits them
# into enc_models.json. `scale` fixes a mis-scaled animated model without a re-bake (see ModelEntry.scale comment). Add a
# key here ONLY when it's a conscious runtime-only override, never to silence an accidental desync.
allow=" scale "

# For each plugin key, check the baker declares a public ModelDef field of that name (`public <type> <key>[ =;]`).
missing=""
for k in $plugin; do
  case "$allow" in *" $k "*) continue;; esac
  if ! grep -qE "public[[:space:]]+[A-Za-z0-9_<>,.[:space:]]*\[?\]?[[:space:]]+$k[[:space:]]*[=;]" "$DEF"; then
    missing="$missing $k"
  fi
done

echo "Plugin reads keys     : $(wc -w <<<"$plugin")  ->  $(tr '\n' ' ' <<<"$plugin")"
if [ -n "$missing" ]; then
  echo "FAIL — plugin reads key(s) the baker never writes:$missing"
  echo "  -> add the field to ModelDef (ModelRegistry.cs) or fix the key name in UniversalInjectPatch.cs"
  exit 1
fi
echo "PASS — every registry key the plugin reads is a ModelDef field the baker writes."
