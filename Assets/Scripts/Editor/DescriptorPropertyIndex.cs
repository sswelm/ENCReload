using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class DescriptorPropertyIndex : EditorWindow
{
    // ── Serialized cache types ────────────────────────────────────────────────
    [Serializable]
    class Row
    {
        public string assetName, startingType, path, targetClass, property, op, formula;
        public string scope;        // "Vanilla" | "Mod"
        public string guid;
        public long   fileId;
        [NonSerialized] public string definition;   // computed at merge
        [NonSerialized] public bool   isModded;     // name in both scopes
        [NonSerialized] public bool   isDup;        // name 2+ times in mod scope
    }

    [Serializable] class MapEntry { public string name; public List<string> owners = new(); }
    [Serializable] class Cache
    {
        public string builtUtc = "";
        public List<Row> vanillaRows = new();
        public List<MapEntry> vanillaDefs = new();
    }

    static string CachePath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "DescriptorIndexCache.json");
    string VanillaFolderKey => "DescIndex.VanillaFolder." + Application.dataPath.GetHashCode();

    // ── Reflection cache ──────────────────────────────────────────────────────
    static Type      s_descriptorType;
    static FieldInfo f_startingType, f_effects;
    static FieldInfo f_applyOnSource, f_path, f_propertyEffects;
    static FieldInfo f_propertyToFollow;
    static FieldInfo f_targetProperty, f_toTargetOp;
    static FieldInfo f_rpnStack, f_constantStack, f_propertyLocalNames;
    static FieldInfo f_rawValue;
    static long      s_oneRaw = 1000;
    static Type      s_operationEnum;
    static int OP_Add, OP_Sub, OP_Mult, OP_Div, OP_Percent, OP_Pow, OP_Max, OP_Min;
    static int OP_GetTarget, OP_GetSource, OP_GetConst, OP_GetVariable, OP_GetWorld;
    static bool s_fieldsResolved;

    // ── State ─────────────────────────────────────────────────────────────────
    List<Row> _vanillaRows = new();
    List<Row> _modRows = new();
    List<Row> _allRows = new();
    List<Row> _view = new();
    Dictionary<string, List<string>> _vanillaDefs = new();
    string _builtUtc = "";
    string _vanillaFolder = "Assets/VanillaReference";

    enum ScopeFilter { All, MyMod, Vanilla, ModdedOriginals, DatabaseDuplicates }
    static readonly string[] SCOPE_LABELS = { "All", "My Mod", "Vanilla", "Modded *", "Dupes !" };
    ScopeFilter _scopeFilter = ScopeFilter.All;

    string _filterName = "", _filterProperty = "", _filterStartType = "", _filterTargetClass = "", _filterPath = "";
    List<string> _startTypes = new(), _targetClasses = new();
    int  _startTypeIdx, _targetClassIdx;
    bool _startTypeDD, _targetClassDD;

    MultiColumnHeader _header;
    MultiColumnHeaderState _headerState;
    Vector2 _scroll;
    const float ROW_H = 18f;

    // Row tints (work on the dark editor skin)
    static readonly Color ROW_ALT   = new Color(1f, 1f, 1f, 0.03f);
    static readonly Color ROW_HOVER = new Color(0.3f, 0.5f, 0.9f, 0.18f);
    static readonly Color ROW_LINE  = new Color(0f, 0f, 0f, 0.25f);
    enum Col { Descriptor, Scope, Definition, Starting, Path, Target, Property, Op, Formula }

    [MenuItem("Tools/Descriptor Property Browser")]
    static void Open() => GetWindow<DescriptorPropertyIndex>("Descriptor Browser");

    void OnEnable()
    {
        wantsMouseMove = true;   // needed for live hover tracking
        _vanillaFolder = EditorPrefs.GetString(VanillaFolderKey, "Assets/VanillaReference");
        BuildHeader();
        TryResolveFields();
        LoadCache();
        ScanModScope();   // live
        RebuildDerived();
    }

    // ── Header ────────────────────────────────────────────────────────────────
    void BuildHeader()
    {
        var cols = new[]
        {
            MakeCol("Descriptor", 220, 130, false),
            MakeCol("Scope",      70,  50,  true),
            MakeCol("Definition", 190, 110, true),
            MakeCol("Starting",   110, 70,  true),
            MakeCol("Path",       160, 90,  true),
            MakeCol("Target",     110, 70,  true),
            MakeCol("Property",   150, 90,  true),
            MakeCol("Op",         60,  40,  true),
            MakeCol("Formula",    400, 140, true),
        };
        _headerState = new MultiColumnHeaderState(cols);
        _header = new MultiColumnHeader(_headerState);
        _header.sortingChanged += (_) => { ReSort(); Repaint(); };
        _header.ResizeToFit();
    }

    static MultiColumnHeaderState.Column MakeCol(string title, float w, float min, bool auto) =>
        new MultiColumnHeaderState.Column
        {
            headerContent = new GUIContent(title),
            width = w, minWidth = min, autoResize = auto, canSort = true,
            allowToggleVisibility = title != "Descriptor",
            headerTextAlignment = TextAlignment.Left,
        };

    // ── GUI ───────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        if (Event.current.type == EventType.MouseMove) Repaint();
        EditorGUILayout.Space(6);

        // Vanilla folder config
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Vanilla folder", GUILayout.Width(90));
        string newFolder = EditorGUILayout.TextField(_vanillaFolder);
        if (newFolder != _vanillaFolder) { _vanillaFolder = newFolder; EditorPrefs.SetString(VanillaFolderKey, _vanillaFolder); }
        if (GUILayout.Button("Pick", GUILayout.Width(50)))
        {
            string abs = EditorUtility.OpenFolderPanel("Vanilla reference folder", Application.dataPath, "");
            if (!string.IsNullOrEmpty(abs) && abs.StartsWith(Application.dataPath))
            {
                _vanillaFolder = "Assets" + abs.Substring(Application.dataPath.Length);
                EditorPrefs.SetString(VanillaFolderKey, _vanillaFolder);
            }
        }
        EditorGUILayout.EndHorizontal();

        // Build controls
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild Vanilla Cache", GUILayout.Height(24))) RebuildVanillaCache();
        if (GUILayout.Button("Refresh Mod", GUILayout.Width(100), GUILayout.Height(24))) { ScanModScope(); RebuildDerived(); }
        using (new EditorGUI.DisabledScope(_vanillaRows.Count == 0))
            if (GUILayout.Button("Clear Cache", GUILayout.Width(90), GUILayout.Height(24))) ClearCache();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(
            $"Vanilla: {_vanillaRows.Count} cached ({(_builtUtc.Length > 0 ? _builtUtc : "never built")}) · Mod: {_modRows.Count} live",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _scopeFilter = (ScopeFilter)GUILayout.Toolbar((int)_scopeFilter, SCOPE_LABELS);
        _filterName = EditorGUILayout.TextField("Descriptor Name", _filterName);
        _filterProperty = EditorGUILayout.TextField("Target Property", _filterProperty);
        DropdownFilter("Starting Type", ref _filterStartType, _startTypes, ref _startTypeDD, ref _startTypeIdx);
        DropdownFilter("Target Class", ref _filterTargetClass, _targetClasses, ref _targetClassDD, ref _targetClassIdx);
        _filterPath = EditorGUILayout.TextField("Path contains", _filterPath);
        if (EditorGUI.EndChangeCheck()) ApplyFilters();

        EditorGUILayout.LabelField($"{_view.Count} / {_allRows.Count} rows shown", EditorStyles.miniLabel);

        if (_allRows.Count > 0) DrawTable();
    }

    void DropdownFilter(string label, ref string filter, List<string> options, ref bool useDD, ref int idx)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(140));
        if (options.Count > 0)
        {
            useDD = EditorGUILayout.ToggleLeft("list", useDD, GUILayout.Width(44));
            if (useDD)
            {
                var opts = new[] { "(any)" }.Concat(options).ToArray();
                idx = Mathf.Clamp(idx, 0, opts.Length - 1);
                idx = EditorGUILayout.Popup(idx, opts, GUILayout.ExpandWidth(true));
                filter = idx == 0 ? "" : options[idx - 1];
            }
            else filter = EditorGUILayout.TextField(filter);
        }
        else filter = EditorGUILayout.TextField(filter);
        EditorGUILayout.EndHorizontal();
    }

    // ── Filtering ─────────────────────────────────────────────────────────────
    void ApplyFilters()
    {
        string nm = _filterName.Trim().ToLowerInvariant();
        string p  = _filterProperty.Trim().ToLowerInvariant();
        string s  = _filterStartType.Trim().ToLowerInvariant();
        string t  = _filterTargetClass.Trim().ToLowerInvariant();
        string pa = _filterPath.Trim().ToLowerInvariant();

        bool ScopeOk(Row r) => _scopeFilter switch
        {
            ScopeFilter.All                => true,
            ScopeFilter.MyMod              => r.scope == "Mod",
            ScopeFilter.Vanilla            => r.scope == "Vanilla",
            ScopeFilter.ModdedOriginals    => r.isModded,
            ScopeFilter.DatabaseDuplicates => r.isDup,
            _ => true
        };

        _view = _allRows.Where(r => ScopeOk(r) &&
            (nm.Length == 0 || (r.assetName   ?? "").ToLowerInvariant().Contains(nm)) &&
            (p.Length  == 0 || (r.property    ?? "").ToLowerInvariant().Contains(p)) &&
            (s.Length  == 0 || (r.startingType?? "").ToLowerInvariant().Contains(s)) &&
            (t.Length  == 0 || (r.targetClass ?? "").ToLowerInvariant().Contains(t)) &&
            (pa.Length == 0 || (r.path        ?? "").ToLowerInvariant().Contains(pa))
        ).ToList();

        ReSort();
        Repaint();
    }

    // Merge vanilla + mod, recompute definitions and collision flags
    void RebuildDerived()
    {
        _allRows = new List<Row>(_vanillaRows.Count + _modRows.Count);
        _allRows.AddRange(_vanillaRows);
        _allRows.AddRange(_modRows);

        // merged definition map (vanilla cached + live mod)
        var defs = new Dictionary<string, List<string>>(_vanillaDefs);
        foreach (var kv in _liveModDefs)
        {
            if (!defs.TryGetValue(kv.Key, out var owners)) defs[kv.Key] = owners = new List<string>();
            foreach (var o in kv.Value) if (!owners.Contains(o)) owners.Add(o);
        }

        var vanillaNames = new HashSet<string>(_vanillaRows.Select(r => r.assetName));
        var modNames     = new HashSet<string>(_modRows.Select(r => r.assetName));
        // distinct mod asset identities per name → duplicate detection
        var modIdentities = new Dictionary<string, HashSet<string>>();
        foreach (var r in _modRows)
        {
            if (!modIdentities.TryGetValue(r.assetName, out var set)) modIdentities[r.assetName] = set = new();
            set.Add(r.guid + ":" + r.fileId);
        }

        foreach (var r in _allRows)
        {
            r.definition = defs.TryGetValue(r.assetName, out var o) ? string.Join(", ", o) : "";
            r.isModded = vanillaNames.Contains(r.assetName) && modNames.Contains(r.assetName);
            r.isDup    = modIdentities.TryGetValue(r.assetName, out var set) && set.Count >= 2;
        }

        _startTypes    = _allRows.Select(r => r.startingType).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        _targetClasses = _allRows.Select(r => r.targetClass).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToList();
        ApplyFilters();
    }

    // ── Table ─────────────────────────────────────────────────────────────────
    string Cell(Row r, Col c) => c switch
    {
        Col.Descriptor => (r.isModded ? "*" : "") + (r.isDup ? "!" : "") + r.assetName,
        Col.Scope      => r.scope,
        Col.Definition => r.definition,
        Col.Starting   => r.startingType,
        Col.Path       => r.path,
        Col.Target     => r.targetClass,
        Col.Property   => r.property,
        Col.Op         => r.op,
        Col.Formula    => r.formula,
        _ => ""
    };

    void DrawTable()
    {
        if (_header == null) BuildHeader();
        float totalW = _headerState.widthOfAllVisibleColumns;
        float headerH = _header.height;
        var visible = _headerState.visibleColumns;

        // Pinned header, horizontally synced to the body scroll
        Rect headerRect = GUILayoutUtility.GetRect(10, 10000, headerH, headerH);
        _header.OnGUI(headerRect, _scroll.x);

        // Body viewport fills the remaining window space
        Rect bodyArea = GUILayoutUtility.GetRect(10, 10000, 10, 100000,
            GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        Rect content = new Rect(0, 0, totalW, _view.Count * ROW_H);

        _scroll = GUI.BeginScrollView(bodyArea, _scroll, content);

        // Virtualization: only draw rows intersecting the viewport
        int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / ROW_H));
        int last  = Mathf.Min(_view.Count, Mathf.CeilToInt((_scroll.y + bodyArea.height) / ROW_H) + 1);

        Vector2 mouse = Event.current.mousePosition;
        for (int i = first; i < last; i++)
        {
            var r = _view[i];
            Rect rowRect = new Rect(0, i * ROW_H, totalW, ROW_H);

            // Background: hover takes priority, else zebra striping
            bool hover = rowRect.Contains(mouse);
            if (hover)             EditorGUI.DrawRect(rowRect, ROW_HOVER);
            else if ((i & 1) == 1) EditorGUI.DrawRect(rowRect, ROW_ALT);

            for (int vc = 0; vc < visible.Length; vc++)
            {
                int colIndex = visible[vc];
                Rect cell = _header.GetCellRect(vc, rowRect);
                string text = Cell(r, (Col)colIndex);
                if ((Col)colIndex == Col.Descriptor)
                {
                    if (GUI.Button(cell, new GUIContent(text, text), EditorStyles.linkLabel)) Ping(r);
                }
                else EditorGUI.LabelField(cell, new GUIContent(text, text));
            }

            // Separator line at the bottom of the row
            EditorGUI.DrawRect(new Rect(0, rowRect.yMax - 1, totalW, 1), ROW_LINE);
        }

        // Hover updates come from the MouseMove repaint at the top of OnGUI
        GUI.EndScrollView();
    }

    void ReSort()
    {
        if (_header == null || _header.sortedColumnIndex < 0) return;
        int col = _header.sortedColumnIndex;
        bool asc = _header.IsSortedAscending(col);
        Func<Row, string> key = r => Cell(r, (Col)col) ?? "";
        _view = (asc ? _view.OrderBy(key) : _view.OrderByDescending(key)).ToList();
    }

    void Ping(Row r)
    {
        if (string.IsNullOrEmpty(r.guid)) return;
        var path = AssetDatabase.GUIDToAssetPath(r.guid);
        if (string.IsNullOrEmpty(path)) return;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(a, out _, out long lid) && lid == r.fileId)
            { EditorGUIUtility.PingObject(a); Selection.activeObject = a; return; }
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(path))
            if (a != null && a.name == r.assetName)
            { EditorGUIUtility.PingObject(a); Selection.activeObject = a; return; }
        Debug.LogWarning($"[DescriptorIndex] '{r.assetName}' not in project (vanilla may be unloaded).");
    }

    // ── Scans ─────────────────────────────────────────────────────────────────
    Dictionary<string, List<string>> _liveModDefs = new();

    bool UnderVanilla(string assetPath) =>
        !string.IsNullOrEmpty(_vanillaFolder) &&
        (assetPath == _vanillaFolder || assetPath.StartsWith(_vanillaFolder + "/", StringComparison.Ordinal));

    void RebuildVanillaCache()
    {
        if (!s_fieldsResolved && !TryResolveFields()) { Debug.LogError("[DescriptorIndex] Field resolution failed."); return; }

        var rows = new List<Row>();
        var defMap = new Dictionary<string, List<string>>();
        try
        {
            var guids = AssetDatabase.FindAssets("t:ScriptableObject");
            int n = guids.Length, i = 0;
            foreach (var guid in guids)
            {
                if (++i % 64 == 0) EditorUtility.DisplayProgressBar("Vanilla Cache", "Scanning…", i / (float)n);
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!UnderVanilla(path)) continue;                 // vanilla only
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                if (all == null) continue;
                foreach (var obj in all)
                {
                    if (obj == null) continue;
                    if (s_descriptorType.IsInstanceOfType(obj)) rows.AddRange(ExtractRows(obj, "Vanilla"));
                    else HarvestReferences(obj, defMap);
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        _vanillaRows = rows;
        _vanillaDefs = defMap;
        _builtUtc = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        SaveCache();
        RebuildDerived();
        Debug.Log($"[DescriptorIndex] Vanilla cache: {rows.Count} rows.");
    }

    void ScanModScope()
    {
        if (!s_fieldsResolved && !TryResolveFields()) return;

        var rows = new List<Row>();
        var defMap = new Dictionary<string, List<string>>();
        foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (UnderVanilla(path)) continue;                      // everything except vanilla
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            if (all == null) continue;
            foreach (var obj in all)
            {
                if (obj == null) continue;
                if (s_descriptorType.IsInstanceOfType(obj)) rows.AddRange(ExtractRows(obj, "Mod"));
                else HarvestReferences(obj, defMap);
            }
        }
        _modRows = rows;
        _liveModDefs = defMap;
    }

    IEnumerable<Row> ExtractRows(UnityEngine.Object obj, string scope)
    {
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string g, out long fid);
        string startShort = ParseClassName(f_startingType.GetValue(obj) as string ?? "");

        if (f_effects.GetValue(obj) is not Array effects) yield break;
        foreach (var effect in effects)
        {
            if (effect == null) continue;
            bool applyOnSource = (bool)f_applyOnSource.GetValue(effect);

            string pathStr = "", targetClass = startShort;
            var pathObj = f_path.GetValue(effect);
            if (pathObj != null && f_propertyToFollow.GetValue(pathObj) is string[] ptf)
            {
                var ne = ptf.Where(z => !string.IsNullOrEmpty(z)).ToArray();
                pathStr = string.Join(".", ne);
                if (!applyOnSource && ne.Length > 0) targetClass = ne[^1];
            }
            if (applyOnSource) targetClass = startShort;

            if (f_propertyEffects.GetValue(effect) is not Array peArr) continue;
            foreach (var pe in peArr)
            {
                if (pe == null) continue;
                yield return new Row
                {
                    assetName = obj.name, scope = scope, startingType = startShort,
                    path = pathStr, targetClass = targetClass,
                    property = f_targetProperty.GetValue(pe) as string ?? "",
                    op = OpName(f_toTargetOp.GetValue(pe)),
                    formula = BuildFormula(pe),
                    guid = g, fileId = fid,
                };
            }
        }
    }

    // ── Cache IO ──────────────────────────────────────────────────────────────
    void SaveCache()
    {
        try
        {
            var c = new Cache { builtUtc = _builtUtc, vanillaRows = _vanillaRows,
                vanillaDefs = _vanillaDefs.Select(kv => new MapEntry { name = kv.Key, owners = kv.Value }).ToList() };
            File.WriteAllText(CachePath, JsonUtility.ToJson(c));
        }
        catch (Exception e) { Debug.LogWarning($"[DescriptorIndex] Cache save failed: {e.Message}"); }
    }

    void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return;
            var c = JsonUtility.FromJson<Cache>(File.ReadAllText(CachePath));
            if (c == null) return;
            _vanillaRows = c.vanillaRows ?? new();
            _builtUtc = c.builtUtc ?? "";
            _vanillaDefs = new();
            foreach (var e in c.vanillaDefs ?? new()) _vanillaDefs[e.name] = e.owners ?? new();
        }
        catch (Exception e) { Debug.LogWarning($"[DescriptorIndex] Cache load failed: {e.Message}"); }
    }

    void ClearCache()
    {
        _vanillaRows.Clear(); _vanillaDefs.Clear(); _builtUtc = "";
        try { if (File.Exists(CachePath)) File.Delete(CachePath); } catch { }
        RebuildDerived();
    }

    // ── Reference harvest ─────────────────────────────────────────────────────
    void HarvestReferences(UnityEngine.Object owner, Dictionary<string, List<string>> map)
    {
        foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.Name.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) < 0) continue;
            object val; try { val = field.GetValue(owner); } catch { continue; }
            if (val is not IList list || list.Count == 0) continue;
            var first = list[0]; if (first == null) continue;
            var enf = first.GetType().GetField("serializableElementName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (enf == null) continue;
            foreach (var item in list)
            {
                if (item == null) continue;
                if (enf.GetValue(item) is not string refName || string.IsNullOrEmpty(refName)) continue;
                if (!map.TryGetValue(refName, out var owners)) map[refName] = owners = new List<string>();
                if (!owners.Contains(owner.name)) owners.Add(owner.name);
            }
        }
    }

    // ── RPN → infix ───────────────────────────────────────────────────────────
    string BuildFormula(object pe)
    {
        var rpn = f_rpnStack?.GetValue(pe) as Array;
        var constants = f_constantStack?.GetValue(pe) as Array;
        var names = f_propertyLocalNames.GetValue(pe) as string[];
        if (rpn == null || rpn.Length == 0)
            return (constants != null && constants.Length > 0)
                ? string.Join(", ", constants.Cast<object>().Select(FormatFixed)) : "0";

        var stack = new Stack<string>(); int propIdx = 0, constIdx = 0;
        foreach (var opObj in rpn)
        {
            int op = Convert.ToInt32(opObj);
            if (op == OP_GetConst)
                stack.Push(constants != null && constIdx < constants.Length ? FormatFixed(constants.GetValue(constIdx++)) : $"c{constIdx++}");
            else if (op == OP_GetTarget)   stack.Push("Target." + NextName(names, ref propIdx));
            else if (op == OP_GetSource)   stack.Push("Source." + NextName(names, ref propIdx));
            else if (op == OP_GetWorld)    stack.Push("World."  + NextName(names, ref propIdx));
            else if (op == OP_GetVariable) stack.Push("var:"    + NextName(names, ref propIdx));
            else
            {
                if (stack.Count < 2) { stack.Push($"?{OpName(opObj)}"); continue; }
                string b = stack.Pop(), a = stack.Pop();
                stack.Push("(" + a + " " + BinSym(op) + " " + b + ")");
            }
        }
        string result = stack.Count > 0 ? stack.Peek() : "?";
        if (result.Length > 1 && result[0] == '(' && result[^1] == ')') result = result.Substring(1, result.Length - 2);
        return result;
    }

    static string NextName(string[] names, ref int idx) => names != null && idx < names.Length ? names[idx++] : $"p{idx++}";

    string BinSym(int op)
    {
        if (op == OP_Add) return "+"; if (op == OP_Sub) return "-";
        if (op == OP_Mult) return "*"; if (op == OP_Div) return "/";
        if (op == OP_Pow) return "^"; if (op == OP_Percent) return "percent";
        if (op == OP_Max) return "max"; if (op == OP_Min) return "min";
        return OpName(op);
    }

    static string OpName(object op) =>
        op == null ? "?" : (s_operationEnum != null ? (Enum.GetName(s_operationEnum, op) ?? op.ToString()) : op.ToString());

    static string FormatFixed(object fp)
    {
        if (f_rawValue == null || fp == null) return "0";
        long raw = Convert.ToInt64(f_rawValue.GetValue(fp));
        double v = (double)raw / s_oneRaw;
        return v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.####");
    }

    // ── Reflection setup ──────────────────────────────────────────────────────
    static bool TryResolveFields()
    {
        s_descriptorType = FindType("Amplitude.Framework.Simulation.Description.Descriptor");
        if (s_descriptorType == null) return false;
        f_startingType = GetField(s_descriptorType, "startingType");
        f_effects      = GetField(s_descriptorType, "Effects");

        var effectType = FindType("Amplitude.Framework.Simulation.Description.Effect");
        var pathType   = FindType("Amplitude.Framework.Simulation.Description.Path");
        var peType     = FindType("Amplitude.Framework.Simulation.Description.PropertyEffect");
        if (effectType == null || pathType == null || peType == null) return false;

        f_applyOnSource    = GetField(effectType, "ApplyEffectOnSource");
        f_path             = GetField(effectType, "Path");
        f_propertyEffects  = GetField(effectType, "PropertyEffects");
        f_propertyToFollow = GetField(pathType, "PropertyToFollow");
        f_targetProperty     = GetField(peType, "TargetProperty");
        f_toTargetOp         = GetField(peType, "ToTargetOperation");
        f_rpnStack           = GetField(peType, "RpnOperationStack");
        f_constantStack      = GetField(peType, "ConstantStack");
        f_propertyLocalNames = GetField(peType, "PropertyLocalName");

        s_operationEnum = f_rpnStack?.FieldType.GetElementType();
        var fixedPointType = f_constantStack?.FieldType.GetElementType();
        if (fixedPointType != null)
        {
            f_rawValue = GetField(fixedPointType, "RawValue");
            var oneRaw = fixedPointType.GetField("OneRaw", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (oneRaw != null) { try { s_oneRaw = Convert.ToInt64(oneRaw.GetValue(null)); } catch { s_oneRaw = 1000; } }
        }
        if (s_operationEnum != null && s_operationEnum.IsEnum)
        {
            OP_Add = OpVal("Add"); OP_Sub = OpVal("Sub"); OP_Mult = OpVal("Mult"); OP_Div = OpVal("Div");
            OP_Percent = OpVal("Percent"); OP_Pow = OpVal("Pow"); OP_Max = OpVal("Max"); OP_Min = OpVal("Min");
            OP_GetTarget = OpVal("GetPropertyFromTarget"); OP_GetSource = OpVal("GetPropertyFromSource");
            OP_GetConst = OpVal("GetFromConstantValue"); OP_GetVariable = OpVal("GetFromVariable");
            OP_GetWorld = OpVal("GetPropertyFromWorld");
        }
        bool ok = f_startingType != null && f_effects != null && f_applyOnSource != null
               && f_path != null && f_propertyToFollow != null && f_propertyEffects != null
               && f_targetProperty != null && f_toTargetOp != null && f_rpnStack != null
               && f_constantStack != null && f_propertyLocalNames != null
               && s_operationEnum != null && f_rawValue != null;
        if (ok) s_fieldsResolved = true;
        return ok;
    }

    static int OpVal(string name) { try { return Convert.ToInt32(Enum.Parse(s_operationEnum, name)); } catch { return -999; } }

    static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { var t = asm.GetType(fullName); if (t != null) return t; }
        Debug.LogWarning($"[DescriptorIndex] Type not found: {fullName}");
        return null;
    }

    static FieldInfo GetField(Type t, string name)
    {
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null) Debug.LogWarning($"[DescriptorIndex] Field not found: {t.Name}.{name}");
        return f;
    }

    static string ParseClassName(string aqn)
    {
        if (string.IsNullOrEmpty(aqn)) return "";
        var comma = aqn.IndexOf(',');
        var typePart = comma >= 0 ? aqn.Substring(0, comma).Trim() : aqn.Trim();
        var dot = typePart.LastIndexOf('.');
        return dot >= 0 ? typePart.Substring(dot + 1) : typePart;
    }
}