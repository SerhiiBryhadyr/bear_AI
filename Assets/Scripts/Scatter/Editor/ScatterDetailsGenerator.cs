using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// ============================================================
// Алгоритм трави для ScatterDetails. Кнопки — в Inspector (ScatterDetailsEditor).
// Пише в сітку деталей terrain — те саме, що пензель Paint Details:
// кожна клітинка (~1–2 м) зберігає, наскільки її заповнює кожен вид трави.
//
// Scatter садить ВЛАСНІ копії префабів (SC_<назва>, папка CopiesFolder), а не оригінали:
//  - оригінали (трава пензля, стара трава) Scatter ніколи не змінює;
//  - у копії фіксована стеля густоти (Detail Density = ScatterDetailPreset.MaxDensity),
//    а густота зони — це частка заповнення клітинок лише в її контурі, тож зони не впливають одна на одну.
// Копії — звичайні види трави в terrain: їх видно в Paint Details, пензлем їх можна стерти чи домалювати.
//
// Наявний Scatter (ScatterZone, ScatterGenerator) не змінюється — з нього лише читаються контур і об'єкти.
// Усе в terrain — через Undo: Ctrl+Z (Cmd+Z) скасовує. Створення копій-префабів не скасовується (це файли).
// Після роботи — Ctrl+S, щоб зберегти terrain.
// ============================================================
public static class ScatterDetailsGenerator
{
    // Необов'язкові налаштування трави, яких може не бути в старих версіях Unity.
    // Читаються й пишуться за назвою, тож код компілюється в будь-якій версії.
    static readonly string[] OptionalProps =
        { "alignToGround", "positionJitter", "density", "useDensityScaling", "useInstancing", "holeEdgePadding", "noiseSeed", "targetCoverage" };

    // ===== Копії для Scatter =====
    public const string CopiesFolder = "Assets/Scripts/Scatter/Grass_Presset/Scatter_Copies";
    public const string CopyPrefix = "SC_";
    const string CopyMark = "ScatterCopyOf:";   // у userData копії — GUID оригіналу

    // Позиції «купками» (квіти): усередині купки їх у стільки разів більше, а поза нею — немає.
    // Купки займають приблизно третину площі, тож загальна кількість лишається приблизно та сама.
    const float ClumpBoost = 3f;

    // Одна рослина пресету на час засаджування
    class Plant
    {
        public ScatterDetailItem item;
        public GameObject copy;      // SC_ копія, яку садимо
        public string note = "";     // що зроблено з копією (створено, з LOD0…)
        public int proto;            // індекс копії в terrain
        public float ceiling = 1f;   // Detail Density копії
        public string info = "";     // налаштування копії для звіту
        public int cells;            // скільки клітинок отримала
        public long sum;             // сума значень — для середнього заповнення
    }

