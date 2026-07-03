// DescriptorNullPinpointer.cs  (editor-only diagnostic)
//
// Pinpoints the EXACT field path of broken references inside a descriptor — instead of just "this asset throws".
// It deep-walks the selected asset(s) via reflection and reports every NULL ELEMENT inside an array/list (which is
// what a missing/undeserializable type looks like at runtime — e.g. a scenario effect/prerequisite/variable whose
// type the Mod-Tools SDK doesn't include). That null element is exactly what the SDK's
// NarrativeEventDefinition.EnumerateSimulationEventVariables(object target) chokes on (target.GetType() with no
// null-guard), so this points you straight at it.
//
// USE: select one or more assets (or a sub-asset) in the Project window -> Tools > Diagnostics > Pinpoint Null
//      References. Results print as warnings with the full path, e.g.
//      [Pinpoint] NarrativeEvent_..._001 .Trigger.SimulationEventTrigger.Variables[0].ElementPrerequisites[2] = NULL
//
// Reflects FIELDS only (not properties) so it never triggers the engine's throwing getters. Editor-only, removable.

#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DescriptorNullPinpointer
{
    private const int MaxDepth = 16;
    private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [MenuItem("Tools/Diagnostics/Pinpoint Null References (selected)")]
    private static void RunSelected()
    {
        var sel = Selection.objects;
        if (sel == null || sel.Length == 0)
        {
            Debug.LogWarning("[Pinpoint] Select one or more assets (or a sub-asset) in the Project window first.");
            return;
        }

        int total = 0, scanned = 0;
        foreach (var o in sel)
        {
            if (o == null) continue;
            // include sub-assets (narrative events are sub-assets of the collection)
            var path = AssetDatabase.GetAssetPath(o);
            var objs = string.IsNullOrEmpty(path) ? new[] { o } : AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var a in objs)
            {
                if (a == null) continue;
                scanned++;
                total += Scan(a);
            }
        }
        Debug.Log($"[Pinpoint] Scanned {scanned} object(s). Found {total} suspicious null element(s). " +
                  (total == 0 ? "No broken references in the data (if it still throws, the cause is runtime/context, not a bad ref)." : ""));
    }

    private static int Scan(UnityEngine.Object root)
    {
        var found = new List<string>();
        Walk(root, root.GetType().Name + " '" + root.name + "'", 0,
             new HashSet<object>(ReferenceComparer.Instance), found);
        foreach (var f in found) Debug.LogWarning("[Pinpoint] " + f, root);
        return found.Count;
    }

    // FIELDS ONLY — reads field values via reflection (no code execution). Never invokes property getters: some
    // engine getters call into native code that crashes the editor when poked outside a running game.
    private static void Walk(object obj, string path, int depth, HashSet<object> seen, List<string> found)
    {
        if (obj == null || depth > MaxDepth) return;
        var t = obj.GetType();
        if (t.IsPrimitive || obj is string || t.IsEnum) return;
        if (!seen.Add(obj)) return;   // cycle guard

        foreach (var f in t.GetFields(BF))
        {
            if (f.IsStatic) continue;
            object val;
            try { val = f.GetValue(obj); } catch { continue; }
            if (val == null) continue;                       // a null leaf field is usually intentional; we hunt null ELEMENTS
            if (val is UnityEngine.Object) continue;          // a separate asset reference -> don't recurse into it

            string fp = path + "." + f.Name;

            if (val is IList list)
            {
                var et = ElementType(val.GetType());
                bool reportNulls = et == null || (!et.IsValueType && et != typeof(string));
                for (int i = 0; i < list.Count; i++)
                {
                    object el;
                    try { el = list[i]; } catch { continue; }
                    if (el == null)
                    {
                        if (reportNulls)
                            found.Add(fp + "[" + i + "] = NULL  (element type " + (et != null ? et.Name : "?") +
                                      ") — a missing/undeserializable type (e.g. SDK lacks this scenario type)");
                    }
                    else Walk(el, fp + "[" + i + "]", depth + 1, seen, found);
                }
            }
            else if (!t.IsPrimitive)
            {
                Walk(val, fp, depth + 1, seen, found);
            }
        }
    }

    private static Type ElementType(Type listType)
    {
        if (listType.IsArray) return listType.GetElementType();
        if (listType.IsGenericType)
        {
            var ga = listType.GetGenericArguments();
            if (ga.Length == 1) return ga[0];
        }
        return null;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
}
#endif
