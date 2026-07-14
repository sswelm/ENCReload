// RetextureWindow.cs — a SEPARATE editor window (Tools ▸ ENC ▸ Unit Retexture) for reskinning an EXISTING unit at
// runtime WITHOUT baking a custom model. Pick the unit's pawn, DOWNLOAD its skin (from the in-game atlas dump) to paint,
// then REPLACE it with your PNG (or just grey it). The runtime plugin hot-loads the result onto an ISOLATED clone of the
// unit's output layer, so the emblematic original is untouched and the vanilla mesh is kept. It writes texture-only
// entries (textureFile / desaturate) into the SAME registry the Factory uses — see ModelDef in ModelRegistry.cs.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class RetextureWindow : EditorWindow
{
    [MenuItem("Tools/ENC/Unit Retexture")]
    static void Open() => GetWindow<RetextureWindow>("Unit Retexture");

    string pawn = "Era6_Common_StealthCorvettes_01";   // the pawn descriptor to retexture (a unique substring the plugin matches)
    string resourceName = "";                          // registry id for this override entry
    string pngPath = "";                               // the painted PNG to inject
    float desaturate = 0f;                             // 0 = keep colours, 1 = full grey
    float tintR = 0f, tintG = 0f, tintB = 0f;          // per-channel colour offset, -255..+255
    bool engineSound = false;                          // fire the per-ship engine move Start/Stop sound on this unit
    string engineStartEvent = "", engineStopEvent = ""; // optional Wwise event names (post by name -> works for the first unit)
    Vector2 scroll;
    string status = "";

    // The game's BepInEx/config (auto-detected by ModelRegistry, same folder the plugin reads).
    static string SkinsDir => Path.Combine(ModelRegistry.ConfigDir, "enc_skins");
    static string DumpDir  => Path.Combine(ModelRegistry.ConfigDir, "enc_atlas_dump");

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Reskin an EXISTING unit at runtime — no bake.\n" +
            "1) Pick the pawn (use the _Common_..._01 copy, NOT the emblematic original).\n" +
            "2) Download its skin: dump atlases in-game first (F8 window ▸ Dump Atlases), then paint the unit's .png.\n" +
            "3) Replace with your PNG and/or adjust it (Desaturate + R/G/B ±255). Relaunch/reload the game to see it — the " +
            "plugin injects onto an isolated copy of the layer, so the original stays as-is.", MessageType.Info);

        var existing = ModelRegistry.Load();

        // --- 1) pawn ---
        EditorGUILayout.LabelField("1 · Pawn", EditorStyles.boldLabel);
        pawn = EditorGUILayout.TextField(new GUIContent("Pawn description",
            "The unit's pawn descriptor — a unique substring the plugin matches, e.g. Era6_Common_StealthCorvettes_01."), pawn);
        var pawns = existing.Select(m => m.pawnDescription).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToArray();
        if (pawns.Length > 0)
        {
            // A visible placeholder as item 0 so the dropdown reads as a populated picker (a Popup at index -1 shows blank).
            var opts = new string[pawns.Length + 1];
            opts[0] = $"— pick from registry ({pawns.Length}) —";
            System.Array.Copy(pawns, 0, opts, 1, pawns.Length);
            int sel = EditorGUILayout.Popup("  pick from registry", 0, opts);
            if (sel > 0) { pawn = pawns[sel - 1]; resourceName = ""; }
        }
        else
            EditorGUILayout.LabelField("  pick from registry", "(nothing in the registry yet — type the descriptor)", EditorStyles.miniLabel);
        if (string.IsNullOrEmpty(resourceName) && !string.IsNullOrEmpty(pawn)) resourceName = "Retex_" + Sanitize(pawn);
        resourceName = EditorGUILayout.TextField(new GUIContent("Entry name", "Registry id for this override (any unique name)."), resourceName);

        EditorGUILayout.Space();
        // --- 2) download ---
        EditorGUILayout.LabelField("2 · Download UV map (skin)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open atlas-dump folder", GUILayout.Height(22)))
            {
                Directory.CreateDirectory(DumpDir);
                EditorUtility.RevealInFinder(DumpDir + Path.DirectorySeparatorChar);
            }
            EditorGUILayout.LabelField("← dump in-game first (F8 ▸ Dump Atlases), then paint the unit's .png", EditorStyles.miniLabel);
        }

        EditorGUILayout.Space();
        // --- 3) replace ---
        EditorGUILayout.LabelField("3 · Replace / adjust skin", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            pngPath = EditorGUILayout.TextField(new GUIContent("Replacement PNG (optional)",
                "A painted skin PNG. Leave empty to adjust the unit's OWN atlas — or, when editing an entry, to keep its current skin and only change the adjustments below."), pngPath);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFilePanel("Pick the painted skin PNG", "", "png");
                if (!string.IsNullOrEmpty(p)) pngPath = p;
            }
        }
        EditorGUILayout.LabelField("Adjustments — applied on top of the skin above (or the own atlas):", EditorStyles.miniLabel);
        desaturate = EditorGUILayout.Slider(new GUIContent("Desaturate",
            "0 = keep colours, 1 = full grey (pull each pixel toward its brightness)."), desaturate, 0f, 1f);
        tintR = EditorGUILayout.Slider(new GUIContent("Red  ±255",
            "Add (or subtract) red. Equal negatives on all three = darken; equal positives = brighten."), tintR, -255f, 255f);
        tintG = EditorGUILayout.Slider(new GUIContent("Green ±255", "Add (or subtract) green."), tintG, -255f, 255f);
        tintB = EditorGUILayout.Slider(new GUIContent("Blue ±255", "Add (or subtract) blue."), tintB, -255f, 255f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset adjustments", GUILayout.Width(150))) { desaturate = 0f; tintR = tintG = tintB = 0f; }
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space();
        engineSound = EditorGUILayout.ToggleLeft(new GUIContent("Engine sound on move",
            "Fire the unit's engine move Start/Stop sound on this unit — our injected/retextured units don't trigger it themselves, so they go silent on move. Naval proven."), engineSound);
        if (engineSound)
        {
            EditorGUILayout.HelpBox("Name the Wwise events to post on move start/stop — then it works for the FIRST unit at load (no live capture). " +
                "Get names from the game: F8 window ▸ Dump Sound Catalog (writes enc_sound_catalog.txt). Leave blank to auto-capture from a same-family vehicle that moved this session.", MessageType.None);
            engineStartEvent = EditorGUILayout.TextField(new GUIContent("  Start event", "e.g. Play_UNIT_Vehicles_StealthCorvette_Start"), engineStartEvent);
            engineStopEvent = EditorGUILayout.TextField(new GUIContent("  Stop event", "e.g. Play_UNIT_Vehicles_StealthCorvette_Stop"), engineStopEvent);
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(pawn) || string.IsNullOrEmpty(resourceName)))
            if (GUILayout.Button("Apply", GUILayout.Height(30)))
                Apply();

        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);

        EditorGUILayout.Space();
        // --- existing overrides ---
        EditorGUILayout.LabelField("Texture-only overrides", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        var overrides = existing.Where(x => x.desaturate > 0f || x.tintR != 0f || x.tintG != 0f || x.tintB != 0f || !string.IsNullOrEmpty(x.textureFile) || x.engineSound).ToList();
        if (overrides.Count == 0) EditorGUILayout.LabelField("  (none yet)", EditorStyles.miniLabel);
        foreach (var m in overrides)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{m.resourceName}  → {m.pawnDescription}  [{Describe(m)}]");
                if (GUILayout.Button("Edit", GUILayout.Width(46)))
                {
                    pawn = m.pawnDescription; resourceName = m.resourceName;
                    desaturate = m.desaturate; tintR = m.tintR; tintG = m.tintG; tintB = m.tintB; engineSound = m.engineSound;
                    engineStartEvent = m.engineStartEvent; engineStopEvent = m.engineStopEvent; pngPath = "";
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("Remove", GUILayout.Width(64)))
                {
                    ModelRegistry.Remove(m.resourceName);
                    status = "Removed '" + m.resourceName + "'.";
                    GUIUtility.ExitGUI();
                }
            }
        EditorGUILayout.EndScrollView();
    }

    void Apply()
    {
        try
        {
            var def = ModelRegistry.Load().FirstOrDefault(m => m.resourceName == resourceName) ?? new ModelDef();
            def.resourceName = resourceName.Trim();
            def.pawnDescription = pawn.Trim();
            def.desaturate = Mathf.Clamp01(desaturate);
            def.tintR = Mathf.Clamp(tintR, -255f, 255f);
            def.tintG = Mathf.Clamp(tintG, -255f, 255f);
            def.tintB = Mathf.Clamp(tintB, -255f, 255f);
            def.engineSound = engineSound;
            def.engineStartEvent = engineSound ? (engineStartEvent ?? "").Trim() : "";
            def.engineStopEvent = engineSound ? (engineStopEvent ?? "").Trim() : "";
            if (!string.IsNullOrEmpty(pngPath))   // a new PNG was picked -> copy it in and use it as the skin
            {
                if (!File.Exists(pngPath)) { status = "PNG not found: " + pngPath; return; }
                Directory.CreateDirectory(SkinsDir);
                string file = Sanitize(resourceName) + ".png";
                File.Copy(pngPath, Path.Combine(SkinsDir, file), true);   // into the game's config/enc_skins the plugin reads
                def.textureFile = file;
            }
            // else: keep def.textureFile as-is (an existing entry's PNG, or "" to adjust the unit's own atlas).

            bool hasSkin = !string.IsNullOrEmpty(def.textureFile);
            bool hasAdjust = def.desaturate > 0f || def.tintR != 0f || def.tintG != 0f || def.tintB != 0f;
            if (!hasSkin && !hasAdjust && !engineSound)
            {
                status = "Nothing to apply — browse a PNG, set an adjustment, or enable Engine sound.";
                return;
            }
            bool ok = ModelRegistry.Upsert(def);
            status = ok
                ? $"Saved '{def.resourceName}' → {def.pawnDescription}  ({Describe(def)}).\nRelaunch the game (or reload a save) to see it."
                : "Registry save FAILED — see the Console.";
        }
        catch (Exception e) { status = "Failed: " + e.Message; }
    }

    static string Describe(ModelDef m)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(m.textureFile)) parts.Add("skin " + m.textureFile);
        if (m.desaturate > 0f) parts.Add($"desat {m.desaturate:0.00}");
        if (m.tintR != 0f || m.tintG != 0f || m.tintB != 0f) parts.Add($"rgb {m.tintR:+0;-0;0}/{m.tintG:+0;-0;0}/{m.tintB:+0;-0;0}");
        if (m.engineSound) parts.Add("engine");
        return parts.Count > 0 ? string.Join(", ", parts) : "no change";
    }

    static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s ?? "") sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.ToString();
    }
}
