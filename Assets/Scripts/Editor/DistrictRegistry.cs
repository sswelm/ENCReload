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

// One district model. `district` is the key (one custom model per district).
[Serializable]
public class DistrictDef
{
    public string district = "";       // ConstructibleDefinitionName to match (e.g. Extension_Base_BreederReactor) — RUNTIME
    public string fxMeshGuid = "";     // baked FxMesh Amplitude GUID "a,b,c,d" — RUNTIME
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
    public Vector3 importAngles = new Vector3(-90f, 0f, 0f);   // FxMesh draw-time rotation; (-90,0,0) = vanilla upright default
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
