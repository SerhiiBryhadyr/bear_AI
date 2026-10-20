using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ============================================================
// Алгоритм розстановки. Викликається кнопками Generate / Clear в Inspector зони.
// Кожна зона має свою групу "Generated" — перегенеровується лише вона.
// Усе йде через Undo: Ctrl+Z (Cmd+Z) скасовує Generate повністю.
// ============================================================
public static class ScatterGenerator
{
    // Лічильники для підсумку в Console
    class Stats
    {
        public int placed, crowded, tree, slope, curve, path, obstacle, noGround, partsHidden;
    }

    // Уже поставлений об'єкт: де стоїть і скільки місця займає
    struct Placed
    {
        public Vector3 pos;
        public float radius;
        public Placed(Vector3 pos, float radius) { this.pos = pos; this.radius = radius; }
    }

    const int AttemptsPerObject = 30; // скільки спроб на один об'єкт, поки шукаємо вільне місце

    // ===== Головна функція =====
    public static void Generate(ScatterZone zone)
    {
        ScatterPreset preset = zone.preset;
        if (preset == null || preset.items.Count == 0) { Debug.LogWarning("[Scatter] Немає пресету або він порожній"); return; }
        if (zone.points.Count < 3) { Debug.LogWarning("[Scatter] Контур має мати хоча б 3 точки"); return; }

        float totalWeight = 0f, maxRadius = 0f;
        foreach (ScatterItem item in preset.items)
        {
            if (item.prefab == null || item.weight <= 0f) continue;
            totalWeight += item.weight;
            maxRadius = Mathf.Max(maxRadius, item.footprintRadius);
        }
        if (totalWeight <= 0f) { Debug.LogWarning("[Scatter] У пресеті немає префабів з вагою > 0"); return; }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Scatter Generate");
        int undoGroup = Undo.GetCurrentGroup();
        Physics.SyncTransforms(); // щоб колайдери в сцені були на актуальних місцях

        // 1. Прибираємо стару генерацію цієї зони і створюємо нову порожню групу
        Clear(zone);
        Transform root = CreateRoot(zone);

        // 2. Готуємо дані: межі, дерева поруч, скільки ставити
        var rng = new System.Random(zone.seed);
        var stats = new Stats();
        var placed = new List<Placed>();
        Bounds area = zone.GetBoundsXZ();
        Bounds treeArea = area;
        float margin = 2f * (maxRadius + preset.treeClearance);
        treeArea.Expand(new Vector3(margin, 0f, margin));
        List<Vector3> trees = ScatterTerrain.GetTreesInBounds(treeArea);

        int target = Mathf.Max(1, Mathf.RoundToInt(zone.AreaXZ() / 100f * preset.density));
        int maxAttempts = target * AttemptsPerObject;

        // 3. Кидаємо випадкові точки в контур і перевіряємо кожну
        for (int attempt = 0; attempt < maxAttempts && stats.placed < target; attempt++)
        {
            Vector3 p = new Vector3(
                Mathf.Lerp(area.min.x, area.max.x, (float)rng.NextDouble()),
                0f,
                Mathf.Lerp(area.min.z, area.max.z, (float)rng.NextDouble()));
            if (!zone.ContainsXZ(p)) continue;

            ScatterItem item = PickItem(preset, rng, totalWeight);
            bool drape = item.fitMode == ScatterFitMode.Drape;

            if (TooClose(p, item.footprintRadius, placed)) { stats.crowded++; continue; }
            float treeDistance = preset.treeClearance + (drape ? 0f : item.footprintRadius * 0.5f); // у Drape дерева перевіряє кожна частина
            if (NearTree(p, treeDistance, trees)) { stats.tree++; continue; }
            if (!ScatterTerrain.SampleGround(p, out float h, out Vector3 normal)) { stats.noGround++; continue; }
            p.y = h;
            if (Vector3.Angle(normal, Vector3.up) > item.maxSlope) { stats.slope++; continue; }
            if (preset.avoidPathLayer && ScatterTerrain.GetLayerWeight(p, preset.pathLayer) > preset.pathThreshold) { stats.path++; continue; }
            if (preset.avoidColliders && HitsObstacle(p, item.footprintRadius * 0.5f, preset.obstacleLayers, root)) { stats.obstacle++; continue; }

            // 4. Ставимо об'єкт і садимо на землю
            GameObject go = Spawn(item.prefab, root);
            float scale = Mathf.Lerp(item.scaleRange.x, item.scaleRange.y, (float)rng.NextDouble());
            go.transform.localScale *= scale;
            Quaternion yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            Quaternion baseRotation = yaw * go.transform.rotation; // зберігаємо власний поворот префаба (FBX часто має -90 по X)

            bool ok = drape
                ? FitDrape(go.transform, p, baseRotation, item, zone, trees, preset, stats)
                : SitOnGround(go.transform, p, baseRotation, item);
            if (!ok) { Object.DestroyImmediate(go); stats.curve++; continue; }

            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            Undo.RegisterCreatedObjectUndo(go, "Scatter object");
            placed.Add(new Placed(p, item.footprintRadius));
            stats.placed++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log($"[Scatter] {zone.name}: поставлено {stats.placed} з {target} (площа {zone.AreaXZ():0} м²). " +
                  $"Відкинуто: тіснота {stats.crowded}, дерева {stats.tree}, схил {stats.slope}, заокруглення {stats.curve}, " +
                  $"стежка {stats.path}, колайдери {stats.obstacle}, поза terrain {stats.noGround}. Вимкнено частин груп: {stats.partsHidden}");
    }

    // ===== Прибрати все, що згенерувала ця зона =====
    public static void Clear(ScatterZone zone)
    {
        Transform old = zone.transform.Find(ScatterZone.GeneratedName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
    }

    static Transform CreateRoot(ScatterZone zone)
    {
        var go = new GameObject(ScatterZone.GeneratedName);
        Undo.RegisterCreatedObjectUndo(go, "Scatter root");
        go.transform.SetParent(zone.transform, false);
        return go.transform;
    }

    // Створити копію зі збереженням зв'язку з префабом
    static GameObject Spawn(GameObject prefab, Transform parent)
    {
        var go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (go == null) go = Object.Instantiate(prefab, parent); // якщо в пресет перетягнули не префаб
        return go;
    }

    // ===== Випадковий асет з урахуванням ваги =====
    static ScatterItem PickItem(ScatterPreset preset, System.Random rng, float totalWeight)
    {
        float r = (float)rng.NextDouble() * totalWeight;
        ScatterItem last = null;
        foreach (ScatterItem item in preset.items)
        {
            if (item.prefab == null || item.weight <= 0f) continue;
            last = item;
            r -= item.weight;
            if (r <= 0f) return item;
        }
        return last;
    }

    // ===== Перевірки місця =====
    static bool TooClose(Vector3 p, float radius, List<Placed> placed)
    {
        foreach (Placed other in placed)
            if (DistXZ(p, other.pos) < radius + other.radius) return true;
        return false;
    }

    static bool NearTree(Vector3 p, float distance, List<Vector3> trees)
    {
        foreach (Vector3 t in trees)
            if (DistXZ(p, t) < distance) return true;
        return false;
    }

    // Чи є поруч чужий колайдер (береза-префаб, ручна група, інша зона). Terrain і наші нові об'єкти не рахуються.
    static bool HitsObstacle(Vector3 p, float radius, LayerMask layers, Transform ownRoot)
    {
        Collider[] hits = Physics.OverlapCapsule(p + Vector3.up * 0.2f, p + Vector3.up * 2f,
                                                 Mathf.Max(radius, 0.3f), layers, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            if (c is TerrainCollider) continue;
            if (c.transform.IsChildOf(ownRoot)) continue;
            return true;
        }
        return false;
    }

    static float DistXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ===== Посадка цілісного об'єкта (Rigid; у Drape — кожної частини) =====
    // 1) міряємо низ об'єкта; 2) нахиляємо під середній схил (alignToSlope);
    // 3) опускаємо, щоб найвищий «висячий» край дістав землі (ефект капелюха);
    // 4) якщо для цього треба вкопати глибше за maxBury — місце не підходить.
    static bool SitOnGround(Transform t, Vector3 pivot, Quaternion baseRotation, ScatterItem item)
    {
        // 1. Без повороту міряємо «коробку» об'єкта відносно його опорної точки
        t.SetPositionAndRotation(pivot, Quaternion.identity);
        if (!TryGetBounds(t, out Bounds b)) { t.rotation = baseRotation; return true; }

        // 9 точок низу коробки: кути, середини сторін, центр
        var bottom = new List<Vector3>(9);
        float bottomY = b.min.y - pivot.y;
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
                bottom.Add(new Vector3(b.center.x - pivot.x + ix * b.extents.x, bottomY,
                                       b.center.z - pivot.z + iz * b.extents.z));

        // 2. Середній нахил землі під об'єктом
        Vector3 avgNormal = Vector3.zero;
        foreach (Vector3 o in bottom)
            if (ScatterTerrain.SampleGround(pivot + baseRotation * o, out _, out Vector3 n)) avgNormal += n;
        avgNormal = avgNormal.sqrMagnitude > 0f ? avgNormal.normalized : Vector3.up;
        Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, avgNormal), item.alignToSlope);
        Quaternion rotation = tilt * baseRotation;

        // 3. Для кожної точки низу: наскільки вона над землею (>0 — висить, <0 — вже в землі)
        float maxGap = float.MinValue, minGap = float.MaxValue;
        foreach (Vector3 o in bottom)
        {
            Vector3 w = pivot + rotation * o;
            if (!ScatterTerrain.SampleGround(w, out float groundY, out _)) continue;
            float gap = w.y - groundY;
            maxGap = Mathf.Max(maxGap, gap);
            minGap = Mathf.Min(minGap, gap);
        }
        if (maxGap == float.MinValue) { maxGap = 0f; minGap = 0f; }

        // 4. Опускаємо на maxGap: найвищий край торкається землі, решта — нижче.
        //    Найглибша точка опиниться на (maxGap - minGap) під землею.
        if (maxGap - minGap > item.maxBury) return false;
        t.SetPositionAndRotation(pivot - Vector3.up * (maxGap + item.sinkDepth), rotation);
        return true;
    }

