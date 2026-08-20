// BakeTestRunnerWindow.cs — THE central testing suite (Tools ▸ HAF ▸ Bake Tests…).
//
// Seven bare menu items ("Bake Conversion Gate Test (litmus)"?) meant nobody could tell what a test did — or which
// to run — without reading source (user, 2026-08-20: "this looks ridiculous… we need a specialized testing dialog
// with clear explanation what we are testing… the center testing suite with clear UI feedback"). This window
// replaces ALL of them:
//   * every bake integration test is one ROW — a plain-language what-it-tests, what it costs, a checkbox,
//   * Quick/Everything presets and ONE Run button,
//   * LIVE feedback: tests run one per editor tick (a delayCall queue), so each row flips to its PASS/FAIL result
//     as it finishes and the current row shows RUNNING — no frozen mystery until a single end dialog,
//   * per-row expandable detail (the full per-model lines, in the window — the Console keeps the deep errors),
//   * one durable report per run: Logs/haf_bake_tests_report.txt (the editor twin of the runtime's
//     haf_smoke_report.txt), so "did the tests pass before this release?" has an answer after the window closes.
// The tests themselves live unchanged in BakeSmokeTest / BakeFeatureTest / ConversionGateTest — they just return a
// BakeTestSection now instead of each talking to its own dialog.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// What every bake test hands back: counts + the human-readable detail its dialog used to show.
public class BakeTestSection
{
    public string title;
    public int pass, fail, skip;
    public string body;
}

public class BakeTestRunnerWindow : EditorWindow
{
    class TestRow
    {
        public string name;            // row title (also the report section title)
        public string what;            // plain language: WHAT is being tested and how
        public string cost;            // what running it costs (time / dependencies)
        public bool needsBlender;      // auto-skipped when Blender is missing
        public bool quick;             // part of the "Quick" preset
        public string group;           // rows sharing a group are mutually exclusive (radio behavior)
        public bool thorough;          // the group member the "Everything" preset picks
        public bool on;                // checkbox state
        public Func<BakeTestSection> run;
        public BakeTestSection last;   // result of the most recent run (this session)
        public bool open;              // detail foldout
    }

    List<TestRow> rows;
    Vector2 scroll;
    string lastReportPath, lastVerdict;
    GUIStyle wrap, mono, wrapBold;

    // Live-run state: the queue makes each test its own editor tick, so the window repaints between tests and the
    // user watches rows turn green/red instead of staring at a frozen editor until one big dialog.
    Queue<TestRow> pending;
    TestRow current;
    List<BakeTestSection> collected;
    System.Diagnostics.Stopwatch runWatch;
    bool blenderAtRunStart;

    [MenuItem("Tools/HAF/Bake Tests…", false, 30)]
    static void Open() => GetWindow<BakeTestRunnerWindow>("Bake Tests");

    void OnEnable()
    {
        rows = new List<TestRow>
        {
            new TestRow { name = "Does the baker still work? (one model per path)", quick = true, on = true, needsBlender = true, group = "smoke",
                cost = "a handful of real bakes (~minutes)",
                what = "Re-bakes ONE representative model per bake path (static / animated / rig-converted, per material " +
                       "mode) under a throwaway name and checks the baked assets exist and are not empty stubs. The " +
                       "quick \"did I break the baker?\" check after baker changes. (The 'smoke test'.)",
                run = BakeSmokeTest.RunRepresentativesSection },

            new TestRow { name = "Does every model still bake? (whole catalog)", needsBlender = true, group = "smoke", thorough = true,
                cost = "one full bake per registry model — slow",
                what = "The same check as the row above, but for EVERY registry entry, not just representatives. " +
                       "Mutually exclusive with that row (this one already covers everything it bakes). Run before a release.",
                run = BakeSmokeTest.RunAllSection },

            new TestRow { name = "Do the bake options do what they claim? (synthetic cubes)", quick = true, on = true,
                cost = "~15 fast cube bakes, no Blender",
                what = "Bakes tiny synthetic cubes with one baker option toggled at a time — double-sided, normal modes, " +
                       "heightUV, atlas size cap, size, position offset, winding fix, multi-material, brightness/" +
                       "saturation — and asserts each one measurably changed the baked result. Also proves the rollback " +
                       "safety net restores your assets after a FAILED re-bake. (The 'feature test', Tier 1.)",
                run = BakeFeatureTest.RunTier1Section },

            new TestRow { name = "Do the Blender + animation options work? (real rigs)", needsBlender = true,
                cost = "real Blender bakes — slow",
                what = "The options a cube can't exercise: triangle-budget decimation (targetTris), removing a named " +
                       "part (stripParts), and the full ANIMATED pipeline end-to-end on two real rigged models borrowed " +
                       "from the registry (skeleton + clip must come out). (The 'feature test', Tier 2.)",
                run = BakeFeatureTest.RunTier2Section },

            new TestRow { name = "Is rig conversion still correct? (control rig)", quick = true, on = true, needsBlender = true,
                cost = "one synthetic rig bake (fast after the first run)",
                what = "Synthesizes a known 12-bone test rig (the 'litmus'), bakes it through the raw-rig conversion, " +
                       "and checks the four invariants the game silently requires: every bone scale exactly 1, parents " +
                       "sorted before children, rotation-only clips, and the animation actually baked. Each invariant " +
                       "was once violated and cost hours of in-game diagnosis. This is the CONTROL for the row below: " +
                       "a synthetic rig separates 'the pipeline broke' from 'this model broke'.",
                run = ConversionGateTest.RunLitmusSection },

            new TestRow { name = "Do the real rigs still convert correctly? (every converted model)", needsBlender = true,
                cost = "a full conversion bake per converted model — slow",
                what = "The same four invariants, but on every REAL converted rig in the registry (animated + 'Convert " +
                       "raw rig', e.g. the Combine soldier's 62-bone auto-rig). The strongest net; needs each source " +
                       "model file on disk. COMPLEMENTS the control-rig row (different fixtures, nothing baked twice): a " +
                       "real rig failing while the control passes points at the model, not the pipeline.",
                run = ConversionGateTest.RunRegistryConvertedSection },

            new TestRow { name = "Did a deploy model change unexpectedly? (golden snapshot)", needsBlender = true,
                cost = "one Blender conversion + bone dump per deploy model",
                what = "Re-runs the deploy conversion for every deploy-converted model (the m114 howitzers, T-62) and " +
                       "diffs the resulting bone poses against a blessed golden snapshot. Catches the per-model " +
                       "regressions the invariant checks can't (the crossed-legs class of bug). NO overlap with the two " +
                       "rows above — they SKIP deploy-convert models entirely.",
                run = ConversionGateTest.RunDeployGoldenSection },
        };
    }

