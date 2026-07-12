using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Bake smoke test — the integration guard for the Model Factory.
//
// It re-bakes ONE representative model per bake-path (animated × material mode — that pairing is what selects the code
// path) and asserts each bake (a) completes without throwing and (b) produces non-empty _ModelMesh / _Skeleton / _Atlas
// assets. It bakes through ModelFactoryWindow.ConfigFor — the SAME config path the Bake button uses — so it can't drift
// from what ships. This is the check that would have caught the multi-material tangent regression (which threw inside
// Amplitude's MeshCollection.ImportMeshes) before it left the editor: unit tests can't reach that Unity/Amplitude seam,
// a real bake does. See ENCAccessProof/docs/Framework-Review.md "Testing strategy".
//
// NOTE: it genuinely RE-BAKES the representative models (regenerating their assets from their stored config — idempotent,
// same config => same output) and it is SLOW (each animated bake runs Blender). Run it before committing baker changes,
// not every save.
public static class BakeSmokeTest
{
    [MenuItem("Tools/ENC/Bake Smoke Test (one per path)")]
    public static void RunRepresentatives()
    {
        var defs = ModelRegistry.Load();
        if (defs == null || defs.Count == 0) { EditorUtility.DisplayDialog("Bake Smoke Test", "No models in the registry.", "OK"); return; }
        // one per (animated, materialMode) bucket — the pairing that determines the bake path
        var reps = defs.GroupBy(d => (d.animated, d.materialMode)).Select(g => g.First()).ToList();
        Run(reps, "one per bake-path");
    }

    [MenuItem("Tools/ENC/Bake Smoke Test (ALL models)")]
    public static void RunAll()
    {
        var defs = ModelRegistry.Load();
        if (defs == null || defs.Count == 0) { EditorUtility.DisplayDialog("Bake Smoke Test", "No models in the registry.", "OK"); return; }
        Run(defs, "all models");
    }

    static void Run(List<ModelDef> models, string scope)
    {
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        try
        {
            for (int i = 0; i < models.Count; i++)
            {
                var cur = models[i];
                EditorUtility.DisplayProgressBar("Bake Smoke Test", $"Baking {cur.resourceName} ({i + 1}/{models.Count})…", (float)i / models.Count);
                string tag = (cur.animated ? "animated" : "static") + "/" + cur.materialMode;
                string result;
                try
                {
                    var cfg = ModelFactoryWindow.ConfigFor(cur);
                    var r = cfg.animated ? UniversalBaker.BuildAnimated(cfg) : UniversalBaker.Build(cfg);
                    if (!r.ok) result = "FAIL — bake error: " + r.error;
                    else
                    {
                        var missing = MissingAssets(cur.resourceName);
                        result = missing.Count == 0 ? "PASS" : "FAIL — missing/empty: " + string.Join(", ", missing);
                    }
                }
                catch (Exception ex) { result = "FAIL — exception: " + ex.GetType().Name + ": " + ex.Message; }

                if (result == "PASS") pass++; else fail++;
                sb.AppendLine($"[{tag}] {cur.resourceName}: {result}");
            }
        }
        finally { EditorUtility.ClearProgressBar(); AssetDatabase.Refresh(); }

        string head = $"Bake Smoke Test ({scope}) — {pass} passed, {fail} failed of {models.Count}\n\n";
        if (fail > 0) Debug.LogError("[SmokeTest]\n" + head + sb); else Debug.Log("[SmokeTest]\n" + head + sb);
        EditorUtility.DisplayDialog("Bake Smoke Test", head + sb + (fail > 0 ? "\nSee Console for the full error(s)." : ""), "OK");
    }

    // A bake-path passes only if all three shipped assets exist and aren't empty stubs (a failed bake can leave a
    // tiny/blank asset behind). 1 KB floor: real meshes/skeletons are 100s of KB–MB, a 256 DXT1 atlas is ~32 KB.
    static List<string> MissingAssets(string name)
    {
        var bad = new List<string>();
        string root = Directory.GetParent(Application.dataPath).FullName;
        foreach (var suffix in new[] { "_ModelMesh", "_Skeleton", "_Atlas" })
        {
            string full = Path.Combine(root, "Assets", "Resources", name + suffix + ".asset");
            if (!File.Exists(full) || new FileInfo(full).Length < 1024) bad.Add(name + suffix);
        }
        return bad;
    }
}
