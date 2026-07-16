// DistrictFactoryWindow.cs (ENC editor) — the DISTRICT Factory dialog (Tools ▸ ENC ▸ District Factory). The district
// counterpart of ModelFactoryWindow: pick a district + a model file, set the bake knobs, press Bake — it runs the same
// static bake core (UniversalBaker.Build; pawnDescription empty, districts don't use one), wraps the result as a
// bone-free FxMesh (DistrictBaker.BakeFxMesh — the district shader is STATIC, a rigged mesh draws nothing), and writes
// the enc_districts.json entry the plugin's district repoint reads. No dummy pawn, no donor, no skeleton wiring.
//
// Runtime prerequisites (docs/District-Visuals.md): the district definition needs a RENDERABLE ConstructibleVisualAffinity
// and CLEARED Additional Visual Levels (data edit in this project), and the plugin's [District] DistrictRepoint = true.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class DistrictFactoryWindow : EditorWindow
{
    [MenuItem("Tools/ENC/District Factory")]
    static void Open() => GetWindow<DistrictFactoryWindow>("District Factory");

    List<DistrictDef> all = new List<DistrictDef>();
    string[] existing = { "<New>" };
    int selected;
    DistrictDef cur = new DistrictDef();
    string status = "";
    Vector2 scroll;

    void OnEnable() => RefreshList();

    void RefreshList()
    {
        all = DistrictRegistry.Load();
        existing = new[] { "<New>" }.Concat(all.Select(d => d.district)).ToArray();
    }

    void OnSelect()
    {
        cur = selected > 0 && selected <= all.Count
            ? JsonUtility.FromJson<DistrictDef>(JsonUtility.ToJson(all[selected - 1]))   // edit a COPY so Cancel/Reset doesn't mutate the list
            : new DistrictDef();
        status = "";
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            int sel = EditorGUILayout.Popup("District model", selected, existing);
            if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshList();
            using (new EditorGUI.DisabledScope(selected <= 0))
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    // key on the SELECTED entry, not the (possibly edited) text field — same E2 pitfall as the unit window
                    var name = selected > 0 && selected < existing.Length ? existing[selected] : null;
                    if (!string.IsNullOrEmpty(name) &&
                        EditorUtility.DisplayDialog("Remove district model",
                            $"Remove '{name}' from the district registry? The plugin will stop swapping its mesh on next launch. " +
                            "(The baked FxMesh assets stay in the project.)", "Remove", "Cancel"))
                    {
                        bool removed = DistrictRegistry.Remove(name);
                        selected = 0; cur = new DistrictDef(); RefreshList(); GUI.FocusControl(null);
                        status = removed ? $"Removed '{name}' from the district registry." : $"'{name}' was not in the registry — nothing removed.";
                    }
                }
            if (sel != selected) { selected = sel; OnSelect(); GUI.FocusControl(null); }
        }
        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            cur.district = EditorGUILayout.TextField(new GUIContent("District",
                "The district's ConstructibleDefinitionName — e.g. Extension_Base_BreederReactor. The plugin matches the " +
                "on-map district by this name. Remember the DATA side: the definition needs a renderable " +
                "ConstructibleVisualAffinity and CLEARED Additional Visual Levels, or nothing renders at all."), cur.district);
            var districts = GatherDistrictNames();
            using (new EditorGUI.DisabledScope(districts.Length == 0))
                if (GUILayout.Button(new GUIContent("Pick", districts.Length == 0 ? "No district definitions found in the project databases — type the name" : null), GUILayout.Width(70)))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    new StringDropdown(new AdvancedDropdownState(), districts, districts, "Districts", n =>
                    {
                        cur.district = n;
                        if (string.IsNullOrWhiteSpace(cur.resourceName)) cur.resourceName = DeriveResourceName(n);
                        Repaint();
                    }).Show(r);
                }
        }
        cur.resourceName = EditorGUILayout.TextField(new GUIContent("Resource name",
            "Unique id — names the baked assets (<name>_ModelMesh / _DistrictMesh / _FxMesh). Letters, digits, '_' or '-' only."), cur.resourceName);
        using (new EditorGUILayout.HorizontalScope())
        {
            cur.modelFile = EditorGUILayout.TextField(new GUIContent("Model file",
                "GLB / glTF / OBJ / FBX / .blend. Leave EMPTY on an existing entry to re-bake with new settings."), cur.modelFile);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFilePanel("Select 3D model", "", "glb,gltf,obj,fbx,blend");
                if (!string.IsNullOrEmpty(p))
                {
                    cur.modelFile = p;
                    if (string.IsNullOrWhiteSpace(cur.resourceName))
                        cur.resourceName = System.Text.RegularExpressions.Regex.Replace(
                            System.IO.Path.GetFileNameWithoutExtension(p), @"[^A-Za-z0-9_\-]", "");
                }
            }
        }
        if ((cur.modelFile ?? "").ToLowerInvariant().EndsWith(".blend") && !UniversalBaker.BlenderAvailable())
            EditorGUILayout.HelpBox(".blend import needs Blender installed (auto-detected). Install it, or set EditorPrefs 'ENC.blenderPath' to blender.exe.", MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bake", EditorStyles.miniBoldLabel);
        cur.size = EditorGUILayout.FloatField(new GUIContent("Size",
            "World length of the model's longest axis. A district tile is ~10 across — ~5 reads imposing, ~2.5 tile-furniture."), cur.size);
        cur.rotation = EditorGUILayout.Vector3Field(new GUIContent("Rotation offset (deg)",
            "On top of the automatic longest-axis align — which can TIP a near-cubic model onto its side, around ANY axis. " +
            "The reactor needed Y=180, Z=90. Check the <name>_FxMesh Inspector preview after baking: it PREDICTS the in-game " +
            "orientation, so dial it in there instead of relaunching the game per guess."), cur.rotation);
        cur.importAngles = EditorGUILayout.Vector3Field(new GUIContent("FxMesh import angles",
            "Draw-time rotation on the FxMesh (no re-bake needed to change it — edit the FxMesh asset in the Inspector). " +
            "(-90,0,0) is the vanilla upright default (the game authors district meshes Z-up)."), cur.importAngles);
        cur.targetTris = EditorGUILayout.IntField(new GUIContent("Target triangles",
            "Quadric-decimate ceiling before baking (0 = off; models under it pass through untouched). District meshes share " +
            "one ~3M-vert GPU buffer that runs nearly FULL in a late-game city — keep this modest, or set the plugin's " +
            "[District] DistrictBufferHeadroom (e.g. 2000000) to enlarge the buffer."), cur.targetTris);
        cur.normalsMode = EditorGUILayout.Popup(new GUIContent("Normals",
            "KeepModel = the artist's; Recalculate = hard edges via smoothing angle (angular models want a LOW angle); Faceted = fully flat."),
            cur.normalsMode, new[] { "Keep model", "Recalculate", "Faceted" });
        using (new EditorGUI.DisabledScope(cur.normalsMode != 1))
            cur.smoothingAngle = EditorGUILayout.Slider("Smoothing angle", cur.smoothingAngle, 0f, 180f);
        cur.convertGrid = EditorGUILayout.IntField(new GUIContent("Convert grid",
            "GLB→OBJ conversion: 0 = faithful (preserves UV seams — textured models), >0 = vertex-cluster decimate (heavy untextured meshes)."), cur.convertGrid);
        cur.stripParts = EditorGUILayout.TextField(new GUIContent("Strip parts",
            "Comma-separated object-name substrings to DELETE from the source model before baking (via Blender). Empty = keep everything."), cur.stripParts ?? "");
        cur.reuseExtracted = EditorGUILayout.Toggle(new GUIContent("Reuse extracted files",
            "Skip re-importing the model file and reuse the OBJ/albedo already extracted — tick after hand-editing the texture so your fix survives a re-bake."), cur.reuseExtracted);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime", EditorStyles.miniBoldLabel);
        cur.isolate = EditorGUILayout.Toggle(new GUIContent("Isolate (this district only)",
            "ON (recommended): only the named district's tiles show your mesh — the plugin builds a private per-instance leaf. " +
            "OFF: the raw shared-leaf swap, which changes EVERY district of that culture using the same building part."), cur.isolate);

        EditorGUILayout.Space();
        char badChar = '\0';
        foreach (char c in cur.resourceName ?? "")
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) { badChar = c; break; }
        bool nameValid = badChar == '\0';
        bool isNew = selected <= 0;
        bool canBake = !string.IsNullOrWhiteSpace(cur.district)
                    && !string.IsNullOrWhiteSpace(cur.resourceName)
                    && nameValid
                    && (!isNew || !string.IsNullOrWhiteSpace(cur.modelFile));
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!canBake))
                if (GUILayout.Button("Bake", GUILayout.Height(34))) DoBake();
            if (GUILayout.Button("Reset", GUILayout.Height(34), GUILayout.Width(72))) { cur = new DistrictDef(); selected = 0; status = ""; GUI.FocusControl(null); }
        }
        if (!canBake)
            EditorGUILayout.HelpBox(
                !nameValid && !string.IsNullOrWhiteSpace(cur.resourceName)
                    ? $"Resource name can't contain '{(badChar == ' ' ? "space" : badChar.ToString())}'. Use letters, digits, '_' or '-' only."
                : isNew ? "New district model: set District, Resource name and a Model file to bake."
                        : "Set District and Resource name to bake.", MessageType.Warning);

        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.HelpBox(
            "Bake imports the model, bakes a bone-free district FxMesh, and writes the enc_districts.json entry the plugin reads.\n" +
            "• Check the baked <name>_FxMesh Inspector preview — it predicts the in-game orientation. Tune Rotation / import angles until it stands.\n" +
            "• DATA prerequisite (once per district): set a renderable ConstructibleVisualAffinity + CLEAR Additional Visual Levels on the definition.\n" +
            "• Plugin prerequisite: [District] DistrictRepoint = true (+ DistrictBufferHeadroom for big meshes in late-game cities).\n" +
            "• Then REBUILD the mod (ships the FxMesh) and relaunch.\n" +
            "Registry: " + DistrictRegistry.RegistryPath, MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open config folder", GUILayout.Width(150)))
                EditorUtility.RevealInFinder(System.IO.File.Exists(DistrictRegistry.RegistryPath)
                    ? DistrictRegistry.RegistryPath : ModelRegistry.ConfigDir);
            GUILayout.Label("↑ enc_districts.json + the plugin .cfg", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    void DoBake()
    {
        // trim on cur ITSELF so what's baked and what's registered stay identical (unit-window review finding E1)
        cur.district = (cur.district ?? "").Trim();
        cur.resourceName = (cur.resourceName ?? "").Trim();
        cur.modelFile = (cur.modelFile ?? "").Trim();
        cur.stripParts = (cur.stripParts ?? "").Trim();

        // 1) the same static bake core as the unit Factory — pawnDescription stays empty (registry-only field, unused by Build)
        var cfg = new BakeConfig
        {
            resourceName = cur.resourceName, modelFile = cur.modelFile, pawnDescription = "",
            rotationEuler = cur.rotation, positionOffset = Vector3.zero, size = cur.size,
            normals = (NormalsMode)cur.normalsMode, smoothingAngle = cur.smoothingAngle, convertGrid = cur.convertGrid,
            targetTris = cur.targetTris, stripParts = cur.stripParts, reuseExtracted = cur.reuseExtracted,
            materialMode = MaterialMode.Auto, atlasMaxDim = 512, albedoBrightness = 1f, albedoSaturation = 1f,
        };
        var r = UniversalBaker.Build(cfg);
        if (!r.ok) { status = "Bake FAILED: " + r.error; return; }

        // 2) wrap the baked mesh as the bone-free district FxMesh
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/" + cur.resourceName + "_ModelMesh.asset");
        if (mesh == null) { status = $"Bake succeeded but '{cur.resourceName}_ModelMesh.asset' wasn't found — can't build the FxMesh."; return; }
        string guid = DistrictBaker.BakeFxMesh(mesh, cur.resourceName, cur.importAngles, out _);
        if (string.IsNullOrEmpty(guid)) { status = "District FxMesh bake FAILED (see Console)."; return; }
        cur.fxMeshGuid = guid;

        // 3) registry entry
        bool saved = DistrictRegistry.Upsert(cur);
        RefreshList();
        selected = Array.IndexOf(existing, cur.district); if (selected < 0) selected = 0;
        if (!saved)
        {
            status = $"Baked '{cur.resourceName}', but the REGISTRY SAVE FAILED (see Console). Close whatever's locking enc_districts.json and re-bake.";
            Debug.LogError("[District] " + status);
            return;
        }
        status = $"Baked district model '{cur.resourceName}' -> '{cur.district}'\nFxMesh {guid}  (verts={mesh.vertexCount})\n" +
                 "Check the FxMesh Inspector preview for orientation, then rebuild the mod + relaunch.";
        Debug.Log("[District] " + status);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Resources/" + cur.resourceName + "_FxMesh.asset");
    }

    // Extension_Base_BreederReactor -> "BreederReactor". Suggested resource name.
    static string DeriveResourceName(string districtName)
    {
        if (string.IsNullOrEmpty(districtName)) return "";
        var parts = districtName.Split('_');
        return parts.Length > 0 ? parts[parts.Length - 1] : districtName;
    }

    // Every district-flavoured ConstructibleDefinition name found in the project databases (vanilla SDK + ENC). District
    // definitions live as sub-assets of the Constructible*ExtensionDefinition database assets; their concrete types all
    // end in "DistrictDefinition" (ExtensionDistrictDefinition, ArtificialDepositDistrictDefinition, Wondrous…).
    static string[] districtCache;
    static string[] GatherDistrictNames()
    {
        if (districtCache != null) return districtCache;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in AssetDatabase.FindAssets("ConstructibleCommonExtensionDefinition"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".asset")) continue;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o != null && o.GetType().Name.EndsWith("DistrictDefinition") && !string.IsNullOrEmpty(o.name))
                    names.Add(o.name);
        }
        districtCache = names.ToArray();
        return districtCache;
    }
}
