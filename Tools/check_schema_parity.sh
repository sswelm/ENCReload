#!/usr/bin/env bash
# Schema parity guard for the Model Factory registry.
#
# The registry (enc_models.json) is WRITTEN by the editor baker from ModelDef (ModelRegistry.cs, JsonUtility) and READ by
# the runtime plugin (UniversalInjectPatch.cs) two ways: the PRIMARY Newtonsoft object parse and a REGEX fallback. Those
# three field lists live in two repos and are hand-synced, so they drift silently — add a field to one, forget another,
# and a feature quietly dies at runtime with no error. This guard makes that drift LOUD. It checks:
#
#   1. N == R   — the Newtonsoft key set equals the regex-fallback key set (the two read paths can't diverge).
#   2. N ⊆ W   — every key the plugin reads is a ModelDef field the baker writes (minus a small runtime-only allowlist).
#   3. types    — for each shared key, the plugin's read cast matches ModelDef's declared type (bool/float/int/string/array/obj).
#   INFO        — ModelDef fields the plugin never reads (expected for bake-time-only knobs; eyeball for a forgotten one).
#
# Source-text comparison, no build coupling. Run before committing a registry-schema change:
#   Tools/check_schema_parity.sh [ENCReload_root] [ENCAccessProof_root]
set -u
RELOAD="${1:-/c/Repo/ENCReload}"
PROOF="${2:-/c/Repo/ENCAccessProof}"
DEF="$RELOAD/Assets/Scripts/Editor/ModelRegistry.cs"
PLUG="$PROOF/Patches/UniversalInjectPatch.cs"
[ -f "$DEF" ]  || { echo "MISSING: $DEF"; exit 2; }
[ -f "$PLUG" ] || { echo "MISSING: $PLUG"; exit 2; }

# Runtime-ONLY keys: intentional overrides the baker deliberately doesn't write (the user hand-edits them into the JSON).
# `scale` fixes a mis-scaled animated model without a re-bake. Add here ONLY for a conscious runtime-only override.
allow=" scale "

# Map a C# type to a one-letter JSON-shape code (how JsonUtility serializes it): S string, I int, F float, B bool,
# V object (Vector3), A array (int[]). Enums serialize as int.
canon() {
  case "$1" in
    string) echo S;; int) echo I;; float) echo F;; bool) echo B;;
    Vector3) echo V;; "int[]") echo A;; MaterialMode) echo I;;
    *) echo "?$1";;
  esac
}

# --- W: ModelDef serialized fields + types (the WRITE schema) ---
declare -A W
defbody=$(awk '/public class ModelDef/{f=1} /class (OverrideRef|RegistryFile)/{f=0} f' "$DEF")
while read -r ty nm; do
  [ -n "${nm:-}" ] && W["$nm"]=$(canon "$ty")
done < <(grep -oE 'public[[:space:]]+[A-Za-z0-9_]+(\[\])?[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*[=;]' <<<"$defbody" \
         | sed -E 's/public[[:space:]]+([A-Za-z0-9_]+(\[\])?)[[:space:]]+([A-Za-z_][A-Za-z0-9_]*).*/\1 \3/')

# --- N: keys the Newtonsoft path reads (every m["key"]) ---
N=$(grep -oE 'm\["[A-Za-z_][A-Za-z0-9_]*"\]' "$PLUG" | sed -E 's/m\["(.*)"\]/\1/' | sort -u)

# --- R: keys the regex fallback reads (first "key" of each Regex.Matches(text, ...)) ---
R=$(grep -oE 'Regex\.Matches\(text, "\\"[A-Za-z_][A-Za-z0-9_]*' "$PLUG" \
    | grep -oE '[A-Za-z_][A-Za-z0-9_]*$' | sort -u)

# How the Newtonsoft path READS a key -> one-letter shape code (skel/atlas/clip are int arrays, position is a Vector3).
ntype() {
  local K=$1 c
  case $K in skel|atlas|clip) echo A; return;; position) echo V; return;; esac
  c=$(grep -oE "\((string|bool\?|float|int)\)m\[\"$K\"\]" "$PLUG" | head -1 | sed -E 's/\(([a-z?]+)\).*/\1/')
  case $c in string) echo S;; "bool?") echo B;; float) echo F;; int) echo I;; *) echo "?";; esac
}

fail=0

