// BackupWindow.cs — Tools ▸ HAF ▸ Backup & Restore. A safety net for everything that ISN'T under git: the editor
// scripts (Assets/Scripts/Editor), the source models (Assets/FactorySource), the baked assets (Assets/Resources),
// the ENC databases, the Tools scripts, and the LIVE runtime config the plugin reads (BepInEx/config: haf_*.json +
// haf_skins + haf_sounds). ENCReload's git only tracks Assets/Databases, so most of this has no version control.
//
// DESIGN — anti-loss first (the user's explicit ask, "guards that ensure we don't lose anything"):
//   • A backup is a TIMESTAMPED, self-describing folder on D: with a manifest.txt recording every source's ORIGINAL
//     absolute path + file count + bytes. Backups are never overwritten (each is a new timestamp) and never auto-deleted.
//   • RESTORE is guarded three ways: (1) it FIRST snapshots the current state to a _prerestore_<ts> backup, so a wrong
//     restore is always undoable; (2) it is ADDITIVE — it copies backed-up files OVER current ones but NEVER deletes a
//     file that exists now and isn't in the backup, so new work can't vanish; (3) it asks for explicit confirmation
//     listing exactly what will be written. After both backup and restore, file counts are verified and reported.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;   // offsite zip (2026-08-17)
using System.Linq;
using UnityEditor;
using UnityEngine;

public class BackupWindow : EditorWindow
{
    // NOTE: '&' is a shortcut-modifier char in Unity MenuItem paths, so the menu uses "and"; the window title keeps "&".
    [MenuItem("Tools/HAF/Backup and Restore")]
    static void Open()
    {
        var w = GetWindow<BackupWindow>("Backup & Restore");
        w.minSize = new Vector2(680, 560);   // wide enough for the backup rows (timestamp + size + Reveal/Restore/Delete) without cramping
    }

    const string PrefDest = "HAF.Backup.Dest";
    const string PrefOffsite = "HAF.Backup.OffsiteDest";   // OFFSITE copy (2026-08-17): D: is the same machine — theft/fire/surge
    const string PrefOffsiteAuto = "HAF.Backup.OffsiteAuto"; // takes the backups with the originals. Each backup can be zipped as ONE
    string dest;                                   // backup root on D:  // file into a second (ideally cloud-synced) folder.
    string offsite;                                // offsite folder ("" = feature off)
    bool offsiteAuto;                              // auto-zip each new manual backup
    Vector2 scroll, listScroll;
    string status = "";
    readonly Dictionary<string, bool> enabled = new Dictionary<string, bool>();   // group key -> included
    readonly Dictionary<string, long> sizeCache = new Dictionary<string, long>();  // path -> bytes (cleared on any change; walks are expensive)

    long CachedBytes(string p) { if (!sizeCache.TryGetValue(p, out var v)) { v = TreeBytes(p); sizeCache[p] = v; } return v; }

    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;   // C:/Repo/ENCReload
    static string AssetsDir => Application.dataPath;

    // A backup group: a display name, a prefs/manifest key, and the concrete source paths it captures (file or dir).
    internal class Group { public string Name, Key; public List<string> Sources; public Group(string n, string k, List<string> s) { Name = n; Key = k; Sources = s; } }

