using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// ============================================================
// Алгоритм трави для ScatterDetails. Кнопки — в Inspector (ScatterDetailsEditor).
// Пише в сітку деталей terrain — те саме, що пензель Paint Details:
// кожна клітинка (~1 м) зберігає, скільки трави в ній росте.
// Наявний Scatter (ScatterZone, ScatterGenerator) не змінюється — з нього лише читаються контур і об'єкти.
// Усе через Undo: Ctrl+Z (Cmd+Z) скасовує. Після роботи — Ctrl+S, щоб зберегти terrain.
// ============================================================
public static class ScatterDetailsGenerator
{
    // Необов'язкові налаштування трави, яких може не бути в старих версіях Unity.
    // Читаються й пишуться за назвою, тож код компілюється в будь-якій версії.
    static readonly string[] OptionalProps = { "alignToGround", "positionJitter", "density", "useDensityScaling", "useInstancing" };

    // ===== Засадити траву в зоні (замінює попередню траву цього пресета в зоні) =====
    public static void Generate(ScatterDetails details)
    {
        if (!Validate(details, out ScatterZone zone, out ScatterDetailPreset preset)) return;

        List<ScatterDetailItem> items = ValidItems(preset);
        if (items.Count == 0) { Debug.LogWarning("[Scatter] Трава: у пресеті немає придатних префабів з вагою > 0"); return; }

        Undo.SetCurrentGroupName("Scatter Grass Generate");
        Vector2[] poly = PolygonXZ(zone);
        Bounds area = zone.GetBoundsXZ();
        float[] weights = new float[items.Count];
        int terrains = 0, cells = 0, planted = 0, added = 0;
        string mode = "";

        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (!CellRect(t, area, out RectInt rect)) continue;
            TerrainData d = t.terrainData;
            Undo.RegisterCompleteObjectUndo(d, "Scatter Grass Generate");

            int[] proto = EnsurePrototypes(d, items, ref added);   // індекс трави в terrain для кожного пункту
            bool coverage = IsCoverageMode(d);
            int maxValue = coverage ? 255 : preset.maxPerCell;
            mode = coverage ? "Coverage" : "Instance Count";

            // Поточна трава цього пресета в прямокутнику зони
            var layers = new Dictionary<int, int[,]>();
            foreach (int p in proto)
                if (!layers.ContainsKey(p)) layers[p] = d.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, p);

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

                float slope = Vector3.Angle(d.GetInterpolatedNormal(u, v), Vector3.up);
                int item = PickItem(items, slope, Hash01(details.seed, w, 1), weights);
                if (item < 0) continue;

                float amount = preset.density * Patch(preset, details.seed, w) * EdgeFade(poly, preset.edgeFade, w);
                int value = Mathf.FloorToInt(amount * maxValue + Hash01(details.seed, w, 2)); // дробова частина — випадкове округлення
                value = Mathf.Clamp(value, 0, maxValue);
                if (value == 0) continue;

