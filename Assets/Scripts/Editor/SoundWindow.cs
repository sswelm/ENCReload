// SoundWindow.cs — a SEPARATE editor window (Tools ▸ ENC ▸ Unit Sound) for configuring a unit's MOVEMENT audio, apart
// from the skin (Unit Retexture) window. Pick a pawn, then set custom WAVs for Start (spool-up), Travel (loop while
// moving) and Stop (spool-down), each with its own volume; or point it at a game (Wwise) engine event. The runtime plugin
// (ENCAccessProof) detects each instance's move start/stop and plays these — no bake. Writes the sound fields onto the
// SAME registry entry the Factory/Retexture use (matched by pawn), so a unit has one entry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class SoundWindow : EditorWindow
{
    [MenuItem("Tools/ENC/Unit Sound")]
    static void Open() => GetWindow<SoundWindow>("Unit Sound");

    string pawn = "";
    string resourceName = "";
    string startFile = "", startPath = ""; float startVol = 1f;   // Start spool-up
    string loopFile = "", loopPath = "";  float loopVol = 1f;     // Travel loop
    string stopFile = "", stopPath = "";  float stopVol = 1f;     // Stop spool-down
    bool engineSound = false; string engineStart = "", engineStop = "";   // game (Wwise) engine event alternative
    Vector2 scroll; string status = "";

    static string SoundsDir => Path.Combine(ModelRegistry.ConfigDir, "enc_sounds");

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Configure a unit's MOVEMENT audio (separate from its skin).\n" +
            "• Custom WAVs — Start (spool-up, one-shot) → Travel (loops while moving) → Stop (spool-down, one-shot). " +
            "Great for units the game has no sound for (drones, zeppelins). 16-bit WAV; mono = 3D-positional.\n" +
            "• Or a Wwise engine event — the game's own per-ship sound, posted on move start/stop (get names from the " +
            "in-game F8 ▸ Dump Sound Catalog).\nRelaunch / reload a save to hear changes.", MessageType.Info);

        var all = ModelRegistry.Load();

        // --- pawn ---
        EditorGUILayout.LabelField("Pawn", EditorStyles.boldLabel);
        pawn = EditorGUILayout.TextField(new GUIContent("Pawn description", "A unique substring of the unit's pawn descriptor, e.g. Era6_Common_ReconDrones_01."), pawn);
        var pawns = all.Select(m => m.pawnDescription).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToArray();
        if (pawns.Length > 0)
        {
            var opts = new string[pawns.Length + 1];
            opts[0] = $"— pick from registry ({pawns.Length}) —";
            Array.Copy(pawns, 0, opts, 1, pawns.Length);
            int sel = EditorGUILayout.Popup("  pick from registry", 0, opts);
            if (sel > 0) { LoadForPawn(pawns[sel - 1], all); GUIUtility.ExitGUI(); }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Custom WAV files (Unity — 16-bit; mono for 3D)", EditorStyles.boldLabel);
        WavVolRow("Start (spool-up)", startFile, ref startPath, ref startVol);
        WavVolRow("Travel (loop)", loopFile, ref loopPath, ref loopVol);
        WavVolRow("Stop (spool-down)", stopFile, ref stopPath, ref stopVol);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("…or a game (Wwise) engine sound", EditorStyles.boldLabel);
        engineSound = EditorGUILayout.ToggleLeft(new GUIContent("Use a Wwise engine event (posted on move start/stop)",
            "The game's own per-ship sound. Get names from F8 ▸ Dump Sound Catalog (enc_sound_catalog.txt)."), engineSound);
        if (engineSound)
        {
            engineStart = EditorGUILayout.TextField(new GUIContent("  Start event", "e.g. Play_UNIT_Vehicles_StealthCorvette_Start"), engineStart);
            engineStop = EditorGUILayout.TextField(new GUIContent("  Stop event", "e.g. Play_UNIT_Vehicles_StealthCorvette_Stop"), engineStop);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(pawn)))
            if (GUILayout.Button("Apply", GUILayout.Height(30))) Apply(all);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);

        // --- units with audio ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Units with audio", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        var withAudio = all.Where(HasAudio).ToList();
        if (withAudio.Count == 0) EditorGUILayout.LabelField("  (none yet)", EditorStyles.miniLabel);
        foreach (var m in withAudio)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{m.pawnDescription}  [{DescribeAudio(m)}]");
                if (GUILayout.Button("Edit", GUILayout.Width(46))) { LoadForPawn(m.pawnDescription, all); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("Clear", GUILayout.Width(52)))
                {
                    m.soundStartFile = m.soundFile = m.soundStopFile = ""; m.engineSound = false; m.engineStartEvent = m.engineStopEvent = "";
                    ModelRegistry.Upsert(m); status = "Cleared audio on '" + m.pawnDescription + "'."; GUIUtility.ExitGUI();
                }
            }
        EditorGUILayout.EndScrollView();
    }

    void LoadForPawn(string p, List<ModelDef> all)
    {
        pawn = p;
        var m = all.FirstOrDefault(x => x.pawnDescription == p);
        if (m != null)
        {
            resourceName = m.resourceName;
            startFile = m.soundStartFile; loopFile = m.soundFile; stopFile = m.soundStopFile;
            startVol = m.soundStartVolume; loopVol = m.soundVolume; stopVol = m.soundStopVolume;
            engineSound = m.engineSound; engineStart = m.engineStartEvent; engineStop = m.engineStopEvent;
        }
        else
        {
            resourceName = "Sound_" + Sanitize(p);
            startFile = loopFile = stopFile = ""; startVol = loopVol = stopVol = 1f;
            engineSound = false; engineStart = engineStop = "";
        }
        startPath = loopPath = stopPath = ""; status = "";
    }

    void WavVolRow(string label, string current, ref string path, ref float vol)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent("  " + label, "current: " + (string.IsNullOrEmpty(current) ? "(none)" : current)), GUILayout.Width(130));
            path = EditorGUILayout.TextField(path);
            if (GUILayout.Button(string.IsNullOrEmpty(current) ? "Browse" : "Replace", GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFilePanel("Pick a WAV", "", "wav");
                if (!string.IsNullOrEmpty(p)) path = p;
            }
        }
        // Perceptual slider: hearing is logarithmic, so a LINEAR amplitude slider feels bunched near the top. Show the
        // slider as sqrt(amplitude) (≈perceived loudness) and store amplitude = slider^2 — the value the plugin feeds to
        // AudioSource.volume. The ×N label shows the real amplitude.
        float perc = Mathf.Sqrt(Mathf.Clamp01(vol));
        using (new EditorGUILayout.HorizontalScope())
        {
            perc = EditorGUILayout.Slider("    volume", perc, 0f, 1f);
            EditorGUILayout.LabelField($"×{perc * perc:0.00}", GUILayout.Width(48));
        }
        vol = perc * perc;
    }

    void Apply(List<ModelDef> all)
    {
        try
        {
            // reuse the unit's existing registry entry (a model / retexture) if it has one, so a pawn has ONE entry.
            var def = all.FirstOrDefault(m => m.pawnDescription == pawn.Trim())
                   ?? all.FirstOrDefault(m => m.resourceName == resourceName)
                   ?? new ModelDef();
            def.pawnDescription = pawn.Trim();
            if (string.IsNullOrEmpty(def.resourceName)) def.resourceName = string.IsNullOrEmpty(resourceName) ? "Sound_" + Sanitize(pawn) : resourceName;

            if (!CopyWav(startPath, def.resourceName + "_start", ref def.soundStartFile)) return;
            if (!CopyWav(loopPath, def.resourceName + "_loop", ref def.soundFile)) return;
            if (!CopyWav(stopPath, def.resourceName + "_stop", ref def.soundStopFile)) return;
            def.soundStartVolume = startVol; def.soundVolume = loopVol; def.soundStopVolume = stopVol;
            def.engineSound = engineSound;
            def.engineStartEvent = engineSound ? (engineStart ?? "").Trim() : "";
            def.engineStopEvent = engineSound ? (engineStop ?? "").Trim() : "";

            bool ok = ModelRegistry.Upsert(def);
            status = ok ? $"Saved audio for '{def.pawnDescription}' ({DescribeAudio(def)}).\nRelaunch (or reload a save) to hear it."
                        : "Registry save FAILED — see the Console.";
        }
        catch (Exception e) { status = "Failed: " + e.Message; }
    }

    // Copy a browsed WAV into enc_sounds/<baseName>.wav and set `field`; keep the existing value if none browsed.
    bool CopyWav(string src, string baseName, ref string field)
    {
        if (string.IsNullOrEmpty(src)) return true;
        if (!File.Exists(src)) { status = "WAV not found: " + src; return false; }
        Directory.CreateDirectory(SoundsDir);
        string f = Sanitize(baseName) + ".wav";
        File.Copy(src, Path.Combine(SoundsDir, f), true);
        field = f;
        return true;
    }

    static bool HasAudio(ModelDef m) => m.engineSound || !string.IsNullOrEmpty(m.soundFile) || !string.IsNullOrEmpty(m.soundStartFile) || !string.IsNullOrEmpty(m.soundStopFile);
    static string DescribeAudio(ModelDef m)
    {
        var p = new List<string>();
        if (!string.IsNullOrEmpty(m.soundStartFile)) p.Add("start");
        if (!string.IsNullOrEmpty(m.soundFile)) p.Add("loop");
        if (!string.IsNullOrEmpty(m.soundStopFile)) p.Add("stop");
        if (m.engineSound) p.Add("wwise");
        return p.Count > 0 ? string.Join("+", p) : "none";
    }
    static string Sanitize(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s ?? "") sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
        return sb.ToString();
    }
}
