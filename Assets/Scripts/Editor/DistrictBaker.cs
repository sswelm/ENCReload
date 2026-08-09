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
using System.Collections.Generic;
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
    public static string BakeFxMesh(Mesh mesh, string baseName, Vector3 importAngles, out string fxMeshPath, bool mergeSubMeshes = false, bool levelOnGround = false, Vector3 postLevelOffset = default, float clipHexPct = 0f, float foundationDepth = 0f, Vector2 foundationUV = default)
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
        var Rinv = Quaternion.Inverse(R);
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

        // FOUNDATION: a solid concrete plinth extruded straight DOWN into the earth (world -Y) under the building's
        // footprint. On a cliff/uneven tile the building otherwise overhangs into empty air; this plants it on a base.
        // Built in DRAWN space (post-rotation, so "down" is true world -Y regardless of importAngles), then inverse-
        // rotated into stored space so R lands it straight-down at draw time. UVs point at a grey concrete swatch the
        // Factory paints into a corner of the atlas.
        if (foundationDepth > 0f && verts.Length > 0)
        {
            var dmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var dmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < verts.Length; i++)
            {
                var d = R * verts[i];
                dmin = Vector3.Min(dmin, d); dmax = Vector3.Max(dmax, d);
            }
            // Exact footprint, top tucked just under the building's lowest point — fills the overhang cleanly in both
            // preview and game. (A Z-fight can appear where the plinth meets the building's own walls at map distance —
            // a depth-buffer-precision limit; deferred, to solve later without disturbing this shape.)
            float x0 = dmin.x, x1 = dmax.x;
            float z0 = dmin.z, z1 = dmax.z;
            float topY = dmin.y + 0.05f;                   // tuck the wall tops just under the building's lowest point
            float botY = dmin.y - foundationDepth;         // straight down into the earth
            var dc = new[]
            {
                new Vector3(x0, topY, z0), new Vector3(x1, topY, z0),
                new Vector3(x1, topY, z1), new Vector3(x0, topY, z1),   // 0-3 top rim
                new Vector3(x0, botY, z0), new Vector3(x1, botY, z0),
                new Vector3(x1, botY, z1), new Vector3(x0, botY, z1),   // 4-7 floor
            };
            int b = verts.Length;
            var nv = new System.Collections.Generic.List<Vector3>(verts);
            var nn = hasN ? new System.Collections.Generic.List<Vector3>(normals) : null;
            var nu = hasU ? new System.Collections.Generic.List<Vector2>(uvs) : null;
            var nt = hasT ? new System.Collections.Generic.List<Vector4>(tangents) : null;
            var ncol = hasC ? new System.Collections.Generic.List<Color>(colors) : null;
            var center = new Vector3((dmin.x + dmax.x) * 0.5f, (topY + botY) * 0.5f, (dmin.z + dmax.z) * 0.5f);
            for (int i = 0; i < 8; i++)
            {
                nv.Add(Rinv * dc[i]);
                if (hasN) { var nrm = dc[i] - center; nrm.y *= 0.15f; nn.Add((Rinv * nrm).normalized); }
                if (hasU) nu.Add(foundationUV);
                if (hasT) nt.Add(new Vector4(1, 0, 0, 1));
                if (hasC) ncol.Add(Color.white);
            }
            // 4 side walls + floor (top cap omitted — hidden under the building). Wound so each face's front normal
            // (Unity's cross(v1-v0,v2-v0)) points OUTWARD / down — the sides face away from the box, the floor faces -Y.
            int[] f =
            {
                b+0,b+5,b+4, b+0,b+1,b+5,   // -Z wall
                b+1,b+6,b+5, b+1,b+2,b+6,   // +X wall
                b+2,b+7,b+6, b+2,b+3,b+7,   // +Z wall
                b+3,b+4,b+7, b+3,b+0,b+4,   // -X wall
                b+4,b+6,b+7, b+4,b+5,b+6,   // floor (-Y)
            };
            verts = nv.ToArray();
            if (hasN) normals = nn.ToArray();
            if (hasU) uvs = nu.ToArray();
            if (hasT) tangents = nt.ToArray();
            if (hasC) colors = ncol.ToArray();
            var s0 = new System.Collections.Generic.List<int>(subTris[0]); s0.AddRange(f); subTris[0] = s0.ToArray();
            Debug.Log($"[District] {baseName}: foundation plinth appended (depth {foundationDepth:0.0}, footprint {(dmax.x - dmin.x):0.0}x{(dmax.z - dmin.z):0.0})");
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

    // ---- PIZZA compose: merge extra part meshes onto the base district mesh ------------------------------------------
    // Bake-time composition (the runtime still ships ONE FxMesh + ONE atlas): each part arrives as its own baked,
    // size-scaled mesh + albedo atlas. All atlases pack into a SUPER-ATLAS (each source's [0,1] UVs remap into its
    // rect), each part is grounded to the BASE's floor and placed by facing + posOffset in DRAWN space (the entry's
    // composed draw rotation R), then everything is transformed back to STORED space and appended — one submesh per
    // source. Downstream (BakeFxMesh auto-level / hex clip / texture injection) runs on the merged result unchanged.
    // One source on the pizza: its baked mesh, its albedo atlas, its OPTIONAL surface-map atlases (null = neutral
    // fill in the super maps), and its placement. The base is source 0 with identity placement.
    public struct ComposeSource
    {
        public Mesh mesh; public Texture2D albedo, normal, rough; public float facing; public Vector3 posOffset;
        public float alphaBoost;   // <=0 or 1 = no-op; >1 multiplies the source's alpha + dilates (cutout-foliage fullness)
        public float leafScale;    // <=0 or 1 = no-op; >1 scales each SMALL disconnected island (leaf card) around its centroid
        public List<Vector3> copies;   // extra placements of the same part (a grove): geometry appended per copy, ONE atlas slot; each copy auto-rotates by the golden angle
    }

    // GEOMETRY leaf sizing: texture dilation can't outgrow the card, so scale every small disconnected triangle
    // island around its own centroid. Selection is by CHARACTERISTIC, not material: leaf cards are thousands of
    // tiny islands, the trunk is one big connected island (any island spanning >25% of the mesh is left alone).
    static Vector3[] ScaledLeafCards(Mesh m, float factor)
    {
        var verts = m.vertices;
        int n = verts.Length;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var t = m.GetTriangles(s);
            for (int k = 0; k < t.Length; k += 3) { Union(t[k], t[k + 1]); Union(t[k + 1], t[k + 2]); }
        }
        // triangles per island: a LEAF CARD is 1-4 tris; a TWIG is a many-tri cylinder. The first size-only
        // selector scaled twigs into spears ("spiked desert bush") — tri-count is the leaf/twig discriminator.
        var triCount = new Dictionary<int, int>();
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var t = m.GetTriangles(s);
            for (int k = 0; k < t.Length; k += 3)
            {
                int r = Find(t[k]);
                triCount.TryGetValue(r, out int cnt); triCount[r] = cnt + 1;
            }
        }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<int>();
            list.Add(i);
        }
        float meshSize = m.bounds.size.magnitude;
        // branch-class vertex cloud (islands >4 tris): each leaf card scales around its STEM — the card vertex
        // nearest to this cloud — so the attachment point stays glued to its twig. Centroid scaling detached the
        // leaves ("hanging in the air"): the stem end moved away from the branch by (factor-1) x half a card.
        var branchVerts = new List<Vector3>();
        foreach (var kv in groups)
        { triCount.TryGetValue(kv.Key, out int tc); if (tc > 4) foreach (var i in kv.Value) branchVerts.Add(verts[i]); }
        int scaled = 0;
        foreach (var kv in groups)
        {
            triCount.TryGetValue(kv.Key, out int tris);
            if (tris > 4) continue;   // twig/trunk-class geometry — leave it (only true leaf CARDS scale)
            var g = kv.Value;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var c = Vector3.zero;
            foreach (var i in g) { var v = verts[i]; min = Vector3.Min(min, v); max = Vector3.Max(max, v); c += v; }
            if ((max - min).magnitude > meshSize * 0.25f) continue;   // oversized flat island — leave it
            c /= g.Count;
            // anchor = the card vertex closest to any branch vertex (fallback: centroid when no branches exist)
            var anchor = c;
            if (branchVerts.Count > 0)
            {
                float best = float.MaxValue;
                foreach (var i in g)
                {
                    var v = verts[i];
                    for (int b = 0; b < branchVerts.Count; b++)
                    {
                        float d = (branchVerts[b] - v).sqrMagnitude;
                        if (d < best) { best = d; anchor = v; }
                    }
                }
            }
            foreach (var i in g) verts[i] = anchor + (verts[i] - anchor) * factor;
            scaled++;
        }
        Debug.Log($"[District] leaf size x{factor:0.0}: scaled {scaled} of {groups.Count} card island(s) around their stems ({branchVerts.Count} branch verts anchored them)");
        return verts;
    }

    public static Mesh ComposeDistrict(ComposeSource baseSrc, List<ComposeSource> parts,
        Quaternion R, int atlasCap, Vector3 entryPosOffset, out Texture2D superAtlas, out Texture2D superNormal, out Texture2D superRough)
    {
        var baseMesh = baseSrc.mesh;
        var Rinv = Quaternion.Inverse(R);
        Texture2D CopyBoosted(Texture2D src, float boost)
        {
            var c = ReadableCopy(src);
            if (boost > 1f)
            {
                // CUTOUT-FOLIAGE FULLNESS, two mechanisms (both needed — measured on the beech leaf sheet):
                // 1) alpha GAIN for soft-alpha sources (authored for a low cutoff, eroded by the game's threshold);
                // 2) DILATION for BINARY-alpha sources (the beech: 19k texels a=0, 2k a=255, ~120 between — gain is
                //    a NO-OP there). Each round grows every leaf by one texel via a 3x3 alpha-max, copying the
                //    winning neighbor's RGB so grown edges stay leaf-coloured instead of fringing black.
                var px = c.GetPixels32();
                int w = c.width, h = c.height;
                for (int i = 0; i < px.Length; i++) px[i].a = (byte)Mathf.Min(255f, px[i].a * boost);
                int rounds = Mathf.Clamp(Mathf.RoundToInt(boost - 1f), 0, 6);
                for (int r = 0; r < rounds; r++)
                {
                    var srcPx = (Color32[])px.Clone();
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int i = y * w + x;
                            if (srcPx[i].a >= 250) continue;
                            byte bestA = srcPx[i].a; int bestI = -1;
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int nx = x + dx, ny = y + dy;
                                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                                    int ni = ny * w + nx;
                                    if (srcPx[ni].a > bestA) { bestA = srcPx[ni].a; bestI = ni; }
                                }
                            if (bestI >= 0) px[i] = srcPx[bestI];
                        }
                }
                c.SetPixels32(px); c.Apply();
                int op = 0; for (int i = 0; i < px.Length; i += 31) if (px[i].a >= 128) op++;
                Debug.Log($"[District] part fullness {boost:0.0}: {rounds} dilation round(s), opaque coverage now ~{op * 3100 / px.Length}% of sampled texels");
            }
            return c;
        }
        var texs = new List<Texture2D> { CopyBoosted(baseSrc.albedo, baseSrc.alphaBoost) };
        foreach (var p in parts) texs.Add(CopyBoosted(p.albedo, p.alphaBoost));
        superAtlas = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = baseMesh.name + "_SuperAtlas" };
        var rects = superAtlas.PackTextures(texs.ToArray(), 2, Mathf.Clamp(atlasCap * 2, 1024, 8192));

        // SUPER SURFACE MAPS — same rects as the albedo pack (the remapped UVs index all three for free), neutral
        // fill where a source ships no maps (flat normal / matte rough — the per-entry verified stand-ins). Without
        // this, composing DROPPED the base's baked maps and the donor's maps tinted the whole model (the blue-temple
        // launch). Area-average blit: a single bilinear tap aliases dense normal maps into rainbow static (measured).
        int sw2 = superAtlas.width, sh2 = superAtlas.height;
        var npx = Fill(sw2, sh2, new Color32(128, 128, 255, 128));
        var rpx = Fill(sw2, sh2, new Color32(140, 140, 140, 140));
        void BlitMaps(int rectIdx, Texture2D normal, Texture2D rough)
        {
            if (normal != null) { var c = ReadableCopy(normal); BlitIntoRectArea(c, npx, sw2, sh2, rects[rectIdx]); UnityEngine.Object.DestroyImmediate(c); }
            if (rough != null) { var c = ReadableCopy(rough); BlitIntoRectArea(c, rpx, sw2, sh2, rects[rectIdx]); UnityEngine.Object.DestroyImmediate(c); }
        }
        BlitMaps(0, baseSrc.normal, baseSrc.rough);
        for (int i = 0; i < parts.Count; i++) BlitMaps(i + 1, parts[i].normal, parts[i].rough);
        superNormal = new Texture2D(sw2, sh2, TextureFormat.RGBA32, false) { name = baseMesh.name + "_SuperNormal" };
        superNormal.SetPixels32(npx); superNormal.Apply();
        superRough = new Texture2D(sw2, sh2, TextureFormat.RGBA32, false) { name = baseMesh.name + "_SuperRough" };
        superRough.SetPixels32(rpx); superRough.Apply();

        // the base's DRAWN footprint center + floor. Parts place RELATIVE TO THE BASE CENTER (not the raw origin):
        // the base-anchored leveling below re-centers everything on the base, so a part placed at raw `off` would be
        // silently shifted by -baseCenter — the temple's center sits north of origin (its -90° bake), which ate the
        // north component of every tree's offset (the "should be NE, lands on the E line" bug). Add baseCenter here,
        // leveling subtracts it, net = exactly `off` on the tile.
        float baseMinY = float.MaxValue;
        var bcMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var bcMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        var bv = baseMesh.vertices;
        for (int i = 0; i < bv.Length; i++) { var d = R * bv[i]; if (d.y < baseMinY) baseMinY = d.y; bcMin = Vector3.Min(bcMin, d); bcMax = Vector3.Max(bcMax, d); }
        var baseCenterXZ = new Vector3((bcMin.x + bcMax.x) * 0.5f, 0f, (bcMin.z + bcMax.z) * 0.5f);

        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt4 = new List<Vector4>();
        var subs = new List<int[]>();
        void Append(Mesh m, Rect rect, bool isBase, float facing, Vector3 off, Vector3[] overrideVerts)
        {
            int start = nv.Count;
            var vs = overrideVerts ?? m.vertices;   // copies share ONE leaf-scaled vertex array (the stem search is the slow part)
            var ns = m.normals; var us = m.uv; var ts = m.tangents;
            bool hasN = ns != null && ns.Length == vs.Length;
            bool hasU = us != null && us.Length == vs.Length;
            bool hasT = ts != null && ts.Length == vs.Length;
            var Rp = Quaternion.Euler(0f, facing, 0f);
            Vector3 shift = Vector3.zero; Vector3 pivot = Vector3.zero;
            if (!isBase)
            {
                // PREDICTABLE PLACEMENT: the offset places the part's own FOOTPRINT CENTER, and facing spins the
                // part around its own axis. Rotating around the raw mesh origin made copies ORBIT a downloaded
                // model's arbitrary pivot (the beech's trunk is off-origin — a golden-angle copy landed off the
                // hex entirely). Pivot = the part's XZ bounds center; ground = its own lowest point.
                var pmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var pmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < vs.Length; i++) { pmin = Vector3.Min(pmin, vs[i]); pmax = Vector3.Max(pmax, vs[i]); }
                pivot = new Vector3((pmin.x + pmax.x) * 0.5f, 0f, (pmin.z + pmax.z) * 0.5f);
                float minY = float.MaxValue;
                for (int i = 0; i < vs.Length; i++) { float y = (Rp * (vs[i] - pivot)).y; if (y < minY) minY = y; }
                // + baseCenterXZ so the offset is measured from the TILE CENTER (leveling subtracts baseCenter again)
                shift = new Vector3(off.x + baseCenterXZ.x, baseMinY - minY + off.y, off.z + baseCenterXZ.z);
            }
            if (!isBase) Debug.Log($"[District] placement: offset ({off.x:0.0}, {off.y:0.0}, {off.z:0.0}) [X=east, Z=north] · facing {facing:0.0}° · lands at tile ({off.x:0.0}, {off.z:0.0}) from center");
            for (int i = 0; i < vs.Length; i++)
            {
                nv.Add(isBase ? vs[i] : Rinv * (Rp * (vs[i] - pivot) + shift));
                var nrm = hasN ? ns[i] : Vector3.up;
                nn.Add(isBase ? nrm : Rinv * (Rp * nrm));
                var uv = hasU ? us[i] : Vector2.zero;
                nu.Add(new Vector2(rect.x + uv.x * rect.width, rect.y + uv.y * rect.height));
                var t4 = hasT ? ts[i] : new Vector4(1f, 0f, 0f, 1f);
                if (!isBase) { var xyz = Rinv * (Rp * new Vector3(t4.x, t4.y, t4.z)); t4 = new Vector4(xyz.x, xyz.y, xyz.z, t4.w); }
                nt4.Add(t4);
            }
            var tris = new List<int>();
            for (int s = 0; s < m.subMeshCount; s++) { var st = m.GetTriangles(s); for (int k = 0; k < st.Length; k++) tris.Add(st[k] + start); }
            subs.Add(tris.ToArray());
        }
        Append(baseMesh, rects[0], isBase: true, 0f, Vector3.zero, null);
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var pv = p.leafScale > 1.01f ? ScaledLeafCards(p.mesh, p.leafScale) : null;   // computed once, shared by all copies
            Append(p.mesh, rects[i + 1], isBase: false, p.facing, p.posOffset, pv);
            if (p.copies != null)
                for (int k = 0; k < p.copies.Count; k++)   // golden-angle facing per copy — a grove, not an army of clones
                    Append(p.mesh, rects[i + 1], isBase: false, p.facing + 137.5f * (k + 1), p.copies[k], pv);
        }

        // BASE-ANCHORED leveling, in drawn space: the generic auto-level centers the UNION footprint, so a grove
        // weighting one side shoved the temple into a corner (measured). The BASE alone decides centering + the
        // ground plane (+ the entry's Position offset); every part rides along exactly where its author put it.
        int baseVertCount = baseMesh.vertexCount;   // the base is appended first
        var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < baseVertCount; i++) { var d = R * nv[i]; bmin = Vector3.Min(bmin, d); bmax = Vector3.Max(bmax, d); }
        var shiftDrawn = new Vector3(-(bmin.x + bmax.x) * 0.5f, -bmin.y, -(bmin.z + bmax.z) * 0.5f) + entryPosOffset;
        var shiftStored = Rinv * shiftDrawn;
        for (int i = 0; i < nv.Count; i++) nv[i] += shiftStored;

        var merged = new Mesh { name = baseMesh.name + "_Composed", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        merged.SetVertices(nv); merged.SetNormals(nn); merged.SetUVs(0, nu); merged.SetTangents(nt4);
        merged.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) merged.SetTriangles(subs[s], s);
        merged.RecalculateBounds();
        return merged;
    }

    static Color32[] Fill(int w, int h, Color32 c)
    {
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        return px;
    }

    // AREA-AVERAGE blit of a readable source into a normalized rect of a Color32 canvas. One source box per dst
    // pixel — a single bilinear tap aliases dense normal maps into rainbow static (the surface-map arc's lesson #1).
    static void BlitIntoRectArea(Texture2D src, Color32[] dst, int dw, int dh, Rect rect)
    {
        if (src == null) return;
        int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.x * dw), 0, dw - 1), y0 = Mathf.Clamp(Mathf.RoundToInt(rect.y * dh), 0, dh - 1);
        int rw = Mathf.Clamp(Mathf.RoundToInt(rect.width * dw), 1, dw - x0), rh = Mathf.Clamp(Mathf.RoundToInt(rect.height * dh), 1, dh - y0);
        var sp = src.GetPixels32(); int sw = src.width, sh = src.height;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                float u0 = x / (float)rw * sw, u1 = (x + 1) / (float)rw * sw;
                float v0 = y / (float)rh * sh, v1 = (y + 1) / (float)rh * sh;
                int iu0 = Mathf.FloorToInt(u0), iu1 = Mathf.Min(sw - 1, Mathf.Max(iu0, Mathf.CeilToInt(u1) - 1));
                int iv0 = Mathf.FloorToInt(v0), iv1 = Mathf.Min(sh - 1, Mathf.Max(iv0, Mathf.CeilToInt(v1) - 1));
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int sy = iv0; sy <= iv1; sy++)
                    for (int sx = iu0; sx <= iu1; sx++)
                    { var c = sp[sy * sw + sx]; r += c.r; g += c.g; b += c.b; a += c.a; n++; }
                dst[(y0 + y) * dw + (x0 + x)] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
            }
    }

    // GPU-blit copy: atlas assets may be compressed / non-readable — PackTextures needs readable RGBA32. Null-safe
    // (an untextured part contributes a flat light-grey patch instead of failing the whole compose).
    static Texture2D ReadableCopy(Texture2D src)
    {
        if (src == null)
        {
            var w = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(200, 200, 200, 255);
            w.SetPixels32(px); w.Apply();
            return w;
        }
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return t;
    }

    // ---- foundation concrete swatch ---------------------------------------------------------------------------------
    // Grow the district atlas set by a CONCRETE STRIP along the top, slide existing content down into the remaining
    // area, and remap the mesh's UVs to match (v' = v · oldFrac). The strip is fresh canvas — no existing texel is
    // overwritten — giving the foundation plinth a flat grey concrete region to sample. Albedo gets a lightly noised
    // grey; the normal map a neutral (flat) fill; the roughness map a rough concrete value. Rewrites the three .asset
    // files (albedo/normal/rough) in place; the caller re-reads their GUIDs afterward. Returns the UV to bake into the
    // foundation faces (the strip's center). No-op-safe: a missing normal/rough atlas is simply skipped.
    public static Vector2 AppendConcreteStrip(string resourceName, Mesh mesh)
    {
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/" + resourceName + "_Atlas.asset");
        if (albedo == null)
        {
            Debug.LogWarning($"[District] foundation: '{resourceName}' has no _Atlas — the plinth will render untextured.");
            return new Vector2(0.5f, 0.5f);
        }
        int w = albedo.width, h = albedo.height;
        int stripH = Mathf.Max(4, ((h / 16) + 3) / 4 * 4);   // ~1/16 of the height, rounded up to a multiple of 4 (DXT)
        int newH = h + stripH;
        float oldFrac = (float)h / newH;
        float uvV = (h + stripH * 0.5f) / newH;               // center of the strip in the grown atlas

        void Rebuild(string suffix, System.Func<int, int, Color32> stripPixel, TextureFormat fmt)
        {
            string p = "Assets/Resources/" + resourceName + suffix + ".asset";
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (src == null) return;                          // map-less model: no normal/rough atlas to grow
            var old = ReadableCopy(src);
            var tex = new Texture2D(w, newH, TextureFormat.RGBA32, false);
            tex.SetPixels32(0, 0, w, h, old.GetPixels32());   // existing content slides to the BOTTOM
            var strip = new Color32[w * stripH];
            for (int y = 0; y < stripH; y++)
                for (int x = 0; x < w; x++) strip[y * w + x] = stripPixel(x, y);
            tex.SetPixels32(0, h, w, stripH, strip);          // concrete strip fills the TOP
            tex.Apply(false, false);
            EditorUtility.CompressTexture(tex, fmt, TextureCompressionQuality.Normal);
            tex.Apply(false, false);
            tex.name = resourceName + suffix;
            AssetDatabase.DeleteAsset(p);
            AssetDatabase.CreateAsset(tex, p);
            UnityEngine.Object.DestroyImmediate(old);
        }

        // subtle deterministic grain so the concrete reads as a surface, not a flat block
        Color32 Concrete(int x, int y)
        {
            uint hsh = (uint)((x * 73856093) ^ (y * 19349663)); hsh ^= hsh >> 13;
            int n = (int)(hsh % 17) - 8;                       // ±8
            byte g(int b) => (byte)Mathf.Clamp(b + n, 0, 255);
            return new Color32(g(150), g(148), g(144), 255);
        }
        Rebuild("_Atlas", Concrete, albedo.format == TextureFormat.DXT5 || albedo.format == TextureFormat.RGBA32 ? TextureFormat.DXT5 : TextureFormat.DXT1);
        Rebuild("_NormalAtlas", (x, y) => new Color32(128, 128, 255, 255), TextureFormat.DXT5);   // flat tangent-space normal
        Rebuild("_RoughAtlas", (x, y) => new Color32(205, 205, 205, 255), TextureFormat.DXT1);    // rough concrete

        // slide every UV down into the old-content band so the model still samples its own texels
        var uv = mesh.uv;
        if (uv != null && uv.Length > 0)
        {
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].x, uv[i].y * oldFrac);
            mesh.uv = uv;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[District] {resourceName}: concrete strip appended ({w}x{h} -> {w}x{newH}); foundation UV=(0.5,{uvV:0.000})");
        return new Vector2(0.5f, uvV);
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
