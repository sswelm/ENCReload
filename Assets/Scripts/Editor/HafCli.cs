using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Headless CLI entry points for HAF authoring, callable via Unity batch mode. See docs/Headless-CLI-Design.md.
//
//   Unity.exe -batchmode -quit -projectPath <ENCReload> -logFile - -executeMethod HAF.Cli.RebuildModel -model <resourceName> [-fresh]
//   Unity.exe -batchmode -quit -projectPath <ENCReload> -logFile - -executeMethod HAF.Cli.RebuildModel -all
//   Unity.exe -batchmode -quit -projectPath <ENCReload> -logFile - -executeMethod HAF.Cli.CleanExport
//
// Each verb prints one JSON result line prefixed [HAF-CLI] and sets the process exit code:
//   0 = ok, 2 = not found / bad arg, 3 = bake or save failed.
// RebuildModel reuses the EXACT path the Model Factory's Bake button and BakeSmokeTest use
// (ModelRegistry.Load -> ModelFactoryWindow.ConfigFor -> UniversalBaker.Build/BuildAnimated -> copy GUIDs -> Upsert),
// so it cannot drift from the GUI. build-mod (the SDK asset-bundle/Community export) is intentionally NOT here yet —
// its exact entry point is still being confirmed against the real build menu.
namespace HAF
{
    public static class Cli
    {
        static string Arg(string key)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return null;
        }
        static bool Flag(string key) => Environment.GetCommandLineArgs().Contains(key);
        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        static void Emit(int code, string json)
        {
            Debug.Log("[HAF-CLI] " + json);
            Console.Out.Flush();
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // -executeMethod HAF.Cli.RebuildModel  -model <resourceName> [-fresh]   |   -all
        public static void RebuildModel()
        {
            try
            {
                var defs = ModelRegistry.Load();
                bool all = Flag("-all");
                string name = Arg("-model");
                if (!all && string.IsNullOrWhiteSpace(name)) { Emit(2, "{\"ok\":false,\"error\":\"pass -model <resourceName> or -all\"}"); return; }

                var targets = all
                    ? defs.Where(d => !string.IsNullOrWhiteSpace(d.modelFile)).ToList()
                    : defs.Where(d => d.resourceName == name).ToList();
                if (!all && targets.Count == 0) { Emit(2, "{\"ok\":false,\"error\":\"model '" + Esc(name) + "' not found in registry\"}"); return; }

                bool fresh = Flag("-fresh");
                int ok = 0, failed = 0;
                foreach (var cur in targets)
                {
                    if (string.IsNullOrWhiteSpace(cur.modelFile)) continue;
                    var cfg = ModelFactoryWindow.ConfigFor(cur);   // the one shared config path — can't drift from the GUI
                    if (fresh) cfg.reuseExtracted = false;         // force a full re-slim (default honours the entry's keep-texture setting)
                    var r = cfg.animated ? UniversalBaker.BuildAnimated(cfg) : UniversalBaker.Build(cfg);
                    if (!r.ok) { failed++; Debug.LogError("[HAF-CLI] bake FAILED " + cur.resourceName + ": " + r.error); continue; }

                    // Copy the baked GUIDs back exactly as ModelFactoryWindow.DoBake does.
                    cur.skel = ModelRegistry.ParseGuid(r.skeletonGuid);
                    cur.atlas = ModelRegistry.ParseGuid(r.atlasGuid);
                    cur.clip = cfg.animated ? ModelRegistry.ParseGuid(r.clipGuid) : new int[4];
                    bool sd = cfg.animated && cfg.animStateDriven;
                    cur.clipMove = sd ? ModelRegistry.ParseGuid(r.clipMoveGuid) : new int[4];
                    cur.clipAfter = sd && !string.IsNullOrEmpty(r.clipAfterGuid) ? ModelRegistry.ParseGuid(r.clipAfterGuid) : new int[4];
                    cur.clipAttack = sd && !string.IsNullOrEmpty(r.clipAttackGuid) ? ModelRegistry.ParseGuid(r.clipAttackGuid) : new int[4];
                    cur.clipCombat = sd && !string.IsNullOrEmpty(r.clipCombatGuid) ? ModelRegistry.ParseGuid(r.clipCombatGuid) : new int[4];
                    cur.clipPreMove = sd && !string.IsNullOrEmpty(r.clipPreMoveGuid) ? ModelRegistry.ParseGuid(r.clipPreMoveGuid) : new int[4];
                    cur.clipIdle = sd && !string.IsNullOrEmpty(r.clipIdleGuid) ? ModelRegistry.ParseGuid(r.clipIdleGuid) : new int[4];
                    cur.clipIdleAlt = sd && !string.IsNullOrEmpty(r.clipIdleAltGuid) ? ModelRegistry.ParseGuid(r.clipIdleAltGuid) : new int[4];
                    cur.clipIdleAlt2 = sd && !string.IsNullOrEmpty(r.clipIdleAlt2Guid) ? ModelRegistry.ParseGuid(r.clipIdleAlt2Guid) : new int[4];

                    if (!ModelRegistry.Upsert(cur)) { failed++; Debug.LogError("[HAF-CLI] registry save FAILED " + cur.resourceName); continue; }
                    ok++;
                    Debug.Log("[HAF-CLI] rebuilt " + cur.resourceName + (cfg.animated ? " (animated)" : " (static)"));
                }
                AssetDatabase.SaveAssets();
                Emit(failed == 0 ? 0 : 3, "{\"ok\":" + (failed == 0 ? "true" : "false") + ",\"rebuilt\":" + ok + ",\"failed\":" + failed + "}");
            }
            catch (Exception ex) { Emit(3, "{\"ok\":false,\"error\":\"" + Esc(ex.ToString()) + "\"}"); }
        }

        // -executeMethod HAF.Cli.CleanExport   removes the previous ENCReload export from Humankind's Community folder
        // (the "An error happens while trying to move your mod ... is denied" fix — mirrors Clean-ENCReload-Export.bat,
        // scoped to ENCReload's own mod GUID only). Run before a mod build.
        public static void CleanExport()
        {
            try
            {
                const string community = @"C:\GameData\Humankind\Community";
                const string modGuid = "cd3480e932114f8084db755ddd65f2d8";
                int removed = 0;
                if (Directory.Exists(community))
                    foreach (var dir in Directory.GetDirectories(community, "ENCReload." + modGuid + ".*"))
                    {
                        Directory.Delete(dir, true);
                        removed++;
                        Debug.Log("[HAF-CLI] removed export " + Path.GetFileName(dir));
                    }
                Emit(0, "{\"ok\":true,\"removed\":" + removed + "}");
            }
            catch (Exception ex) { Emit(3, "{\"ok\":false,\"error\":\"" + Esc(ex.ToString()) + "\"}"); }
        }

        // -executeMethod HAF.Cli.BuildMod   FULL build + deploy — calls the game's own Mod Editor build:
        // Amplitude.Mercury.Production.Modification.ModuleEditor.BuildModification(RuntimeModule, StandaloneWindows64, false),
        // the private synchronous overload that builds the versioned runtime module AND copies it to the Community folder
        // (CopyModification). It's batch-mode-aware (skips dialogs, returns false on DB errors). Called via reflection so
        // the editor compile-check stays independent of the Mercury SDK DLL. This is the exact "Mercury ▸ Mod Editor" build.
        public static void BuildMod()
        {
            try
            {
                var meType = ResolveType("Amplitude.Mercury.Production.Modification.ModuleEditor");
                if (meType == null) { Emit(3, "{\"ok\":false,\"error\":\"ModuleEditor type not found\"}"); return; }
                var rmProp = meType.GetProperty("RuntimeModule", BindingFlags.Public | BindingFlags.Static);
                var runtimeModule = rmProp?.GetValue(null);
                if (rmProp == null || runtimeModule == null) { Emit(3, "{\"ok\":false,\"error\":\"active RuntimeModule not found\"}"); return; }
                var m = meType.GetMethod("BuildModification", BindingFlags.NonPublic | BindingFlags.Static, null,
                            new[] { rmProp.PropertyType, typeof(BuildTarget), typeof(bool) }, null);
                if (m == null) { Emit(3, "{\"ok\":false,\"error\":\"BuildModification(RuntimeModule,BuildTarget,bool) not found\"}"); return; }
                bool ok = (bool)m.Invoke(null, new object[] { runtimeModule, BuildTarget.StandaloneWindows64, false });
                Emit(ok ? 0 : 3, "{\"ok\":" + (ok ? "true" : "false") + ",\"note\":\"" + (ok ? "built + deployed to Community" : "build/deploy failed — see log") + "\"}");
            }
            catch (Exception ex) { Emit(3, "{\"ok\":false,\"error\":\"" + Esc(ex.ToString()) + "\"}"); }
        }

        static Type ResolveType(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(fullName); if (t != null) return t; }
            return null;
        }
    }
}