    // Closing the window (or a domain reload) kills the run: the delayCall chain checks `pending` and stops.
    void OnDisable() { pending = null; current = null; }

    bool Running => pending != null;

    void OnGUI()
    {
        if (wrap == null) wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
        if (mono == null) mono = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, font = EditorStyles.miniFont };
        if (wrapBold == null) wrapBold = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };   // titles WRAP, never clip
        bool blender = UniversalBaker.BlenderAvailable();

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "Integration tests that run REAL bakes. All of them are non-destructive: everything bakes under throwaway " +
            "names — your models, assets and registry are never touched. Results appear on each row (expand for " +
            "detail), in the Console, and in Logs/haf_bake_tests_report.txt.", MessageType.Info);
        if (!blender)
            EditorGUILayout.HelpBox("Blender not found — rows marked 'needs Blender' will be skipped.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(Running))
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Select:", GUILayout.Width(44));
            if (GUILayout.Button("Quick set", GUILayout.Width(90))) foreach (var r in rows) r.on = r.quick;
            // "Everything" honors the exclusive groups: it picks the thorough member (ALL models), not both scopes.
            if (GUILayout.Button("Everything", GUILayout.Width(90))) foreach (var r in rows) r.on = r.group == null || r.thorough;
            if (GUILayout.Button("None", GUILayout.Width(60))) foreach (var r in rows) r.on = false;
        }
        EditorGUILayout.Space(2);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var r in rows)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(Running))
                    {
                        bool was = r.on;
                        r.on = EditorGUILayout.ToggleLeft(r.name, r.on, wrapBold);
                        if (r.on && !was && r.group != null)   // radio behavior inside a group: checking one unchecks the rest
                            foreach (var other in rows)
                                if (other != r && other.group == r.group) other.on = false;
                    }
                    GUILayout.FlexibleSpace();
                    var keep = GUI.color;
                    if (Running && r == current)
                    { GUI.color = new Color(0.5f, 0.8f, 1f); GUILayout.Label("RUNNING…", EditorStyles.boldLabel); }
                    else if (Running && pending.Contains(r))
                    { GUI.color = new Color(0.7f, 0.7f, 0.7f); GUILayout.Label("queued", EditorStyles.boldLabel); }
                    else if (r.last != null)
                    {
                        GUI.color = r.last.fail > 0 ? new Color(1f, 0.45f, 0.45f)
                                  : r.last.pass > 0 ? new Color(0.45f, 1f, 0.45f) : new Color(1f, 0.85f, 0.4f);
                        GUILayout.Label(ResultLabel(r.last), EditorStyles.boldLabel);
                    }
                    GUI.color = keep;
                }
                EditorGUILayout.LabelField(r.what, wrap);
                EditorGUILayout.LabelField("Costs: " + r.cost + (r.needsBlender ? "  •  needs Blender" : ""), EditorStyles.miniLabel);
                if (r.last != null && !string.IsNullOrEmpty(r.last.body))
                {
                    r.open = EditorGUILayout.Foldout(r.open, "details", true);
                    if (r.open)
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            foreach (var line in r.last.body.Split('\n'))
                                EditorGUILayout.LabelField(line, mono);
                }
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(2);
        int selected = rows.Count(x => x.on);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Running || selected == 0))
                if (GUILayout.Button(Running ? "Running…" : selected == 0 ? "Run (nothing selected)" : $"Run {selected} selected test(s)", GUILayout.Height(28)))
                    StartRun(blender);
            if (!string.IsNullOrEmpty(lastReportPath))
                if (GUILayout.Button("Open report", GUILayout.Width(100), GUILayout.Height(28)))
                    EditorUtility.OpenWithDefaultApp(lastReportPath);
        }
        if (Running)
            EditorGUILayout.LabelField($"Running {current?.name}…  ({collected.Count} done, {pending.Count} to go — the editor freezes during each test, results appear between them)", EditorStyles.boldLabel);
        else if (!string.IsNullOrEmpty(lastVerdict))
            EditorGUILayout.LabelField(lastVerdict, EditorStyles.boldLabel);
        EditorGUILayout.Space(2);
    }

    void StartRun(bool blender)
    {
        pending = new Queue<TestRow>(rows.Where(x => x.on));
        collected = new List<BakeTestSection>();
        current = null;
        blenderAtRunStart = blender;
        runWatch = System.Diagnostics.Stopwatch.StartNew();
        lastVerdict = null;
        foreach (var r in rows) if (r.on) r.last = null;
        EditorApplication.delayCall += StepAnnounce;   // two-phase ticks: announce (paint "RUNNING…"), then execute
    }

    // Phase A: mark the next test as current and let the window PAINT that state before the synchronous bake work
    // freezes the editor — this is what makes the feedback live instead of one long freeze.
    void StepAnnounce()
    {
        if (pending == null) return;   // window closed / domain reloaded mid-run
        if (pending.Count == 0) { FinishRun(); return; }
        current = pending.Peek();
        Repaint();
        EditorApplication.delayCall += StepExecute;
    }

    void StepExecute()
    {
        if (pending == null || pending.Count == 0) return;
        var r = pending.Dequeue();
        current = r;
        if (r.needsBlender && !blenderAtRunStart)
            r.last = new BakeTestSection { title = r.name, skip = 1, body = "SKIP — Blender not found." };
        else
        {
            Debug.Log("[BakeTests] running: " + r.name + "…");
            try { r.last = r.run(); r.last.title = r.name; }
            catch (Exception ex)
            { r.last = new BakeTestSection { title = r.name, fail = 1, body = "harness exception: " + ex.GetType().Name + ": " + ex.Message }; }
            Debug.Log("[BakeTests] " + r.name + ": " + ResultLabel(r.last) + "\n" + r.last.body);
        }
        collected.Add(r.last);
        if (r.last.fail > 0) r.open = true;   // failures unfold themselves — the detail is the point
        current = null;
        Repaint();
        EditorApplication.delayCall += StepAnnounce;
    }

    void FinishRun()
    {
        runWatch.Stop();
        int pass = collected.Sum(s => s.pass), fail = collected.Sum(s => s.fail), skip = collected.Sum(s => s.skip);
        lastVerdict = FormattableString.Invariant(
            $"{(fail == 0 ? "PASS" : "FAIL")} — {pass} passed, {fail} failed, {skip} skipped, in {runWatch.Elapsed.TotalMinutes:0.0} min");
        lastReportPath = WriteReport(collected, lastVerdict);
        Debug.Log("[BakeTests] " + lastVerdict + " — report: " + lastReportPath);
        pending = null; current = null;
        Repaint();
    }

    static string ResultLabel(BakeTestSection s) =>
        s.fail > 0 ? $"FAIL — {s.fail} failed, {s.pass} passed"
        : s.pass > 0 ? $"PASS — {s.pass} passed" + (s.skip > 0 ? $", {s.skip} skipped" : "")
        : "SKIPPED";

    // One durable record per run (overwritten each run — git/backup history is not the job of a test artifact).
    static string WriteReport(List<BakeTestSection> sections, string verdict)
    {
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
        string path = Path.Combine(dir, "haf_bake_tests_report.txt");
        try
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine(FormattableString.Invariant($"HAF bake-test report — {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));
            sb.AppendLine(verdict);
            sb.AppendLine();
            foreach (var s in sections)
            {
                sb.AppendLine("== " + s.title + ": " + ResultLabel(s));
                if (!string.IsNullOrEmpty(s.body)) sb.AppendLine(s.body.TrimEnd());
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch (Exception ex) { Debug.LogWarning("[BakeTests] could not write the report: " + ex.Message); return null; }
    }
}
