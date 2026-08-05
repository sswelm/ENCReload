// FormationRegistry.cs (HAF editor) — the Formation Override window's config store: enc_formations.json in the game's
// BepInEx/config, read by the plugin's FormationOverride (Patches/FormationOverridePatch.cs). Mirrors DistrictRegistry
// (same target dir, same corrupt-guard + atomic write + git-tracked project backup) but for FORMATION links: each
// entry binds one unit (PresentationUnitDefinition name) to one formation, carrying the formation's FULL data —
// dummy positions, the per-orientation coordinate grids AND the six hidden ColumnsCountPerRow arrays — so the plugin
// can rebuild the PresentationFormationDefinition at runtime without the asset ever entering a bundle (a bundled
// formation never reaches the game's datatable system; by-name injection through Database.Add does).
//
// The RUNTIME reads { unit, formation, lowSpec, dummies[{position,coords}], columns0..5 } (Newtonsoft JObject —
// extra fields ignored); `sourceAsset` is EDITOR-ONLY state so the window can re-read a formation after edits.
// Same JsonUtility caveat as ModelRegistry: the editor WRITES with JsonUtility, the plugin parses with Newtonsoft.
// (Coordinates use our own GridCell {x,y} instead of Vector2Int — JsonUtility would serialize Vector2Int's private
// m_X/m_Y backing fields and the plugin would read zeros.)

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[Serializable]
public class GridCell { public int x; public int y; }   // one (row, column) cell — six per dummy, one per hex orientation

[Serializable]
public class FormationDummy
{
    public Vector3 position;                              // local position inside the tile (y stays 0)
    public List<GridCell> coords = new List<GridCell>();  // CoordinatePerDirection: exactly 6 entries (row=x, column=y)
}

// Formation-by-size row (unit links only): when the unit's EFFECTIVE scale (Resize Lab rule x Global Era Lab cell)
// drops to <= threshold, the unit swaps to `formation`. First matching row wins (sorted ascending on Save).
[Serializable]
public class SizeFormation
{
    public float threshold = 0.3f;
    public string formation = "";
}

// One entry = either a unit LINK (`unit` set: repoint that unit to `formation`) or a MACRO REPLACEMENT
// (`unit` EMPTY: overwrite the formation named `formation` in the live database with this data — every unit of
// every mod that references that name inherits the new layout; per-unit links still overrule it).
[Serializable]
public class FormationLink
{
    public string unit = "";        // PresentationUnitDefinition name to repoint (e.g. Era5_Common_Riflemen) — RUNTIME. EMPTY = macro replacement entry.
    public string formation = "";   // formation name injected into the live database (must be unique vs vanilla) — RUNTIME
    public string lowSpec = "Formation_1";   // low-spec graphics fallback formation (vanilla default) — RUNTIME
    public float dummyOffset = -1f;   // RUNTIME: override the unit's random per-model jitter (CoordinationValues.DummyOffsetPosition). -1 = leave vanilla; 0 = perfectly on the grid; small (e.g. 0.05) = tightly packed. No rebuild.
    public float scale = -1f;         // RUNTIME: formation scale multiplier — scales the MODELS (pawn root localScale) AND the dummy spacing together (the natural reading). -1 or 1 = vanilla; 0.7 = smaller+tighter; >1 = larger. Uniform only. No rebuild.
    public float layoutScale = -1f;   // RUNTIME: optional FOOTPRINT-only multiplier on the dummy positions. -1 = follow `scale`; set explicitly to decouple spacing from model size (e.g. small men on a wide skirmish line). No rebuild.
    public string scaleMode = "transform";   // RUNTIME: how `scale` is applied. "transform" = pawn root localScale (simple, decent on bodies/vehicles; rigid gear mis-anchors on humans). "data" = cloned skeleton with scaled binds + meshes (deep path; humans still WIP — procedural bone layers ignore it).
    public float turnRate = 0f;       // RUNTIME (unit links only): TURN EASE for this unit — eased facing on move/attack heading changes at this rate (deg/s) instead of the engine snap, and its map bombard WAITS for the pivot (muzzle/sound/shell/recoil hold until aligned). 0 = vanilla. Works on VANILLA units — the per-unit route to docs/Turn-Ease.md. No rebuild.
    public List<SizeFormation> sizeFormations = new List<SizeFormation>();   // RUNTIME (unit links only): era-ageing formation swaps — first row with threshold >= effective scale wins; empty = never swap
    public List<FormationDummy> dummies = new List<FormationDummy>();   // dummy count = pawn count at full health — RUNTIME
    public List<int> columns0 = new List<int>();   // ColumnsCountPerRow0..5: columns per row, one array per orientation — RUNTIME
    public List<int> columns1 = new List<int>();
    public List<int> columns2 = new List<int>();
    public List<int> columns3 = new List<int>();
    public List<int> columns4 = new List<int>();
    public List<int> columns5 = new List<int>();

