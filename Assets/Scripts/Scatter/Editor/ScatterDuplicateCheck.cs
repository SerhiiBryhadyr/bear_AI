using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
// Пошук дублікатів: два однакові об'єкти на тому самому місці.
// Зазвичай з'являються після Cmd+D (Ctrl+C / Ctrl+V) на засадженій зоні Scatter чи на ручному об'єкті:
// копії ростуть одна в одній, їх не видно, але навантаження подвоюється.
//
// Дублікат = той самий префаб (або той самий меш, якщо це не префаб) + центр ближче за SamePlace
//            + той самий поворот і розмір. Перевіряються:
//  - об'єкти Scatter (у Generated зон) і всі ручні об'єкти сцени (екземпляри префабів, окремі меші);
//  - зони-близнюки Scatter: однаковий пресет, seed і контур (типово після Cmd+D на зоні).
// Вимкнені об'єкти не перевіряються (вони не малюються). Частини всередині префаба — разом із префабом.
//
// Запуск: Tools → Scatter → Перевірити дублікати — звіт у Console, зайві копії виділяються в Hierarchy.
// Автоматично при збереженні сцени (Cmd+S) — пише, лише якщо щось знайдено (вимкнути — у тому ж меню).
// Нічого не видаляє: видаляєш сам (Delete) після перегляду.
// ============================================================
[InitializeOnLoad]
public static class ScatterDuplicateCheck
{
    const float SamePlace = 0.05f;  // центри ближче — «на тому самому місці» (м)
    const float SameAngle = 1f;     // поворот відрізняється менше (градуси)
    const float SameScale = 0.01f;  // розмір відрізняється менше (1%)
    const int MaxLines = 15;        // скільки місць показувати у звіті

    const string OnSaveKey = "ScatterDuplicateCheck.OnSave";
    const string OnSaveMenu = "Tools/Scatter/Перевіряти дублікати при збереженні";

    // Один об'єкт для перевірки
    class Item
    {
        public GameObject go;
        public string key;    // що це: префаб чи меш (однакові key — однакові об'єкти)
        public string name;   // назва префаба / меша для звіту
        public int index;     // порядок у Hierarchy: лишаємо перший, решта — зайві копії
    }

    // Результат перевірки
    class Report
    {
        public int checkedCount;
        public readonly List<GameObject> extras = new List<GameObject>();
        public readonly Dictionary<string, int> places = new Dictionary<string, int>();
        public readonly List<string> twins = new List<string>();

        public void AddDuplicate(Item keep, Item extra)
        {
            extras.Add(extra.go);
            string a = Owner(keep.go), b = Owner(extra.go);
            string place = a == b ? $"{keep.name} — {a}" : $"{keep.name} — {a} ↔ {b}";
            places[place] = places.TryGetValue(place, out int n) ? n + 1 : 1;
        }

        public void Log(string when, bool fromMenu)
        {
            if (extras.Count == 0 && twins.Count == 0)
            {
                if (fromMenu) Debug.Log($"[Scatter] Дублікатів немає ({when}; перевірено {checkedCount} об'єктів)");
                return;
            }
            var sb = new StringBuilder(extras.Count > 0
                ? $"[Scatter] ⚠ Дублікати ({when}): {extras.Count} зайвих копій серед {checkedCount} об'єктів"
                : $"[Scatter] ⚠ Зайвих копій немає ({when}; перевірено {checkedCount} об'єктів), але є зони-близнюки: {twins.Count}");
            var list = new List<KeyValuePair<string, int>>(places);
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            for (int i = 0; i < list.Count && i < MaxLines; i++) sb.Append($"\n• {list[i].Key}: {list[i].Value}");
            if (list.Count > MaxLines) sb.Append($"\n• … ще {list.Count - MaxLines} місць");
            foreach (string t in twins) sb.Append($"\n• зони-близнюки {t}: однаковий пресет, seed і контур — натисни Clear на одній або видали її");
            if (extras.Count > 0)
                sb.Append(fromMenu ? "\nЗайві копії виділено в Hierarchy — переглянь і натисни Delete."
                                   : "\nДеталі й виділення — Tools → Scatter → Перевірити дублікати.");
            Debug.LogWarning(sb.ToString());
        }
    }

