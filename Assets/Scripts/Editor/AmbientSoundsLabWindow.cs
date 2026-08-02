// AmbientSoundsLabWindow.cs (HAF editor) — authors enc_sounds.json: global audio overrides that silence vanilla Wwise
// sounds by event-name substring. The plugin (UniversalInject.ShouldSilenceEvent) drops any posted event whose name
// contains one of these substrings, at the AudioManager.PostEvent service sink. Relaunch the game to apply edits.
//
// Distinct from the Sound Window (which edits PER-MODEL sounds in the unit registry): this is a GLOBAL override list,
// not tied to any one model. `Replace with` is reserved for a future silence-then-substitute step (unused today).

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AmbientSoundsLabWindow : EditorWindow
{
    List<SoundOverrideDef> entries;
    Vector2 scroll;
    string status = "";

    [MenuItem("Tools/HAF/Ambient Sounds Lab")]
    static void Open() => GetWindow<AmbientSoundsLabWindow>("Ambient Sounds Lab").minSize = new Vector2(460, 320);

    void OnEnable() => Reload();

    void Reload()
    {
        entries = SoundOverrideRegistry.Load();
        status = $"{entries.Count} override(s) loaded from enc_sounds.json.";
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Silence vanilla Wwise sounds by event-name SUBSTRING (case-insensitive). The plugin drops any sound whose " +
            "event name contains one of these, at the service sink every sound passes through — so keep substrings " +
            "SPECIFIC (a broad word can mute more than you intend).\n\n" +
            "Find real event names in-game: plugin F8 window → Audio Trace: ON, trigger the sound, read the " +
            "[AudioTrace] lines in BepInEx/LogOutput.log.\n\n" +
            "Writes enc_sounds.json — relaunch the game to apply. 'Replace with' is reserved for a future substitute " +
            "step (has no effect yet).", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Overrides", EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        int removeAt = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Silence (event substring)", GUILayout.Width(170));
            e.silence = EditorGUILayout.TextField(e.silence);
            if (GUILayout.Button("Remove", GUILayout.Width(70))) removeAt = i;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Note (editor only)", GUILayout.Width(170));
            e.note = EditorGUILayout.TextField(e.note);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Replace with (future)", GUILayout.Width(170));
                e.replaceWith = EditorGUILayout.TextField(e.replaceWith);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
        if (removeAt >= 0) entries.RemoveAt(removeAt);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add override")) entries.Add(new SoundOverrideDef());
        if (GUILayout.Button("Reload")) Reload();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save", GUILayout.Width(120)))
        {
            status = SoundOverrideRegistry.Save(entries)
                ? $"Saved {entries.FindAll(o => !string.IsNullOrWhiteSpace(o.silence)).Count} override(s) → enc_sounds.json. Relaunch the game to apply."
                : "SAVE FAILED — see the Console.";
            Reload();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
    }
}