    // ===== Посадка групового префаба (Drape) =====
    // Група стає рівно (лише поворот), потім кожна частина (прямий дочірній об'єкт) окремо
    // сідає на рельєф через SitOnGround. Частини поза контуром, біля дерев, на крутому схилі
    // або там, де треба вкопати глибше maxBury, вимикаються.
    static bool FitDrape(Transform root, Vector3 ground, Quaternion baseRotation, ScatterItem item,
                         ScatterZone zone, List<Vector3> trees, ScatterPreset preset, Stats stats)
    {
        root.SetPositionAndRotation(ground, baseRotation);
        PrefabUtility.RecordPrefabInstancePropertyModifications(root);

        int visible = 0;
        foreach (Transform part in root)
        {
            if (!TryGetBounds(part, out _)) continue; // порожні об'єкти без моделі пропускаємо

            Vector3 pivot = part.position;
            bool ok = zone.ContainsXZ(pivot) && !NearTree(pivot, preset.treeClearance, trees);
            if (ok && ScatterTerrain.SampleGround(pivot, out float h, out Vector3 n))
            {
                pivot.y = h;
                ok = Vector3.Angle(n, Vector3.up) <= item.maxSlope
                  && SitOnGround(part, pivot, part.rotation, item);
            }
            else ok = false;

            if (ok) visible++;
            else { part.gameObject.SetActive(false); stats.partsHidden++; }

            PrefabUtility.RecordPrefabInstancePropertyModifications(part);
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);
        }
        return visible > 0;
    }

    // ===== «Коробка» всіх видимих моделей об'єкта (світові координати) =====
    static bool TryGetBounds(Transform t, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }
}
