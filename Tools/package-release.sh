#!/usr/bin/env bash
# package-release.sh — assemble the ENCReload player download: the Humankind mod AND the HAF plugin that renders
# its custom units, in one reproducible artifact.
#
# WHY THIS EXISTS. The v0.1.0 HAF zip was assembled BY HAND, and its INSTALL.txt lived nowhere but inside that zip —
# the one artifact in a project that otherwise treats every deployed thing as derived-never-edited. Nobody could
# regenerate what shipped. This script is that artifact made reproducible.
#
# TWO PARTS, TWO DESTINATIONS, on purpose. The mod is a Humankind Community module; the plugin is a BepInEx DLL.
# They install to different roots and no single "extract here" covers both, so the zip has two clearly numbered
# top-level folders and Tools/release/READ_ME_FIRST.txt (TRACKED, not invented at zip time) says where each goes.
#
# THE GUARD THAT EARNS ITS KEEP: pack.json's skel/atlas/clip GUIDs point INTO the asset bundle. Ship a bundle older
# than the last bake and the player gets the two failures the District Factory already warns about at authoring time
# — "waiting for leaves to load..." forever, and scrambled textures from a mismatched mesh/atlas pair. Ship Status
# catches that in the editor; nothing caught it at PACKAGING time, which is where it reaches a player. Same rule as
# DistrictFactoryWindow's health check (newestBaked > newestBundle = STALE), and it excludes the bake-test fixtures
# by the same three prefixes the delete guard uses — a __feat_ fixture from a test run is newer than everything and
# would otherwise fail every package.
#
#   bash Tools/package-release.sh [--haf DIR] [--out DIR] [--no-zip]
set -uo pipefail
cd "$(dirname "$0")/.." || exit 2
ROOT="$(pwd)"

HAF="" ; OUT="$ROOT/Distribution/release" ; DOZIP=1
while [ $# -gt 0 ]; do
  case "$1" in
    --haf) HAF="$2"; shift 2;;
    --out) OUT="$2"; shift 2;;
    --no-zip) DOZIP=0; shift;;
    *) echo "unknown arg: $1"; exit 2;;
  esac
done
if [ -z "$HAF" ]; then
  for d in "$ROOT/../HumankindAssetFramework" "/c/Repo/HumankindAssetFramework"; do
    [ -f "$d/HumankindAssetFramework.csproj" ] && { HAF="$d"; break; }
  done
fi
[ -n "$HAF" ] && [ -f "$HAF/HumankindAssetFramework.csproj" ] || {
  echo "FAIL — plugin checkout not found. Pass --haf <path to HumankindAssetFramework>."; exit 2; }

fail() { echo "FAIL — $*"; exit 1; }

# ---- 1) the mod bundle: newest built ENCReload.<guid>.<version> ----
BUNDLE_SRC=$(ls -1d "$ROOT/Assets/AssetBundles/StandaloneWindows64/ENCReload."* 2>/dev/null | while read -r d; do
                [ -d "$d" ] && printf '%s\t%s\n' "$(stat -c %Y "$d")" "$d"; done | sort -rn | head -1 | cut -f2-)
