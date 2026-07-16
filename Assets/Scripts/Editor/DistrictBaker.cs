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

    // STEP 1 — wrap a baked mesh as an FxMesh. Select the model's <name>_ModelMesh.asset (the output of a normal static
    // bake, already oriented/scaled by the Factory) and run this. Writes <name>_FxMesh.asset and logs its GUID.
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
        var fxMeshType = FindType("Amplitude.Graphics.Fx.FxMesh");
        if (fxMeshType == null) { Debug.LogError("[District] Amplitude.Graphics.Fx.FxMesh type not found (SDK not loaded?)."); return; }

        string baseName = mesh.name.Replace("_ModelMesh", "");

        // A unit static-bake rigs the mesh (boneWeights + bindposes) for its Skeleton. The DISTRICT path renders through a
        // STATIC shader that can't read a skinned vertex format — the mesh uploads but draws nothing. So build a bone-FREE
        // static copy (geometry only) and wrap THAT in the FxMesh. Keeps the original _ModelMesh intact for the unit path.
        var stat = new Mesh { name = baseName + "_DistrictMesh", indexFormat = mesh.indexFormat };
        stat.SetVertices(mesh.vertices);
        if (mesh.normals != null && mesh.normals.Length == mesh.vertexCount) stat.SetNormals(mesh.normals);
        if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount) stat.SetUVs(0, mesh.uv);
        if (mesh.tangents != null && mesh.tangents.Length == mesh.vertexCount) stat.SetTangents(mesh.tangents);
        if (mesh.colors != null && mesh.colors.Length == mesh.vertexCount) stat.SetColors(mesh.colors);
        stat.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++) stat.SetTriangles(mesh.GetTriangles(s), s);
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
        // default) — the game authors meshes Z-up and rotates them to the tile's Y-up. Start there so it stands; the modder
        // can fine-tune this on the FxMesh asset in the Inspector (no re-bake — just tweak + rebuild the mod) if it's tilted.
        var ia = fxMeshType.GetField("importAngles", BF);
        if (ia != null && ia.FieldType == typeof(Vector3)) ia.SetValue(fxMesh, new Vector3(-90f, 0f, 0f));
        AssetDatabase.CreateAsset(fxMesh, path);
        EditorUtility.SetDirty(fxMesh);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        string guid = AmplitudeGuid(fxMesh);
        Debug.Log($"[District] FxMesh baked: {path}  (verts={mesh.vertexCount})  GUID={guid}");
        EditorGUIUtility.systemCopyBuffer = guid;
        EditorUtility.DisplayDialog("District FxMesh baked",
            $"{path}\nverts = {mesh.vertexCount}\nFxMesh GUID = {guid}\n\n(GUID copied to clipboard.)\n\n" +
            "Next: select a vanilla FxEvolverMaterialDrawer asset to clone (step 2), which points a district material at this FxMesh.", "OK");
        Selection.activeObject = fxMesh;
    }

    // STEP 2 — clone a vanilla drawer material and repoint it at our FxMesh. Select TWO assets: a source
    // FxEvolverMaterialDrawer (a vanilla district material, so we inherit its shader/output-layer setup) and our
    // <name>_FxMesh.asset from step 1. Writes <name>_DistrictMat.asset; its GUID goes into the plugin's DistrictEvolverGuid.
    [MenuItem("Tools/ENC/District/2. Clone District Material (select a vanilla drawer + our FxMesh)")]
    static void CloneDistrictMaterial()
    {
        var drawerType = FindType("Amplitude.Mercury.Fx.HgFx.FxEvolverMaterialDrawer");
        var fxMeshType = FindType("Amplitude.Graphics.Fx.FxMesh");
        if (drawerType == null || fxMeshType == null) { Debug.LogError("[District] FxEvolverMaterialDrawer / FxMesh type not found (SDK not loaded?)."); return; }

        var sel = Selection.objects;
        var srcDrawer = sel.FirstOrDefault(o => o != null && drawerType.IsInstanceOfType(o));
        var ourFxMesh = sel.FirstOrDefault(o => o != null && fxMeshType.IsInstanceOfType(o));
        if (srcDrawer == null || ourFxMesh == null)
        {
            EditorUtility.DisplayDialog("Clone District Material",
                "Select BOTH:\n • a vanilla FxEvolverMaterialDrawer asset (the donor material to clone), and\n" +
                " • our <name>_FxMesh.asset (from step 1).\n\nThen run this again.", "OK");
            return;
        }

        // resolve the FxMesh's GUID and write it into a clone of the donor drawer's private 'mesh' field.
        var meshGuidStr = AmplitudeGuid(ourFxMesh);
        var guidType = FindType("Amplitude.Framework.Guid");
        var parts = meshGuidStr.Split(',');
        object meshGuid = Activator.CreateInstance(guidType);
        guidType.GetField("a", BF)?.SetValue(meshGuid, int.Parse(parts[0]));
        guidType.GetField("b", BF)?.SetValue(meshGuid, int.Parse(parts[1]));
        guidType.GetField("c", BF)?.SetValue(meshGuid, int.Parse(parts[2]));
        guidType.GetField("d", BF)?.SetValue(meshGuid, int.Parse(parts[3]));

        var clone = UnityEngine.Object.Instantiate(srcDrawer);   // deep-copies the serialized shader/output-layer wiring
        var meshField = drawerType.GetField("mesh", BF);
        if (meshField == null) { Debug.LogError("[District] FxEvolverMaterialDrawer.mesh field not found (SDK changed?)."); return; }
        meshField.SetValue(clone, meshGuid);

        string baseName = ourFxMesh.name.Replace("_FxMesh", "");
        string path = "Assets/Resources/" + baseName + "_DistrictMat.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clone, path);
        EditorUtility.SetDirty(clone);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();

        string matGuid = AmplitudeGuid(clone);
        Debug.Log($"[District] District material cloned from '{srcDrawer.name}' -> {path}  mesh={meshGuidStr}  MATERIAL GUID={matGuid}");
        EditorGUIUtility.systemCopyBuffer = matGuid;
        EditorUtility.DisplayDialog("District material cloned",
            $"{path}\ncloned from: {srcDrawer.name}\nmesh -> our FxMesh ({meshGuidStr})\n\nMATERIAL GUID = {matGuid}\n(copied to clipboard)\n\n" +
            "Put this into the plugin config:\n[District]\nDistrictEvolverGuid = " + matGuid, "OK");
        Selection.activeObject = clone;
    }
}