    // ---- editor-only (runtime ignores) ----
    public string sourceAsset = "";      // project path of the formation asset this data was read from (re-read after edits)
    public string sourceFormation = "";  // sub-asset name the data came from — may differ from `formation` on a macro replacement (data from your _19 asset, target name a vanilla one)
}

[Serializable]
class FormationRegistryFile
{
    public List<FormationLink> links = new List<FormationLink>();
}

public static class FormationRegistry
{
    public static string RegistryPath => Path.Combine(ModelRegistry.ConfigDir, "enc_formations.json");

    // Versioned shadow copy in the mod repo (Assets/Databases is git-tracked) — survives a game reinstall,
    // and Load() auto-restores from it if the live file goes missing. Mirrors enc_districts.backup.json.
    public static string ProjectBackupPath => Path.Combine(Application.dataPath, "Databases", "enc_formations.backup.json");

    static bool lastLoadCorrupt;

    static List<FormationLink> Sort(List<FormationLink> list)
    {
        list?.Sort((a, b) => string.Compare(a?.unit, b?.unit, StringComparison.OrdinalIgnoreCase));
        return list ?? new List<FormationLink>();
    }

    public static List<FormationLink> Load()
    {
        try
        {
            if (!File.Exists(RegistryPath))
            {
                lastLoadCorrupt = false;
                if (File.Exists(ProjectBackupPath))
                {
                    try
                    {
                        var backupJson = File.ReadAllText(ProjectBackupPath);
                        var b = JsonUtility.FromJson<FormationRegistryFile>(backupJson);
                        if (b?.links != null && b.links.Count > 0)
                        {
                            try { Directory.CreateDirectory(ModelRegistry.ConfigDir); File.WriteAllText(RegistryPath, backupJson); } catch { }
                            Debug.Log($"[Formation] game formation registry was missing — restored {b.links.Count} link{(b.links.Count == 1 ? "" : "s")} from the project backup.");
                            return Sort(b.links);
                        }
                    }
                    catch (Exception be) { Debug.LogWarning($"[Formation] the project backup '{ProjectBackupPath}' is unreadable ({be.Message}) — treating as no backup."); }
                }
                return new List<FormationLink>();
            }
            var data = JsonUtility.FromJson<FormationRegistryFile>(File.ReadAllText(RegistryPath));
            lastLoadCorrupt = false;
            return Sort(data?.links ?? new List<FormationLink>());
        }
        catch (Exception e)
        {
            lastLoadCorrupt = true;
            try { File.Copy(RegistryPath, RegistryPath + ".corrupt.json", true); } catch { }
            Debug.LogError($"[Formation] registry '{RegistryPath}' is unreadable ({e.Message}) — backed up to " +
                           $"'{Path.GetFileName(RegistryPath)}.corrupt.json'. Fix or delete it; saving won't work until then.");
            return new List<FormationLink>();
        }
    }

    // True = written. False = nothing saved (corrupt-guard tripped, or the atomic write hit a lock) — surface it.
    public static bool Save(List<FormationLink> links)
    {
        if (lastLoadCorrupt)
        {
            Debug.LogError("[Formation] not saving: the existing formation registry was unreadable (see the .corrupt.json backup). Fix or delete it first.");
            return false;
        }
        Sort(links);
        var json = JsonUtility.ToJson(new FormationRegistryFile { links = links }, true);
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
            Debug.LogError($"[Formation] registry write FAILED — the link was NOT saved to '{RegistryPath}' ({e.Message}). " +
                           "Close whatever's locking it (AV, indexer, the running game) and save again.");
            return false;
        }
        try { File.WriteAllText(ProjectBackupPath, json); } catch (Exception e) { Debug.LogWarning("[Formation] project backup write failed: " + e.Message); }
        AssetDatabase.Refresh();
        return true;
    }

    // Entry identity: unit links key on the unit (one formation per unit); macro replacements key on the
    // TARGET formation name (one macro per formation). Without this, saving a second macro would
    // wipe every other replacement (all sharing unit == "").
    public static string KeyOf(FormationLink l) =>
        string.IsNullOrEmpty(l?.unit) ? "formation:" + (l?.formation ?? "") : "unit:" + l.unit;

    public static bool Upsert(FormationLink link)
    {
        var list = Load();
        var key = KeyOf(link);
        list.RemoveAll(l => KeyOf(l) == key);
        list.Add(link);
        return Save(list);
    }

    public static bool Remove(FormationLink link)
    {
        var list = Load();
        var key = KeyOf(link);
        int before = list.Count;
        list.RemoveAll(l => KeyOf(l) == key);
        if (list.Count == before) return false;
        return Save(list);
    }
}
