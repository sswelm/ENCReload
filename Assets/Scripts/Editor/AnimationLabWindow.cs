// AnimationLabWindow.cs (ENC editor) — Tools > ENC > Animation Lab.
// The dedicated dialog for a model's ANIMATION: which clip plays and how (fire-on-attack, deploy-on-stop, recoil —
// and, next, the state-driven idle/movement/after-movement set). Mutually exclusive with the Model Factory
// and working together with it: the Factory owns the MODEL (identity, file, transform, size, geometry/shading) and
// shows a read-only animation summary with a jump button here; this Lab owns the animation settings and shows the
// model identity read-only. Both bake through the same pipeline (ConfigFor -> UniversalBaker.BuildAnimated ->
// ModelRegistry.Upsert), so it does not matter where Bake is pressed — the settings are just EDITED in one place each.
// Docks as a tab next to the Factory so the pair presents as one tabbed dialog.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class AnimationLabWindow : EditorWindow
{
    // [SerializeField] so the form survives a DOMAIN RELOAD (recompile / play-mode toggle), matching the other Labs.
    [SerializeField] ModelDef cur = new ModelDef { animated = true };
    [SerializeField] int selected;                 // 0 = <New>, else index into `existing`
    [SerializeField] Vector2 scroll;
    string[] existing = { "<New>" };
    string status = "";
    List<string> animClips = new List<string>();   // clip names read from the model (Clip picker)
    List<KeyValuePair<string, int>> animBonePrefixes = new List<KeyValuePair<string, int>>();  // bone-name prefix -> count (Bones picker)
    string clipProbeFile = "\0";                    // sentinel != any real path so the first real path always inspects
    UnityEditor.Editor previewEditor;               // FIT PREVIEW (model + hand prop combined) — non-serializable, rebuilt on demand
    [SerializeField] string previewPath = "";       // the combined prefab shown (survives domain reloads)

    [MenuItem("Tools/HAF/Animation Lab")]
    static void Open()
    {
        // Dock as a TAB next to the Model Factory (desiredDockNextTo) so the two authoring tools present as
        // one tabbed dialog. Falls back to a floating window when the Factory isn't open.
        var w = GetWindow<AnimationLabWindow>("Animation Lab", false, typeof(ModelFactoryWindow));
        w.minSize = new Vector2(480, 420);
        w.RefreshList();
    }

    void OnEnable() { RefreshList(); if (!string.IsNullOrEmpty(previewPath)) LoadFitPreview(previewPath); }
    void OnDisable() { DestroyFitPreview(); }

    void DestroyFitPreview()
    {
        if (previewEditor == null) return;
        try { DestroyImmediate(previewEditor); } catch { }
        previewEditor = null;
    }

    void LoadFitPreview(string prefabPath)
    {
        DestroyFitPreview();
        previewPath = prefabPath ?? "";
        var asset = string.IsNullOrEmpty(previewPath) ? null : AssetDatabase.LoadMainAssetAtPath(previewPath);
        if (asset != null) previewEditor = UnityEditor.Editor.CreateEditor(asset);
    }

    // FIT PREVIEW: the model's slim FBX (its rest pose IS the idle stance after rest-normalization, and its bone
    // hierarchy is exactly what the game composes) with the hand prop's SHIPPED mesh parented to the glue bone using
    // the same math as the runtime: identity local transform + the registry handPropAngles rotation (default zero).
    // What you see here is what the game glues — dial the Prop Lab's Rotation/Position offsets, re-bake the prop,
    // press Refresh: no relaunch needed for fit iteration.
    void BuildFitPreview()
    {
        try
        {
            string res = (cur.resourceName ?? "").Trim();
            string prop = (cur.handPropName ?? "").Trim();
            if (res.Length == 0 || prop.Length == 0) { status = "Fit preview needs a loaded entry with a Hand prop selected."; return; }
            string fbxRel = "Assets/FactorySource/" + res + "/anim/" + res + "_anim.fbx";
            var fbxGo = AssetDatabase.LoadAssetAtPath<GameObject>(fbxRel);
            if (fbxGo == null) { status = "Fit preview: no slim FBX at " + fbxRel + " (bake the model first)."; return; }
            var propMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Resources/" + prop + "_ModelMesh.asset");
            var propMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/" + prop + "_Mat.mat");
            if (propMesh == null) { status = "Fit preview: no baked mesh for '" + prop + "' (bake it in the Prop Lab)."; return; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxGo);
            try
            {
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                // texture the body with the model's baked atlas (reuse the Factory's preview material when present)
                var bodyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/FactorySource/" + res + "/" + res + "_PreviewMat.mat");
                if (bodyMat != null)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>())
                    {
                        var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                        for (int i = 0; i < mats.Length; i++) mats[i] = bodyMat;
                        r.sharedMaterials = mats;
                    }
                // the glue bone — same substring match as the plugin (renamed b###_<orig> bones)
                string sub = string.IsNullOrEmpty(cur.handPropBone) ? "R_Hand" : cur.handPropBone;
                Transform bone = inst.GetComponentsInChildren<Transform>()
                    .FirstOrDefault(t => t.name.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0);
                if (bone == null) { status = $"Fit preview: no bone matches '{sub}' in the FBX."; return; }
                // the prop, glued exactly like the runtime: identity local + the registry override angles (default 0)
                var pgo = new GameObject(prop);
                pgo.transform.SetParent(bone, false);
                var av = (cur.handPropAngles ?? "").Split(',');
                if (av.Length == 3
                    && float.TryParse(av[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ax)
                    && float.TryParse(av[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ay)
                    && float.TryParse(av[2].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float az))
                    pgo.transform.localRotation = Quaternion.Euler(ax, ay, az);
                pgo.AddComponent<MeshFilter>().sharedMesh = propMesh;
                var pmr = pgo.AddComponent<MeshRenderer>();
                var pmats = new Material[Mathf.Max(1, propMesh.subMeshCount)];
                for (int i = 0; i < pmats.Length; i++) pmats[i] = propMat;
                pmr.sharedMaterials = pmats;
                string outPath = "Assets/FactorySource/" + res + "/" + res + "_PropFit.prefab";
                AssetDatabase.DeleteAsset(outPath);
                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
                LoadFitPreview(outPath);
                status = "Fit preview rebuilt (" + outPath + ") — bone '" + bone.name + "'. NOT shipped; preview-only.";
            }
            finally { DestroyImmediate(inst); }
        }
        catch (Exception e) { status = "Fit preview FAILED: " + e.Message; Debug.LogError("[AnimLab] " + e); }
    }

    // Context handoff from the Model Factory ("Edit in Animation Lab"): land on the RIGHT entry. Match an
    // existing animated entry by resource name, then by model file; otherwise pre-fill a NEW animated entry from the
    // Factory's current form (a fresh rigged model getting its animation configured for the first time).
    internal static void OpenFor(string resourceName, string modelFile, string pawnDescription)
    {
        Open();
        var w = GetWindow<AnimationLabWindow>();
        var all = ModelRegistry.Load();
        var e = all.FirstOrDefault(x => x.animated && !string.IsNullOrEmpty(resourceName) && x.resourceName == resourceName)
             ?? all.FirstOrDefault(x => x.animated && !string.IsNullOrEmpty(modelFile) && x.modelFile == modelFile)
             ?? all.FirstOrDefault(x => !string.IsNullOrEmpty(resourceName) && x.resourceName == resourceName);   // not-yet-animated entry being upgraded
        if (e != null)
        {
            w.cur = JsonUtility.FromJson<ModelDef>(JsonUtility.ToJson(e));   // clone, as OnSelectResource does
            w.cur.animated = true;
            w.status = "Loaded '" + e.resourceName + "' (handed over from the Model Factory).";
        }
        else
        {
            w.cur = new ModelDef { animated = true, resourceName = resourceName ?? "", modelFile = modelFile ?? "", pawnDescription = pawnDescription ?? "" };
            w.status = "New animated entry pre-filled from the Model Factory — pick a clip and Bake.";
        }
        w.RefreshList();   // re-derives the dropdown index from the loaded name
        w.Repaint();
    }

    // Only ANIMATED entries are listed (static models have no animation to configure).
    void RefreshList()
    {
        var names = ModelRegistry.Load().Where(m => m.animated).Select(m => m.resourceName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        names.Insert(0, "<New>");
        existing = names.ToArray();
        // Index-by-NAME, never a persisted numeric index: the list is filtered + rebuilt per reload, so a stale index
        // would present a different entry than the form holds.
        selected = Array.IndexOf(existing, cur.resourceName);
        if (selected < 0) selected = 0;
    }

    // Re-read the model's clip/bone names only when the path changes (OnGUI runs every frame; file I/O must not).
    void EnsureClips()
    {
        string f = cur.modelFile ?? "";
        if (f == clipProbeFile) return;
        clipProbeFile = f;
        (animClips, animBonePrefixes) = ModelFactoryWindow.InspectModel(f);
    }

    // One clip text field + Pick dropdown — shared by the single-clip field and the three state-role fields, so
    // every role gets the same pick-from-model UX.
    void ClipRow(string label, string tooltip, Func<string> get, Action<string> set)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            set(EditorGUILayout.TextField(new GUIContent(label, tooltip), get()));
            using (new EditorGUI.DisabledScope(animClips.Count == 0))
                if (GUILayout.Button(new GUIContent("Pick", animClips.Count == 0 ? "No clips readable (glb/gltf only) — type the name" : null), GUILayout.Width(70)))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    var arr = animClips.ToArray();
                    new StringDropdown(new AdvancedDropdownState(), arr, arr, "Clips", n => { set(n); Repaint(); }).Show(r);
                }
        }
    }

    void OnSelectResource()
    {
        if (selected <= 0) { cur = new ModelDef { animated = true }; status = ""; return; }
        var e = ModelRegistry.Load().FirstOrDefault(x => x.resourceName == existing[selected]);
        if (e == null) return;
        cur = JsonUtility.FromJson<ModelDef>(JsonUtility.ToJson(e));   // clone so edits don't mutate the stored copy
        cur.animated = true;
        status = "Loaded '" + e.resourceName + "'.";
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Animation Lab", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Configures the ANIMATION of a model: which clip plays and how (continuous loop, fire-on-attack, " +
            "deploy-on-stop + recoil). The model itself (file, transform, size, shading) is set up in the " +
            "Model Factory — each setting lives in exactly one of the two windows.", MessageType.None);

        // --- Pick the animated entry to edit ---
        using (new EditorGUILayout.HorizontalScope())
        {
            int newSel = EditorGUILayout.Popup("Edit existing", selected, existing);
            if (newSel != selected) { selected = newSel; OnSelectResource(); GUI.FocusControl(null); }
            using (new EditorGUI.DisabledScope(selected <= 0))
                if (GUILayout.Button("Remove", GUILayout.Width(72)))
                    if (EditorUtility.DisplayDialog("Remove entry",
                        $"Remove '{existing[selected]}' from the registry? (Baked assets stay on disk.)", "Remove", "Cancel"))
                    { bool rem = ModelRegistry.Remove(existing[selected]); cur = new ModelDef { animated = true }; selected = 0; RefreshList(); status = rem ? "Removed." : "Remove FAILED — see the Console (registry locked or corrupt)."; }
        }
        EditorGUILayout.Space();

        // --- Model identity: READ-ONLY here (the Factory owns it) ---
        bool hasModel = !string.IsNullOrWhiteSpace(cur.resourceName);
        if (!hasModel)
        {
            EditorGUILayout.HelpBox("No model loaded. Pick one under 'Edit existing', or set the model up in the " +
                "Model Factory first (file, transform, size) and press its 'Edit in Animation Lab' button.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Resource", cur.resourceName);
            EditorGUILayout.LabelField("Target pawn", string.IsNullOrWhiteSpace(cur.pawnDescription) ? "(set in the Model Factory)" : cur.pawnDescription);
            EditorGUILayout.LabelField("Model file", string.IsNullOrWhiteSpace(cur.modelFile) ? "(re-bake uses the extracted files)" : cur.modelFile, EditorStyles.wordWrappedMiniLabel);
        }
        EnsureClips();

        // --- Clip(s) ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clip", EditorStyles.miniBoldLabel);
        cur.animStateDriven = EditorGUILayout.Toggle(new GUIContent("State-driven (idle / move / after / attack)",
            "OFF = today's single-clip modes: one clip, played as a continuous loop or via the Behavior flags below " +
            "(the drone, the howitzer). ON = a STATE MACHINE for characters: the IDLE clip plays standing, the " +
            "MOVEMENT clip plays while the unit moves (fixes the idle-slide), and the optional AFTER-MOVEMENT clip " +
            "plays once on stopping before settling into Idle. All clips come from the same model file and bake " +
            "against ONE shared skeleton. Mutually exclusive with Fire-on-attack / Deploy-when-stopped. Re-Bake after changing."),
            cur.animStateDriven);
        ClipRow(cur.animStateDriven ? "Idle clip" : "Clip name",
            cur.animStateDriven
                ? "Plays while the unit stands still (also the rest/reference clip — its first frame defines the shared rest pose on the conversion path)."
                : "Which animation to bake when the model has several clips. Use Pick to choose from the clips found in the " +
                  "model. Leave EMPTY to use the model's assigned/first clip. Changing the clip needs a re-Bake.",
            () => cur.animClip ?? "", v => cur.animClip = v);
        if (cur.animStateDriven)
        {
            ClipRow("Movement clip", "REQUIRED. Plays in a loop while the unit moves (e.g. a run cycle like 'a_RunN').",
                () => cur.animClipMove ?? "", v => cur.animClipMove = v);
            ClipRow("After-movement clip", "Optional. Played ONCE when the unit stops (a settle/plant motion), then Idle. Empty = stop straight into Idle.",
                () => cur.animClipAfter ?? "", v => cur.animClipAfter = v);
            ClipRow("Attack clip", "Optional. Played ONCE when the unit fires a ranged attack (e.g. 'shootAR2s'), overriding every other state for its duration. Empty = no attack animation.",
                () => cur.animClipAttack ?? "", v => cur.animClipAttack = v);
            ClipRow("Combat-idle clip", "Optional. Replaces Idle while the army is locked in a battle (a weapon-raised stance like 'CombatIdle1' — a single-frame pose clip is fine). Empty = normal Idle in battle.",
                () => cur.animClipCombat ?? "", v => cur.animClipCombat = v);
            if (!string.IsNullOrWhiteSpace(cur.animClipAttack))
                cur.attackRepeats = EditorGUILayout.IntSlider(new GUIContent("Attack repeats",
                    "How many times the Attack clip replays per trigger (the sim fires ONCE per attack, so a short " +
                    "recoil-pop clip like shootAR2s (0.17s) reads as a blip at 1; e.g. 6 ≈ 1s of sustained fire). " +
                    "RUNTIME-ONLY — 'Save (no bake)' + rebuild is enough, no re-bake."),
                    Mathf.Max(1, cur.attackRepeats), 1, 20);
            if (cur.fireOnAttack || cur.deployOnStop)
                EditorGUILayout.HelpBox("State-driven is mutually exclusive with Fire-on-attack / Deploy-when-stopped — " +
                    "those flags are ignored while State-driven is ON.", MessageType.Warning);
        }

        // --- Hand prop (runtime-only: Save (no bake) + rebuild the mod) ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hand prop (weapon — runtime-only)", EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            // Combobox over the baked props (every Assets/Resources/<name>_Collection.asset the Prop Lab produced):
            // picking one fills BOTH the name and the collection GUID — no clipboard round-trip. "(none)" clears.
            var propFiles = System.IO.Directory.Exists("Assets/Resources")
                ? System.IO.Directory.GetFiles("Assets/Resources", "*_Collection.asset")
                    .Select(p => System.IO.Path.GetFileName(p))
                    .Select(f => f.Substring(0, f.Length - "_Collection.asset".Length))
                    .OrderBy(n => n).ToArray()
                : new string[0];
            var options = new[] { "(none)" }.Concat(propFiles).ToArray();
            int curIdx = System.Array.IndexOf(options, string.IsNullOrEmpty(cur.handPropName) ? "(none)" : cur.handPropName);
            if (curIdx < 0) curIdx = 0;
            int pick = EditorGUILayout.Popup(new GUIContent("Hand prop",
                "A weapon glued to a bone of THIS model's skeleton at runtime. The list shows every prop baked in " +
                "Tools ▸ HAF ▸ Prop Lab (its <name>_Collection assets); picking one fills the collection GUID " +
                "automatically. Bake the weapon there first (e.g. 'M60'). '(none)' = no hand prop."),
                curIdx, options);
            if (pick != curIdx)
            {
                if (pick == 0) { cur.handPropName = ""; cur.handPropGuid = ""; }
                else
                {
                    cur.handPropName = options[pick];
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Resources/" + options[pick] + "_Collection.asset");
                    cur.handPropGuid = asset != null ? PropBakerWindow.AmplitudeGuid(asset) : "";
                    if (string.IsNullOrEmpty(cur.handPropGuid))
                        Debug.LogWarning("[AnimLab] could not read the Amplitude GUID of " + options[pick] + "_Collection.asset — re-bake the prop in the Prop Lab.");
                }
            }
        }
        if (!string.IsNullOrEmpty(cur.handPropName) && string.IsNullOrEmpty(cur.handPropGuid))
            EditorGUILayout.HelpBox("No collection GUID for '" + cur.handPropName + "' — re-pick it (or re-bake the prop).", MessageType.Warning);
        if (!string.IsNullOrWhiteSpace(cur.handPropGuid))
        {
            cur.handPropBone = EditorGUILayout.TextField(new GUIContent("Bone substring",
                "Which bone of THIS model's skeleton the prop glues to — a case-insensitive SUBSTRING of the bone name " +
                "(the conversion renames bones to b###_<original>, so match the original part, e.g. 'R_Hand'). " +
                "Empty = 'R_Hand'."), cur.handPropBone ?? "");
            cur.handPropMat = EditorGUILayout.TextField(new GUIContent("Material GUID (borrowed)",
                "MaterialRef whose output layer the prop renders with — \"a,b,c,d\". Empty = the shared " +
                "EQ_DLC04_Weapons material (verified working for weapon props)."), cur.handPropMat ?? "");
            // NOTE: the prop's orientation is authored in the PROP LAB recipe (Rotation offset — baked vertices).
            // The registry still supports a per-model runtime override (`handPropAngles` "x,y,z", hand-editable in
            // enc_models.json): the plugin stamps it onto the FxMesh asset before encoding, making orientation
            // dial-in relaunch-only. Deliberately not exposed here to keep one owner per setting.
            if (GUILayout.Button(new GUIContent("Refresh fit preview (model + prop, as glued in-game)",
                "Rebuilds the combined preview below: the model's rig at rest (the idle stance) with the prop's SHIPPED " +
                "mesh parented to the glue bone using the exact runtime math. Iterate: adjust the Prop Lab's " +
                "Rotation/Position offsets, Bake the prop, press this — no game relaunch needed for fit tuning.")))
                BuildFitPreview();
        }
        // Animate only bones — free text + a Pick that appends a bone-name prefix (grouped, with counts) from the model.
        using (new EditorGUILayout.HorizontalScope())
        {
            cur.animateBones = EditorGUILayout.TextField(new GUIContent("Animate only bones",
                "Optional. Comma-separated bone-name PREFIXES to keep animation on — e.g. 'prop' keeps the spinning parts " +
                "and strips camera / body-bob curves that make the model wobble. Use Pick to add a prefix found in the model. " +
                "Leave EMPTY to keep the whole clip. The frame range is always auto-clamped (kills the ~1s per-loop stall)."), cur.animateBones ?? "");
            using (new EditorGUI.DisabledScope(animBonePrefixes.Count == 0))
                if (GUILayout.Button(new GUIContent("Pick", animBonePrefixes.Count == 0 ? "No bones readable from this model (glb/gltf only) — type prefixes" : null), GUILayout.Width(70)))
                {
                    var r = GUILayoutUtility.GetLastRect();
                    var labels = animBonePrefixes.Select(kv => $"{kv.Key}  ({kv.Value} part{(kv.Value == 1 ? "" : "s")})").ToArray();
                    var values = animBonePrefixes.Select(kv => kv.Key).ToArray();
                    new StringDropdown(new AdvancedDropdownState(), labels, values, "Bone prefixes", p =>
                    {
                        var set = (cur.animateBones ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                        if (!set.Contains(p)) set.Add(p);
                        cur.animateBones = string.Join(",", set);
                        Repaint();
                    }).Show(r);
                }
        }
        cur.animUnitFix = EditorGUILayout.Toggle(new GUIContent("Fix 100× oversize (FBX unit scale)",
            "Bake-time scale fix for rigged exports that embed a metre→centimetre unit scale (bake ~100× too big, float " +
            "in the sky). PER-MODEL: if the model bakes huge/floating, tick it; if ticking makes it vanish, untick (the " +
            "drone bakes right OFF; the howitzer needs it ON). Re-bake after changing."),
            cur.animUnitFix);
        cur.convertRig = EditorGUILayout.Toggle(new GUIContent("Convert raw rig (auto-rigged models)",
            "Bake-time PIPELINE switch. ON = the raw-rig conversion: rest-normalize + visual rebake (for rigs whose clips " +
            "assemble the body with location keys), no-op root collapse, topological bone rename, clean-unit export — what " +
            "made the Combine soldier work. OFF = the byte-identical legacy pipeline for purpose-made rigs (drone, howitzer). " +
            "Tick it when a rig plays fine in the preview but tears apart / displaces in-game; usually paired with " +
            "Fix 100× OFF. Re-bake after changing."),
            cur.convertRig);
        if (!UniversalBaker.BlenderAvailable())
            EditorGUILayout.HelpBox("The animated bake needs Blender (to slim the rig + bake the clip) — it wasn't found. " +
                "Install Blender or set EditorPrefs 'ENC.blenderPath' to blender.exe.", MessageType.Warning);

        // --- Behavior (runtime flags — Save (no bake) + relaunch applies them) ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Behavior", EditorStyles.miniBoldLabel);
        cur.fireOnAttack = EditorGUILayout.Toggle(new GUIContent("Fire on attack (play once)",
            "Play the baked clip ONCE when this unit attacks, instead of looping — the model rests, then plays a single pass " +
            "on the shot and returns to rest (e.g. a howitzer barrel that elevates only when it bombards). AUTHOR THE CLIP TO " +
            "START AND END AT REST. Leave OFF for a continuous loop (a drone's spinning prop)."),
            cur.fireOnAttack);
        cur.deployOnStop = EditorGUILayout.Toggle(new GUIContent("Deploy when stopped",
            "Hold the baked clip's DEPLOYED pose while idle, and snap to the UNDEPLOYED pose (frame 0) the instant it moves — " +
            "e.g. a howitzer that deploys its barrel/trails when it stops and folds them for travel. AUTHOR THE CLIP so frame 0 " +
            "= travelling and the deployed pose sits at 'Deployed pose time'."),
            cur.deployOnStop);
        using (new EditorGUI.DisabledScope(!cur.deployOnStop))
            cur.deployPoseTime = EditorGUILayout.Slider(new GUIContent("Deployed pose time",
                "Normalized clip time (0..1) of the DEPLOYED pose held when idle. 1 = a purpose-made deploy clip's end frame."),
                cur.deployPoseTime <= 0f ? 1f : cur.deployPoseTime, 0f, 1f);
        using (new EditorGUI.DisabledScope(!cur.deployOnStop))
            cur.deploySpeed = EditorGUILayout.Slider(new GUIContent("Deploy speed",
                "Speed multiplier on the gradual deploy-on-stop ramp: 1 = the clip's authored speed. Folding on move is always instant."),
                cur.deploySpeed <= 0f ? 1f : cur.deploySpeed, 0.25f, 5f);
        using (new EditorGUI.DisabledScope(!cur.deployOnStop || !cur.fireOnAttack))
            cur.recoilSpeed = EditorGUILayout.Slider(new GUIContent("Recoil speed",
                "Speed multiplier on the recoil-on-fire kickback (needs Deploy-when-stopped + Fire-on-attack): 1 = the clip " +
                "tail's authored speed."),
                cur.recoilSpeed <= 0f ? 1f : cur.recoilSpeed, 0.25f, 8f);

        // --- Bake / Save ---
        EditorGUILayout.Space();
        bool hasBaked = cur.skel != null && cur.skel.Length == 4 && !(cur.skel[0] == 0 && cur.skel[1] == 0 && cur.skel[2] == 0 && cur.skel[3] == 0);
        bool canBake = hasModel && !string.IsNullOrWhiteSpace(cur.pawnDescription)
                    && (hasBaked || !string.IsNullOrWhiteSpace(cur.modelFile));
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!canBake))
                if (GUILayout.Button(new GUIContent("Bake", "Re-run the animated pipeline (Blender slim + skeleton + clip + atlas) with the settings above, then save the registry entry."), GUILayout.Height(34))) DoBake();
            using (new EditorGUI.DisabledScope(!hasBaked || !hasModel))
                if (GUILayout.Button(new GUIContent("Save (no bake)",
                    "Write the registry entry with the current settings WITHOUT re-baking assets — enough for the runtime " +
                    "Behavior flags/sliders (the clip fields need a real Bake). Relaunch the game to see it."),
                    GUILayout.Height(34), GUILayout.Width(110)))
                    SaveOnly();
            if (GUILayout.Button("Reset", GUILayout.Height(34), GUILayout.Width(72)))
            { cur = new ModelDef { animated = true }; selected = 0; status = ""; GUI.FocusControl(null); }
        }
        if (!canBake && hasModel)
            EditorGUILayout.HelpBox("This entry has no baked assets and no model file — open it in the Model Factory to set the file, then bake.", MessageType.Warning);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.HelpBox("Registry: " + ModelRegistry.RegistryPath, MessageType.None);
        // --- FIT PREVIEW (model + hand prop, glued as in-game) ---
        if (previewEditor != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fit preview — model + hand prop  (drag to orbit, scroll to zoom)", EditorStyles.miniBoldLabel);
            var rect = GUILayoutUtility.GetRect(200, 320, GUILayout.ExpandWidth(true));
            previewEditor.OnInteractivePreviewGUI(rect, GUIStyle.none);
        }
        EditorGUILayout.EndScrollView();
    }

    // ENFORCED OWNERSHIP: a bake/save from this window rebases onto the FRESHEST registry entry and applies only the
    // ANIMATION-owned fields from this form. Without this, whichever window held a stale copy silently clobbered the
    // other's work at bake time (cost three bakes on the Combine soldier: the Factory bake lost the Lab's Fix-100×,
    // then the Lab bake lost the Factory's rotation AND size). Model-owned fields (rotation, position, size, tris,
    // material, shading, …) always come from the registry — this window can't even display them, so it must not
    // write its stale copies of them either. No-op for a brand-new entry (nothing saved yet to rebase on).
    void RebaseOnRegistry()
    {
        var reg = ModelRegistry.Load().FirstOrDefault(x => x.resourceName == (cur.resourceName ?? "").Trim());
        if (reg == null) return;
        var mine = cur;
        cur = JsonUtility.FromJson<ModelDef>(JsonUtility.ToJson(reg));
        cur.animated = true;
        cur.animClip = mine.animClip; cur.animateBones = mine.animateBones; cur.animUnitFix = mine.animUnitFix;
        cur.convertRig = mine.convertRig;
        cur.animStateDriven = mine.animStateDriven; cur.animClipMove = mine.animClipMove; cur.animClipAfter = mine.animClipAfter; cur.animClipAttack = mine.animClipAttack; cur.animClipCombat = mine.animClipCombat; cur.attackRepeats = mine.attackRepeats;
        cur.handPropName = mine.handPropName; cur.handPropGuid = mine.handPropGuid; cur.handPropMat = mine.handPropMat; cur.handPropBone = mine.handPropBone;
        // handPropAngles deliberately NOT carried: it's a hand-edited registry-only knob now (no UI here) — carrying
        // the Lab's stale copy resurrected old values on every Save/Bake, silently overriding the recipe's angles.
        cur.fireOnAttack = mine.fireOnAttack; cur.deployOnStop = mine.deployOnStop;
        cur.deployPoseTime = mine.deployPoseTime; cur.deploySpeed = mine.deploySpeed; cur.recoilSpeed = mine.recoilSpeed;
    }

    // Persist runtime-only tweaks without touching the baked assets: the entry keeps its existing skeleton/clip/atlas
    // GUIDs (loaded with the entry), so the plugin re-reads the new settings on the next game launch.
    void SaveOnly()
    {
        RebaseOnRegistry();
        cur.animated = true;
        cur.resourceName = (cur.resourceName ?? "").Trim();
        cur.pawnDescription = (cur.pawnDescription ?? "").Trim();
        bool saved = ModelRegistry.Upsert(cur);
        RefreshList();
        status = saved
            ? $"Saved '{cur.resourceName}' (registry only, assets untouched). Relaunch the game — no re-bake, no mod rebuild."
            : "REGISTRY SAVE FAILED (see Console). Close whatever's locking enc_models.json and retry.";
        Debug.Log("[AnimLab] " + status);
    }

    // Same flow as ModelFactoryWindow.DoBake, scoped to the animated path: trim -> ConfigFor -> BuildAnimated ->
    // capture the baked GUIDs onto the entry -> Upsert to the registry.
    void DoBake()
    {
        ModelFactoryWindow.ReleasePreviews();   // the bake rewrites the preview prefab — a live Factory preview watching it throws from Unity internals
        RebaseOnRegistry();   // bake with the freshest model-owned fields (rotation/size/…) — only animation fields are ours
        cur.animated = true;
        cur.resourceName = (cur.resourceName ?? "").Trim();
        cur.pawnDescription = (cur.pawnDescription ?? "").Trim();
        cur.modelFile = (cur.modelFile ?? "").Trim();
        cur.animClip = (cur.animClip ?? "").Trim();
        cur.animateBones = (cur.animateBones ?? "").Trim();
        cur.animClipMove = (cur.animClipMove ?? "").Trim();
        cur.animClipAfter = (cur.animClipAfter ?? "").Trim();
        cur.animClipAttack = (cur.animClipAttack ?? "").Trim();
        cur.animClipCombat = (cur.animClipCombat ?? "").Trim();
        var cfg = ModelFactoryWindow.ConfigFor(cur);
        // Geometry caching is AUTOMATIC (mirror of the Factory's DoBake): re-slim exactly when a Blender-step input
        // changed; the 'Reuse extracted' checkbox only protects the hand-edited extracted texture (cfg.keepTexture).
        cfg.reuseExtracted = !ModelFactoryWindow.AnimatedSlimInputsChanged(cur);
        if (!cfg.reuseExtracted) Debug.Log("[AnimLab] " + cur.resourceName + ": Blender-step settings changed — re-slimming (automatic).");
        var r = UniversalBaker.BuildAnimated(cfg);
        if (!r.ok) { status = "Bake FAILED: " + r.error; Debug.LogError("[AnimLab] " + r.error); return; }
        cur.skel = ModelRegistry.ParseGuid(r.skeletonGuid);
        cur.atlas = ModelRegistry.ParseGuid(r.atlasGuid);
        cur.clip = ModelRegistry.ParseGuid(r.clipGuid);
        cur.clipMove = cur.animStateDriven ? ModelRegistry.ParseGuid(r.clipMoveGuid) : new int[4];
        cur.clipAfter = cur.animStateDriven && !string.IsNullOrEmpty(r.clipAfterGuid) ? ModelRegistry.ParseGuid(r.clipAfterGuid) : new int[4];
        cur.clipAttack = cur.animStateDriven && !string.IsNullOrEmpty(r.clipAttackGuid) ? ModelRegistry.ParseGuid(r.clipAttackGuid) : new int[4];
        cur.clipCombat = cur.animStateDriven && !string.IsNullOrEmpty(r.clipCombatGuid) ? ModelRegistry.ParseGuid(r.clipCombatGuid) : new int[4];
        bool saved = ModelRegistry.Upsert(cur);
        RefreshList();
        ModelFactoryWindow.ReloadPreviews();   // give the Factory tab its preview back (fresh from this bake)
        status = saved
            ? $"Baked ANIMATED '{cur.resourceName}' -> '{cur.pawnDescription}'\nskeleton {r.skeletonGuid}\nclip {r.clipGuid}{(cur.animStateDriven ? $"\nmove clip {r.clipMoveGuid}{(string.IsNullOrEmpty(r.clipAfterGuid) ? "" : $"  after clip {r.clipAfterGuid}")}{(string.IsNullOrEmpty(r.clipAttackGuid) ? "" : $"  attack clip {r.clipAttackGuid}")}{(string.IsNullOrEmpty(r.clipCombatGuid) ? "" : $"  combat clip {r.clipCombatGuid}")}" : "")}\nRebuild the mod + relaunch."
            : $"Baked '{cur.resourceName}', but the REGISTRY SAVE FAILED (see Console). Close whatever's locking enc_models.json and re-bake.";
        Debug.Log("[AnimLab] " + status);
    }
}
