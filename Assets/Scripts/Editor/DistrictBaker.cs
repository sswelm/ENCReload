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
    // Public: the District Factory also stamps the baked albedo atlas GUID into the registry (texture injection).
    public static string AmplitudeGuid(UnityEngine.Object asset)
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
    // levelOnGround (DISTRICT paths only): the game plants the mesh by its ORIGIN at the tile surface and rotates it by
    // importAngles at draw time — nothing re-grounds it, so a bake Rotation offset that changes which axis is "up" moves
    // the model's bottom off the origin plane (the nuclear plant sank to its domes). Shift the vertices so that AFTER the
    // importAngles rotation the lowest point sits at y=0 and the footprint is centered. NEVER for props/projectiles —
    // their pivots are meaningful (props glue to hand bones; a projectile's mesh-Z welds to its velocity).
    // postLevelOffset: the District Factory's Position-offset knob — a nudge in DRAWN-space world units (X/Z across the
    // tile, Y lifts off the ground) applied AFTER the leveling, so leveling can't cancel it out.
    // clipHexPct (>0): CLIP the leveled mesh to the tile hex (100 = the exact in-game cell, inradius 3.465 — the same
    // hex the previews draw, flat edge facing +Z), so an oversized site-plan model ends at the cell border like a
    // vanilla district instead of overhanging its neighbors. Clipping runs in the same drawn-space frame as the
    // leveling (via rotated plane normals); cut faces are left open — fine from the game's top-down camera.
    public static string BakeFxMesh(Mesh mesh, string baseName, Vector3 importAngles, out string fxMeshPath, bool mergeSubMeshes = false, bool levelOnGround = false, Vector3 postLevelOffset = default, float clipHexPct = 0f)
    {
        fxMeshPath = null;
        if (mesh == null) { Debug.LogError("[District] BakeFxMesh: no mesh."); return null; }
        var fxMeshType = FindType("Amplitude.Graphics.Fx.FxMesh");
        if (fxMeshType == null) { Debug.LogError("[District] Amplitude.Graphics.Fx.FxMesh type not found (SDK not loaded?)."); return null; }

        // A unit static-bake rigs the mesh (boneWeights + bindposes) for its Skeleton. The DISTRICT path renders through a
        // STATIC shader that can't read a skinned vertex format — the mesh uploads but draws nothing. So build a bone-FREE
        // static copy (geometry only) and wrap THAT in the FxMesh. Keeps the original _ModelMesh intact for the unit path.
        var verts = mesh.vertices;
        var R = Quaternion.Euler(importAngles);
        if (levelOnGround && verts.Length > 0)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < verts.Length; i++)
            {
                var w = R * verts[i];
                min = Vector3.Min(min, w); max = Vector3.Max(max, w);
            }
            // desired shift in DRAWN space: footprint centered on the tile, bottom on the surface, then the author's
            // Position-offset nudge — applied to the stored vertices through the inverse rotation so the draw-time
            // importAngles land it exactly there
            var shift = new Vector3(-(min.x + max.x) * 0.5f, -min.y, -(min.z + max.z) * 0.5f) + postLevelOffset;
            if (shift.sqrMagnitude > 1e-10f)
            {
                var t = Quaternion.Inverse(R) * shift;
                for (int i = 0; i < verts.Length; i++) verts[i] += t;
                Debug.Log($"[District] {baseName}: leveled on the tile surface (drawn-space shift {shift}, offset {postLevelOffset})");
            }
        }

        // gather attributes + per-submesh triangle lists (the clip rebuilds them; the plain path passes them through)
        var normals = mesh.normals; var uvs = mesh.uv; var tangents = mesh.tangents; var colors = mesh.colors;
        bool hasN = normals != null && normals.Length == mesh.vertexCount;
        bool hasU = uvs != null && uvs.Length == mesh.vertexCount;
        bool hasT = tangents != null && tangents.Length == mesh.vertexCount;
        bool hasC = colors != null && colors.Length == mesh.vertexCount;
        var subTris = new int[mesh.subMeshCount][];
        for (int s = 0; s < mesh.subMeshCount; s++) subTris[s] = mesh.GetTriangles(s);

        if (clipHexPct > 0f && verts.Length > 0)
        {
            int before = verts.Length;
            ClipToTileHex(ref verts, ref normals, ref uvs, ref tangents, ref colors, hasN, hasU, hasT, hasC, subTris, R, clipHexPct);
            Debug.Log($"[District] {baseName}: clipped to the tile hex at {clipHexPct:0}% ({before} -> {verts.Length} verts)");
        }

        var stat = new Mesh { name = baseName + "_DistrictMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        stat.SetVertices(verts);
        if (hasN && normals.Length == verts.Length) stat.SetNormals(normals);
        if (hasU && uvs.Length == verts.Length) stat.SetUVs(0, uvs);
        if (hasT && tangents.Length == verts.Length) stat.SetTangents(tangents);
        if (hasC && colors.Length == verts.Length) stat.SetColors(colors);
        if (mergeSubMeshes && subTris.Length > 1)
        {
            var tris = new System.Collections.Generic.List<int>();
            for (int s = 0; s < subTris.Length; s++) tris.AddRange(subTris[s]);
            stat.subMeshCount = 1;
            stat.SetTriangles(tris, 0);
        }
        else
        {
            stat.subMeshCount = subTris.Length;
            for (int s = 0; s < subTris.Length; s++) stat.SetTriangles(subTris[s], s);
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

    // ---- tile-hex clipping ------------------------------------------------------------------------------------------
    // Cut the mesh to the in-game tile cell: six vertical planes forming the SAME hex the previews draw (inradius
    // 3.465 × pct, flat edge facing drawn-space +Z, corners at 30°+k·60°). The mesh verts are in STORED space (the
    // draw-time importAngles rotation R not yet applied), so each drawn-space plane normal n is tested as (R⁻¹n)·v —
    // rotations preserve distances, no vertex round-trip needed. Sutherland–Hodgman per boundary triangle with linear
    // interpolation of ALL attributes; fully-inside triangles keep their original shared vertices (no growth), fully-
    // outside ones are dropped. Cut faces are left OPEN (no cap) — invisible from the game's camera angles.
    struct ClipV
    {
        public Vector3 p, n; public Vector2 uv; public Vector4 t; public Color c;
        public static ClipV Lerp(ClipV a, ClipV b, float f) => new ClipV
        {
            p = Vector3.LerpUnclamped(a.p, b.p, f),
            n = Vector3.LerpUnclamped(a.n, b.n, f),
            uv = Vector2.LerpUnclamped(a.uv, b.uv, f),
            t = Vector4.LerpUnclamped(a.t, b.t, f),
            c = Color.LerpUnclamped(a.c, b.c, f),
        };
    }

    static void ClipToTileHex(ref Vector3[] verts, ref Vector3[] normals, ref Vector2[] uvs, ref Vector4[] tangents,
        ref Color[] colors, bool hasN, bool hasU, bool hasT, bool hasC, int[][] subTris, Quaternion R, float pct)
    {
        const float TileInradius = 3.465f;   // = ModelFactoryWindow.TileInradius (the measured 6.93 tile spacing / 2)
        float r = TileInradius * pct / 100f;
        var planes = new Vector3[6];
        var Rinv = Quaternion.Inverse(R);
        for (int k = 0; k < 6; k++)
        {
            float a = (30f + 60f * k) * Mathf.Deg2Rad;   // district cell is CORNER-forward: edge normals at 30°+k·60° from +Z (matches the preview hex)
            planes[k] = Rinv * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
        }

        // per-vertex inside-ness (against all 6 planes) so interior triangles can be kept without any rebuild
        var inside = new bool[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < 6 && ok; k++) ok = Vector3.Dot(verts[i], planes[k]) <= r + 1e-4f;
            inside[i] = ok;
        }

        var nv = new System.Collections.Generic.List<Vector3>(verts);
        var nn = new System.Collections.Generic.List<Vector3>(hasN ? normals : new Vector3[verts.Length]);
        var nu = new System.Collections.Generic.List<Vector2>(hasU ? uvs : new Vector2[verts.Length]);
        var nt = new System.Collections.Generic.List<Vector4>(hasT ? tangents : new Vector4[verts.Length]);
        var nc = new System.Collections.Generic.List<Color>(hasC ? colors : new Color[verts.Length]);
        ClipV At(int i) => new ClipV
        {
            p = nv[i], n = hasN ? nn[i] : Vector3.up, uv = hasU ? nu[i] : Vector2.zero,
            t = hasT ? nt[i] : new Vector4(1, 0, 0, 1), c = hasC ? nc[i] : Color.white,
        };
        int Emit(ClipV v)
        {
            nv.Add(v.p); nn.Add(v.n); nu.Add(v.uv); nt.Add(v.t); nc.Add(v.c);
            return nv.Count - 1;
        }

        var poly = new System.Collections.Generic.List<ClipV>(8);
        var next = new System.Collections.Generic.List<ClipV>(8);
        for (int s = 0; s < subTris.Length; s++)
        {
            var src = subTris[s];
            var dst = new System.Collections.Generic.List<int>(src.Length);
            for (int i = 0; i < src.Length; i += 3)
            {
                int a = src[i], b = src[i + 1], c = src[i + 2];
                if (inside[a] && inside[b] && inside[c]) { dst.Add(a); dst.Add(b); dst.Add(c); continue; }
                poly.Clear(); poly.Add(At(a)); poly.Add(At(b)); poly.Add(At(c));
                for (int k = 0; k < 6 && poly.Count >= 3; k++)
                {
                    next.Clear();
                    for (int j = 0; j < poly.Count; j++)
                    {
                        var cur = poly[j]; var nxt = poly[(j + 1) % poly.Count];
                        float dc = Vector3.Dot(cur.p, planes[k]) - r, dn = Vector3.Dot(nxt.p, planes[k]) - r;
                        if (dc <= 0f) next.Add(cur);
                        if ((dc <= 0f) != (dn <= 0f)) next.Add(ClipV.Lerp(cur, nxt, dc / (dc - dn)));
                    }
                    (poly, next) = (next, poly);
                }
                if (poly.Count < 3) continue;   // fully outside
                int i0 = Emit(poly[0]);
                int prev = Emit(poly[1]);
                for (int j = 2; j < poly.Count; j++)
                {
                    int curIdx = Emit(poly[j]);
                    dst.Add(i0); dst.Add(prev); dst.Add(curIdx);
                    prev = curIdx;
                }
            }
            subTris[s] = dst.ToArray();
        }
        verts = nv.ToArray();
        if (hasN) normals = nn.ToArray();
        if (hasU) uvs = nu.ToArray();
        if (hasT) tangents = nt.ToArray();
        if (hasC) colors = nc.ToArray();
    }

    // MANUAL step — wrap a baked mesh as an FxMesh from the Project selection. Superseded by the District Factory window
    // (which bakes model -> mesh -> FxMesh -> registry in one go) but kept for hand-driven experiments.
    [MenuItem("Tools/HAF/District/1. Bake District FxMesh (from selected _ModelMesh)")]
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
        string guid = BakeFxMesh(mesh, baseName, new Vector3(-90f, 0f, 0f), out var path, levelOnGround: true);
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