    // Resolved fresh each time (paths must exist to be offered). Runtime config = the haf_* entries the plugin reads.
    internal static List<Group> BuildGroups()
    {
        var g = new List<Group>();
        void Add(string name, string key, IEnumerable<string> srcs)
        {
            var present = srcs.Where(p => !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))).ToList();
            if (present.Count > 0) g.Add(new Group(name, key, present));
        }
        Add("Editor scripts (Assets/Scripts/Editor)", "editor", new[] { Path.Combine(AssetsDir, "Scripts/Editor") });
        Add("Source models (Assets/FactorySource)", "source", new[] { Path.Combine(AssetsDir, "FactorySource") });
        Add("Baked assets (Assets/Resources)", "resources", new[] { Path.Combine(AssetsDir, "Resources") });
        Add("ENC Databases (Assets/Databases)", "databases", new[] { Path.Combine(AssetsDir, "Databases") });
        Add("Tools (Blender / converters)", "tools", new[] { Path.Combine(ProjectRoot, "Tools") });
        // Runtime config: the LIVE files the plugin reads — haf_*.json + the skins/sounds folders (skip the regenerable
        // atlas dump) AND the haf_packs/ tree, where the MODEL REGISTRY (pack.json) has lived since the multi-pack
        // migration. haf_packs was MISSING here until 2026-08-17 — found the hard way, mid recovery-drill: the restore
        // brought every baked asset back but the registry entry "restored" from a backup that never contained it.
        string cfg = SafeConfigDir();
        if (!string.IsNullOrEmpty(cfg) && Directory.Exists(cfg))
        {
            var cfgSrcs = new List<string>();
            cfgSrcs.AddRange(Directory.GetFiles(cfg, "haf_*.json"));
            foreach (var sub in new[] { "haf_packs", "haf_skins", "haf_sounds" }) { var p = Path.Combine(cfg, sub); if (Directory.Exists(p)) cfgSrcs.Add(p); }
            Add("Runtime config (registry packs + skins + sounds)", "config", cfgSrcs);
        }
        return g;
    }

    static string SafeConfigDir() { try { return ModelRegistry.ConfigDir; } catch { return null; } }

    void OnEnable()
    {
        dest = EditorPrefs.GetString(PrefDest, "D:/HAF_Backups");
        offsite = EditorPrefs.GetString(PrefOffsite, "");
        offsiteAuto = EditorPrefs.GetBool(PrefOffsiteAuto, true);
        foreach (var g in BuildGroups()) if (!enabled.ContainsKey(g.Key)) enabled[g.Key] = true;   // everything on by default
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Backup everything that git does NOT protect (editor scripts, source & baked models, databases, Tools, and the " +
            "live BepInEx runtime config). Each backup is a timestamped folder on D: with a manifest.\n\n" +
            "Restore is safe: it first snapshots the current state to a _prerestore backup, then copies files back ADDITIVELY " +
            "(it never deletes anything you've added since). Nothing is ever overwritten or auto-deleted here.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            dest = EditorGUILayout.TextField(new GUIContent("Backup folder (on D:)", "Root folder for all backups; each backup is a timestamped subfolder."), dest);
            if (GUILayout.Button(new GUIContent("Browse", "Pick the local backup ROOT folder — every backup becomes a timestamped subfolder inside it."), GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFolderPanel("Pick the backup root folder", Directory.Exists(dest) ? dest : "D:/", "");
                if (!string.IsNullOrEmpty(p)) { dest = p; EditorPrefs.SetString(PrefDest, dest); }
            }
        }
        EditorPrefs.SetString(PrefDest, dest);

        // OFFSITE: one .zip per backup into a second folder. D: is the same machine — a machine-level event (theft,
        // fire, surge) takes the backups with the originals; a cloud-synced folder here is the "just in case" copy.
        using (new EditorGUILayout.HorizontalScope())
        {
            offsite = EditorGUILayout.TextField(new GUIContent("Offsite folder", "Second folder each backup is zipped into as ONE file — ideally cloud-synced (OneDrive/Drive/NAS). Blank = off."), offsite);
            if (GUILayout.Button(new GUIContent("Browse", "Pick the OFFSITE folder — ideally one a cloud service syncs, so a machine-level event can't take the backups with the originals."), GUILayout.Width(70)))
            {
                var p = EditorUtility.OpenFolderPanel("Pick the OFFSITE folder (ideally cloud-synced)", Directory.Exists(offsite) ? offsite : "", "");
                if (!string.IsNullOrEmpty(p)) { offsite = p; EditorPrefs.SetString(PrefOffsite, offsite); }
            }
        }
        EditorPrefs.SetString(PrefOffsite, offsite);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool na = EditorGUILayout.ToggleLeft(new GUIContent("Auto-zip each new backup to the offsite folder", "After every successful 'Back up now', the snapshot is also written offsite as HAF_<timestamp>.zip."), offsiteAuto);
            if (na != offsiteAuto) { offsiteAuto = na; EditorPrefs.SetBool(PrefOffsiteAuto, offsiteAuto); }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(offsite) || ListBackups().All(b => Path.GetFileName(b).StartsWith("_prerestore"))))
                if (GUILayout.Button(new GUIContent("Zip latest backup → offsite now", "Zip the NEWEST backup into the offsite folder on demand — for backups made before offsite was configured. Runs in the background; result lands in the status line."), GUILayout.Width(210)))
                {
                    var latest = ListBackups().FirstOrDefault(b => !Path.GetFileName(b).StartsWith("_prerestore"));
                    if (latest != null) OffsiteZipAsync(latest);
                }
        }

        // AUTOMATIC guards (implemented in BackupAuto.cs) — both silent, both optional, both feeding THIS list.
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Automatic", EditorStyles.boldLabel);
        bool dg = EditorPrefs.GetBool(HafDeleteGuard.PrefOn, true);
        bool ndg = EditorGUILayout.ToggleLeft(new GUIContent("Delete guard — snapshot any asset before it is deleted",
            "Before ANYTHING under FactorySource / Resources / Databases / Scripts/Editor is deleted (Factory Remove, a Project-window delete), it is first copied to a _deleted_<timestamp> folder here. The delete then proceeds normally."), dg);
        if (ndg != dg) EditorPrefs.SetBool(HafDeleteGuard.PrefOn, ndg);
        bool ab = EditorPrefs.GetBool(HafAutoBackup.PrefOn, true);
        long lastTicks = long.TryParse(EditorPrefs.GetString(HafAutoBackup.PrefLast, "0"), out var lt) ? lt : 0;
        bool nab = EditorGUILayout.ToggleLeft(new GUIContent(
            $"Daily auto-version — full silent backup (assets + configuration) on first editor load of the day; keeps the newest {HafAutoBackup.Keep}" +
            (lastTicks > 0 ? $"   (last: {new DateTime(lastTicks):yyyy-MM-dd HH:mm})" : "   (never run yet)"),
            "Runs the same backup as the button, all groups, and appears below with a Restore button like any version. Only _auto_ versions rotate; manual backups and _deleted snapshots are never auto-deleted."), ab);
        if (nab != ab) EditorPrefs.SetBool(HafAutoBackup.PrefOn, nab);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Include in backup / restore (the same checkboxes scope both)", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("↻ sizes", "Recompute the size readouts — they are cached because walking the folders is slow; refresh after big changes."), GUILayout.Width(70))) sizeCache.Clear();
        }
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(130));
        foreach (var grp in BuildGroups())
        {
            if (!enabled.ContainsKey(grp.Key)) enabled[grp.Key] = true;
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled[grp.Key] = EditorGUILayout.ToggleLeft(grp.Name, enabled[grp.Key]);
                EditorGUILayout.LabelField(Human(grp.Sources.Sum(CachedBytes)), EditorStyles.miniLabel, GUILayout.Width(90));
            }
        }
        EditorGUILayout.EndScrollView();

        using (new EditorGUI.DisabledScope(!BuildGroups().Any(g => enabled.TryGetValue(g.Key, out var b) && b)))
            if (GUILayout.Button(new GUIContent("Back up now", "Snapshot every TICKED group into a new timestamped folder — never overwrites, never auto-deleted. Verifies file counts AND that the model registry actually landed in the snapshot; zips offsite if configured."), GUILayout.Height(30))) DoBackup();

        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Existing backups (newest first)", EditorStyles.boldLabel);
        listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(180));
        var backups = ListBackups();
        if (backups.Count == 0) EditorGUILayout.LabelField("  (none yet)", EditorStyles.miniLabel);
        foreach (var b in backups)
            using (new EditorGUILayout.HorizontalScope())
            {
                string bn = Path.GetFileName(b);
                string tag = bn.StartsWith("_prerestore") ? "   (auto safety snapshot)"
                           : bn.StartsWith("_deleted") ? "   (delete-guard snapshot)"
                           : bn.StartsWith("_removed") ? "   (Factory remove-undo snapshot)"
                           : bn.StartsWith("_auto") ? "   (daily auto-version)" : "";
                EditorGUILayout.LabelField($"{bn}   {Human(CachedBytes(b))}" + tag);
                if (GUILayout.Button(new GUIContent("Reveal", "Open this backup's folder in Explorer."), GUILayout.Width(60))) EditorUtility.RevealInFinder(b + Path.DirectorySeparatorChar);
                if (GUILayout.Button(new GUIContent("Restore", "Copy this backup's TICKED groups back over the originals. Guarded: your current state is snapshotted first (_prerestore), files you added since are never deleted, and only missing/changed files are written (identical ones untouched)."), GUILayout.Width(64))) { DoRestore(b); GUIUtility.ExitGUI(); }
                if (GUILayout.Button(new GUIContent("Delete", "Permanently delete this backup folder (asks first). Live project files are untouched."), GUILayout.Width(60))) { DeleteBackup(b); GUIUtility.ExitGUI(); }
            }
        EditorGUILayout.EndScrollView();
    }

    // ---- backup ----
    void DoBackup()
    {
        var groups = BuildGroups().Where(g => enabled.TryGetValue(g.Key, out var b) && b).ToList();
        string dir = DoBackupInto(NewBackupDir(""), groups, "manual backup");
        // Offsite ride-along: zip the fresh snapshot into the second folder — OPTIONAL (blank folder = off) and
        // SILENT (background thread; a multi-GB FactorySource zip must not freeze the editor). A failure here NEVER
        // un-does the local backup — the result surfaces in the status line when it lands. (_prerestore safety
        // snapshots are deliberately not auto-zipped; they're local undo state, not archives.)
        if (dir != null && offsiteAuto && !string.IsNullOrEmpty(offsite)) OffsiteZipAsync(dir);
        sizeCache.Clear();
    }

    // ---- offsite (background) ----
    volatile string offsitePending;   // result from the worker thread, appended to status on the editor tick
    bool offsiteRunning;

    void OffsiteZipAsync(string backupDir)
    {
        if (offsiteRunning) { status += "\nOffsite: a zip is already running — this backup was NOT zipped; use 'Zip latest backup → offsite now' when it finishes."; return; }
        offsiteRunning = true;
        status += $"\nOffsite: zipping '{Path.GetFileName(backupDir)}' in the background…";
        string off = offsite;   // capture for the thread (prefs/UI stay main-thread-only)
        EditorApplication.update += PumpOffsiteResult;   // subscribed on the MAIN thread, before the worker starts
        System.Threading.Tasks.Task.Run(() =>
        {
            var result = OffsiteZipCore(backupDir, off);
            offsitePending = result;   // picked up by PumpOffsiteResult on the editor tick
        });
    }

    void PumpOffsiteResult()
    {
        var r = offsitePending;
        if (r == null) return;
        offsitePending = null;
        offsiteRunning = false;
        EditorApplication.update -= PumpOffsiteResult;
        if (this == null) { Debug.Log("[HAF Backup] " + r); return; }   // window was closed mid-zip (Unity fake-null) — result goes to the Console instead
        status += "\n" + r;
        Repaint();
    }

    // Zip one backup folder into the offsite folder as a single file, atomically (.partial then rename), never
    // overwriting an existing zip, and VERIFY by re-opening the zip and comparing its entry count to the folder's.
    // Pure file IO — safe off the main thread (no Unity APIs).
    internal static string OffsiteZipCore(string backupDir, string offsiteDir)
    {
        try
        {
            if (string.IsNullOrEmpty(offsiteDir)) return "Offsite: no folder set.";
            Directory.CreateDirectory(offsiteDir);
            string zip = Path.Combine(offsiteDir, "HAF_" + Path.GetFileName(backupDir) + ".zip");
            if (File.Exists(zip)) return $"Offsite: '{Path.GetFileName(zip)}' already exists — kept (never overwritten).";
            string partial = zip + ".partial";
            if (File.Exists(partial)) File.Delete(partial);   // leftover from an interrupted run
            ZipFile.CreateFromDirectory(backupDir, partial, System.IO.Compression.CompressionLevel.Optimal, false);
            int expect = FileCount(backupDir);
            int inZip;
            using (var z = ZipFile.OpenRead(partial)) inZip = z.Entries.Count(e => !string.IsNullOrEmpty(e.Name));   // entries with a name = files (skips pure dir entries)
            if (inZip != expect) { File.Delete(partial); return $"⚠ Offsite zip VERIFY FAILED ({inZip} files in zip vs {expect} in backup) — partial deleted, local backup untouched."; }
            File.Move(partial, zip);
            return $"Offsite: zipped {inZip} files ({Human(new FileInfo(zip).Length)}) → {zip}";
        }
        catch (Exception e) { return "⚠ Offsite zip FAILED: " + e.Message + " (local backup untouched)"; }
    }

    // Snapshot the given groups into a fresh folder; write a manifest; verify counts. Returns the folder (or null on failure).
    string DoBackupInto(string dir, List<Group> groups, string note)
    {
        var r = SnapshotInto(dir, groups, note);
        status = r.report;
        return r.ok ? r.dir : null;
    }

    // Static core so the AUTOMATIC half (BackupAuto.cs: delete guard + daily auto) can snapshot without a window open.
    internal struct SnapResult { public string dir, report; public bool ok; }
    internal static SnapResult SnapshotInto(string dir, List<Group> groups, string note)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var manifest = new List<string> { "# HAF backup manifest", "# note: " + note, "# created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "" };
            int totalFiles = 0; long totalBytes = 0;
            foreach (var g in groups)
                foreach (var src in g.Sources)
                {
                    // Store under <group>/<sourceLeafName>; record the ORIGINAL absolute path so Restore knows where it goes back.
                    string leaf = Path.GetFileName(src.TrimEnd('/', '\\'));
                    string rel = Path.Combine(g.Key, leaf);
                    string dst = Path.Combine(dir, rel);
                    int files = File.Exists(src) ? CopyFile(src, dst) : CopyTree(src, dst);
                    long bytes = TreeBytes(dst);
                    totalFiles += files; totalBytes += bytes;
                    manifest.Add($"SRC\t{rel.Replace('\\', '/')}\t{src.Replace('\\', '/')}\t{files}\t{bytes}");
                }
            File.WriteAllLines(Path.Combine(dir, "manifest.txt"), manifest);
            // verify: re-count what actually landed vs the manifest
            int landed = ManifestSources(dir).Sum(m => FileCount(Path.Combine(dir, m.rel)));
            bool ok = landed == totalFiles;
            // CRITICAL-CONTENT VERIFY (2026-08-17, "it can't even survive the most basic test"): a backup that runs
            // green while missing the ONE file a recovery needs is worse than no backup — the recovery drill proved
            // it (the registry silently absent for weeks). A snapshot that took the config group must CONTAIN every
            // live pack registry, or it is NOT ok, full stop.
            string critical = VerifyCriticalContents(dir, groups);
            if (critical != null) ok = false;
            return new SnapResult
            {
                dir = dir,
                ok = ok,
                report = critical != null ? critical
                    : ok
                    ? $"Backed up {totalFiles} files ({Human(totalBytes)}) → {Path.GetFileName(dir)} — registry verified in snapshot."
                    : $"⚠ Backup COUNT MISMATCH: expected {totalFiles}, found {landed} in {Path.GetFileName(dir)} — inspect before trusting it."
            };
        }
        catch (Exception e)
        {
            try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
            return new SnapResult { dir = dir, ok = false, report = "Backup FAILED: " + e.Message };
        }
    }

    // ---- restore (guarded) ----
    void DoRestore(string backupDir)
    {
        var srcs = ManifestSources(backupDir);
        if (srcs.Count == 0) { status = "Nothing to restore — no manifest in " + Path.GetFileName(backupDir) + "."; return; }

        // SELECTIVE RESTORE (2026-08-17, critical-review gap: restore was all-or-nothing, so recovering ONE thing
        // from an older snapshot also rolled every other group back to snapshot time). The SAME group checkboxes
        // that scope a backup scope a restore: a manifest rel path starts with its group key ("resources/…"), so
        // filter to the ticked groups. Non-standard snapshots (_deleted / _prerestore: keys like "restore0") have
        // no matching group keys — those restore whole, as before (they're single-purpose snapshots by nature).
        var groupKeys = new HashSet<string>(BuildGroups().Select(g => g.Key));
        bool standard = srcs.Any(s => groupKeys.Contains(s.rel.Replace('\\', '/').Split('/')[0]));
        string scopeNote = "";
        if (standard)
        {
            var picked = srcs.Where(s => enabled.TryGetValue(s.rel.Replace('\\', '/').Split('/')[0], out var on) && on).ToList();
            int skipped = srcs.Count - picked.Count;
            if (picked.Count == 0) { status = "Nothing selected to restore — tick the group checkboxes above (they scope Restore as well as Backup)."; return; }
            srcs = picked;
            scopeNote = skipped > 0 ? $"\n\nSCOPE: restoring only the {picked.Count} TICKED group source(s); {skipped} unticked group source(s) in this backup are left alone (the checkboxes above scope Restore too)." : "";
        }

        string list = string.Join("\n", srcs.Select(s => "  • " + s.original + "   (" + s.files + " files)"));
        if (!EditorUtility.DisplayDialog("Restore this backup?",
            $"Restoring '{Path.GetFileName(backupDir)}' will copy these back OVER the current files:\n\n{list}{scopeNote}\n\n" +
            "GUARDS: your CURRENT state is snapshotted to a _prerestore backup first, and files you've ADDED since are NOT deleted. " +
            "This only overwrites files that also exist in the backup.", "Snapshot & Restore", "Cancel")) { status = "Restore cancelled."; return; }

        // GUARD 1: snapshot the CURRENT state of exactly the paths we're about to overwrite (unique key per source so
        // two originals sharing a leaf name can't collide in the snapshot folder).
        var affected = new List<Group>();
        for (int i = 0; i < srcs.Count; i++) affected.Add(new Group(srcs[i].original, "restore" + i, new List<string> { srcs[i].original }));
        string snap = DoBackupInto(NewBackupDir("_prerestore_"), affected, "auto snapshot before restoring " + Path.GetFileName(backupDir));
        if (snap == null) { status = "Restore ABORTED — could not take the pre-restore safety snapshot (nothing was changed)."; return; }

        // GUARD 2: SMART additive copy back (2026-08-17, user-designed): only files that are MISSING or actually
        // DIFFERENT (byte-compared, not just size) are written — identical files are left completely untouched, so
        // Unity doesn't re-import ~1,600 unchanged assets — and the status reports all three classes.
        var st = new CopyStats();
        try
        {
            foreach (var s in srcs)
            {
                string from = Path.Combine(backupDir, s.rel);
                if (File.Exists(from)) SmartCopyFile(from, s.original, ref st);
                else if (Directory.Exists(from)) SmartCopyTree(from, s.original, ref st);
            }
            sizeCache.Clear();
            if (st.missing + st.changed > 0) AssetDatabase.Refresh();   // nothing written = nothing to reimport
            status = $"Restored {st.missing} missing + {st.changed} changed file(s) from '{Path.GetFileName(backupDir)}' " +
                     $"({st.identical} identical file(s) untouched). Current state was saved to '{Path.GetFileName(snap)}' first (undo by restoring that).";
        }
        catch (Exception e) { status = $"Restore FAILED midway ({e.Message}). Your pre-restore snapshot '{Path.GetFileName(snap)}' is intact — restore IT to get back."; }
    }

    // A snapshot that includes the config group must contain every live pack registry (haf_packs/*/pack.json) —
    // the exact file class whose silent absence the 2026-08-17 recovery drill exposed. Returns null when clean,
    // else a loud message; the caller marks the whole backup NOT ok. Extend as new must-have classes appear.
    static string VerifyCriticalContents(string dir, List<Group> groups)
    {
        try
        {
            if (!groups.Any(g => g.Key == "config")) return null;   // config not taken -> nothing to assert
            string cfg = SafeConfigDir();
            if (string.IsNullOrEmpty(cfg)) return null;
            string packs = Path.Combine(cfg, "haf_packs");
            if (!Directory.Exists(packs)) return null;
            var missing = new List<string>();
            foreach (var p in Directory.GetFiles(packs, "pack.json", SearchOption.AllDirectories))
            {
                string rel = p.Substring(cfg.Length).TrimStart('/', '\\');   // haf_packs/<mod>/pack.json
                if (!File.Exists(Path.Combine(dir, "config", rel))) missing.Add(rel.Replace('\\', '/'));
            }
            return missing.Count == 0 ? null
                : $"⚠ CRITICAL: this backup is MISSING the model registry file(s): {string.Join(", ", missing)} — it CANNOT fully recover a removed model. Do not trust it; fix the config group and back up again.";
        }
        catch { return null; }   // the verify must never break the backup itself
    }

    // ---- smart copy (restore only) ----
    struct CopyStats { public int missing, changed, identical; }

    static void SmartCopyFile(string src, string dst, ref CopyStats st)
    {
        if (!File.Exists(dst))
        { Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(src, dst); st.missing++; }
        else if (!FilesIdentical(src, dst))
        { File.Copy(src, dst, true); st.changed++; }
        else st.identical++;
    }

    static void SmartCopyTree(string src, string dst, ref CopyStats st)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) SmartCopyFile(f, Path.Combine(dst, Path.GetFileName(f)), ref st);
        foreach (var d in Directory.GetDirectories(src)) SmartCopyTree(d, Path.Combine(dst, Path.GetFileName(d)), ref st);
    }

    // Byte-equality via length check then buffered stream compare — cheap enough for a restore (a few seconds
    // over a 1 GB group on local disk), and CORRECT, unlike mtime/size heuristics.
    static bool FilesIdentical(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        const int BUF = 1 << 16;
        using (var sa = fa.OpenRead())
        using (var sb = fb.OpenRead())
        {
            var ba = new byte[BUF]; var bb = new byte[BUF];
            int ra;
            while ((ra = sa.Read(ba, 0, BUF)) > 0)
            {
                int rb = 0;
                while (rb < ra) { int k = sb.Read(bb, rb, ra - rb); if (k <= 0) return false; rb += k; }
                for (int i = 0; i < ra; i++) if (ba[i] != bb[i]) return false;
            }
        }
        return true;
    }

    void DeleteBackup(string dir)
    {
        if (!EditorUtility.DisplayDialog("Delete backup?", $"Permanently delete '{Path.GetFileName(dir)}'?\n\nThis removes the backup folder only — your live files are untouched.", "Delete", "Cancel")) return;
        try { Directory.Delete(dir, true); sizeCache.Clear(); status = "Deleted backup '" + Path.GetFileName(dir) + "'."; }
        catch (Exception e) { status = "Delete FAILED: " + e.Message; }
    }

    // ---- helpers ----
    string NewBackupDir(string prefix) => Path.Combine(dest, prefix + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));

    List<string> ListBackups()
    {
        try { return Directory.Exists(dest) ? Directory.GetDirectories(dest).OrderByDescending(d => d).ToList() : new List<string>(); }
        catch { return new List<string>(); }
    }

    struct MSrc { public string rel, original; public int files; }
    static List<MSrc> ManifestSources(string backupDir)
    {
        var outp = new List<MSrc>();
        string mf = Path.Combine(backupDir, "manifest.txt");
        if (!File.Exists(mf)) return outp;
        foreach (var line in File.ReadAllLines(mf))
        {
            if (!line.StartsWith("SRC\t")) continue;
            var p = line.Split('\t');
            if (p.Length >= 4) outp.Add(new MSrc { rel = p[1], original = p[2], files = int.TryParse(p[3], out var n) ? n : 0 });
        }
        return outp;
    }

    internal static int CopyFile(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        File.Copy(src, dst, true);
        return 1;
    }

    // Recursively copy src dir into dst, overwriting matching files but NEVER deleting files already in dst — so this is
    // inherently safe/additive for both backup (dst is fresh) and restore (dst keeps anything added since the backup).
    internal static int CopyTree(string src, string dst)
    {
        int n = 0;
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++;
        }
        foreach (var d in Directory.GetDirectories(src))
            n += CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        return n;
    }

    internal static int FileCount(string path)
    {
        try { return File.Exists(path) ? 1 : Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length : 0; }
        catch { return 0; }
    }

    internal static long TreeBytes(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }

    internal static string Human(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB" }; double b = bytes; int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:0.#} {u[i]}";
    }
}
