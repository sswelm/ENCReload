// ConversionGateTest.cs (ENC editor) — Tools > ENC > Tests > Bake Conversion Gate Test.
// The FOURTH regression guard (Factory-Manual §11): asserts the raw-rig CONVERSION invariants that the animated
// runtime silently requires (established by decompiling Amplitude's bake + runtime, and by the Combine-soldier
// campaign — each was once violated, each produced an in-game failure that took hours to diagnose by hand):
//   1. every baked bone's BindPose/Local scale == 1        (a scale sandwich displaces deep chains)
//   2. every bone's ParentIndex < its own index            (bones are sorted by NAME; consumers assume topological)
//   3. every clip curve entry is ROTATION-only             (translations don't survive Amplitude's clip format)
//   4. the clip actually carries the animation             (per-bone entries + a real frame count)
// Two fixtures, two menu items:
//   (litmus)            — the deterministic synthetic 12-deep chain (Tools/make_litmus.py, built via Blender on
//                         demand). Fast-ish, always available, covers the mechanics.
//   (registry converted models) — THE REAL RIGS: every registry model on the conversion path (animated +
//                         convertRig, e.g. the Combine soldier's location-keyed auto-rig — 62 bones, 342 frames,
//                         the full rest-normalization) baked under a throwaway name. The strongest net; needs the
//                         source model files on disk.
// Both bake through the SAME ConfigFor route as the Bake button and clean up after themselves; the registry is
// never touched. Slow (real Blender bakes) — pre-commit checks after touching rig_anim.py / UniversalBaker.
//   (deploy golden diff) — the DEPLOY-CONVERT models (deployConvert, not convertRig — the two above SKIP them):
//                         a golden-master diff of each model's converted rig vs Tools/deploy_golden/<res>.txt. This
//                         is what catches a per-model regression an invariant pass can't (the m114 crossed legs are
//                         still a "valid rotation-only clip"). Mirror of the CLI `bash Tools/deploy_regression.sh`.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class ConversionGateTest
{
    const string PREFIX = "__convgate__";

    [MenuItem("Tools/HAF/Tests/Bake Conversion Gate Test (litmus)")]
    public static void RunLitmus()
    {
        // --- fixture: synthesize the litmus rig if it isn't cached ---
        string litmus = Path.Combine(Path.GetTempPath(), "enc_litmus.glb");
        if (!File.Exists(litmus))
        {
            string script = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Tools", "make_litmus.py");
            if (!File.Exists(script)) { Debug.LogError("[ConvGate] Tools/make_litmus.py missing"); return; }
            string blender = UniversalBaker.FindBlender();
            if (string.IsNullOrEmpty(blender)) { Debug.LogError("[ConvGate] Blender not found — the gate needs it"); return; }
            var psi = new System.Diagnostics.ProcessStartInfo(blender, $"-b --python \"{script}\" -- \"{litmus}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            // Drain BOTH pipes concurrently via RunBounded: the old sequential ReadToEnd(stdout) then ReadToEnd(stderr)
            // deadlocks if Blender fills the stderr pipe buffer while we're blocked reading stdout (and the 180s
            // WaitForExit came AFTER, so it never armed) → the gate-test menu could freeze the whole editor.
            using (var p = System.Diagnostics.Process.Start(psi))
                if (!UniversalBaker.RunBounded(p, 180000, out string _, out string _)) { Debug.LogError("[ConvGate] litmus synthesis: Blender timed out (3 min)"); return; }
            if (!File.Exists(litmus)) { Debug.LogError("[ConvGate] litmus synthesis produced no GLB"); return; }
        }
        var def = new ModelDef
        {
            resourceName = "litmus", pawnDescription = "__convgate_dummy__", modelFile = litmus.Replace('\\', '/'),
            animated = true, convertRig = true, animClip = "", rotation = new Vector3(90f, 0f, 0f), size = 2f,
            targetTris = 5000, atlasMaxDim = 256, materialMode = MaterialMode.Auto
        };
        int fails = BakeAndAssert(def);
        Debug.Log(fails == 0
            ? "[ConvGate] LITMUS PASS — conversion invariants hold (all scales 1, parents before children, rotation-only clip)."
            : $"[ConvGate] LITMUS: {fails} FAILURE(S) — the conversion pipeline regressed; see errors above.");
        EditorUtility.DisplayDialog("Conversion Gate — litmus",
            fails == 0 ? "PASS — conversion invariants hold (scales 1, parents before children, rotation-only clip)."
                       : $"{fails} FAILURE(S) — the conversion pipeline regressed. See the Console for detail.", "OK");
    }

    [MenuItem("Tools/HAF/Tests/Bake Conversion Gate Test (registry converted models)")]
    public static void RunRegistryConverted()
    {
        // The REAL adversarial fixtures: every registry model on the conversion path (the Combine soldier's
        // location-keyed ValveBiped being the canonical one). Skips models whose source file is gone.
        var defs = ModelRegistry.Load().Where(d => d.animated && d.convertRig
                                               && !d.resourceName.StartsWith(PREFIX)).ToList();
        if (defs.Count == 0) { Debug.LogWarning("[ConvGate] no converted models in the registry (animated + 'Convert raw rig') — nothing to test."); return; }
        int total = 0, tested = 0, skipped = 0;
        var failed = new System.Collections.Generic.List<string>();
        foreach (var src in defs)
        {
            if (string.IsNullOrWhiteSpace(src.modelFile) || !File.Exists(src.modelFile))
            { Debug.LogWarning($"[ConvGate] SKIP {src.resourceName} — source model file not on disk ({src.modelFile})"); skipped++; continue; }
            var clone = JsonUtility.FromJson<ModelDef>(JsonUtility.ToJson(src));   // never mutate the real entry
            int fails = BakeAndAssert(clone);
            Debug.Log(fails == 0
                ? $"[ConvGate] {src.resourceName}: PASS (full conversion on the real rig)"
                : $"[ConvGate] {src.resourceName}: {fails} FAILURE(S) — see errors above.");
            if (fails > 0) failed.Add($"{src.resourceName} ({fails})");
            total += fails; tested++;
        }
        Debug.Log($"[ConvGate] registry converted models: {tested} tested, {total} total failure(s).");
        EditorUtility.DisplayDialog("Conversion Gate — registry converted models",
            total == 0 ? $"PASS — {tested} model(s) tested, no conversion-invariant failures{(skipped > 0 ? $" ({skipped} skipped: source not on disk)" : "")}."
                       : $"{total} failure(s) across {failed.Count} model(s):\n  {string.Join("\n  ", failed)}\n\nSee the Console for the per-check detail.", "OK");
    }

    // THE DEPLOY-CONVERT GATE (2026-08-01) — the two variants above test convertRig rigs and INVARIANTS; they skip
    // deployConvert models (m114 howitzers, T-62) AND an invariant pass can't catch a per-model regression (the m114's
    // crossed legs are still a "valid rotation-only clip"). This is a GOLDEN-MASTER diff: re-run deploy_convert on every
    // model's recorded args (FactorySource/<res>/deploy_converted.args.txt) and compare a deterministic bone snapshot
    // (deploy_bonedump.py: armature name = legacy/contract path, bone count, per-bone rot+loc) against the blessed
    // Tools/deploy_golden/<res>.txt. A FAIL on a model you didn't mean to touch IS the regression (the T-62 contract that
    // broke the m114). Shares scripts + goldens with the CLI form `bash Tools/deploy_regression.sh` (which prints the
    // line-level diff and can (re)capture goldens with --capture).
    [MenuItem("Tools/HAF/Tests/Bake Conversion Gate Test (deploy golden diff)")]
    public static void RunDeployGolden()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string blender = UniversalBaker.FindBlender();
        if (string.IsNullOrEmpty(blender)) { Debug.LogError("[ConvGate] Blender not found — the deploy golden diff needs it"); return; }
        string convert = Path.Combine(root, "Tools", "deploy_convert.py");
        string dump = Path.Combine(root, "Tools", "deploy_bonedump.py");
        string goldDir = Path.Combine(root, "Tools", "deploy_golden");
        string fsRoot = Path.Combine(root, "Assets", "FactorySource");
        if (!File.Exists(convert) || !File.Exists(dump)) { Debug.LogError("[ConvGate] Tools/deploy_convert.py or deploy_bonedump.py missing"); return; }
        if (!Directory.Exists(fsRoot)) { Debug.LogWarning("[ConvGate] no Assets/FactorySource — no deploy-convert models to test"); return; }
        var argFiles = Directory.GetFiles(fsRoot, "deploy_converted.args.txt", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
        if (argFiles.Length == 0) { Debug.LogWarning("[ConvGate] no deploy-convert models (FactorySource/*/deploy_converted.args.txt)"); return; }
        int pass = 0, fail = 0, miss = 0;
        foreach (var af in argFiles)
        {
            string res = new DirectoryInfo(Path.GetDirectoryName(af)).Name;
            var fields = File.ReadAllText(af).Trim().Split('|');   // source | srcMtime | toolMtime | 14 args
            if (fields.Length < 4) { Debug.LogWarning($"[ConvGate] SKIP {res} — malformed args.txt"); continue; }
            string src = fields[0];
            if (!File.Exists(src)) { Debug.LogWarning($"[ConvGate] SKIP {res} — source missing ({src})"); continue; }
            string qargs = string.Join(" ", fields.Skip(3).Select(a => "\"" + a + "\""));
            string outGlb = Path.Combine(Path.GetTempPath(), "convgate_" + res + ".glb");
            try { if (File.Exists(outGlb)) File.Delete(outGlb); } catch { }
            string convOut = RunBlenderCapture(blender, $"-b --python \"{convert}\" -- \"{src}\" \"{outGlb}\" {qargs}");
            if (!File.Exists(outGlb) || !convOut.Contains("DEPLOY wrote:"))
            { Debug.LogError($"[ConvGate] {res}: FAIL — deploy_convert did not complete (see the DEPLOY log)"); fail++; continue; }
            string got = FilterDump(RunBlenderCapture(blender, $"-b --python \"{dump}\" -- \"{outGlb}\""));
            try { File.Delete(outGlb); } catch { }
            string goldFile = Path.Combine(goldDir, res + ".txt");
            if (!File.Exists(goldFile)) { Debug.LogWarning($"[ConvGate] {res}: NO GOLDEN — run `bash Tools/deploy_regression.sh --capture` once"); miss++; continue; }
            if (Norm(got) == Norm(File.ReadAllText(goldFile))) { Debug.Log($"[ConvGate] {res}: PASS (deploy golden match)"); pass++; }
            else { Debug.LogError($"[ConvGate] {res}: FAIL — converted rig CHANGED vs golden. Run `bash Tools/deploy_regression.sh` for the line diff."); fail++; }
        }
        Debug.Log($"[ConvGate] deploy golden diff: {pass} pass, {fail} fail, {miss} missing golden (of {argFiles.Length} models).");
        EditorUtility.DisplayDialog("Conversion Gate — deploy golden diff",
            (fail == 0 && miss == 0) ? $"PASS — {pass} deploy-convert model(s) match their golden."
                                     : $"{pass} pass, {fail} FAIL, {miss} missing golden.\nSee the Console; run `bash Tools/deploy_regression.sh` for the line-level diff.", "OK");
    }

    static string RunBlenderCapture(string blender, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(blender, args)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Concurrent pipe drain (RunBounded) — a sequential ReadToEnd(stdout)+ReadToEnd(stderr) deadlocks when Blender
        // fills the stderr buffer while we're blocked on stdout; RunBounded reads both on background tasks + bounds the wait.
        using (var p = System.Diagnostics.Process.Start(psi))
        { UniversalBaker.RunBounded(p, 180000, out string so, out string _); return so; }
    }

    // Keep only the deterministic snapshot lines (same set the CLI greps), so C# and bash compare identically.
    static string FilterDump(string s)
    {
        var keep = new System.Text.StringBuilder();
        foreach (var line in s.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.TrimEnd();
            if (t.StartsWith("ARMATURE") || t.StartsWith("BONES") || t.StartsWith("FRAMES") ||
                (t.Length > 1 && t[0] == 'f' && char.IsDigit(t[1]))) keep.Append(t).Append('\n');
        }
        return keep.ToString();
    }

    static string Norm(string s) => s.Replace("\r\n", "\n").TrimEnd('\n', ' ', '\t') + "\n";

    // Bake `def` under a throwaway name via the SAME ConfigFor route as the Bake button, assert every conversion
    // invariant on the baked Amplitude assets (reflection — they're Amplitude's types), clean up. Returns failures.
    static int BakeAndAssert(ModelDef def)
    {
        string testName = PREFIX + def.resourceName;
        def.resourceName = testName;
        int fails = 0;
        try
        {
            var cfg = ModelFactoryWindow.ConfigFor(def);
            var r = UniversalBaker.BuildAnimated(cfg);
            if (!r.ok) { Debug.LogError($"[ConvGate] {testName}: bake errored: " + r.error); return 1; }

            // --- skeleton invariants: scale-1 everywhere + parents before children ---
            var skel = AssetDatabase.LoadAllAssetsAtPath($"Assets/Resources/{testName}_Skeleton.asset")
                .FirstOrDefault(o => o != null && o.GetType().Name == "Skeleton");
            int boneCount = 0;
            if (skel == null) { Debug.LogError($"[ConvGate] {testName}: no baked Skeleton asset"); fails++; }
            else
            {
                var bones = (Array)skel.GetType().GetField("BoneInfos", BindingFlags.Public | BindingFlags.Instance)?.GetValue(skel);
                if (bones == null || bones.Length == 0) { Debug.LogError($"[ConvGate] {testName}: Skeleton has no BoneInfos"); fails++; }
                else
                {
                    boneCount = bones.Length;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        object bi = bones.GetValue(i);
                        float sBind = TrsScale(Member(bi, "BindPose")), sLocal = TrsScale(Member(bi, "Local"));
                        if (Mathf.Abs(sBind - 1f) > 0.01f || Mathf.Abs(sLocal - 1f) > 0.01f)
                        { Debug.LogError($"[ConvGate] {testName}: bone {i} scale != 1 (bind {sBind:0.####}, local {sLocal:0.####}) — the scale sandwich is back"); fails++; }
                        int parent = Convert.ToInt32(Member(bi, "ParentIndex"));
                        if (parent >= i) { Debug.LogError($"[ConvGate] {testName}: bone {i} has ParentIndex {parent} (parents must sort before children)"); fails++; }
                    }
                }
            }

            // --- clip invariants: rotation-only, one entry per bone, a real frame count ---
            var clips = AssetDatabase.LoadAllAssetsAtPath($"Assets/Resources/{testName}_Clips.asset")
                .FirstOrDefault(o => o != null && o.GetType().Name == "ClipCollection");
            if (clips == null) { Debug.LogError($"[ConvGate] {testName}: no baked ClipCollection asset"); fails++; }
            else
            {
                var curves = (Array)clips.GetType().GetProperty("AnimationClipCurveEntries")?.GetValue(clips);
                if (curves == null || curves.Length == 0) { Debug.LogError($"[ConvGate] {testName}: no clip curve entries"); fails++; }
                else
                {
                    if (boneCount > 0 && curves.Length != boneCount)
                    { Debug.LogError($"[ConvGate] {testName}: {curves.Length} curve entries for {boneCount} bones — the runtime addresses animId+boneIndex, these MUST match"); fails++; }
                    foreach (object e in curves)
                    {
                        string fmt = Member(e, "EncodingFormat")?.ToString() ?? "?";
                        if (fmt != "Rotation" && fmt != "Fixe")
                        { Debug.LogError($"[ConvGate] {testName}: bone {Member(e, "BoneIndex")} encoded as {fmt} (conversion must yield rotation-only clips)"); fails++; }
                    }
                }
                // FrameCount — the animation must actually bake. BUT a STATE-DRIVEN model's PRIMARY clip is often a
                // DELIBERATE single-frame held stance (a spin-vehicle's Spin[0..0] idle; the motion lives in the MOVE
                // role) — so for those, frame-check the MOVE clip, not the intentional single-frame primary. (Without
                // this, TankDestroyers / AntiTankIFV / ArmouredCar — all Spin[0..0] + Spin — cried wolf though they
                // bake and run correctly in-game.)
                string motionAsset = def.animStateDriven
                    ? $"Assets/Resources/{testName}_ClipsMove.asset" : $"Assets/Resources/{testName}_Clips.asset";
                string which = def.animStateDriven ? "MOVE" : "primary";
                var motion = AssetDatabase.LoadAllAssetsAtPath(motionAsset).FirstOrDefault(o => o != null && o.GetType().Name == "ClipCollection");
                if (motion == null) { Debug.LogError($"[ConvGate] {testName}: no {which} ClipCollection to frame-check ({motionAsset})"); fails++; }
                else
                {
                    var entries = (Array)motion.GetType().GetProperty("AnimationClipEntries")?.GetValue(motion);
                    if (entries != null && entries.Length > 0)
                    {
                        int frameCount = Convert.ToInt32(Member(entries.GetValue(0), "FrameCount"));
                        if (frameCount < 2) { Debug.LogError($"[ConvGate] {testName}: {which} clip FrameCount {frameCount} — the animation didn't bake"); fails++; }
                    }
                    else { Debug.LogError($"[ConvGate] {testName}: no ClipEntry in the {which} clip (animation missing entirely)"); fails++; }
                }
            }
            return fails;
        }
        catch (Exception ex) { Debug.LogError($"[ConvGate] {testName}: exception {ex.GetType().Name}: {ex.Message}"); return fails + 1; }
        finally
        {
            foreach (var suffix in new[] {
                "_Skeleton.asset", "_Atlas.asset", "_PreviewMesh.asset",
                "_Clips.asset", "_ClipsPoseData.bytes",
                "_ClipsMove.asset", "_ClipsMovePoseData.bytes", "_ClipsAfter.asset", "_ClipsAfterPoseData.bytes",
                "_ClipsAttack.asset", "_ClipsAttackPoseData.bytes", "_ClipsPreMove.asset", "_ClipsPreMovePoseData.bytes",
                "_ClipsIdle.asset", "_ClipsIdlePoseData.bytes", "_ClipsCombat.asset", "_ClipsCombatPoseData.bytes",
                "_ClipsIdleAlt.asset", "_ClipsIdleAltPoseData.bytes", "_ClipsIdleAlt2.asset", "_ClipsIdleAlt2PoseData.bytes" })
                AssetDatabase.DeleteAsset($"Assets/Resources/{testName}{suffix}");
            AssetDatabase.DeleteAsset($"Assets/FactorySource/{testName}");
            AssetDatabase.Refresh();
        }
    }

    static object Member(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        return (object)t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o)
            ?? t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
    }

    // TRS.Scale as float regardless of the exact TRS type
    static float TrsScale(object trs)
    {
        var s = Member(trs, "Scale");
        try { return Convert.ToSingle(s); } catch { return 1f; }
    }
}
