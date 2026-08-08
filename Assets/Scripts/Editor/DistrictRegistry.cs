// DistrictRegistry.cs (ENC editor) — the District Factory's config store: enc_districts.json in the game's
// BepInEx/config, read by the plugin's district repoint (UniversalInjectPatch.EnsureDistrictConfig). Mirrors
// ModelRegistry (same target dir, same corrupt-guard + atomic write + git-tracked project backup) but for DISTRICT
// models: each entry binds one district (ConstructibleDefinitionName) to one baked FxMesh GUID.
//
// The RUNTIME reads only { district, fxMeshGuid, isolate } (Newtonsoft JObject — extra fields ignored); everything
// else here is BAKE-TIME state so the window can reload + re-bake an entry with its knobs intact. Same JsonUtility
// caveat as ModelRegistry: the editor WRITES with JsonUtility, the plugin must keep parsing with Newtonsoft.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// One extra model composed onto the district tile at bake ("pizza topping"): baked with its own knobs, grounded to
// the BASE model's floor, placed by facing + posOffset, then merged into the entry's single mesh + super-atlas.
// Purely bake-time — the runtime still ships ONE FxMesh + ONE atlas per entry.
[Serializable]
public class DistrictPart
{
    public string modelFile = "";      // .glb/.gltf/.obj/.fbx/.blend for this part
    public float size = 2f;            // world length of the part's longest axis (its own Size knob)
    public Vector3 rotation;           // stand-it-up rotation offset, baked in the part's own import (same semantic as the entry's)
    public float facing = 0f;          // turn the part on the tile (deg about the vertical)
    public Vector3 posOffset;          // place it: X/Z slide across the tile, Y lifts off the base's floor
    public float alphaBoost = 1f;      // cutout-foliage fullness: multiplies the part's texture alpha at compose (1 = as authored). Sources authored for a LOW alpha cutoff (the beech: 0.227) erode to slivers against the game's fixed threshold — 2-3 restores full leaves.
    public float leafScale = 1f;       // GEOMETRY: scale every small disconnected triangle island (leaf cards) around its own centroid at compose. Texture tricks can't outgrow the card — this makes each leaf physically bigger. Trunk/big islands untouched (size-characteristic selection).
}

// One district model. `district` is the key (one custom model per district).
[Serializable]
public class DistrictDef
{
    public string district = "";       // ConstructibleDefinitionName to match (e.g. Extension_Base_BreederReactor) — RUNTIME
    public string fxMeshGuid = "";     // baked FxMesh Amplitude GUID "a,b,c,d" — RUNTIME
    public string atlasGuid = "";      // baked albedo atlas Amplitude GUID "a,b,c,d" — RUNTIME (texture injection; empty = untextured legacy entry)
    public string normalAtlasGuid = ""; // baked normal atlas GUID — RUNTIME (bound on _NormalMap instead of the neutral flat; empty = neutral)
    public string roughAtlasGuid = "";  // baked roughness atlas GUID — RUNTIME (bound on _RoughnessMap; empty = neutral matte)
    public bool isolate = true;        // true = private per-instance leaf (this district's tiles only); false = global culture-wide swap — RUNTIME

    // ---- bake-time knobs (runtime ignores; kept so re-bakes reload the same settings) ----
    public string resourceName = "";   // names the baked assets (<name>_ModelMesh / _DistrictMesh / _FxMesh)
    public string modelFile = "";      // .glb/.obj/.fbx/.blend; empty = re-bake the existing resource with new settings
    public Vector3 rotation;           // bake rotation offset (deg) on top of the auto longest-axis align — near-cubic models often need Y/Z (the reactor: Y=180, Z=90)
    public float size = 5f;            // world length of the model's longest axis (a district tile is ~10; ~5 imposing, ~2.5 tile-furniture)
    public int normalsMode = 1;        // 0 KeepModel, 1 Recalculate, 2 Faceted
    public float smoothingAngle = 20f;
    public int convertGrid = 0;        // GLB->OBJ: 0 = faithful (preserve UV seams), >0 = decimate
    public int targetTris = 24000;     // quadric-decimate ceiling; districts share the 'Visual' GPU buffer (see DistrictBufferHeadroom)
    public string stripParts = "";     // Blender: comma-separated object-name substrings to DELETE before baking
    public bool reuseExtracted = false; // reuse the extracted OBJ/albedo on re-bake (keeps hand-edited textures)
    public Vector3 importAngles = Vector3.zero;   // FxMesh draw-time rotation — LEGACY (pre-Facing entries keep theirs; composed at bake). No longer a UI control: new entries stand up via `rotation`, turn via `facing`. Vanilla's own district FxMeshes use (-90,0,0) (Z-up authoring), ours bake upright.
    public float facing = 0f;          // rotation ON the tile (deg, about the drawn-space vertical) — composed on top of importAngles at bake; the safe "turn the building" knob
    public Vector3 posOffset = Vector3.zero;      // position on the tile, drawn-space world units (X/Z across the tile — a tile is ~10 — Y lifts off the ground); applied AFTER auto-level at bake
    public Vector3 posOffsetBaked = Vector3.zero; // BAKE-STATE: the posOffset the current FxMesh carries — the preview shows posOffset edits live as a delta against this
    public float clipHexPct = 0f;                 // >0 = CLIP the mesh to the tile hex at bake (100 = the exact in-game cell, 6.93 across flats), so the model tiles like a vanilla district; 0 = off
    public int atlasMaxDim = 1024;                // packed-atlas resolution (was hardcoded 512 — ten 1024² source sheets crushed to ~160² each on the temple); districts render close-up, 1024-2048 is right for multi-material models
    public int sourceTris = -1;                   // BAKE-STATE: the SOURCE model's triangle count before decimation (parsed from the Blender prep; -1 = unknown / no reduce ran)
    public List<DistrictPart> parts = new List<DistrictPart>();   // extra models composed onto the tile at bake (see DistrictPart) — runtime ignores (it ships as one merged mesh)
}

