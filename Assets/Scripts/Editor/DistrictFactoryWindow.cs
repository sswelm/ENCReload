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
    [MenuItem("Tools/HAF/District Factory")]
    static void Open() => GetWindow<DistrictFactoryWindow>("District Factory");

    List<DistrictDef> all = new List<DistrictDef>();
    string[] existing = { "<New>" };
    int selected;
    DistrictDef cur = new DistrictDef();
    string status = "";
    Vector2 scroll;

    // EMBEDDED PREVIEW — renders the entry's baked mesh exactly as the game draws it (the FxMesh path: the bone-free
    // _DistrictMesh rotated by the draw-time FxMesh rotation), standing on a district-tile ground square (~10 across) so
    // the Size knob reads at a glance. Facing comes LIVE from the form field, so the model can be turned by eye
    // in here; the Rotation offset is baked into the vertices and only shows after a re-Bake. Same PreviewRenderUtility
    // owner-camera pattern as the Animation Lab (built-in previews have no zoom and the scroll view steals the wheel).
    PreviewRenderUtility pru;                       // non-serializable; lazily created, cleaned in OnDisable
    Material pvFallbackMat, pvTileMat;              // created on demand, HideAndDontSave, destroyed in OnDisable
    Mesh pvTileMesh;
    Mesh pvMesh; Material[] pvMats;                 // the baked district mesh + its atlas preview material
    string pvLoadedFor;                             // resourceName the cache was built for (null = load on next paint)
    [SerializeField] Vector2 pvOrbit = new Vector2(35f, -30f);
    [SerializeField] float pvZoom = 1f;
    [SerializeField] Vector2 pvPan;

    void OnEnable() => RefreshList();

    void OnDisable()
    {
        if (pru != null) { pru.Cleanup(); pru = null; }
        if (pvFallbackMat != null) DestroyImmediate(pvFallbackMat);
        if (pvTileMat != null) DestroyImmediate(pvTileMat);
        if (pvTileMesh != null) DestroyImmediate(pvTileMesh);
    }

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
        LoadPreviewAssets(force: true);   // preview follows the selection — never show the previous entry's model
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
            "The STAND-IT-UP control, baked into the mesh — on top of the automatic longest-axis align, which can TIP a " +
            "near-cubic model onto its side around ANY axis (the plant needed Z=-90). The preview below shows the result " +
            "after each Bake — dial it there, no relaunch needed. To merely TURN a standing building, use Facing instead."), cur.rotation);
        cur.facing = EditorGUILayout.Slider(new GUIContent("Facing on tile (deg)",
            "Turn the building on its tile — always about the vertical, can't tip it. Previewed LIVE; written into the " +
            "FxMesh at Bake (auto-level re-grounds for the result)."), cur.facing, 0f, 360f);
        cur.posOffset = EditorGUILayout.Vector3Field(new GUIContent("Position offset",
            "Nudge the building on its tile, in world units (a tile is ~10 across): X/Z slide it over the tile, Y lifts " +
            "it off the ground. Applied AFTER the auto-level at Bake (so leveling can't cancel it); previewed LIVE. " +
            "The same knob the Model Factory has for units."), cur.posOffset);
        // cur.importAngles stays in the registry for entries authored before Facing (their FxMesh rotation composes it),
        // but it's no longer a UI control: Rotation offset stands the model up (previewed per bake), Facing turns it
        // (previewed live) — two rotation fields with overlapping jobs only bred "which one do I use?".
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

        DrawPreviewPane();

        EditorGUILayout.HelpBox(
            "Bake imports the model, bakes a bone-free district FxMesh, and writes the enc_districts.json entry the plugin reads.\n" +
            "• The preview below predicts the in-game look. Tune Rotation offset until it stands (re-Bake to see), Facing to turn it (live).\n" +
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
        string guid = DistrictBaker.BakeFxMesh(mesh, cur.resourceName, ComposedImportAngles(), out _, levelOnGround: true, postLevelOffset: cur.posOffset);
        if (string.IsNullOrEmpty(guid)) { status = "District FxMesh bake FAILED (see Console)."; return; }
        cur.fxMeshGuid = guid;
        cur.posOffsetBaked = cur.posOffset;   // the preview shows future posOffset edits as a live delta against this

        // the baked albedo atlas GUID — the plugin paints it into the district atlas page (texture injection)
        var atlasTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + cur.resourceName + "_Atlas.asset");
        cur.atlasGuid = atlasTex != null ? DistrictBaker.AmplitudeGuid(atlasTex) ?? "" : "";
        if (string.IsNullOrEmpty(cur.atlasGuid))
            Debug.LogWarning($"[District] no baked atlas for '{cur.resourceName}' — the model will render untextured in-game (vanilla district shading).");

        LoadPreviewAssets(force: true);   // fresh assets exist even if the registry save below fails

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

    // ---- embedded preview ----

    void LoadPreviewAssets(bool force = false)
    {
        string res = (cur.resourceName ?? "").Trim();
        if (!force && res == pvLoadedFor) return;
        pvLoadedFor = res; pvMesh = null; pvMats = null; pvPan = Vector2.zero;
        if (string.IsNullOrEmpty(res)) return;
        // prefer the SHIPPED district mesh (bone-free, rotation offset baked in); fall back to the unit-style bake output
        pvMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/" + res + "_DistrictMesh.asset")
              ?? AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/" + res + "_ModelMesh.asset");
        if (pvMesh == null) return;
        // the static bake writes the atlased Standard material to Resources/<res>_Mat.mat — use it so the preview is
        // textured; the FactorySource PreviewMat only exists for unit-path bakes of the same resource name
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/" + res + "_Mat.mat")
               ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/FactorySource/" + res + "/" + res + "_PreviewMat.mat");
        var mats = new Material[Mathf.Max(1, pvMesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = mat;   // null slots fall back at draw time (proper fake-null check there)
        pvMats = mats;
    }

    void DrawPreviewPane()
    {
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Preview — predicts the in-game orientation", EditorStyles.miniBoldLabel);
            if (GUILayout.Button(new GUIContent("Center", "Re-center the view on the model (resets pan + zoom; keeps the orbit angle)"), GUILayout.Width(60)))
            { pvPan = Vector2.zero; pvZoom = 1f; Repaint(); }
            if (GUILayout.Button("Refresh", GUILayout.Width(70))) LoadPreviewAssets(force: true);
        }
        LoadPreviewAssets();
        if (pvMesh == null)
        {
            EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(cur.resourceName)
                ? "Set a Resource name (or pick an existing entry) to preview its baked mesh."
                : $"No baked mesh for '{cur.resourceName.Trim()}' yet — press Bake and the preview appears here.", MessageType.Info);
            return;
        }
        // grow with the window: ~45% of its height so a tall dock gets a big viewport, never under 300px
        var rect = GUILayoutUtility.GetRect(10f, Mathf.Max(300f, position.height * 0.45f), GUILayout.ExpandWidth(true));
        DrawPreview(rect);
        EditorGUILayout.LabelField($"{pvMesh.vertexCount} verts · ground square = one district tile (~10 across) at the in-game surface level · LMB orbit, wheel zoom, MMB/RMB pan", EditorStyles.miniLabel);
        EditorGUILayout.LabelField("Facing + Position offset preview LIVE (Bake makes them real). Rotation offset is baked into the mesh — re-Bake to see it.", EditorStyles.miniLabel);
    }

    void DrawPreview(Rect rect)
    {
        var e = Event.current;
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.ScrollWheel)
            {
                // consume the wheel HERE so the window's scroll view never sees it — this is the zoom
                pvZoom = Mathf.Clamp(pvZoom * Mathf.Pow(1.12f, e.delta.y > 0 ? 1f : -1f), 0.1f, 5f);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0)
            {
                pvOrbit += new Vector2(e.delta.x, -e.delta.y) * 0.7f;
                pvOrbit.y = Mathf.Clamp(pvOrbit.y, -89f, 89f);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2))
            {
                pvPan += new Vector2(-e.delta.x, e.delta.y) * 0.0035f;   // pan in the camera plane, scaled by view distance at render time
                e.Use(); Repaint();
            }
        }
        if (e.type != EventType.Repaint) return;
        if (pvMesh == null) { pvLoadedFor = null; return; }   // asset deleted under us (a re-bake) — reload on the next paint
        if (pru == null) pru = new PreviewRenderUtility();
        if (pvFallbackMat == null) pvFallbackMat = new Material(Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };
        if (pvTileMat == null)
        {
            pvTileMat = new Material(Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave, color = new Color(0.33f, 0.40f, 0.29f) };
            pvTileMat.SetFloat("_Glossiness", 0f);
        }
        pru.BeginPreview(rect, GUIStyle.none);
        // try/finally so a throw in DrawMesh/Render can never skip EndPreview (the "BeginPreview not closed" cascade)
        Texture tex = null;
        try
        {
            // the game's draw-time rotation, LIVE from the form fields — dial the model upright + turned right here.
            // Position offset previews as a DELTA vs what the current bake already carries (baked into the vertices).
            var mtx = Matrix4x4.Translate(cur.posOffset - cur.posOffsetBaked)
                    * Matrix4x4.Rotate(Quaternion.Euler(0f, cur.facing, 0f) * Quaternion.Euler(cur.importAngles));
            var b = TransformBounds(mtx, pvMesh.bounds);
            // the tile square is the TRUE in-game surface: the plane through the origin. It must NOT follow the model —
            // anchoring it under the mesh's lowest point hid a half-sunk bake (the nuclear plant surfaced only its domes
            // in-game while the preview looked grounded). A model below this plane previews sunk because it IS sunk.
            var tileMtx = Matrix4x4.Translate(new Vector3(0f, -0.02f, 0f));
            var frame = b; frame.Encapsulate(TransformBounds(tileMtx, TileMesh().bounds));

            var cam = pru.camera;
            float radius = Mathf.Max(frame.extents.magnitude, 0.1f);
            float dist = radius * 2.0f * pvZoom;
            var rot = Quaternion.Euler(-pvOrbit.y, pvOrbit.x, 0f);
            var lookAt = frame.center + rot * new Vector3(pvPan.x, pvPan.y, 0f) * dist;
            cam.transform.position = lookAt + rot * (Vector3.back * dist);
            cam.transform.rotation = Quaternion.LookRotation(lookAt - cam.transform.position);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = dist + radius * 4f;
            cam.fieldOfView = 30f;
            pru.lights[0].intensity = 1.3f;
            pru.lights[0].transform.rotation = Quaternion.Euler(45f, 45f, 0f);
            if (pru.lights.Length > 1) pru.lights[1].intensity = 0.6f;
            pru.ambientColor = new Color(0.3f, 0.3f, 0.3f);

            pru.DrawMesh(TileMesh(), tileMtx, pvTileMat, 0);
            for (int s = 0; s < pvMesh.subMeshCount; s++)
            {
                var m = pvMats != null && pvMats.Length > 0 ? pvMats[Mathf.Min(s, pvMats.Length - 1)] : null;
                if (m == null) m = pvFallbackMat;   // Unity fake-null too (the material asset dies on a re-bake)
                pru.DrawMesh(pvMesh, mtx, m, s);
            }
            cam.Render();
        }
        finally { tex = pru.EndPreview(); }
        if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
    }

    Mesh TileMesh()
    {
        if (pvTileMesh != null) return pvTileMesh;
        pvTileMesh = new Mesh { name = "DistrictTilePreview", hideFlags = HideFlags.HideAndDontSave };
        pvTileMesh.vertices = new[] { new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, -5f), new Vector3(5f, 0f, 5f), new Vector3(-5f, 0f, 5f) };
        pvTileMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        pvTileMesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
        return pvTileMesh;
    }

    static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        var c = m.MultiplyPoint3x4(b.center);
        var e = b.extents;
        var ne = new Vector3(
            Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
            Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
            Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
        return new Bounds(c, ne * 2f);
    }

    // The full draw-time rotation written into the FxMesh: Facing (a yaw about the drawn-space vertical) applied AFTER
    // the import angles — so import angles stand the model up and Facing turns the standing model, never tips it.
    Vector3 ComposedImportAngles() =>
        (Quaternion.Euler(0f, cur.facing, 0f) * Quaternion.Euler(cur.importAngles)).eulerAngles;

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
