using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
// Перевірка колайдерів: Tools → Colliders → Перевірка колайдерів.
// Шукає в сцені об'єкти з мешем без жодного колайдера і групує однакові копії
// (усі бочки разом). Кнопки групи виділяють копії — колайдер ти додаєш сам
// через Add Component, одразу на всі виділені. Окремо — типи дерев Paint Trees.
// Сцену НЕ змінює: лише шукає, показує й виділяє.
// ============================================================
public class ColliderChecker : EditorWindow
{
    // ===== Налаштування пошуку (поля у вікні) =====
    float minObjectSize = 0.5f;               // менші об'єкти (камінці, відра) не показуються, м
    Transform center;                   // центр зони перевірки; порожньо — вся сцена
    float radius = 150f;                // радіус від центру по горизонталі, м
    bool skipScatter = true;            // не показувати те, що поставив Scatter (кущі й камінці без колайдера навмисно)
    bool activeSceneOnly = true;        // лише активна сцена (жирна в Hierarchy)
    string skipNames = "Grass, Water";  // частини назв, які пропускати, через кому
    bool drawInScene = true;            // червоні рамки навколо знайденого в Scene view

    // ===== Результати сканування =====
    class Group
    {
        public string name;                                  // назва префаба або об'єкта без " (12)"
        public List<GameObject> roots = new List<GameObject>(); // усі копії без колайдера
        public List<Bounds> bounds = new List<Bounds>();       // їхні рамки для Scene view
    }

    class TreeInfo
    {
        public GameObject prefab;   // префаб типу дерева на terrain
        public bool hasCollider;
        public int count;           // скільки таких дерев на terrain
    }

    List<Group> groups = new List<Group>();
    List<TreeInfo> trees = new List<TreeInfo>();
    int checkedCount, missingCount;
    bool scanned;
    Vector2 scroll;

    static readonly Regex CopySuffix = new Regex(@"\s*\(\d+\)$"); // " (12)", який Unity додає при дублюванні

    [MenuItem("Tools/Colliders/Перевірка колайдерів")]
    static void Open() => GetWindow<ColliderChecker>("Колайдери");

    void OnEnable() => SceneView.duringSceneGui += DrawInScene;
    void OnDisable() => SceneView.duringSceneGui -= DrawInScene;