# 1) N == R
onlyN=$(comm -23 <(echo "$N") <(echo "$R"))
onlyR=$(comm -13 <(echo "$N") <(echo "$R"))
if [ -n "$onlyN$onlyR" ]; then
  fail=1
  echo "FAIL — the two plugin read paths disagree (Newtonsoft vs regex fallback):"
  [ -n "$onlyN" ] && echo "  only in Newtonsoft: $(tr '\n' ' ' <<<"$onlyN")"
  [ -n "$onlyR" ] && echo "  only in regex     : $(tr '\n' ' ' <<<"$onlyR")"
  echo "  -> add the missing key to whichever path lacks it (both must parse every field)."
fi

# 2) N ⊆ W (+ allowlist) and 3) type match on shared keys
missing=""; typemismatch=""
for k in $N; do
  case "$allow" in *" $k "*) continue;; esac
  if [ -z "${W[$k]+x}" ]; then
    missing="$missing $k"
  else
    nt=$(ntype "$k"); wt=${W[$k]}
    [ "$nt" != "$wt" ] && typemismatch="$typemismatch $k(reads:$nt,writes:$wt)"
  fi
done
if [ -n "$missing" ]; then
  fail=1
  echo "FAIL — plugin reads key(s) the baker never writes:$missing"
  echo "  -> add the field to ModelDef (ModelRegistry.cs), fix the key name, or allowlist a runtime-only override."
fi
if [ -n "$typemismatch" ]; then
  fail=1
  echo "FAIL — type mismatch between the plugin's read cast and ModelDef's declared type:$typemismatch"
  echo "  -> (S string, I int, F float, B bool, V Vector3, A array). Align the cast or the field type."
fi

# 4) WRAPPER parity (HAF multi-mod): the plugin's top-level root["..."] reads must all be RegistryFile fields the baker
#    writes. These are per-FILE keys (modId/schemaVersion/dependsOn/loadAfter/overrides), a separate surface from the
#    per-model keys above — so they drift independently and need their own guard.
rfbody=$(awk '/class RegistryFile/{f=1} f&&/^}/{f=0} f' "$DEF")
WR=$(grep -oE 'public[[:space:]]+[A-Za-z0-9_<>]+[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*[=;]' <<<"$rfbody" \
     | sed -E 's/.*[[:space:]]([A-Za-z_][A-Za-z0-9_]*)[[:space:]]*[=;]/\1/' | sort -u)
NR=$(grep -oE 'root\["[A-Za-z_][A-Za-z0-9_]*"\]' "$PLUG" | sed -E 's/root\["(.*)"\]/\1/' | sort -u)
# `districts` is a FALSE MATCH of the root["..."] grep: that read parses enc_districts.json (its own file, written by
# DistrictRegistry.cs with its own schema), not the model registry this wrapper check guards.
wrapallow=" districts "
wrapmiss=""
for k in $NR; do
  case "$wrapallow" in *" $k "*) continue;; esac
  case " $(tr '\n' ' ' <<<"$WR") " in *" $k "*) ;; *) wrapmiss="$wrapmiss $k";; esac
done
if [ -n "$wrapmiss" ]; then
  fail=1
  echo "FAIL — plugin reads wrapper key(s) the baker never writes:$wrapmiss"
  echo "  -> add the field to RegistryFile (ModelRegistry.cs) or fix the key name in ParsePack (UniversalInjectPatch.cs)."
fi

# INFO: ModelDef fields never read at runtime (expected for bake-time-only knobs; scan for a genuinely-forgotten one).
unread=""
for k in "${!W[@]}"; do
  case " $(tr '\n' ' ' <<<"$N") " in *" $k "*) ;; *) unread="$unread $k";; esac
done

echo "Plugin reads (Newtonsoft): $(wc -w <<<"$N") keys"
echo "Plugin reads (regex)     : $(wc -w <<<"$R") keys"
echo "ModelDef writes          : ${#W[@]} fields"
echo "Wrapper reads (root)     : $(wc -w <<<"$NR") keys | RegistryFile writes: $(wc -w <<<"$WR") fields"
[ -n "$unread" ] && echo "INFO — ModelDef fields not read at runtime (bake-time-only, expected):$(echo "$unread" | tr ' ' '\n' | sort | tr '\n' ' ')"

if [ "$fail" -ne 0 ]; then exit 1; fi
echo "PASS — Newtonsoft == regex read paths, all read keys (model + wrapper) are written by the baker, and types agree."