    // ===== Засадити траву в зоні (замінює попередню траву цього пресета в зоні) =====
    // Як пензель Paint Details: у кожній клітинці — усі рослини разом, у пропорції ваг.
    // Режим Coverage: заповнення клітинки = Density пресету / стеля копії (Detail Density копії).
    public static void Generate(ScatterDetails details)
    {
        if (!Validate(details, out ScatterZone zone, out ScatterDetailPreset preset)) return;

        // ===== Рослини і їхні копії SC_ =====
        var skipped = new List<string>();
        var plants = new List<Plant>();
        Dictionary<string, GameObject> copies = CopyIndex();
        foreach (ScatterDetailItem item in preset.items)
        {
            string problem = ItemProblem(item);
            if (problem != null) { skipped.Add(problem); continue; }

            GameObject copy = GetOrCreateCopy(item.prefab, copies, out string note, out string error);
            if (copy == null) { skipped.Add($"{item.prefab.name}: не вдалося зробити копію SC_ — {error}"); continue; }
            plants.Add(new Plant { item = item, copy = copy, note = note });
        }
        if (plants.Count == 0)
        {
            details.lastReport = "Нічого не засаджено: у пресеті немає придатних позицій." + Lines(skipped);
            Debug.LogWarning("[Scatter] Трава: у пресеті немає придатних позицій" + Lines(skipped));
            return;
        }

        Undo.SetCurrentGroupName("Scatter Grass Generate");
        Vector2[] poly = PolygonXZ(zone);
        Bounds area = zone.GetBoundsXZ();
        int n = plants.Count;
        float[] share = new float[n];    // частка кожної рослини в поточній клітинці
        float[] full = new float[n];     // скільки писати в клітинку, якби рослина була одна
        int terrains = 0, cells = 0, planted = 0, added = 0;
        bool coverage = true;
        List<GameObject> presetPrefabs = AllPrefabs(preset, copies);

        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (!CellRect(t, area, out RectInt rect)) continue;
            TerrainData d = t.terrainData;
            Undo.RegisterCompleteObjectUndo(d, "Scatter Grass Generate");

            coverage = IsCoverageMode(d);
            added += EnsurePrototypes(d, plants);

            // Coverage — частка від стелі копії (густота лише в цій зоні);
            // Instance Count — кількість травинок: густота × Max Per Cell.
            for (int i = 0; i < n; i++)
                full[i] = coverage
                    ? 255f * Mathf.Min(1f, preset.density / Mathf.Max(0.0001f, plants[i].ceiling))
                    : preset.density * preset.maxPerCell;

            // Шари, які зона перезаписує: копії і оригінали префабів пресету
            // (оригінали — щоб прибрати траву, яку посадила попередня версія Scatter)
            var layers = new Dictionary<int, int[,]>();
            foreach (int p in FindPrototypes(d, presetPrefabs))
                layers[p] = d.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, p);
            foreach (Plant pl in plants)
                if (!layers.ContainsKey(pl.proto)) layers[pl.proto] = d.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, pl.proto);

            PathMap path = preset.avoidPathLayer ? PathMap.Read(d, preset.pathLayer, rect) : null;
            Vector3 origin = t.transform.position;
            Vector3 size = d.size;

            for (int z = 0; z < rect.height; z++)
            for (int x = 0; x < rect.width; x++)
            {
                // Центр клітинки: u, v — 0..1 по terrain; w — у світі
                float u = (rect.x + x + 0.5f) / d.detailWidth;
                float v = (rect.y + z + 0.5f) / d.detailHeight;
                Vector3 w = new Vector3(origin.x + u * size.x, 0f, origin.z + v * size.z);
                if (!InsideXZ(poly, w)) continue;
                cells++;

                // Зона перезаписує свою траву: спершу клітинку очищаємо
                foreach (int[,] layer in layers.Values) layer[z, x] = 0;

                if (path != null && path.Weight(u, v) > preset.pathThreshold) continue;

                // Частки рослин у клітинці: вага, нахил, купки
                float slope = Vector3.Angle(d.GetInterpolatedNormal(u, v), Vector3.up);
                float total = 0f;
                for (int i = 0; i < n; i++)
                {
                    ScatterDetailItem it = plants[i].item;
                    float wgt = slope <= it.maxSlope ? it.weight : 0f;
                    if (wgt > 0f && it.clumped) wgt *= ClumpBoost * Clump(preset, details.seed, i, w);
                    share[i] = wgt;
                    total += wgt;
                }
                if (total <= 0f) continue;

                float amount = Patch(preset, details.seed, w) * EdgeFade(poly, preset.edgeFade, w);
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    if (share[i] <= 0f) continue;
                    float exact = full[i] * amount * share[i] / total;
                    int value = Mathf.FloorToInt(exact + Hash01(details.seed, w, 2 + i)); // дробова частина — випадкове округлення
                    if (value <= 0) continue;

                    int[,] layer = layers[plants[i].proto];
                    layer[z, x] = Mathf.Min(255, layer[z, x] + value);  // той самий префаб двічі в пресеті — складаємо
                    plants[i].cells++;
                    plants[i].sum += value;
                    any = true;
                }
                if (any) planted++;
            }

            foreach (var kv in layers) d.SetDetailLayer(rect.x, rect.y, kv.Key, kv.Value);
            EditorUtility.SetDirty(d);
            terrains++;
        }

        if (terrains == 0)
        {
            details.lastReport = "Під зоною немає terrain із сіткою деталей.";
            Debug.LogWarning("[Scatter] Трава: під зоною немає terrain з сіткою деталей");
            return;
        }

        // ===== Звіт: в Inspector і в Console =====
        var sb = new StringBuilder();
        sb.Append($"Засаджено {planted} з {cells} клітинок · режим {(coverage ? "Coverage" : "Instance Count")} · Density {preset.density:0.##}");
        if (added > 0) sb.Append($" · нових копій у terrain: {added}");
        foreach (Plant pl in plants)
        {
            string fill = pl.cells == 0 ? "—"
                : coverage ? $"заповнення ~{(float)pl.sum / pl.cells / 255f:0%}"
                           : $"~{(float)pl.sum / pl.cells:0.#} шт. у клітинці";
            sb.Append($"\n• {pl.item.prefab.name} → {pl.copy.name} (terrain #{pl.proto}): {pl.cells} клітинок, {fill} · {pl.info}{pl.note}");
            if (coverage && preset.density > pl.ceiling + 0.001f)
                sb.Append($"\n   ⚠ Density {preset.density:0.##} більша за стелю копії {pl.ceiling:0.##} — засаджено по стелі");
        }
        sb.Append(Lines(skipped));
        details.lastReport = sb.ToString();
        Debug.Log($"[Scatter] Трава {zone.name}: {details.lastReport}");
    }

    static string Lines(List<string> list)
    {
        var sb = new StringBuilder();
        foreach (string s in list) sb.Append("\n⚠ ").Append(s);
        return sb.ToString();
    }

    // ===== Прибрати траву цього пресета в зоні: копії SC_ і оригінали (траву інших видів не чіпає) =====
    public static void Clear(ScatterDetails details)
    {
        if (!Validate(details, out ScatterZone zone, out ScatterDetailPreset preset)) return;

        Undo.SetCurrentGroupName("Scatter Grass Clear");
        Vector2[] poly = PolygonXZ(zone);
        Bounds area = zone.GetBoundsXZ();
        List<GameObject> prefabs = AllPrefabs(preset, CopyIndex());
        int cleared = 0;

        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (!CellRect(t, area, out RectInt rect)) continue;
            TerrainData d = t.terrainData;
            List<int> proto = FindPrototypes(d, prefabs);
            if (proto.Count == 0) continue;

            Undo.RegisterCompleteObjectUndo(d, "Scatter Grass Clear");
            Vector3 origin = t.transform.position;
            Vector3 size = d.size;

            foreach (int p in proto)
            {
                int[,] layer = d.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, p);
                for (int z = 0; z < rect.height; z++)
                for (int x = 0; x < rect.width; x++)
                {
                    if (layer[z, x] == 0) continue;
                    float u = (rect.x + x + 0.5f) / d.detailWidth;
                    float v = (rect.y + z + 0.5f) / d.detailHeight;
                    if (!InsideXZ(poly, new Vector3(origin.x + u * size.x, 0f, origin.z + v * size.z))) continue;
                    layer[z, x] = 0;
                    cleared++;
                }
                d.SetDetailLayer(rect.x, rect.y, p, layer);
            }
            EditorUtility.SetDirty(d);
        }

        details.lastReport = $"Прибрано {cleared} клітинок трави цього пресета в контурі.";
        Debug.Log($"[Scatter] Трава {zone.name}: прибрано {cleared} клітинок");
    }

    // ===== Прибрати будь-яку траву під об'єктами зони (кущі, камені з Generate) =====
    public static void ClearUnderObjects(ScatterDetails details)
    {
        ScatterZone zone = details.Zone;
        Transform group = zone != null ? zone.transform.Find(ScatterZone.GeneratedName) : null;
        MeshRenderer[] renderers = group != null ? group.GetComponentsInChildren<MeshRenderer>() : new MeshRenderer[0];
        if (renderers.Length == 0) { Debug.LogWarning("[Scatter] Трава: у зоні немає об'єктів — спершу Generate"); return; }

        // Сліди об'єктів на землі (прямокутники зверху), трохи менші за сам об'єкт
        float s = details.clearUnderObjectsScale;
        var marks = new List<Bounds>();
        Bounds area = renderers[0].bounds;
        foreach (MeshRenderer r in renderers)
        {
            Bounds b = r.bounds;
            b.extents = new Vector3(b.extents.x * s, b.extents.y, b.extents.z * s);
            marks.Add(b);
            area.Encapsulate(b);
        }

        Undo.SetCurrentGroupName("Scatter Grass Clear Under Objects");
        int cleared = 0;

        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (!CellRect(t, area, out RectInt rect)) continue;
            TerrainData d = t.terrainData;
            int protoCount = d.detailPrototypes.Length;
            if (protoCount == 0) continue;

            // Які клітинки під об'єктами
            bool[,] mask = new bool[rect.height, rect.width];
            bool any = false;
            foreach (Bounds b in marks)
            {
                if (!CellRect(t, b, out RectInt r)) continue;
                for (int z = Mathf.Max(r.yMin, rect.yMin); z < Mathf.Min(r.yMax, rect.yMax); z++)
                for (int x = Mathf.Max(r.xMin, rect.xMin); x < Mathf.Min(r.xMax, rect.xMax); x++)
                {
                    mask[z - rect.y, x - rect.x] = true;
                    any = true;
                }
            }
            if (!any) continue;

            Undo.RegisterCompleteObjectUndo(d, "Scatter Grass Clear Under Objects");
            for (int p = 0; p < protoCount; p++)
            {
                int[,] layer = d.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, p);
                bool changed = false;
                for (int z = 0; z < rect.height; z++)
                for (int x = 0; x < rect.width; x++)
                    if (mask[z, x] && layer[z, x] != 0) { layer[z, x] = 0; changed = true; cleared++; }
                if (changed) d.SetDetailLayer(rect.x, rect.y, p, layer);
            }
            EditorUtility.SetDirty(d);
        }

        Debug.Log($"[Scatter] Трава {zone.name}: під {renderers.Length} частинами об'єктів прибрано {cleared} клітинок");
    }

    // ===== Перевірки перед роботою =====
    static bool Validate(ScatterDetails details, out ScatterZone zone, out ScatterDetailPreset preset)
    {
        zone = details.Zone;
        preset = details.preset;
        if (preset == null) { Debug.LogWarning("[Scatter] Трава: немає пресету трави"); return false; }
        if (zone == null || zone.points.Count < 3) { Debug.LogWarning("[Scatter] Трава: контур зони має мати хоча б 3 точки"); return false; }
        if (Terrain.activeTerrains.Length == 0) { Debug.LogWarning("[Scatter] Трава: у сцені немає terrain"); return false; }
        return true;
    }

    // ===== Чи придатна позиція пресету. null — так, інакше причина (показується в Inspector і звіті) =====
    public static string ItemProblem(ScatterDetailItem item)
    {
        if (item == null || item.prefab == null) return "порожній рядок (немає префаба) — пропускаю";
        if (item.weight <= 0f) return $"{item.prefab.name}: вага 0 — пропускаю";
        if (!FindMesh(item.prefab, out _, out _)) return $"{item.prefab.name}: у префабі немає меша з MeshRenderer — пропускаю";
        return null;
    }

    // Меш на корені префаба — копія буде Prefab Variant; інакше копія збирається з меша LOD0 / першої частини
    public static bool MeshOnRoot(GameObject prefab)
    {
        return prefab != null && prefab.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null
               && prefab.GetComponent<MeshRenderer>() != null;
    }

    // ===== Меш, який садитиме terrain: корінь → LOD0 → перша частина з MeshRenderer =====
    static bool FindMesh(GameObject prefab, out MeshFilter filter, out MeshRenderer renderer)
    {
        filter = null; renderer = null;
        if (prefab == null) return false;
        if (MeshOnRoot(prefab))
        {
            filter = prefab.GetComponent<MeshFilter>();
            renderer = prefab.GetComponent<MeshRenderer>();
            return true;
        }

        LODGroup lod = prefab.GetComponentInChildren<LODGroup>(true);
        if (lod != null)
        {
            LOD[] lods = lod.GetLODs();
            if (lods.Length > 0)
                foreach (Renderer r in lods[0].renderers)
                    if (r is MeshRenderer mr && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    { filter = mf; renderer = mr; return true; }
        }

        foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null && mf.TryGetComponent(out MeshRenderer mr))
            { filter = mf; renderer = mr; return true; }
        return false;
    }

    // ===== Чи всі матеріали меша вміють GPU Instancing. bad — назви тих, що ні =====
    public static bool SupportsInstancing(GameObject prefab, out string bad)
    {
        var names = new List<string>();
        if (FindMesh(prefab, out _, out MeshRenderer mr))
            foreach (Material m in mr.sharedMaterials)
                if (m != null && !m.enableInstancing && !names.Contains(m.name)) names.Add(m.name);
        bad = string.Join(", ", names);
        return names.Count == 0;
    }

    // ===== Наявні копії: GUID оригіналу → копія (за позначкою в userData) =====
    static Dictionary<string, GameObject> CopyIndex()
    {
        var map = new Dictionary<string, GameObject>();
        if (!AssetDatabase.IsValidFolder(CopiesFolder)) return map;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CopiesFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AssetImporter imp = AssetImporter.GetAtPath(path);
            if (imp == null || imp.userData == null || !imp.userData.StartsWith(CopyMark)) continue;
            string original = imp.userData.Substring(CopyMark.Length);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null && !map.ContainsKey(original)) map[original] = go;
        }
        return map;
    }

    // ===== Знайти або створити копію SC_<назва> для префаба =====
    // Меш на корені → Prefab Variant (той самий меш і матеріал, зміни оригіналу підхоплюються).
    // Меш у дочірній частині (LOD, кілька частин) → простий префаб із меша LOD0 / першої частини;
    // якщо частина зсунута чи повернута відносно кореня — меш «запікається» в окремий файл _mesh.asset.
    static GameObject GetOrCreateCopy(GameObject original, Dictionary<string, GameObject> copies, out string note, out string error)
    {
        note = ""; error = "";
        string origPath = AssetDatabase.GetAssetPath(original);
        if (string.IsNullOrEmpty(origPath)) { error = "це не префаб із Project"; return null; }
        if (origPath.StartsWith(CopiesFolder + "/")) return original;          // у пресет поклали саму копію
        string guid = AssetDatabase.AssetPathToGUID(origPath);
        if (copies.TryGetValue(guid, out GameObject existing)) return existing;

        if (!FindMesh(original, out MeshFilter mf, out MeshRenderer mr)) { error = "немає меша"; return null; }
        EnsureFolder(CopiesFolder);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{CopiesFolder}/{CopyPrefix}{SafeName(original.name)}.prefab");
        GameObject saved;

        if (MeshOnRoot(original))
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(original);
            try { saved = PrefabUtility.SaveAsPrefabAsset(instance, path); }
            finally { Object.DestroyImmediate(instance); }
            note = $" · створено копію {Path.GetFileName(path)}";
        }
        else
        {
            Mesh mesh = mf.sharedMesh;
            Matrix4x4 m = original.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            if (!IsIdentity(m))
            {
                mesh = BakeMesh(mf.sharedMesh, m, Path.ChangeExtension(path, null) + "_mesh.asset", out error);
                if (mesh == null) return null;
            }
            var go = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
            note = $" · створено копію {Path.GetFileName(path)} з меша «{mf.name}» (лише одна частина)";
        }

        if (saved == null) { error = "Unity не зберіг префаб"; return null; }
        AssetImporter imp = AssetImporter.GetAtPath(path);
        imp.userData = CopyMark + guid;
        imp.SaveAndReimport();
        saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        copies[guid] = saved;
        return saved;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    static string SafeName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    static bool IsIdentity(Matrix4x4 m)
    {
        for (int r = 0; r < 4; r++)
        for (int c = 0; c < 4; c++)
            if (Mathf.Abs(m[r, c] - (r == c ? 1f : 0f)) > 0.0001f) return false;
        return true;
    }

    // ===== Меш частини з урахуванням її зсуву, повороту й масштабу відносно кореня =====
    static Mesh BakeMesh(Mesh source, Matrix4x4 m, string path, out string error)
    {
        error = "";
        Mesh mesh = Object.Instantiate(source);
        Vector3[] v = mesh.vertices;
        if (v == null || v.Length == 0)
        {
            Object.DestroyImmediate(mesh);
            error = "меш не читається — увімкни Read/Write у налаштуваннях моделі";
            return null;
        }
        mesh.name = Path.GetFileNameWithoutExtension(path);

        for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
        mesh.vertices = v;

        Matrix4x4 nm = m.inverse.transpose;
        Vector3[] normals = mesh.normals;
        if (normals.Length == v.Length)
        {
            for (int i = 0; i < normals.Length; i++) normals[i] = nm.MultiplyVector(normals[i]).normalized;
            mesh.normals = normals;
        }
        Vector4[] tangents = mesh.tangents;
        if (tangents.Length == v.Length)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 t = m.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
            }
            mesh.tangents = tangents;
        }
        if (m.determinant < 0f)                                   // дзеркальний масштаб — розвернути трикутники
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] tris = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                mesh.SetTriangles(tris, s);
            }
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(path));
        return mesh;
    }

    // ===== Префаби пресету: оригінали + наявні копії (для очищення зони) =====
    static List<GameObject> AllPrefabs(ScatterDetailPreset preset, Dictionary<string, GameObject> copies)
    {
        var list = new List<GameObject>();
        foreach (ScatterDetailItem item in preset.items)
        {
            if (item == null || item.prefab == null) continue;
            if (!list.Contains(item.prefab)) list.Add(item.prefab);
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item.prefab));
            if (copies.TryGetValue(guid, out GameObject copy) && !list.Contains(copy)) list.Add(copy);
        }
        return list;
    }

    // Індекси видів трави в terrain для цих префабів (нічого не додає)
    static List<int> FindPrototypes(TerrainData d, List<GameObject> prefabs)
    {
        DetailPrototype[] protos = d.detailPrototypes;
        var result = new List<int>();
        for (int i = 0; i < protos.Length; i++)
            if (protos[i].usePrototypeMesh && protos[i].prototype != null && prefabs.Contains(protos[i].prototype))
                result.Add(i);
        return result;
    }

    // ===== Копії SC_ у terrain: знайти або додати. Оригінали НЕ змінюються =====
    // Нова копія бере всі налаштування оригіналу з terrain (розмір, кольори, Align to Ground, Position Jitter…),
    // а Detail Density = стеля MaxDensity — один раз. Далі Scatter у копії міняє лише:
    //  - розмір — з галочкою Size From Preset;
    //  - GPU Instancing — лише якщо всі матеріали його вміють, інакше terrain таку траву не малює.
    // Повертає, скільки копій додано.
    static int EnsurePrototypes(TerrainData d, List<Plant> plants)
    {
        var list = new List<DetailPrototype>(d.detailPrototypes);
        DetailPrototype anyMesh = list.Find(p => p.usePrototypeMesh && p.prototype != null);
        int added = 0;

        foreach (Plant pl in plants)
        {
            ScatterDetailItem item = pl.item;
            int index = list.FindIndex(p => p.usePrototypeMesh && p.prototype == pl.copy);
            if (index < 0)
            {
                DetailPrototype original = list.Find(p => p.usePrototypeMesh && p.prototype == item.prefab);
                var proto = new DetailPrototype
                {
                    prototype = pl.copy,
                    usePrototypeMesh = true,
                    renderMode = DetailRenderMode.VertexLit,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                    noiseSpread = 0.1f,
                    minWidth = item.scaleRange.x, minHeight = item.scaleRange.x,
                    maxWidth = item.scaleRange.y, maxHeight = item.scaleRange.y
                };
                if (original != null)
                {
                    proto.renderMode = original.renderMode;
                    proto.healthyColor = original.healthyColor;
                    proto.dryColor = original.dryColor;
                    proto.noiseSpread = original.noiseSpread;
                    proto.minWidth = original.minWidth; proto.maxWidth = original.maxWidth;
                    proto.minHeight = original.minHeight; proto.maxHeight = original.maxHeight;
                    CopyOptional(original, proto);
                    pl.note += " · налаштування — з оригіналу в terrain";
                }
                else
                {
                    if (anyMesh != null) CopyOptional(anyMesh, proto);
                    pl.note += " · оригіналу в terrain немає — розмір з пресету";
                }

                // Стеля густоти — один раз. Якщо terrain її не прийме — 1
                SetOptional(proto, "density", ScatterDetailPreset.MaxDensity);
                if (!IsValid(proto, out string error))
                {
                    SetOptional(proto, "density", 1f);
                    pl.note += $" · стелю {ScatterDetailPreset.MaxDensity} terrain не прийняв ({error}), стоїть 1";
                }

                list.Add(proto);
                index = list.Count - 1;
                added++;
            }

            DetailPrototype target = list[index];
            if (item.sizeFromPreset)
            {
                target.minWidth = target.minHeight = item.scaleRange.x;
                target.maxWidth = target.maxHeight = item.scaleRange.y;
            }

            // GPU Instancing: матеріал без нього → малюємо без інстансингу, інакше траву не видно
            bool instancing = SupportsInstancing(pl.copy, out string badMaterials);
            bool wasInstancing = GetOptional(target, "useInstancing", false);
            SetOptional(target, "useInstancing", instancing);
            if (!instancing && wasInstancing && target.renderMode != DetailRenderMode.Grass)
                target.renderMode = DetailRenderMode.VertexLit;

            pl.ceiling = GetOptional(target, "density", 1f);
            pl.proto = index;
            pl.info = $"стеля {pl.ceiling:0.##}, розмір {target.minWidth:0.##}–{target.maxWidth:0.##} ({(item.sizeFromPreset ? "з пресету" : "як у копії")})"
                    + (instancing ? "" : $", без GPU Instancing (вимкнено в матеріалах: {badMaterials}) — краще увімкнути Enable GPU Instancing");
        }

        d.detailPrototypes = list.ToArray();
        d.RefreshPrototypes();
        return added;
    }

    // Чи прийме terrain цей вид трави (DetailPrototype.Validate; у старих версіях Unity його немає — тоді «так»)
    static bool IsValid(DetailPrototype proto, out string error)
    {
        error = "";
        MethodInfo m = typeof(DetailPrototype).GetMethod("Validate", new[] { typeof(string).MakeByRefType() });
        if (m == null) return true;
        object[] args = { null };
        bool ok = (bool)m.Invoke(proto, args);
        error = args[0] as string ?? "";
        return ok;
    }

    static void CopyOptional(DetailPrototype from, DetailPrototype to)
    {
        foreach (string name in OptionalProps)
        {
            PropertyInfo p = typeof(DetailPrototype).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanRead && p.CanWrite) p.SetValue(to, p.GetValue(from));
        }
    }

    static void SetOptional(DetailPrototype proto, string name, object value)
    {
        PropertyInfo p = typeof(DetailPrototype).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p != null && p.CanWrite && p.PropertyType == value.GetType()) p.SetValue(proto, value);
    }

    // ===== Режим terrain: Coverage (0–255 — частка покриття) чи Instance Count (кількість у клітинці) =====
    public static bool IsCoverageMode(TerrainData d)
    {
        PropertyInfo p = typeof(TerrainData).GetProperty("detailScatterMode", BindingFlags.Public | BindingFlags.Instance);
        object value = p != null ? p.GetValue(d) : null;
        return value != null && value.ToString() == "CoverageMode";
    }

    // ===== Клітинки сітки деталей terrain, що накриває прямокутник (світові X і Z) =====
    public static bool CellRect(Terrain t, Bounds area, out RectInt rect)
    {
        TerrainData d = t.terrainData;
        rect = default;
        if (d == null || d.detailWidth == 0 || d.detailHeight == 0) return false;

        Vector3 p = t.transform.position;
        Vector3 s = d.size;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((area.min.x - p.x) / s.x * d.detailWidth), 0, d.detailWidth);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((area.max.x - p.x) / s.x * d.detailWidth), 0, d.detailWidth);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((area.min.z - p.z) / s.z * d.detailHeight), 0, d.detailHeight);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((area.max.z - p.z) / s.z * d.detailHeight), 0, d.detailHeight);
        rect = new RectInt(x0, z0, x1 - x0, z1 - z0);
        return rect.width > 0 && rect.height > 0;
    }

    // ===== Контур зони зверху, порахований один раз (та сама перевірка, що в ScatterZone.ContainsXZ, але швидше) =====
    static Vector2[] PolygonXZ(ScatterZone zone)
    {
        var poly = new Vector2[zone.points.Count];
        for (int i = 0; i < poly.Length; i++)
        {
            Vector3 w = zone.GetWorldPoint(i);
            poly[i] = new Vector2(w.x, w.z);
        }
        return poly;
    }

    static bool InsideXZ(Vector2[] poly, Vector3 w)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            bool crosses = (a.y > w.z) != (b.y > w.z);
            if (crosses && w.x < (b.x - a.x) * (w.z - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    // ===== Біля краю зони трава рідшає: 0 на самому краю → 1 через edgeFade метрів =====
    static float EdgeFade(Vector2[] poly, float fade, Vector3 w)
    {
        if (fade <= 0f) return 1f;
        Vector2 p = new Vector2(w.x, w.z);
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], ab = poly[i] - poly[j];
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            if (best <= 0f) break;
        }
        return Mathf.Clamp01(best / fade);
    }

    // ===== Плями: шум Перліна → 0 у прогалинах, 1 у плямах; patchiness змішує з рівним килимом =====
    static float Patch(ScatterDetailPreset preset, int seed, Vector3 w)
    {
        if (preset.patchiness <= 0f) return 1f;
        float ox = Hash01(seed, 11, 13, 17) * 1000f;
        float oz = Hash01(seed, 19, 23, 29) * 1000f;
        float n = Mathf.PerlinNoise((w.x + ox) / preset.patchSize, (w.z + oz) / preset.patchSize);
        float patch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, n));
        return Mathf.Lerp(1f, patch, preset.patchiness);
    }

    static T GetOptional<T>(DetailPrototype proto, string name, T fallback)
    {
        PropertyInfo p = typeof(DetailPrototype).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p != null && p.CanRead && p.PropertyType == typeof(T) ? (T)p.GetValue(proto) : fallback;
    }

    // ===== Купки для позицій Clumped: свій шум Перліна для кожної позиції → 0 поза купкою, 1 у купці =====
    // Поріг 0.55–0.75 залишає купкам приблизно третину площі (див. ClumpBoost).
    static float Clump(ScatterDetailPreset preset, int seed, int item, Vector3 w)
    {
        float size = Mathf.Max(1f, preset.clumpSize);
        float ox = Hash01(seed, 31 + item, 37, 41) * 1000f;
        float oz = Hash01(seed, 43, 47 + item, 53) * 1000f;
        float n = Mathf.PerlinNoise((w.x + ox) / size, (w.z + oz) / size);
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.75f, n));
    }

    // ===== Випадкове число 0..1 для клітинки: той самий seed і місце — те саме число =====
    static float Hash01(int seed, Vector3 w, int salt)
    {
        return Hash01(seed, Mathf.RoundToInt(w.x * 10f), Mathf.RoundToInt(w.z * 10f), salt);
    }

    static float Hash01(int seed, int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u;
            h ^= (uint)a * 0x85EBCA77u; h = (h << 13) | (h >> 19);
            h ^= (uint)b * 0xC2B2AE3Du; h = (h << 13) | (h >> 19);
            h ^= (uint)c * 0x27D4EB2Fu;
            h ^= h >> 16; h *= 0x7FEB352Du;
            h ^= h >> 15; h *= 0x846CA68Bu;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    // ===== Шар стежки: читається один раз для прямокутника зони =====
    class PathMap
    {
        float[,,] map;
        int layer, x0, z0, width, height, alphaW, alphaH;

        public static PathMap Read(TerrainData d, TerrainLayer pathLayer, RectInt cells)
        {
            if (pathLayer == null) return null;
            int index = System.Array.IndexOf(d.terrainLayers, pathLayer);
            if (index < 0) return null;

            int aw = d.alphamapWidth, ah = d.alphamapHeight;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((float)cells.xMin / d.detailWidth * aw), 0, aw - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((float)cells.xMax / d.detailWidth * aw), x0 + 1, aw);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((float)cells.yMin / d.detailHeight * ah), 0, ah - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((float)cells.yMax / d.detailHeight * ah), z0 + 1, ah);

            return new PathMap
            {
                map = d.GetAlphamaps(x0, z0, x1 - x0, z1 - z0),
                layer = index, x0 = x0, z0 = z0, width = x1 - x0, height = z1 - z0, alphaW = aw, alphaH = ah
            };
        }

        public float Weight(float u, float v)
        {
            int x = Mathf.Clamp((int)(u * alphaW) - x0, 0, width - 1);
            int z = Mathf.Clamp((int)(v * alphaH) - z0, 0, height - 1);
            return map[z, x, layer];
        }
    }
}