    // ===== Вікно: налаштування, кнопка, список результатів =====
    void OnGUI()
    {
        EditorGUILayout.LabelField("Що перевіряти", EditorStyles.boldLabel);
        minObjectSize = EditorGUILayout.FloatField(new GUIContent("Мін. розмір, м", "Об'єкти, в яких найбільша сторона менша, не показуються"), minObjectSize);

        EditorGUI.BeginChangeCheck();
        center = (Transform)EditorGUILayout.ObjectField(new GUIContent("Центр зони", "Порожньо — вся сцена"), center, typeof(Transform), true);
        using (new EditorGUI.DisabledScope(center == null))
            radius = EditorGUILayout.FloatField("Радіус, м", radius);
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll(); // перемалювати жовте коло зони

        skipScatter = EditorGUILayout.Toggle("Пропустити Scatter", skipScatter);
        activeSceneOnly = EditorGUILayout.Toggle("Лише активна сцена", activeSceneOnly);
        skipNames = EditorGUILayout.TextField(new GUIContent("Пропустити назви", "Частини назв через кому"), skipNames);

        bool draw = EditorGUILayout.Toggle("Рамки в Scene", drawInScene);
        if (draw != drawInScene) { drawInScene = draw; SceneView.RepaintAll(); }

        if (GUILayout.Button("Сканувати", GUILayout.Height(28))) Scan();
        if (!scanned) return;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox($"Перевірено об'єктів: {checkedCount}. Без колайдера: {missingCount} у {groups.Count} групах.", MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        // ----- Об'єкти без колайдера, найчисленніші групи зверху -----
        EditorGUILayout.LabelField("Об'єкти без колайдера", EditorStyles.boldLabel);
        foreach (Group g in groups)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{g.name}  × {g.roots.Count}");
            if (GUILayout.Button(new GUIContent("Показати", "Виділити й наблизити першу копію"), GUILayout.Width(75))) ShowFirst(g);
            if (GUILayout.Button(new GUIContent("Частини", "Виділити всі частини з мешем — далі Add Component → Box Collider"), GUILayout.Width(70))) SelectParts(g);
            if (GUILayout.Button(new GUIContent("Об'єкти", "Виділити корені всіх копій"), GUILayout.Width(70))) SelectRoots(g);
            EditorGUILayout.EndHorizontal();
        }

        // ----- Дерева Paint Trees: колайдер ставиться один раз у префаб -----
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Дерева terrain (Paint Trees)", EditorStyles.boldLabel);
        foreach (TreeInfo t in trees)
        {
            EditorGUILayout.BeginHorizontal();
            string state = t.hasCollider ? "є колайдер" : "НЕМАЄ колайдера";
            EditorGUILayout.LabelField($"{t.prefab.name}  × {t.count}   — {state}");
            if (GUILayout.Button("Префаб", GUILayout.Width(70)))
            {
                Selection.activeObject = t.prefab;
                EditorGUIUtility.PingObject(t.prefab);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    // ===== Сканування: знайти об'єкти без колайдера і типи дерев =====
    void Scan()
    {
        groups.Clear();
        trees.Clear();
        checkedCount = 0;
        missingCount = 0;

        string[] skips = skipNames.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        Scene active = SceneManager.GetActiveScene();
        var seenRoots = new HashSet<GameObject>();
        var byKey = new Dictionary<string, Group>();

        // ----- 1. Кожен меш → його "об'єкт" (копія, яку ти ставив на карту) → є колайдер чи ні -----
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            GameObject root = PlacedRoot(r.gameObject);
            if (!seenRoots.Add(root)) continue; // цей об'єкт уже перевірено через іншу його частину

            if (activeSceneOnly && root.scene != active) continue;
            if (skipScatter && root.GetComponentInParent<ScatterZone>() != null) continue;
            if (skips.Any(s => root.name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)) continue;

            Bounds b = RootBounds(root);
            if (Mathf.Max(b.size.x, b.size.y, b.size.z) < minObjectSize) continue;
            if (center != null && radius > 0f)
            {
                Vector2 d = new Vector2(b.center.x - center.position.x, b.center.z - center.position.z);
                if (d.magnitude > radius) continue;
            }

            checkedCount++;
            if (HasSolidCollider(root)) continue;

            missingCount++;
            string key = GroupKey(root, out string display);
            if (!byKey.TryGetValue(key, out Group g))
            {
                g = new Group { name = display };
                byKey[key] = g;
            }
            g.roots.Add(root);
            g.bounds.Add(b);
        }
        groups = byKey.Values.OrderByDescending(g => g.roots.Count).ThenBy(g => g.name).ToList();

        // ----- 2. Дерева terrain: рахуємо дерева кожного типу і дивимось, чи є колайдер у префабі -----
        var byPrefab = new Dictionary<GameObject, TreeInfo>();
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (activeSceneOnly && terrain.gameObject.scene != active) continue;

            TerrainData data = terrain.terrainData;
            TreePrototype[] protos = data.treePrototypes;
            int[] counts = new int[protos.Length];
            foreach (TreeInstance t in data.treeInstances)
                if (t.prototypeIndex >= 0 && t.prototypeIndex < counts.Length) counts[t.prototypeIndex]++;

            for (int i = 0; i < protos.Length; i++)
            {
                GameObject prefab = protos[i].prefab;
                if (prefab == null) continue;
                if (!byPrefab.TryGetValue(prefab, out TreeInfo info))
                {
                    info = new TreeInfo { prefab = prefab, hasCollider = HasSolidCollider(prefab) };
                    byPrefab[prefab] = info;
                }
                info.count += counts[i];
            }
        }
        trees = byPrefab.Values.OrderBy(t => t.hasCollider).ThenByDescending(t => t.count).ToList(); // без колайдера — зверху

        scanned = true;
        int treesMissing = trees.Count(t => !t.hasCollider);
        Debug.Log($"[Colliders] Перевірено {checkedCount}, без колайдера {missingCount} у {groups.Count} групах; типів дерев без колайдера: {treesMissing} з {trees.Count}");
        SceneView.RepaintAll();
    }

    // ===== Допоміжне: що вважається одним об'єктом, чи є колайдер, як групувати =====

    // Копія, яку ставили на карту: для префаба (і моделі FBX) — його зовнішній корінь, для звичайного об'єкта — він сам.
    static GameObject PlacedRoot(GameObject go)
    {
        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        return prefabRoot != null ? prefabRoot : go;
    }

    // Колайдер, що реально блокує (не trigger), — на самому об'єкті, на будь-якій його частині або на батьківському.
    static bool HasSolidCollider(GameObject go)
    {
        if (go.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger)) return true;
        Collider parent = go.GetComponentInParent<Collider>();
        return parent != null && !parent.isTrigger;
    }

    // Копії одного префаба — одна група; звичайні об'єкти — за назвою без " (12)".
    static string GroupKey(GameObject root, out string display)
    {
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
        if (!string.IsNullOrEmpty(path))
        {
            display = System.IO.Path.GetFileNameWithoutExtension(path);
            return path;
        }
        display = CopySuffix.Replace(root.name, "");
        return "name:" + display;
    }

    // Рамка, що охоплює всі видимі частини об'єкта.
    static Bounds RootBounds(GameObject root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // ===== Кнопки групи: виділення =====
    void ShowFirst(Group g)
    {
        GameObject first = g.roots.FirstOrDefault(x => x != null);
        if (first == null) return;
        Selection.activeGameObject = first;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
    }

    // Частини з мешем: Box Collider, доданий на них, сам підлаштовується під розмір кожної частини.
    void SelectParts(Group g)
    {
        var parts = new List<Object>();
        foreach (GameObject root in g.roots)
        {
            if (root == null) continue;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
                if (mf.GetComponent<MeshRenderer>() != null) parts.Add(mf.gameObject);
        }
        Selection.objects = parts.ToArray();
        Debug.Log($"[Colliders] {g.name}: виділено частин з мешем — {parts.Count}");
    }

    void SelectRoots(Group g) => Selection.objects = g.roots.Where(x => x != null).ToArray();

    // ===== Scene view: жовте коло зони і червоні рамки навколо знайденого =====
    void DrawInScene(SceneView view)
    {
        if (center != null && radius > 0f)
        {
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(center.position, Vector3.up, radius);
        }

        if (!drawInScene || !scanned) return;
        Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        int drawn = 0;
        foreach (Group g in groups)
            foreach (Bounds b in g.bounds)
            {
                Handles.DrawWireCube(b.center, b.size);
                if (++drawn >= 3000) return; // забагато рамок гальмує Scene view
            }
    }
}
