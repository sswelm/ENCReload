// ClipRangeDialog.cs — the CLIP RANGE PICKER (2026-07-19, user-designed). Opened from any clip field's ▶ button:
// shows a playable/scrubbable 3D preview of the model's clips (via an "inspection FBX" — a pure Blender format
// conversion carrying ALL clips, no rig surgery) with Start/End frame fields; Confirm writes `clip[start..end]`
// (or the plain clip name for the full range) back into the field. This is how a modder finds segment boundaries
// inside a long multi-motion clip (e.g. the M114's deploy 0..180 + recoil 180..250) without opening Blender.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ClipRangeDialog : EditorWindow
{
    const float FPS = 24f;   // Blender-standard export rate; exact on every model so far (deploy 250f=10.417s, Idle1 341f=14.208s)

    string modelFile, resourceName, fbxDir;
    Action<string> onConfirm;
    AnimationClip[] clips = new AnimationClip[0];
    string[] clipNames = new string[0];    // EXACT action names (the "HAFCLIP|" take prefix stripped) — what Confirm writes
    string[] clipPaths = new string[0];    // which inspection FBX carries each clip (one action per file)
    string[] clipLabels = new string[0];
    int clipIdx;
    string instPath;                       // the FBX the current instance came from
    float frame; int startF, endF;
    bool playing; double lastTick;

    GameObject inst;
    readonly List<SkinnedMeshRenderer> smrs = new List<SkinnedMeshRenderer>();
    readonly List<MeshFilter> mfs = new List<MeshFilter>();
    Mesh[] bakedMeshes = new Mesh[0];
    PreviewRenderUtility pru;
    Vector2 orbit = new Vector2(150f, -15f);
    float zoom = 1.4f;
    Bounds bounds; bool boundsValid;
    static Material fallbackMat;

    public static void Open(string modelFile, string resourceName, string currentSpec, Action<string> onConfirm)
    {
        var w = GetWindow<ClipRangeDialog>(true, "Clip range picker", true);
        w.minSize = new Vector2(560, 560);
        w.modelFile = modelFile ?? "";
        w.resourceName = (resourceName ?? "").Trim();
        w.onConfirm = onConfirm;
        w.Prepare(currentSpec ?? "");
    }

    void Prepare(string currentSpec)
    {
        // parse a pre-existing "name[a..b]" spec so the dialog reopens where the field points
        string wantClip = currentSpec; int wantS = -1, wantE = -1;
        var m = System.Text.RegularExpressions.Regex.Match(currentSpec, @"^(.*)\[(\d+)\.\.(\d+)\]$");
        if (m.Success) { wantClip = m.Groups[1].Value; wantS = int.Parse(m.Groups[2].Value); wantE = int.Parse(m.Groups[3].Value); }

        if (string.IsNullOrEmpty(resourceName) || string.IsNullOrEmpty(modelFile) || !System.IO.File.Exists(modelFile))
        { ShowNotification(new GUIContent("Needs a loaded entry with an existing model file.")); return; }
        fbxDir = "Assets/FactorySource/" + resourceName + "/inspect";
        string proj = System.IO.Directory.GetParent(Application.dataPath).FullName;
        string dirFull = System.IO.Path.Combine(proj, fbxDir);
        var existing = System.IO.Directory.Exists(dirFull) ? System.IO.Directory.GetFiles(dirFull, "*.fbx") : new string[0];
        bool stale = existing.Length == 0
                     || existing.Any(f => System.IO.Path.GetFileName(f) == resourceName + "_inspect.fbx")   // legacy single-file converter output (mirrored anim bug) — force re-convert
                     || System.IO.File.GetLastWriteTimeUtc(modelFile) > existing.Max(f => System.IO.File.GetLastWriteTimeUtc(f));
        if (stale && !BuildInspectFbx(proj, dirFull)) return;

        var clipL = new List<AnimationClip>(); var nameL = new List<string>(); var pathL = new List<string>();
        foreach (var full in System.IO.Directory.GetFiles(dirFull, "*.fbx").OrderBy(f => f))
        {
            string rel = fbxDir + "/" + System.IO.Path.GetFileName(full);
            var imp = AssetImporter.GetAtPath(rel) as ModelImporter;
            if (imp != null && (imp.animationType != ModelImporterAnimationType.Generic || !imp.importAnimation))
            { imp.animationType = ModelImporterAnimationType.Generic; imp.importAnimation = true; imp.SaveAndReimport(); }
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(rel).OfType<AnimationClip>())
            {
                if (c.name.StartsWith("__preview")) continue;
                // the take name is "HAFCLIP|<action>" (sentinel armature name) — strip the FIXED prefix to recover
                // the exact action name, which may itself contain '|' (e.g. "Soldier_reference_skeleton|Idle1")
                string nm = c.name.StartsWith("HAFCLIP|") ? c.name.Substring("HAFCLIP|".Length) : c.name;
                clipL.Add(c); nameL.Add(nm); pathL.Add(rel);
            }
        }
        clips = clipL.ToArray(); clipNames = nameL.ToArray(); clipPaths = pathL.ToArray();
        clipLabels = clips.Select((c, i) => $"{clipNames[i]}   (frames 0..{Mathf.RoundToInt(c.length * FPS)}, {c.length:0.0}s)").ToArray();
        if (clips.Length == 0) { ShowNotification(new GUIContent("No animation clips in the inspection FBXs.")); return; }
        clipIdx = Mathf.Max(0, Array.IndexOf(clipNames, wantClip));
        int total = TotalFrames;
        startF = wantS >= 0 ? Mathf.Clamp(wantS, 0, total) : 0;
        endF = wantE >= 0 ? Mathf.Clamp(wantE, 0, total) : total;
        frame = startF;
        DestroyInstance();
        Repaint();
    }

    bool BuildInspectFbx(string proj, string dirFull)
    {
        try
        {
            EditorUtility.DisplayProgressBar("Clip range picker", "Converting the model's clips to inspection FBXs (Blender)…", 0.4f);
            if (System.IO.Directory.Exists(dirFull))                                    // clear stale per-clip files (removed clips)
                foreach (var f in System.IO.Directory.GetFiles(dirFull, "*.fbx")) System.IO.File.Delete(f);
            var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = UniversalBaker.FindBlender();
            p.StartInfo.Arguments = $"--background --python \"{System.IO.Path.Combine(proj, "Tools", "inspect_fbx.py")}\" -- \"{modelFile}\" \"{dirFull}\"";
            p.StartInfo.UseShellExecute = false; p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true; p.StartInfo.RedirectStandardError = true;
            p.Start();
            string so = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            if (!p.WaitForExit(180000)) { try { p.Kill(); } catch { } Debug.LogError("[ClipRange] Blender inspect conversion timed out."); return false; }
            if (!System.IO.Directory.Exists(dirFull) || System.IO.Directory.GetFiles(dirFull, "*.fbx").Length == 0)
            { Debug.LogError("[ClipRange] no inspection FBXs produced:\n" + so); return false; }
            AssetDatabase.Refresh();
            return true;
        }
        catch (Exception e) { Debug.LogError("[ClipRange] " + e.Message); return false; }
        finally { EditorUtility.ClearProgressBar(); }
    }

    int TotalFrames => clips.Length == 0 ? 0 : Mathf.RoundToInt(clips[Mathf.Clamp(clipIdx, 0, clips.Length - 1)].length * FPS);

    void EnsureInstance()
    {
        string want = clips.Length > 0 ? clipPaths[Mathf.Clamp(clipIdx, 0, clipPaths.Length - 1)] : null;
        if (inst != null && instPath == want) return;
        DestroyInstance();
        if (want == null) return;
        instPath = want;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(want);
        if (prefab == null) return;
        inst = Instantiate(prefab);
        inst.hideFlags = HideFlags.HideAndDontSave;
        smrs.Clear(); mfs.Clear();
        smrs.AddRange(inst.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        mfs.AddRange(inst.GetComponentsInChildren<MeshFilter>(true));
        bakedMeshes = new Mesh[smrs.Count];
        for (int i = 0; i < bakedMeshes.Length; i++) bakedMeshes[i] = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        boundsValid = false;
    }

    void DestroyInstance()
    {
        if (inst != null) { DestroyImmediate(inst); inst = null; }
        foreach (var bm in bakedMeshes) if (bm != null) DestroyImmediate(bm);
        bakedMeshes = new Mesh[0];
        smrs.Clear(); mfs.Clear();
        boundsValid = false;
    }

    void OnEnable() { EditorApplication.update += Tick; lastTick = EditorApplication.timeSinceStartup; }
    void OnDisable()
    {
        EditorApplication.update -= Tick;
        DestroyInstance();
        if (pru != null) { try { pru.Cleanup(); } catch { } pru = null; }
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (playing && clips.Length > 0)
        {
            frame += (float)((now - lastTick) * FPS);
            int total = Mathf.Max(1, TotalFrames);
            if (frame > total) frame -= total;   // loop
            Repaint();
        }
        lastTick = now;
    }

    void SamplePose()
    {
        if (clips.Length == 0) return;
        EnsureInstance();
        if (inst == null) return;
        var clip = clips[Mathf.Clamp(clipIdx, 0, clips.Length - 1)];
        clip.SampleAnimation(inst, Mathf.Clamp(frame, 0, TotalFrames) / FPS);
        for (int i = 0; i < smrs.Count; i++)
            if (smrs[i] != null) smrs[i].BakeMesh(bakedMeshes[i]);
        if (!boundsValid)
        {
            bool first = true;
            for (int i = 0; i < smrs.Count; i++)
            {
                if (smrs[i] == null) continue;
                var b = TransformBounds(GlueMatrix(smrs[i].transform), bakedMeshes[i].bounds);
                if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
            }
            foreach (var mf in mfs)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var b = TransformBounds(mf.transform.localToWorldMatrix, mf.sharedMesh.bounds);
                if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
            }
            boundsValid = !first;
        }
    }

    // BakeMesh output already carries the renderer's scale — draw with rotation+position only (no double-scale).
    static Matrix4x4 GlueMatrix(Transform t) => Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

    static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        var c = m.MultiplyPoint3x4(b.center); var e = b.extents;
        var ne = new Vector3(
            Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
            Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
            Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
        return new Bounds(c, ne * 2f);
    }

    void OnGUI()
    {
        if (clips.Length == 0)
        {
            EditorGUILayout.HelpBox("No clips loaded — open this dialog from a clip field's ▶ button on an entry with a model file.", MessageType.Info);
            return;
        }
        int total = TotalFrames;

        int newIdx = EditorGUILayout.Popup("Clip", clipIdx, clipLabels);
        if (newIdx != clipIdx) { clipIdx = newIdx; total = TotalFrames; frame = 0; startF = 0; endF = total; boundsValid = false; }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(playing ? "❚❚ Pause" : "► Play", GUILayout.Width(80))) playing = !playing;
            EditorGUI.BeginChangeCheck();
            frame = GUILayout.HorizontalSlider(frame, 0, total);
            if (EditorGUI.EndChangeCheck()) playing = false;   // scrubbing pauses
            GUILayout.Label($"frame {Mathf.RoundToInt(frame)} / {total}", GUILayout.Width(110));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            startF = EditorGUILayout.IntField("Start", startF, GUILayout.Width(220));
            if (GUILayout.Button("◄ set current", GUILayout.Width(95))) startF = Mathf.RoundToInt(frame);
            GUILayout.Space(12);
            endF = EditorGUILayout.IntField("End", endF, GUILayout.Width(220));
            if (GUILayout.Button("◄ set current", GUILayout.Width(95))) endF = Mathf.RoundToInt(frame);
        }
        startF = Mathf.Clamp(startF, 0, total);
        endF = Mathf.Clamp(endF, 0, total);
        EditorGUILayout.HelpBox(
            "Play or scrub to find where a motion begins/ends, capture the numbers with 'set current'. " +
            "Start > End plays the slice REVERSED (a fold from an unfold); Start = End is a held stance. " +
            "Confirm writes the slice into the clip field.", MessageType.None);

        // preview (own camera: drag orbits, scroll zooms — the wheel is consumed here)
        var rect = GUILayoutUtility.GetRect(200, 330, GUILayout.ExpandWidth(true));
        HandlePreviewInput(rect);
        if (Event.current.type == EventType.Repaint) RenderPreview(rect);

        using (new EditorGUILayout.HorizontalScope())
        {
            string clipName = clipNames[clipIdx];
            string spec = (startF == 0 && endF == total) ? clipName : $"{clipName}[{startF}..{endF}]";
            if (GUILayout.Button("Confirm:   " + spec, GUILayout.Height(30)))
            { onConfirm?.Invoke(spec); Close(); }
            if (GUILayout.Button("Cancel", GUILayout.Height(30), GUILayout.Width(90))) Close();
        }
    }

    void HandlePreviewInput(Rect rect)
    {
        var e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;
        if (e.type == EventType.ScrollWheel)
        { zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f, e.delta.y > 0 ? 1f : -1f), 0.2f, 5f); e.Use(); Repaint(); }
        else if (e.type == EventType.MouseDrag && e.button == 0)
        { orbit += new Vector2(e.delta.x, -e.delta.y) * 0.7f; orbit.y = Mathf.Clamp(orbit.y, -89f, 89f); e.Use(); Repaint(); }
    }

    void RenderPreview(Rect rect)
    {
        SamplePose();
        if (!boundsValid) return;
        if (pru == null) pru = new PreviewRenderUtility();
        if (fallbackMat == null) fallbackMat = new Material(Shader.Find("Standard"));
        pru.BeginPreview(rect, GUIStyle.none);
        var cam = pru.camera;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
        float dist = radius * 2.0f * zoom;
        var rot = Quaternion.Euler(-orbit.y, orbit.x, 0f);
        cam.transform.position = bounds.center + rot * (Vector3.back * dist);
        cam.transform.rotation = Quaternion.LookRotation(bounds.center - cam.transform.position);
        cam.nearClipPlane = 0.01f; cam.farClipPlane = dist + radius * 4f; cam.fieldOfView = 30f;
        pru.lights[0].intensity = 1.3f;
        pru.lights[0].transform.rotation = Quaternion.Euler(45f, 45f, 0f);
        if (pru.lights.Length > 1) pru.lights[1].intensity = 0.6f;
        pru.ambientColor = new Color(0.3f, 0.3f, 0.3f);
        for (int i = 0; i < smrs.Count; i++)
        {
            if (smrs[i] == null) continue;
            var mats = smrs[i].sharedMaterials;
            var mtx = GlueMatrix(smrs[i].transform);
            for (int s = 0; s < bakedMeshes[i].subMeshCount; s++)
                pru.DrawMesh(bakedMeshes[i], mtx, mats != null && mats.Length > 0 ? (mats[Mathf.Min(s, mats.Length - 1)] ?? fallbackMat) : fallbackMat, s);
        }
        foreach (var mf in mfs)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var r = mf.GetComponent<MeshRenderer>();
            var mats = r != null ? r.sharedMaterials : null;
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                pru.DrawMesh(mf.sharedMesh, mf.transform.localToWorldMatrix, mats != null && mats.Length > 0 ? (mats[Mathf.Min(s, mats.Length - 1)] ?? fallbackMat) : fallbackMat, s);
        }
        cam.Render();
        GUI.DrawTexture(rect, pru.EndPreview(), ScaleMode.StretchToFill, false);
    }
}
