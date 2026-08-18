using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// SHIP STATUS (user request 2026-08-18, born from the HandCrankedSubmarine pre-flight catch: re-bake at 19:34,
// mod build at 19:29 — the game resolved a dead skeleton GUID because the shipped mod predated the bake by five
// minutes). Baked Resources assets ONLY reach the game through a mod build; the registry + skins + sounds are
// read straight from BepInEx/config and never go stale this way. This window answers the one question the trap
// hides: "which bakes has the game not seen yet?" The Model Factory shows the same verdict inline for the
// selected entry (ShipStatus.IsBakedNotShipped) — one shared core, so the two surfaces can never disagree.
internal static class ShipStatus
{
    internal const string CommunityDir = @"C:\GameData\Humankind\Community";   // matches HafCli.CleanExport

    // Newest file inside the newest ENCReload.* build (dir mtime alone lies — deploy copies preserve it).
    internal static DateTime? LastBuildUtc(out string buildDir)
    {
        buildDir = null;
        try
        {
            if (!Directory.Exists(CommunityDir)) return null;
            buildDir = Directory.GetDirectories(CommunityDir, "ENCReload.*")
                                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).FirstOrDefault();
            if (buildDir == null) return null;
            var files = Directory.GetFiles(buildDir, "*", SearchOption.AllDirectories);
            return files.Length == 0 ? Directory.GetLastWriteTimeUtc(buildDir)
                                     : files.Max(f => File.GetLastWriteTimeUtc(f));
        }
        catch { return null; }
    }

    static string ResourcesFull => Path.Combine(Application.dataPath, "Resources");

    // Newest baked output for `name`, using the baker's own whitelist (UniversalBaker.OutputSuffixes) so this
    // can never drift from what a bake actually writes/ships. null = no baked outputs at all.
    internal static DateTime? LastBakeUtc(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        DateTime? newest = null;
        foreach (var s in UniversalBaker.OutputSuffixes)
        {
            var p = Path.Combine(ResourcesFull, name + s);
            if (!File.Exists(p)) continue;
            var t = File.GetLastWriteTimeUtc(p);
            if (newest == null || t > newest) newest = t;
        }
        return newest;
    }

    // The Factory's inline verdict for one entry. False when never baked or no build exists yet (those are
    // different problems with different fixes — the WINDOW distinguishes them; the inline flag is only the trap).
    internal static bool IsBakedNotShipped(string name)
    {
        var bake = LastBakeUtc(name);
        if (bake == null) return false;
        var build = LastBuildUtc(out _);
        return build == null || bake > build;
    }
}

public class ShipStatusWindow : EditorWindow
{
    [MenuItem("Tools/HAF/Ship Status")]
    static void Open()
    {
        var w = GetWindow<ShipStatusWindow>(false, "Ship Status");
        w.minSize = new Vector2(520, 300);
        w.Scan();
    }

    class Row { public string name, state, detail; public int severity; }   // severity: 2 problem, 1 attention, 0 fine

    string buildDir; DateTime? buildUtc; readonly List<Row> rows = new List<Row>(); Vector2 scroll;

    void OnEnable() { Scan(); }

    // One row per baked thing, whoever owns it. THREE registries bake into Assets/Resources — units
    // (ModelRegistry/pack.json), districts (DistrictRegistry/haf_districts.json), props (PropRegistry/
    // haf_props.json) — plus hand-prop names referenced from unit entries. The first version only knew units and
    // accused every district/prop of being an ORPHANED BAKE (2026-08-18 first-run finding, user screenshot).
    void Scan()
    {
        buildUtc = ShipStatus.LastBuildUtc(out buildDir);
        rows.Clear();
        var entries = ModelRegistry.Load();
        var names = new HashSet<string>(entries.Select(e => e.resourceName));
        foreach (var e in entries)
        {
            var bake = ShipStatus.LastBakeUtc(e.resourceName);
            bool hasGuids = (e.skel != null && e.skel.Any(x => x != 0)) || (e.atlas != null && e.atlas.Any(x => x != 0));
            if (bake == null && !hasGuids)
                rows.Add(new Row { name = e.resourceName, state = "no bake needed", detail = "retex/borrow entry — no authored assets", severity = 0 });
            else if (bake == null)
                rows.Add(new Row { name = e.resourceName, state = "BAKE MISSING", detail = "the entry authors asset GUIDs but no baked outputs exist in Assets/Resources — re-bake it", severity = 2 });
            else
                rows.Add(ShippedRow(e.resourceName, "unit", bake));
        }
        AddOwned(names, () => DistrictRegistry.Load().Select(d => d.resourceName), "district");
        AddOwned(names, () => PropRegistry.Load().Select(p => p.resourceName), "prop");
        // Hand props referenced by name from unit entries (may not appear in haf_props.json when authored elsewhere).
        AddOwned(names, () => entries.Select(e => e.handPropName).Where(n => !string.IsNullOrEmpty(n)), "hand prop");
        // Orphaned bakes: output files NO registry owns (renamed/removed entries leave these behind; they still
        // SHIP — Resources force-includes everything — so they are dead weight in the bundle). ConversionGateTest
        // debris gets its own label: it's test scratch, not a lost model.
        try
        {
            var res = Path.Combine(Application.dataPath, "Resources");
            var orphans = new HashSet<string>();
            foreach (var f in Directory.GetFiles(res))
            {
                var b = Path.GetFileName(f);
                if (b.EndsWith(".meta")) continue;
                foreach (var s in UniversalBaker.OutputSuffixes)
                    if (b.EndsWith(s)) { var n = b.Substring(0, b.Length - s.Length); if (!names.Contains(n)) orphans.Add(n); break; }
            }
            foreach (var n in orphans.OrderBy(x => x))
                rows.Add(n.StartsWith("__convgate__")
                    ? new Row { name = n, state = "TEST ARTIFACT", detail = "ConversionGateTest scratch bake — still ships as dead bundle weight; safe to delete", severity = 1 }
                    : new Row { name = n, state = "ORPHANED BAKE", detail = "baked outputs no registry owns (renamed/removed?) — dead weight that still ships; Remove-style cleanup applies", severity = 1 });
        }
        catch { }
        rows.Sort((a, b) => b.severity != a.severity ? b.severity - a.severity : string.CompareOrdinal(a.name, b.name));
    }

