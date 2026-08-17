// BackupAuto.cs — the AUTOMATIC half of Backup & Restore (2026-08-17, user: "auto backup, especially when I
// remove assets… assets but also configuration… go back versions"). Two independent guards, both optional
// (toggles in the Backup & Restore window), both writing into the SAME versioned, restorable backup list:
//
//   1) DELETE GUARD (default ON): before ANY asset under a protected root (FactorySource, Resources, Databases,
//      Scripts/Editor) is deleted — the Factory's Remove flow, a Project-window delete, a script — the file or
//      folder (+ .meta) is first copied to <backup root>/_deleted_<timestamp>_<name>/ with a manifest naming the
//      original path. The delete then proceeds normally; the guard NEVER blocks it, it only makes it undoable.
//
//   2) DAILY AUTO-VERSION (default ON): on the first editor load of a day (>24h since the last), a full silent
//      backup of ALL groups — assets AND configuration (registry + skins + sounds + Databases) — runs through the
//      same core the "Back up now" button uses, so it appears in the window's list with a Restore button like any
//      manual version. The offsite zip rides along if configured. RETENTION: only the newest 3 _auto_ versions are
//      kept (rotation is logged loudly); manual backups and _deleted_ snapshots are NEVER auto-deleted.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

class HafDeleteGuard : UnityEditor.AssetModificationProcessor
{
    internal const string PrefOn = "HAF.Backup.DeleteGuard";
    static readonly string[] roots = { "Assets/FactorySource", "Assets/Resources", "Assets/Databases", "Assets/Scripts/Editor" };

    static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
    {
        try
        {
            if (!EditorPrefs.GetBool(PrefOn, true)) return AssetDeleteResult.DidNotDelete;
            string p = assetPath.Replace('\\', '/');
            if (!roots.Any(r => p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase) || p.Equals(r, StringComparison.OrdinalIgnoreCase)))
                return AssetDeleteResult.DidNotDelete;
            string dest = EditorPrefs.GetString("HAF.Backup.Dest", "D:/HAF_Backups");
            string projRoot = Directory.GetParent(Application.dataPath).FullName;
            string abs = Path.Combine(projRoot, p);
            string dir = Path.Combine(dest, "_deleted_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + "_" + Path.GetFileNameWithoutExtension(p));
            int n = 0;
            if (File.Exists(abs)) n = BackupWindow.CopyFile(abs, Path.Combine(dir, Path.GetFileName(abs)));
            else if (Directory.Exists(abs)) n = BackupWindow.CopyTree(abs, Path.Combine(dir, Path.GetFileName(abs.TrimEnd('/', '\\'))));
            if (File.Exists(abs + ".meta")) n += BackupWindow.CopyFile(abs + ".meta", Path.Combine(dir, Path.GetFileName(abs) + ".meta"));
            if (n > 0)
            {
                File.WriteAllLines(Path.Combine(dir, "manifest.txt"),
                    new[] { "# HAF delete-guard snapshot", "# original: " + abs.Replace('\\', '/'), "# files: " + n, "# restore: copy the content back to the original path (or keep it — it costs only disk)" });
                Debug.Log($"[HAF Backup] delete guard: {n} file(s) of '{p}' snapshotted → {dir} (the delete proceeded normally)");
            }
        }
        catch (Exception e) { Debug.LogWarning("[HAF Backup] delete guard could not snapshot '" + assetPath + "' (the delete still proceeded): " + e.Message); }
        return AssetDeleteResult.DidNotDelete;   // NEVER block or fail the delete — the guard only copies first
    }
}

[InitializeOnLoad]
static class HafAutoBackup
{
    internal const string PrefOn = "HAF.Backup.AutoDaily";
    internal const string PrefLast = "HAF.Backup.AutoLastTicks";
    internal const int Keep = 3;   // newest N _auto_ versions retained; older ones rotate out (logged)

    static HafAutoBackup() { EditorApplication.delayCall += MaybeRun; }

    static void MaybeRun()
    {
        try
        {
            if (!EditorPrefs.GetBool(PrefOn, true)) return;
            long last = long.TryParse(EditorPrefs.GetString(PrefLast, "0"), out var t) ? t : 0;
            if ((DateTime.Now - new DateTime(last)).TotalHours < 24) return;
            string dest = EditorPrefs.GetString("HAF.Backup.Dest", "D:/HAF_Backups");
            var groups = BackupWindow.BuildGroups();   // ALL groups — assets AND configuration; the auto net is deliberately complete
            if (groups.Count == 0) return;
            string dir = Path.Combine(dest, "_auto_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
            var r = BackupWindow.SnapshotInto(dir, groups, "daily auto-version");
            EditorPrefs.SetString(PrefLast, DateTime.Now.Ticks.ToString());
            Debug.Log("[HAF Backup] daily auto-version: " + r.report);
            if (!r.ok) return;

            // Offsite ride-along (background thread; Debug.Log is thread-safe).
            string off = EditorPrefs.GetString("HAF.Backup.OffsiteDest", "");
            if (EditorPrefs.GetBool("HAF.Backup.OffsiteAuto", true) && !string.IsNullOrEmpty(off))
                System.Threading.Tasks.Task.Run(() => Debug.Log("[HAF Backup] auto offsite: " + BackupWindow.OffsiteZipCore(dir, off)));

            // RETENTION: rotate _auto_ versions only — keep the newest N. Manual backups, _prerestore and _deleted
            // snapshots are never touched (the never-auto-delete rule holds for everything a human made or lost).
            var autos = Directory.GetDirectories(dest).Where(d => Path.GetFileName(d).StartsWith("_auto_")).OrderByDescending(d => d).ToList();
            foreach (var old in autos.Skip(Keep))
            {
                try { Directory.Delete(old, true); Debug.Log("[HAF Backup] rotated out old auto-version '" + Path.GetFileName(old) + "' (keeping the newest " + Keep + ")"); }
                catch (Exception e) { Debug.LogWarning("[HAF Backup] could not rotate '" + Path.GetFileName(old) + "': " + e.Message); }
            }
        }
        catch (Exception e) { Debug.LogWarning("[HAF Backup] daily auto-version failed: " + e.Message); }
    }
}
