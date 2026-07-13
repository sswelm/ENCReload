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
    bool grey = false;                                 // grey (desaturate) instead of a custom skin
    float greyStrength = 1f;
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
            "3) Replace with your PNG (or just Grey it). Relaunch/reload the game to see it — the plugin injects onto an " +
            "isolated copy of the layer, so the original stays as-is.", MessageType.Info);

        var existing = ModelRegistry.Load();

        // --- 1) pawn ---
        EditorGUILayout.LabelField("1 · Pawn", EditorStyles.boldLabel);
        pawn = EditorGUILayout.TextField(new GUIContent("Pawn description",
            "The unit's pawn descriptor — a unique substring the plugin matches, e.g. Era6_Common_StealthCorvettes_01."), pawn);
        var pawns = existing.Select(m => m.pawnDescription).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToArray();
        if (pawns.Length > 0)
        {
            int sel = EditorGUILayout.Popup("  pick from registry", -1, pawns);
            if (sel >= 0) { pawn = pawns[sel]; resourceName = ""; }
        }
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
        EditorGUILayout.LabelField("3 · Replace UV map", EditorStyles.boldLabel);
        grey = EditorGUILayout.ToggleLeft(new GUIContent("Grey it (desaturate the original) — no PNG needed",
            "Skip the custom skin: just desaturate the unit's own atlas + kill the civ-colour tint."), grey);
        if (grey)
            greyStrength = EditorGUILayout.Slider("  Grey strength", greyStrength, 0f, 1f);
        else
            using (new EditorGUILayout.HorizontalScope())
            {
                pngPath = EditorGUILayout.TextField(new GUIContent("Replacement PNG", "Your painted skin (PNG)."), pngPath);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    var p = EditorUtility.OpenFilePanel("Pick the painted skin PNG", "", "png");
                    if (!string.IsNullOrEmpty(p)) pngPath = p;
                }
            }

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(pawn) || string.IsNullOrEmpty(resourceName) || (!grey && string.IsNullOrEmpty(pngPath))))
            if (GUILayout.Button(grey ? "Apply grey" : "Replace UV map", GUILayout.Height(30)))
                Apply();

        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);

        EditorGUILayout.Space();
        // --- existing overrides ---
        EditorGUILayout.LabelField("Texture-only overrides", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        var overrides = existing.Where(x => x.desaturate > 0f || !string.IsNullOrEmpty(x.textureFile)).ToList();
        if (overrides.Count == 0) EditorGUILayout.LabelField("  (none yet)", EditorStyles.miniLabel);
        foreach (var m in overrides)
            using (new EditorGUILayout.HorizontalScope())
            {
                string what = !string.IsNullOrEmpty(m.textureFile) ? "skin " + m.textureFile : $"grey {m.desaturate:0.00}";
                EditorGUILayout.LabelField($"{m.resourceName}  → {m.pawnDescription}  [{what}]");
                if (GUILayout.Button("Edit", GUILayout.Width(46)))
                {
                    pawn = m.pawnDescription; resourceName = m.resourceName;
                    grey = m.desaturate > 0f; greyStrength = m.desaturate > 0f ? m.desaturate : 1f; pngPath = "";
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
            if (grey)
            {
                def.desaturate = Mathf.Max(0.01f, greyStrength);
                def.textureFile = "";
            }
            else
            {
                if (!File.Exists(pngPath)) { status = "PNG not found: " + pngPath; return; }
                Directory.CreateDirectory(SkinsDir);
                string file = Sanitize(resourceName) + ".png";
                File.Copy(pngPath, Path.Combine(SkinsDir, file), true);   // into the game's config/enc_skins the plugin reads
                def.textureFile = file;
                def.desaturate = 0f;
            }
            bool ok = ModelRegistry.Upsert(def);
            status = ok
                ? $"Saved '{def.resourceName}' → {def.pawnDescription}  ({(grey ? $"grey {greyStrength:0.00}" : "skin " + def.textureFile)}).\nRelaunch the game (or reload a save) to see it."
                : "Registry save FAILED — see the Console.";
        }
        catch (Exception e) { status = "Failed: " + e.Message; }
    }

    static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s ?? "") sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.ToString();
    }
}