    // Add rows for another registry's entries and claim their names so the orphan sweep skips them. A saved-but-
    // never-baked district/prop recipe is quiet (severity 0): unlike units, their recipes routinely exist pre-bake.
    void AddOwned(HashSet<string> names, Func<IEnumerable<string>> source, string kind)
    {
        IEnumerable<string> list;
        try { list = source().Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList(); }
        catch (Exception e) { Debug.LogWarning($"[ShipStatus] could not read the {kind} registry: {e.Message}"); return; }
        foreach (var n in list)
        {
            bool fresh = names.Add(n);
            if (!fresh) continue;   // a unit entry already produced this row (e.g. a hand prop also in haf_props.json)
            var bake = ShipStatus.LastBakeUtc(n);
            if (bake == null) rows.Add(new Row { name = n, state = $"no bake yet ({kind})", detail = $"{kind} recipe saved, no baked outputs in Assets/Resources", severity = 0 });
            else rows.Add(ShippedRow(n, kind, bake));
        }
    }

    Row ShippedRow(string name, string kind, DateTime? bake) =>
        buildUtc == null || bake > buildUtc
            ? new Row { name = name, state = $"BAKED, NOT BUILT ({kind})", detail = $"baked {Local(bake)} — newer than the last mod build; the game still loads the previous assets", severity = 2 }
            : new Row { name = name, state = $"shipped ({kind})", detail = $"baked {Local(bake)}", severity = 0 };

    static string Local(DateTime? utc) => utc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never";

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ship Status — which bakes has the game not seen yet?", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Baked assets reach the game ONLY through a mod build. The registry, skins and sounds are read " +
            "directly from BepInEx/config and are always current — this window is about the baked Resources assets.", MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(buildUtc == null ? "Last mod build: NONE FOUND" : $"Last mod build: {Local(buildUtc)}   ({Path.GetFileName(buildDir)})");
            if (GUILayout.Button(new GUIContent("Refresh", "Re-scan bake times and the newest mod build."), GUILayout.Width(70))) Scan();
        }
        int problems = rows.Count(r => r.severity == 2);
        if (problems > 0)
            EditorGUILayout.HelpBox($"{problems} entr{(problems == 1 ? "y is" : "ies are")} not in the current build — run the mod build, then relaunch the game. " +
                "(The boot pre-flight in haf_load_report.txt warns about exactly these.)", MessageType.Warning);
        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (var r in rows)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var style = r.severity == 2 ? EditorStyles.boldLabel : EditorStyles.label;
                var c = GUI.color;
                GUI.color = r.severity == 2 ? new Color(1f, 0.75f, 0.3f) : r.severity == 1 ? new Color(1f, 0.9f, 0.5f) : c;
                EditorGUILayout.LabelField(new GUIContent(r.name, r.detail), style, GUILayout.Width(220));
                EditorGUILayout.LabelField(new GUIContent(r.state, r.detail), style);
                GUI.color = c;
            }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.HelpBox("Hover a row for details. BAKED, NOT BUILT = the HandCrankedSubmarine trap (2026-08-18): " +
            "the entry's GUIDs point at assets newer than the shipped bundle, so the game can't resolve them.", MessageType.None);
    }
}