                layers[proto[item]][z, x] = value;
                planted++;
            }

            foreach (var kv in layers) d.SetDetailLayer(rect.x, rect.y, kv.Key, kv.Value);
            EditorUtility.SetDirty(d);
            terrains++;
        }

        if (terrains == 0) { Debug.LogWarning("[Scatter] Трава: під зоною немає terrain з сіткою деталей"); return; }
        Debug.Log($"[Scatter] Трава {zone.name}: засаджено {planted} з {cells} клітинок, terrain: {terrains}, режим {mode}" +
                  (added > 0 ? $", нових типів трави в terrain: {added}" : ""));
    }

    // ===== Прибрати траву цього пресета в зоні (траву, намальовану пензлем іншими типами, не чіпає) =====
    public static void Clear(ScatterDetails details)
    {
        if (!Validate(details, out ScatterZone zone, out ScatterDetailPreset preset)) return;

        Undo.SetCurrentGroupName("Scatter Grass Clear");
        Vector2[] poly = PolygonXZ(zone);
        Bounds area = zone.GetBoundsXZ();
        int cleared = 0;

        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (!CellRect(t, area, out RectInt rect)) continue;
            TerrainData d = t.terrainData;
            List<int> proto = FindPrototypes(d, preset.items);
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

    // Придатні пункти: є префаб, вага > 0, меш на корені. Про вимкнений GPU Instancing — лише попередження.
    static List<ScatterDetailItem> ValidItems(ScatterDetailPreset preset)
    {
        var result = new List<ScatterDetailItem>();
        foreach (ScatterDetailItem item in preset.items)
        {
            if (item == null || item.prefab == null || item.weight <= 0f) continue;

            if (item.prefab.GetComponent<MeshFilter>() == null)
            {
                Debug.LogWarning($"[Scatter] Трава: у префаба {item.prefab.name} немає MeshFilter на корені — terrain бере меш лише з кореня, пропускаю");
                continue;
            }

            MeshRenderer mr = item.prefab.GetComponent<MeshRenderer>();
            if (mr != null)
                foreach (Material m in mr.sharedMaterials)
                    if (m != null && !m.enableInstancing)
                        Debug.LogWarning($"[Scatter] Трава: у матеріалі {m.name} ({item.prefab.name}) вимкнено Enable GPU Instancing — увімкни, так terrain малює траву швидше");

            result.Add(item);
        }
        return result;
    }

    // ===== Типи трави в terrain (Detail Prototypes): знайти або додати =====
    // Нова трава бере налаштування (Align to Ground, Position Jitter…) з уже наявної трави-меша цього terrain.
    // Розмір від–до оновлюється з пресету — він діє на цю траву по всьому terrain.
    static int[] EnsurePrototypes(TerrainData d, List<ScatterDetailItem> items, ref int added)
    {
        var list = new List<DetailPrototype>(d.detailPrototypes);
        DetailPrototype template = list.Find(p => p.usePrototypeMesh && p.prototype != null);
        var result = new int[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            ScatterDetailItem item = items[i];
            int index = list.FindIndex(p => p.usePrototypeMesh && p.prototype == item.prefab);

            if (index < 0)
            {
                var proto = new DetailPrototype
                {
                    prototype = item.prefab,
                    usePrototypeMesh = true,
                    renderMode = DetailRenderMode.VertexLit,
                    healthyColor = Color.white,
                    dryColor = Color.white,
                    noiseSpread = 0.1f
                };
                if (template != null) CopyOptional(template, proto);
                SetOptional(proto, "useInstancing", true);
                list.Add(proto);
                index = list.Count - 1;
                added++;

                if (template == null)
                    Debug.Log($"[Scatter] Трава: {item.prefab.name} додано в terrain. Перевір Terrain → Paint Details → Edit Details: Align to Ground, Position Jitter");
            }

            DetailPrototype target = list[index];
            target.minWidth = target.minHeight = item.scaleRange.x;
            target.maxWidth = target.maxHeight = item.scaleRange.y;
            result[i] = index;
        }

        d.detailPrototypes = list.ToArray();
        d.RefreshPrototypes();
        return result;
    }

    // Індекси наявних типів трави з пресету (нічого не додає)
    static List<int> FindPrototypes(TerrainData d, List<ScatterDetailItem> items)
    {
        DetailPrototype[] protos = d.detailPrototypes;
        var result = new List<int>();
        for (int i = 0; i < protos.Length; i++)
            foreach (ScatterDetailItem item in items)
                if (item != null && item.prefab != null && protos[i].usePrototypeMesh && protos[i].prototype == item.prefab && !result.Contains(i))
                    result.Add(i);
        return result;
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

    // ===== Вибір трави за вагою серед тих, що ростуть на такому нахилі; -1 — жодна =====
    static int PickItem(List<ScatterDetailItem> items, float slope, float random01, float[] weights)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            weights[i] = slope <= items[i].maxSlope ? items[i].weight : 0f;
            total += weights[i];
        }
        if (total <= 0f) return -1;

        float pick = random01 * total;
        for (int i = 0; i < items.Count; i++)
        {
            if (weights[i] <= 0f) continue;
            if (pick < weights[i]) return i;
            pick -= weights[i];
        }
        for (int i = items.Count - 1; i >= 0; i--)
            if (weights[i] > 0f) return i;
        return -1;
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