    static ScatterDuplicateCheck()
    {
        EditorSceneManager.sceneSaved -= OnSceneSaved;
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    // ===== Меню: повна перевірка всіх відкритих сцен =====
    [MenuItem("Tools/Scatter/Перевірити дублікати")]
    static void CheckFromMenu()
    {
        var report = new Report();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) CheckScene(s, report);
        }
        CheckZoneTwins(null, report);
        report.Log("перевірка з меню", true);
        if (report.extras.Count > 0)
        {
            Selection.objects = report.extras.ToArray();
            EditorGUIUtility.PingObject(report.extras[0]);
        }
    }

    // ===== Перевірка при збереженні (Cmd+S) — вмикається / вимикається в меню =====
    static bool OnSaveEnabled => EditorPrefs.GetBool(OnSaveKey, true);

    [MenuItem(OnSaveMenu)]
    static void ToggleOnSave() => EditorPrefs.SetBool(OnSaveKey, !OnSaveEnabled);

    [MenuItem(OnSaveMenu, true)]
    static bool ToggleOnSaveValidate()
    {
        Menu.SetChecked(OnSaveMenu, OnSaveEnabled);
        return true;
    }

    static void OnSceneSaved(Scene scene)
    {
        if (!OnSaveEnabled) return;
        var report = new Report();
        CheckScene(scene, report);
        CheckZoneTwins(scene, report);
        report.Log($"збереження сцени {scene.name}", false);
    }

    // ===== Дублікати об'єктів в одній сцені =====
    static void CheckScene(Scene scene, Report report)
    {
        var items = new List<Item>();
        foreach (GameObject root in scene.GetRootGameObjects()) Collect(root.transform, items);
        report.checkedCount += items.Count;

        // Розкладаємо по «клітинках» простору розміром SamePlace: дублікат шукаємо лише в сусідніх клітинках
        var cells = new Dictionary<(string, int, int, int), List<Item>>();
        foreach (Item it in items)
        {
            var c = Cell(it);
            if (!cells.TryGetValue(c, out List<Item> list)) cells[c] = list = new List<Item>();
            list.Add(it);
        }

        var extra = new HashSet<GameObject>();
        foreach (Item it in items)
        {
            if (extra.Contains(it.go)) continue;  // це вже зайва копія — від неї не шукаємо
            var (key, cx, cy, cz) = Cell(it);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!cells.TryGetValue((key, cx + dx, cy + dy, cz + dz), out List<Item> list)) continue;
                foreach (Item other in list)
                {
                    if (other.index <= it.index || extra.Contains(other.go)) continue; // лишаємо перший за порядком
                    if (!SameTransform(it.go.transform, other.go.transform)) continue;
                    extra.Add(other.go);
                    report.AddDuplicate(it, other);
                }
            }
        }
    }

    // Обхід Hierarchy: екземпляр префаба — один об'єкт (усередину не заходимо); інакше — меш, і далі діти
    static void Collect(Transform t, List<Item> items)
    {
        GameObject go = t.gameObject;
        if (!go.activeInHierarchy || (go.hideFlags & HideFlags.HideInHierarchy) != 0) return;

        if (PrefabUtility.IsOutermostPrefabInstanceRoot(go))
        {
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source != null)
            {
                items.Add(new Item { go = go, key = "p" + source.GetInstanceID(), name = source.name, index = items.Count });
                return;
            }
        }
        if (go.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            items.Add(new Item { go = go, key = "m" + mf.sharedMesh.GetInstanceID(), name = mf.sharedMesh.name, index = items.Count });
        foreach (Transform child in t) Collect(child, items);
    }

    static (string, int, int, int) Cell(Item it)
    {
        Vector3 p = it.go.transform.position;
        return (it.key, Mathf.FloorToInt(p.x / SamePlace), Mathf.FloorToInt(p.y / SamePlace), Mathf.FloorToInt(p.z / SamePlace));
    }

    static bool SameTransform(Transform a, Transform b)
    {
        if (Vector3.Distance(a.position, b.position) > SamePlace) return false;
        if (Quaternion.Angle(a.rotation, b.rotation) > SameAngle) return false;
        float sa = a.lossyScale.magnitude, sb = b.lossyScale.magnitude;
        return Mathf.Abs(sa - sb) <= SameScale * Mathf.Max(sa, sb);
    }

    // Де стоїть об'єкт: зона Scatter або батьківський об'єкт
    static string Owner(GameObject go)
    {
        ScatterZone zone = go.GetComponentInParent<ScatterZone>(true);
        if (zone != null) return $"зона {zone.name}";
        Transform parent = go.transform.parent;
        return parent != null ? parent.name : "корінь сцени";
    }

    // ===== Зони-близнюки: однаковий пресет об'єктів, seed і контур =====
    // Зони трави (без пресета об'єктів) не рахуються: клони з тим самим контуром — звичайний спосіб садити шари трави.
    static void CheckZoneTwins(Scene? scene, Report report)
    {
        var zones = new List<ScatterZone>();
        foreach (ScatterZone z in Object.FindObjectsOfType<ScatterZone>())
            if (z.preset != null && z.points.Count >= 3 && (scene == null || z.gameObject.scene == scene.Value)) zones.Add(z);

        for (int i = 0; i < zones.Count; i++)
        for (int j = i + 1; j < zones.Count; j++)
            if (zones[i].preset == zones[j].preset && zones[i].seed == zones[j].seed && SameContour(zones[i], zones[j]))
                report.twins.Add($"{zones[i].name} ↔ {zones[j].name}");
    }

    static bool SameContour(ScatterZone a, ScatterZone b)
    {
        if (a.points.Count != b.points.Count) return false;
        for (int i = 0; i < a.points.Count; i++)
            if (Vector3.Distance(a.GetWorldPoint(i), b.GetWorldPoint(i)) > SamePlace) return false;
        return true;
    }
}