[Serializable]
class DistrictRegistryFile
{
    public List<DistrictDef> districts = new List<DistrictDef>();
}

public static class DistrictRegistry
{
    // Same resolution as the unit registry: manual override > Steam auto-detect > fallback (all via ModelRegistry).
    public static string RegistryPath => Path.Combine(ModelRegistry.ConfigDir, "enc_districts.json");

    // Versioned shadow copy in the mod repo (Assets/Databases is git-tracked) — survives a game reinstall,
    // and Load() auto-restores from it if the live file goes missing. Mirrors enc_models.backup.json.
    public static string ProjectBackupPath => Path.Combine(Application.dataPath, "Databases", "enc_districts.backup.json");

    // Set when the last Load() found a file it couldn't parse; Save() refuses while set, so a corrupt /
    // half-edited registry is never silently replaced with a fresh empty list.
    static bool lastLoadCorrupt;

    static List<DistrictDef> Sort(List<DistrictDef> list)
    {
        list?.Sort((a, b) => string.Compare(a?.district, b?.district, StringComparison.OrdinalIgnoreCase));
        return list ?? new List<DistrictDef>();
    }

    public static List<DistrictDef> Load()
    {
        try
        {
            if (!File.Exists(RegistryPath))
            {
                lastLoadCorrupt = false;
                if (File.Exists(ProjectBackupPath))
                {
                    // parse the backup in its OWN try/catch (see ModelRegistry E6): an unreadable backup while the live
                    // file is missing must read as "no backup", not lock Save forever.
                    try
                    {
                        var backupJson = File.ReadAllText(ProjectBackupPath);
                        var b = JsonUtility.FromJson<DistrictRegistryFile>(backupJson);
                        if (b?.districts != null && b.districts.Count > 0)
                        {
                            try { Directory.CreateDirectory(ModelRegistry.ConfigDir); File.WriteAllText(RegistryPath, backupJson); } catch { }
                            Debug.Log($"[District] game district registry was missing — restored {b.districts.Count} entr{(b.districts.Count == 1 ? "y" : "ies")} from the project backup.");
                            return Sort(b.districts);
                        }
                    }
                    catch (Exception be) { Debug.LogWarning($"[District] the project backup '{ProjectBackupPath}' is unreadable ({be.Message}) — treating as no backup."); }
                }
                return new List<DistrictDef>();
            }
            var data = JsonUtility.FromJson<DistrictRegistryFile>(File.ReadAllText(RegistryPath));
            lastLoadCorrupt = false;
            return Sort(data?.districts ?? new List<DistrictDef>());
        }
        catch (Exception e)
        {
            lastLoadCorrupt = true;
            try { File.Copy(RegistryPath, RegistryPath + ".corrupt.json", true); } catch { }
            Debug.LogError($"[District] registry '{RegistryPath}' is unreadable ({e.Message}) — backed up to " +
                           $"'{Path.GetFileName(RegistryPath)}.corrupt.json'. Fix or delete it; baking won't save until then.");
            return new List<DistrictDef>();
        }
    }

    // True = written. False = nothing saved (corrupt-guard tripped, or the atomic write hit a lock) — surface it.
    public static bool Save(List<DistrictDef> districts)
    {
        if (lastLoadCorrupt)
        {
            Debug.LogError("[District] not saving: the existing district registry was unreadable (see the .corrupt.json backup). Fix or delete it first.");
            return false;
        }
        Sort(districts);
        var json = JsonUtility.ToJson(new DistrictRegistryFile { districts = districts }, true);
        try
        {
            Directory.CreateDirectory(ModelRegistry.ConfigDir);
            var tmp = RegistryPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(RegistryPath)) File.Replace(tmp, RegistryPath, null);
            else File.Move(tmp, RegistryPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[District] registry write FAILED — the model baked but its entry was NOT saved to '{RegistryPath}' ({e.Message}). " +
                           "Close whatever's locking it (AV, indexer, the running game) and re-bake.");
            return false;
        }
        try { File.WriteAllText(ProjectBackupPath, json); } catch (Exception e) { Debug.LogWarning("[District] project backup write failed: " + e.Message); }
        AssetDatabase.Refresh();
        return true;
    }

    public static bool Upsert(DistrictDef def)
    {
        var list = Load();
        list.RemoveAll(d => d.district == def.district);
        list.Add(def);
        return Save(list);
    }

    public static bool Remove(string district)
    {
        var list = Load();
        int before = list.Count;
        list.RemoveAll(d => d.district == district);
        if (list.Count == before) return false;
        return Save(list);
    }
}