[ -n "$BUNDLE_SRC" ] || fail "no built mod bundle under Assets/AssetBundles/StandaloneWindows64/ — build the mod first (haf build-mod)."
BASE=$(basename "$BUNDLE_SRC"); REST=${BASE#ENCReload.}; GUID=${REST%%.*}; VER=${REST#*.}
BUNDLE_FILE=$(ls -1 "$BUNDLE_SRC"/*.assetbundle 2>/dev/null | head -1)
[ -n "$BUNDLE_FILE" ] || fail "'$BASE' holds no .assetbundle — the build did not finish."

# ---- 2) STALE-BUNDLE GUARD (see header) ----
# newest BAKED asset, excluding the three bake-test fixture prefixes (BakeSmokeTest.PREFIX / BakeFeatureTest.Prefix /
# ConversionGateTest.PREFIX). Keep in step with those constants if a suite ever renames its fixtures.
NEWEST_BAKED=$(find "$ROOT/Assets/Resources" -type f ! -name '*.meta' \
                 ! -name '__feat_*' ! -name '__smoketest__*' ! -name '__convgate__*' \
                 -printf '%T@ %p\n' 2>/dev/null | sort -rn | head -1)
BUNDLE_T=$(stat -c %Y "$BUNDLE_FILE")
PACK_T=$(stat -c %Y "$ROOT/Assets/Pack/ENCReload/pack.json" 2>/dev/null || echo 0)
BAKED_T=${NEWEST_BAKED%% *}; BAKED_T=${BAKED_T%%.*}; BAKED_T=${BAKED_T:-0}
NEWEST_SRC=$BAKED_T; WHAT="a baked asset (${NEWEST_BAKED#* })"
[ "$PACK_T" -gt "$NEWEST_SRC" ] && { NEWEST_SRC=$PACK_T; WHAT="pack.json"; }
if [ "$NEWEST_SRC" -gt "$BUNDLE_T" ]; then
  echo "FAIL — STALE BUNDLE. $WHAT ($(date -d @"$NEWEST_SRC" '+%F %T')) is newer than the built mod bundle"
  echo "       ($(date -d @"$BUNDLE_T" '+%F %T')). The pack's GUIDs would point into assets this bundle does not"
  echo "       contain: in-game that is 'waiting for leaves to load...' forever, or a scrambled texture."
  echo "       REBUILD the mod (haf build-mod) and package again."
  exit 1
fi

# ---- 3) stage ----
STAGE="$OUT/ENCReload-$VER"
rm -rf "$STAGE"; mkdir -p "$STAGE" || fail "cannot create $STAGE"
GAMEDIR="$STAGE/1_Humankind_game_folder"
COMMDIR="$STAGE/2_Community_mods_folder"
mkdir -p "$GAMEDIR/BepInEx/plugins" "$GAMEDIR/BepInEx/config/haf_packs/ENCReload" "$COMMDIR"

# 3a) plugin — built fresh, so the zip can never carry a stale DLL the way the hand-made one did
echo "building the plugin (Release)…"
dotnet build "$HAF/HumankindAssetFramework.csproj" -c Release --nologo -v q >/dev/null || fail "plugin build failed."
for d in HumankindAssetFramework.dll Haf.Schema.dll; do
  [ -f "$HAF/bin/Release/$d" ] || fail "built, but '$d' is missing from $HAF/bin/Release."
  cp "$HAF/bin/Release/$d" "$GAMEDIR/BepInEx/plugins/$d"
done
# NOTE: only these two. bin/Release also holds System.*.dll reference facades — shipping those into BepInEx/plugins
# would have BepInEx try to load framework facades as plugins.

# 3b) the pack — pack.json + skins/ + sounds/ + CREDITS, minus Unity .meta files (not part of the pack)
( cd "$ROOT/Assets/Pack/ENCReload" && find . -type f ! -name '*.meta' -print0 \
    | while IFS= read -r -d '' f; do mkdir -p "$GAMEDIR/BepInEx/config/haf_packs/ENCReload/$(dirname "$f")"
        cp "$f" "$GAMEDIR/BepInEx/config/haf_packs/ENCReload/$f"; done )

# 3c) the mod — the 4 core files only (strip .meta and .assetbundle.txt, matching the CLI's deploy)
mkdir -p "$COMMDIR/$BASE"
for f in "$BUNDLE_SRC"/*; do
  case "$(basename "$f")" in *.meta|*.assetbundle.txt) continue;; esac
  cp "$f" "$COMMDIR/$BASE/"
done

# 3d) the instructions — from the TRACKED template, never invented here
TPL="$ROOT/Tools/release/READ_ME_FIRST.txt"
[ -f "$TPL" ] || fail "missing tracked template $TPL"
sed -e "s/@@VER@@/$VER/g" -e "s/@@GUID@@/$GUID/g" "$TPL" > "$STAGE/READ_ME_FIRST.txt"

# ---- 4) report + zip ----
echo
echo "=== staged: $STAGE ==="
printf '  version   %s\n  module    ENCReload.%s\n' "$VER" "$GUID"
printf '  plugin    %s (%s bytes)\n' "HumankindAssetFramework.dll" "$(stat -c %s "$GAMEDIR/BepInEx/plugins/HumankindAssetFramework.dll")"
printf '  pack      %s file(s)\n' "$(find "$GAMEDIR/BepInEx/config/haf_packs" -type f | wc -l)"
printf '  mod       %s file(s), %s\n' "$(find "$COMMDIR" -type f | wc -l)" "$(du -sh "$COMMDIR" | cut -f1)"
printf '  total     %s\n' "$(du -sh "$STAGE" | cut -f1)"

if [ "$DOZIP" -eq 1 ]; then
  ZIP="$OUT/ENCReload-$VER.zip"
  echo; echo "zipping → $ZIP (the bundle is ~250 MB and already compressed; using Fastest)…"
  rm -f "$ZIP"
  powershell.exe -NoProfile -NonInteractive -Command \
    "Compress-Archive -Path '$(cygpath -w "$STAGE")\*' -DestinationPath '$(cygpath -w "$ZIP")' -CompressionLevel Fastest" \
    || fail "Compress-Archive failed."
  printf 'PASS — %s (%s)\n' "$ZIP" "$(du -h "$ZIP" | cut -f1)"
else
  echo; echo "PASS — staged only (--no-zip)."
fi
