// FormationOverrideWindow.cs (HAF editor) — the FORMATION Override dialog (Tools ▸ HAF ▸ Formation Override).
// Links a UNIT (PresentationUnitDefinition name) to a CUSTOM formation asset authored in this project (extract a
// vanilla PresentationFormationDefinition into Assets/Databases/UnitFormation, duplicate it, edit dummies in the
// SDK's visual inspector), changing how many pawn models the unit displays: pawn count = ceil(health% × dummies).
//
// No bake, no bundle: a formation asset shipped in the mod bundle would never enter the game's datatable system
// (unit references resolve BY NAME against the live database). Instead this window serializes the formation's FULL
// data into enc_formations.json — dummy positions, the 6 per-orientation coordinate grids AND the six HIDDEN
// ColumnsCountPerRow arrays (invisible in the Inspector; historically the reason hand-made formations crashed the
// load with the misleading "mismatched mods" dialog) — and the plugin rebuilds + injects it at runtime through the
// public Database.Add, then repoints the unit. Consistency is VALIDATED here before anything can be saved.
//
// Picking a formation name that already exists in the game's database (e.g. a vanilla Formation_Close_9) makes the
// entry a pure repoint: the plugin links the unit to the existing formation and injects nothing.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class FormationOverrideWindow : EditorWindow
{
    [MenuItem("Tools/HAF/Formation Override")]
    static void Open() => GetWindow<FormationOverrideWindow>("Formation Override");

    List<FormationLink> all = new List<FormationLink>();
    string[] existing = { "<New>" };
    int selected;
    FormationLink cur = new FormationLink();
    string status = "";
    Vector2 scroll;

    void OnEnable() => RefreshList();

    void RefreshList()
    {
        all = FormationRegistry.Load();
        existing = new[] { "<New>" }.Concat(all.Select(l => $"{l.unit}  →  {l.formation}")).ToArray();
    }

    void OnSelect()
    {
        cur = selected > 0 && selected <= all.Count
            ? JsonUtility.FromJson<FormationLink>(JsonUtility.ToJson(all[selected - 1]))   // edit a COPY
            : new FormationLink();
        status = "";
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            int sel = EditorGUILayout.Popup("Formation link", selected, existing);
            if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshList();
            using (new EditorGUI.DisabledScope(selected <= 0))
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    var unit = selected > 0 && selected <= all.Count ? all[selected - 1].unit : null;   // key on the SELECTED entry
                    if (!string.IsNullOrEmpty(unit) &&
                        EditorUtility.DisplayDialog("Remove formation link",
                            $"Remove the formation link for '{unit}'? The unit shows its vanilla formation again on next launch.",
                            "Remove", "Cancel"))
                    {
                        bool removed = FormationRegistry.Remove(unit);
                        selected = 0; cur = new FormationLink(); RefreshList(); GUI.FocusControl(null);
                        status = removed ? $"Removed the link for '{unit}'." : $"'{unit}' was not in the registry — nothing removed.";
                    }
                }
            if (sel != selected) { selected = sel; OnSelect(); GUI.FocusControl(null); }
        }
        EditorGUILayout.Space();

        // ---- unit ----
        using (new EditorGUILayout.HorizontalScope())
        {
            cur.unit = EditorGUILayout.TextField(new GUIContent("Unit",
                "The unit's PresentationUnitDefinition name — e.g. Era5_Common_Riflemen. The plugin repoints this " +
                "definition's formation reference at load. Type it, or Pick from the definitions found in the project."),
                cur.unit);
            var units = GatherUnitNames();
            using (new EditorGUI.DisabledScope(units.Length == 0))
                if (GUILayout.Button(new GUIContent("Pick", units.Length == 0 ? "No PresentationUnitDefinition assets in the project — type the name" : null), GUILayout.Width(70)))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    new StringDropdown(new AdvancedDropdownState(), units, units, "Units", n => { cur.unit = n; Repaint(); }).Show(r);
                }
        }

        // ---- formation ----
        using (new EditorGUILayout.HorizontalScope())
        {
            cur.formation = EditorGUILayout.TextField(new GUIContent("Formation",
                "The formation to link. Pick a PresentationFormationDefinition asset from the project to capture its " +
                "data (injected at runtime), or type the name of a formation that already exists in the game's database " +
                "(e.g. Formation_Close_9) for a pure repoint with no custom data."), cur.formation);
            var formations = GatherFormations();
            var labels = formations.Keys.ToArray();
            using (new EditorGUI.DisabledScope(labels.Length == 0))
                if (GUILayout.Button(new GUIContent("Pick", labels.Length == 0 ? "No PresentationFormationDefinition assets in the project — type a name for a pure repoint" : null), GUILayout.Width(70)))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    new StringDropdown(new AdvancedDropdownState(), labels, labels, "Formations", n =>
                    {
                        if (ExtractFormation(formations[n], n, cur)) status = $"Read '{n}' from {formations[n]}.";
                        Repaint();
                    }).Show(r);
                }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(cur.sourceAsset) || string.IsNullOrEmpty(cur.formation)))
                if (GUILayout.Button(new GUIContent("Re-read", "Re-extract the formation data from its source asset — press after editing dummies in the Inspector."), GUILayout.Width(70)))
                    status = ExtractFormation(cur.sourceAsset, cur.formation, cur)
                        ? $"Re-read '{cur.formation}' from {cur.sourceAsset}."
                        : $"Could not re-read '{cur.formation}' from '{cur.sourceAsset}' — pick it again.";
        }

        // ---- captured data summary + validation ----
        EditorGUILayout.Space();
        string error = Validate(cur);
        if (cur.dummies.Count > 0)
        {
            EditorGUILayout.LabelField("Captured formation data", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Dummies (pawns at full health): {cur.dummies.Count}");
            EditorGUILayout.LabelField($"Rows per orientation: {string.Join(" / ", Enumerable.Range(0, 6).Select(i => Columns(cur, i).Count.ToString()).ToArray())}");
            EditorGUILayout.LabelField($"Low-spec fallback: {(string.IsNullOrEmpty(cur.lowSpec) ? "Formation_1 (default)" : cur.lowSpec)}");
            if (error == null && cur.dummies.Count > 10)
                EditorGUILayout.HelpBox($"{cur.dummies.Count} dummies exceeds the vanilla Formation3D dummy pool (vanilla's biggest formation has 10). " +
                                        "The plugin grows the pool automatically before it is cloned — just make sure you run a plugin build that has the " +
                                        "Formation axis (check the log for '[Formation] Formation3DPrefab dummy pool extended').", MessageType.Info);
        }
        else if (!string.IsNullOrEmpty(cur.formation))
        {
            EditorGUILayout.HelpBox("No formation data captured — this saves as a PURE REPOINT: the plugin links the unit to a formation " +
                                    "already in the game's database with this exact name. For a custom formation, Pick its project asset instead.", MessageType.Info);
        }
        if (error != null)
            EditorGUILayout.HelpBox("Formation data INVALID — " + error + "\nThe game would throw during load (the misleading \"mismatched mods\" " +
                                    "dialog). Fix the asset in the Inspector, then Re-read.", MessageType.Error);

        // ---- save ----
        EditorGUILayout.Space();
        bool canSave = !string.IsNullOrWhiteSpace(cur.unit) && !string.IsNullOrWhiteSpace(cur.formation) && error == null;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!canSave))
                if (GUILayout.Button("Save link", GUILayout.Height(34)))
                {
                    cur.unit = cur.unit.Trim(); cur.formation = cur.formation.Trim();
                    // ALWAYS re-read the source asset first so edits made in the Inspector since Pick are captured — the
                    // window only DISPLAYS the dummy data (the asset is the single source of truth), so a Save that wrote
                    // the Pick-time cache silently shipped stale positions/coords ("as if the save had no effect").
                    string staleWarn = null;
                    if (!string.IsNullOrEmpty(cur.sourceAsset))
                    {
                        if (!ExtractFormation(cur.sourceAsset, cur.formation, cur))
                            staleWarn = $" (⚠ could not re-read '{cur.formation}' from its asset — saved the last-read data; re-Pick it)";
                    }
                    string reErr = Validate(cur);   // re-validate the freshly-read data before it can be written
                    if (reErr != null)
                    {
                        status = $"NOT saved — re-read gave invalid data: {reErr}";
                    }
                    else
                    {
                        bool saved = FormationRegistry.Upsert(cur);
                        RefreshList();
                        selected = Array.IndexOf(existing, $"{cur.unit}  →  {cur.formation}"); if (selected < 0) selected = 0;
                        status = saved
                            ? $"Saved: '{cur.unit}' → '{cur.formation}'" + (cur.dummies.Count > 0 ? $" ({cur.dummies.Count} pawns at full health)." : " (pure repoint).") + (staleWarn ?? "")
                            : "REGISTRY SAVE FAILED (see Console).";
                    }
                }
            if (GUILayout.Button("Reset", GUILayout.Height(34), GUILayout.Width(72))) { cur = new FormationLink(); selected = 0; status = ""; GUI.FocusControl(null); }
        }
        if (!canSave && error == null)
            EditorGUILayout.HelpBox("Set Unit and Formation to save.", MessageType.Warning);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);

        EditorGUILayout.HelpBox(
            "Workflow: extract a vanilla formation into Assets/Databases/UnitFormation (Database Browser), duplicate + edit it in the " +
            "Inspector (the SDK preview shows the layout), then Pick it here, Pick the unit, Save. No mod rebuild needed — the plugin " +
            "reads the registry at game launch.\n" +
            "• Pawn count on the map scales with health: ceil(health% × dummies).\n" +
            "• The hidden per-orientation grids are captured and validated here — the historical crash cause for hand-made formations.\n" +
            "• Plugin prerequisite: [Formations] FormationOverride = true (default ON).\n" +
            "Registry: " + FormationRegistry.RegistryPath, MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open config folder", GUILayout.Width(150)))
                EditorUtility.RevealInFinder(System.IO.File.Exists(FormationRegistry.RegistryPath)
                    ? FormationRegistry.RegistryPath : ModelRegistry.ConfigDir);
            GUILayout.Label("↑ enc_formations.json + the plugin .cfg", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    static List<int> Columns(FormationLink l, int i) =>
        i == 0 ? l.columns0 : i == 1 ? l.columns1 : i == 2 ? l.columns2 : i == 3 ? l.columns3 : i == 4 ? l.columns4 : l.columns5;

    // Same rules the game's BuildDummiesGrid enforces by crashing (and the plugin re-checks before injecting). Null = valid.
    static string Validate(FormationLink l)
    {
        int n = l.dummies.Count;
        if (n == 0) return null;   // pure repoint
        foreach (var d in l.dummies)
            if (d.coords.Count != 6) return $"a dummy has {d.coords.Count} orientation coordinates (need 6)";
        for (int i = 0; i < 6; i++)
        {
            var cols = Columns(l, i);
            if (cols == null || cols.Count == 0) return $"ColumnsCountPerRow{i} is empty (the hidden grid arrays weren't authored — " +
                "duplicate an existing formation asset instead of creating one from scratch, or fill them via the Inspector's Debug mode)";
            int total = 0; foreach (var c in cols) total += c;
            if (total != n) return $"ColumnsCountPerRow{i} cells ({total}) != dummy count ({n})";
            var seen = new HashSet<int>();
            foreach (var d in l.dummies)
            {
                var c = d.coords[i];
                if (c.x < 0 || c.x >= cols.Count) return $"orientation {i}: row {c.x} out of range (rows={cols.Count})";
                if (c.y < 0 || c.y >= cols[c.x]) return $"orientation {i}: column {c.y} out of range (row {c.x} has {cols[c.x]} columns)";
                if (!seen.Add(c.x * 4096 + c.y)) return $"orientation {i}: duplicate cell ({c.x},{c.y})";
            }
        }
        return null;
    }

    // Read the formation's data off the asset via SerializedObject — no compile-time Amplitude type references,
    // and it reaches the [HideInInspector] ColumnsCountPerRow0..5 the Inspector never shows.
    static bool ExtractFormation(string assetPath, string formationName, FormationLink into)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (o == null || o.GetType().Name != "PresentationFormationDefinition" || o.name != formationName) continue;
            var so = new SerializedObject(o);
            into.formation = formationName;
            into.sourceAsset = assetPath;
            into.lowSpec = so.FindProperty("LowSpecFormationDefinition")?.FindPropertyRelative("serializableElementName")?.stringValue ?? "";
            into.dummies = new List<FormationDummy>();
            var dArr = so.FindProperty("Dummies");
            for (int i = 0; i < (dArr != null ? dArr.arraySize : 0); i++)
            {
                var el = dArr.GetArrayElementAtIndex(i);
                var fd = new FormationDummy { position = el.FindPropertyRelative("Position")?.vector3Value ?? Vector3.zero };
                var cp = el.FindPropertyRelative("CoordinatePerDirection");
                for (int j = 0; j < (cp != null ? cp.arraySize : 0); j++)
                {
                    var v = cp.GetArrayElementAtIndex(j).vector2IntValue;
                    fd.coords.Add(new GridCell { x = v.x, y = v.y });
                }
                into.dummies.Add(fd);
            }
            for (int i = 0; i < 6; i++)
            {
                var list = new List<int>();
                var p = so.FindProperty("ColumnsCountPerRow" + i);
                for (int j = 0; j < (p != null ? p.arraySize : 0); j++) list.Add(p.GetArrayElementAtIndex(j).intValue);
                switch (i)
                {
                    case 0: into.columns0 = list; break;
                    case 1: into.columns1 = list; break;
                    case 2: into.columns2 = list; break;
                    case 3: into.columns3 = list; break;
                    case 4: into.columns4 = list; break;
                    case 5: into.columns5 = list; break;
                }
            }
            return true;
        }
        return false;
    }

    // Every PresentationUnitDefinition name found in the project databases (sub-assets of the collection assets).
    // NOT cached: the user extracts new definitions from the archives while the window is open.
    static string[] GatherUnitNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in AssetDatabase.FindAssets("PresentationUnitDefinition"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asset")) continue;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o != null && o.GetType().Name == "PresentationUnitDefinition" && !string.IsNullOrEmpty(o.name))
                    names.Add(o.name);
        }
        return names.ToArray();
    }

    // Every PresentationFormationDefinition in the project (e.g. the user's Assets/Databases/UnitFormation extraction),
    // name -> asset path. The collection asset itself (…DefinitionCollection) is excluded by the exact type-name match.
    static Dictionary<string, string> GatherFormations()
    {
        var map = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in AssetDatabase.FindAssets("PresentationFormationDefinition"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asset")) continue;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o != null && o.GetType().Name == "PresentationFormationDefinition" && !string.IsNullOrEmpty(o.name))
                    map[o.name] = path;
        }
        return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
    }
}
