// EXPERIMENTAL — the DISTRICT injection axis (the runtime half lives in HumankindAssetFramework's Hk_DistrictRepoint;
// see docs/District-Visuals.md). A district's on-map building is a static Amplitude FxMesh referenced by an
// FxEvolverMaterial (the "drawer" variant), resolved from the district's visual-affinity slot. To replace it we need two
// baked assets in the shipped bundle:
//   1. an FxMesh   — a ScriptableObject wrapping our UnityEngine.Mesh (trivial to author).
//   2. an FxEvolverMaterialDrawer that references that FxMesh — the material the game's public
//      PresentationLevelBuildComponent.SetChannel(int, Guid, ...) loads. Authoring one from scratch means guessing the
//      output-layer/subshader wiring, so instead we CLONE a vanilla drawer the user selects (inheriting all its shader
//      setup) and only swap its mesh GUID to ours.
// Two menu commands, matching that split. Both operate on the current Project selection so the modder drives the
// browse-the-SDK-assets step (finding a donor drawer) where it belongs — in the editor.
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DistrictBaker
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(t => t.FullName == fullName);
    static Type[] SafeTypes(Assembly a) { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }

    // "a,b,c,d" for an authored asset — same convention the unit registry uses (mirrors UniversalBaker.AmplitudeGuid).
    static string AmplitudeGuid(UnityEngine.Object asset)
    {
        var adb = FindType("Amplitude.Framework.Asset.AssetDatabase");
        var g = adb?.GetMethod("GetAssetGUID", new[] { typeof(UnityEngine.Object) })?.Invoke(null, new object[] { asset });
        if (g == null) return "";
        var t = g.GetType();
        return $"{t.GetField("a", BF)?.GetValue(g)},{t.GetField("b", BF)?.GetValue(g)},{t.GetField("c", BF)?.GetValue(g)},{t.GetField("d", BF)?.GetValue(g)}";
    }

    // CORE — wrap a baked mesh as a district FxMesh. Callable from the District Factory window (the normal path), the
    // Prop Lab (pawn attachments), and the menu command below. Returns the FxMesh's Amplitude GUID "a,b,c,d", or null.
    // mergeSubMeshes: flatten a multi-material bake's submeshes into ONE — the pawn-fragment GPU encoder only draws
    // submesh 0 (a two-material sling rendered cords but no pouch). Safe post-atlas: all submeshes share the packed UVs.
    public static string BakeFxMesh(Mesh mesh, string baseName, Vector3 importAngles, out string fxMeshPath, bool mergeSubMeshes = false)
    {
        fxMeshPath = null;
        if (mesh == null) { Debug.LogError("[District] BakeFxMesh: no mesh."); return null; }
        var fxMeshType = FindType("Amplitude.Graphics.Fx.FxMesh");
        if (fxMeshType == null) { Debug.LogError("[District] Amplitude.Graphics.Fx.FxMesh type not found (SDK not loaded?)."); return null; }

        // A unit static-bake rigs the mesh (boneWeights + bindposes) for its Skeleton. The DISTRICT path renders through a
        // STATIC shader that can't read a skinned vertex format — the mesh uploads but draws nothing. So build a bone-FREE
        // static copy (geometry only) and wrap THAT in the FxMesh. Keeps the original _ModelMesh intact for the unit path.
        var stat = new Mesh { name = baseName + "_DistrictMesh", indexFormat = mesh.indexFormat };
        stat.SetVertices(mesh.vertices);
        if (mesh.normals != null && mesh.normals.Length == mesh.vertexCount) stat.SetNormals(mesh.normals);
        if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount) stat.SetUVs(0, mesh.uv);
        if (mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount) stat.SetTangents(mesh.tangents);
        if (mesh.colors != null && mesh.colors.Length == mesh.vertexCount) stat.SetColors(mesh.colors);
        if (mergeSubMeshes && mesh.subMeshCount > 1)
        {
            var tris = new System.Collections.Generic.List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) tris.AddRange(mesh.GetTriangles(s));
            stat.subMeshCount = 1;
            stat.SetTriangles(tris, 0);
        }
        else
        {
            stat.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) stat.SetTriangles(mesh.GetTriangles(s), s);
        }
        // NO boneWeights / bindposes -> a pure static mesh the district shader can render.
        if (stat.tangents == null || stat.tangents.Length != stat.vertexCount) stat.RecalculateTangents();
        stat.RecalculateBounds();
        string statPath = "Assets/Resources/" + baseName + "_DistrictMesh.asset";
        AssetDatabase.DeleteAsset(statPath); AssetDatabase.CreateAsset(stat, statPath);

        string path = "Assets/Resources/" + baseName + "_FxMesh.asset";
        AssetDatabase.DeleteAsset(path);   // delete-first: CreateAsset over an existing asset can keep a stale serialized ref

        var fxMesh = ScriptableObject.CreateInstance(fxMeshType);
        fxMeshType.GetField("mesh", BF)?.SetValue(fxMesh, stat);   // wrap the BONE-FREE static copy
        // importAngles rotates the mesh at draw time. Vanilla district FxMeshes stand upright with (-90,0,0) (the FxMesh
        // default) — the game authors meshes Z-up and rotates them to the tile's Y-up. The Inspector preview on the
        // resulting <name>_FxMesh PREDICTS the in-game orientation — tune the bake rotation / these angles until it stands.
        var ia = fxMeshType.GetField("importAngles", BF);
        if (ia != null && ia.FieldType == typeof(Vector3)) ia.SetValue(fxMesh, importAngles);
        AssetDatabase.CreateAsset(fxMesh, path);
        EditorUtility.SetDirty(fxMesh);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        string guid = AmplitudeGuid(fxMesh);
        Debug.Log($"[District] FxMesh baked: {path}  (verts={mesh.vertexCount})  GUID={guid}");
        fxMeshPath = path;
        return string.IsNullOrEmpty(guid) ? null : guid;
    }

    // MANUAL step — wrap a baked mesh as an FxMesh from the Project selection. Superseded by the District Factory window
    // (which bakes model -> mesh -> FxMesh -> registry in one go) but kept for hand-driven experiments.
    [MenuItem("Tools/ENC/District/1. Bake District FxMesh (from selected _ModelMesh)")]
    static void BakeDistrictFxMesh()
    {
        var mesh = Selection.activeObject as Mesh;
        if (mesh == null)
        {
            EditorUtility.DisplayDialog("District FxMesh",
                "Select a baked mesh asset first (a <name>_ModelMesh.asset in Assets/Resources, produced by a normal static bake).", "OK");
            return;
        }
        string baseName = mesh.name.Replace("_ModelMesh", "");
        string guid = BakeFxMesh(mesh, baseName, new Vector3(-90f, 0f, 0f), out var path);
        if (guid == null) return;
        EditorGUIUtility.systemCopyBuffer = guid;
        EditorUtility.DisplayDialog("District FxMesh baked",
            $"{path}\nverts = {mesh.vertexCount}\nFxMesh GUID = {guid}\n\n(GUID copied to clipboard.)\n\n" +
            "Prefer the District Factory window (Tools ▸ ENC ▸ District Factory) — it writes the registry entry too.", "OK");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
    }

    // (A former "Step 2 — Clone District Material" menu command lived here: clone a vanilla FxEvolverMaterialDrawer and
    // repoint its mesh at our FxMesh, for the SetChannel path. REMOVED — the investigation proved any material handed in
    // via SetChannel is context-gated and draws nothing (see District-Visuals.md "History"); the working pipeline is the
    // District Factory window + the plugin's leaf fxMesh-swap. Recover from git history if ever needed.)
}
