using UnityEditor;
using UnityEngine;

// ============================================================
// Inspector і Scene view для ScatterZone:
// кнопки Generate / New Seed / Clear і малювання контуру мишкою.
// Shift + тягни: режим «Прямокутник» — з кута в кут; «Ласо» — обвести край, як олівцем.
// Зона показується напівпрозорою заливкою на рельєфі.
// ============================================================
[CustomEditor(typeof(ScatterZone))]
public class ScatterZoneEditor : Editor
{
    static readonly Color OutlineColor = new Color(1f, 0.6f, 0f);
    static readonly Color FillColor = new Color(1f, 0.6f, 0f, 0.18f);   // заливка зони
    static readonly Color PreviewColor = new Color(1f, 0.85f, 0.2f, 0.28f); // заливка під час малювання

    // Режим малювання (запам'ятовується в редакторі, на гру не впливає)
    enum DrawMode { Rectangle, Lasso }
    static readonly string[] DrawModeNames = { "Прямокутник (з кута в кут)", "Ласо (обвести край)" };
    static DrawMode Mode
    {
        get => (DrawMode)EditorPrefs.GetInt("Scatter.DrawMode", 0);
        set => EditorPrefs.SetInt("Scatter.DrawMode", (int)value);
    }
    const float PickPixels = 12f; // наскільки близько (у пікселях) клікнути до точки, щоб її видалити
    const float LassoStep = 2f;   // крок між точками, коли контур малюється протягуванням (м)

    // Стан малювання протягуванням (Shift + тягни)
    bool lassoPressed;   // кнопку натиснуто з Shift
    bool lassoDrawing;   // мишка вже рушила — малюємо новий контур
    Vector3 lassoStart;  // точка, де натиснули
    Vector3 dragCurrent; // де мишка зараз (для прямокутника)
    readonly System.Collections.Generic.List<Vector3> lassoPoints = new System.Collections.Generic.List<Vector3>(); // ласо до відпускання мишки

    // ===== Inspector =====
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var zone = (ScatterZone)target;

        EditorGUILayout.Space();
        Mode = (DrawMode)EditorGUILayout.Popup("Shift + тягни", (int)Mode, DrawModeNames);
        EditorGUILayout.HelpBox(
            "Контур у Scene view:\n" +
            (Mode == DrawMode.Rectangle
                ? "• Shift + тягни з кута в кут — прямокутна зона\n"
                : "• Shift + тягни — обвести край зони, як олівцем\n") +
            "• зона вже засаджена → Shift + тягни створює НОВУ зону поруч, стара лишається\n" +
            "• Shift + клік — додати одну точку\n" +
            "• тягни точку — перемістити\n" +
            "• Ctrl (або Cmd) + клік по точці — видалити\n\n" +
            $"Точок: {zone.points.Count}   Площа: {zone.AreaXZ():0} м²",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate", GUILayout.Height(28))) ScatterGenerator.Generate(zone);
        if (GUILayout.Button("New Seed", GUILayout.Height(28)))
        {
            Undo.RecordObject(zone, "Scatter New Seed");
            zone.seed = Random.Range(1, 100000);
            ScatterGenerator.Generate(zone);
        }
        if (GUILayout.Button("Clear", GUILayout.Height(28)))
        {
            Undo.SetCurrentGroupName("Scatter Clear");
            ScatterGenerator.Clear(zone);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("＋ Нова зона (той самий пресет)"))
            Selection.activeGameObject = CreateSiblingZone(zone).gameObject;

        if (GUILayout.Button("Видалити всі точки контуру"))
        {
            Undo.RecordObject(zone, "Scatter Clear Points");
            zone.points.Clear();
            SceneView.RepaintAll();
        }
    }

    // ===== Підписка на Scene view =====
    // Мишку ловимо в beforeSceneGui — це викликається РАНІШЕ, ніж Unity обробляє рамку виділення.
    // Інакше Shift + тягни забирає собі рамка, і до зони нічого не доходить.
    // Малюємо через duringSceneGui — він працює, навіть коли в Scene view вимкнено Gizmos.
    void OnEnable()  { SceneView.beforeSceneGui += BeforeSceneGUI; SceneView.duringSceneGui += DuringSceneGUI; }
    void OnDisable() { SceneView.beforeSceneGui -= BeforeSceneGUI; SceneView.duringSceneGui -= DuringSceneGUI; }

    void BeforeSceneGUI(SceneView view)
    {
        if (target == null) return;
        HandleMouse((ScatterZone)target, Event.current);
    }

    // Заливка, лінія контуру і точки-ручки
    void DuringSceneGUI(SceneView view)
    {
        if (target == null) return;
        var zone = (ScatterZone)target;

        if (lassoDrawing && Mode == DrawMode.Rectangle)
        {
            // Під час малювання прямокутника — жовта заливка, рамка і розмір
            Bounds r = RectXZ(lassoStart, dragCurrent);
            DrawFill(r, p => true, PreviewColor);
            DrawOutline(new[] { new Vector3(r.min.x, 0, r.min.z), new Vector3(r.max.x, 0, r.min.z),
                                new Vector3(r.max.x, 0, r.max.z), new Vector3(r.min.x, 0, r.max.z) }, PreviewColor);
            Handles.Label(Ground(r.center.x, r.center.z) + Vector3.up * 2f,
                $"{r.size.x:0} × {r.size.z:0} м   {r.size.x * r.size.z:0} м²", EditorStyles.whiteLargeLabel);
        }
        else if (lassoDrawing && Mode == DrawMode.Lasso && lassoPoints.Count >= 2)
        {
            // Під час ласо — жовта лінія по слідах мишки і заливка всередині
            var b = new Bounds(lassoPoints[0], Vector3.zero);
            foreach (var p in lassoPoints) b.Encapsulate(p);
            if (lassoPoints.Count >= 3) DrawFill(b, p => InsidePolygonXZ(lassoPoints, p), PreviewColor);
            DrawOutline(lassoPoints.ToArray(), PreviewColor);
        }
        else if (zone.showOutline && zone.points.Count >= 2)
        {
            var corners = new Vector3[zone.points.Count];
            for (int i = 0; i < corners.Length; i++) corners[i] = zone.GetWorldPoint(i);
            if (corners.Length >= 3) DrawFill(zone.GetBoundsXZ(), zone.ContainsXZ, FillColor);
            DrawOutline(corners, OutlineColor);
        }
        DrawPointHandles(zone);
    }

    // ===== Товста лінія контуру по рельєфу (крок ~1 м) =====
    static void DrawOutline(Vector3[] corners, Color color)
    {
        if (Event.current.type != EventType.Repaint || corners.Length < 2) return;
        var line = new System.Collections.Generic.List<Vector3>();
        int n = corners.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % n];
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z))), 1, 200);
            for (int s = 0; s < steps; s++)
            {
                Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                line.Add(Ground(p.x, p.z) + Vector3.up * 0.1f);
            }
        }
        line.Add(line[0]);
        Handles.color = new Color(color.r, color.g, color.b, 1f);
        Handles.DrawAAPolyLine(5f, line.ToArray());
    }

    // ===== Прямокутник з двох кутів → 4 точки контуру =====
    static void SetRectangle(ScatterZone zone, Vector3 a, Vector3 b)
    {
        Bounds r = RectXZ(a, b);
        if (r.size.x < 1f || r.size.z < 1f) { Debug.LogWarning("[Scatter] Прямокутник замалий (менше 1 м)"); return; }
        var corners = new System.Collections.Generic.List<Vector3>();
        foreach (var (x, z) in new[] { (r.min.x, r.min.z), (r.max.x, r.min.z), (r.max.x, r.max.z), (r.min.x, r.max.z) })
            corners.Add(Ground(x, z));
        ApplyContour(zone, corners, $"Прямокутник {r.size.x:0} × {r.size.z:0} м");
    }

    // ===== Записати новий контур у зону =====
    // Якщо зона вже засаджена — контур іде в НОВУ зону поруч (той самий пресет), стара посадка лишається.
    static void ApplyContour(ScatterZone zone, System.Collections.Generic.List<Vector3> world, string what)
    {
        Transform gen = zone.transform.Find(ScatterZone.GeneratedName);
        bool planted = gen != null && gen.childCount > 0;
        if (planted) zone = CreateSiblingZone(zone);

        Undo.RecordObject(zone, "Scatter Draw Outline");
        zone.points.Clear();
        foreach (Vector3 p in world) zone.points.Add(zone.transform.InverseTransformPoint(p));
        if (planted) Selection.activeGameObject = zone.gameObject;
        Debug.Log($"[Scatter] {what}: площа {zone.AreaXZ():0} м²" + (planted ? $" → нова зона {zone.name}" : $" ({zone.name})"));
    }

    // ===== Нова зона поруч: той самий батько і пресет, новий seed =====
    static ScatterZone CreateSiblingZone(ScatterZone from)
    {
        string baseName = "Zone_" + (from.preset != null ? from.preset.name.Replace("Preset_", "") : "New");
        var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(from.transform.parent, baseName));
        Undo.RegisterCreatedObjectUndo(go, "Scatter New Zone");
        Undo.SetTransformParent(go.transform, from.transform.parent, "Scatter New Zone");
        go.transform.SetPositionAndRotation(from.transform.position, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var zone = Undo.AddComponent<ScatterZone>(go);
        zone.preset = from.preset;
        zone.seed = Random.Range(1, 100000);
        zone.showOutline = from.showOutline;
        return zone;
    }

    // Чи точка всередині ламаної (для заливки ласо під час малювання)
    static bool InsidePolygonXZ(System.Collections.Generic.List<Vector3> poly, Vector3 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vector3 a = poly[i], b = poly[j];
            if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
        }
        return inside;
    }

    static Bounds RectXZ(Vector3 a, Vector3 b)
    {
        var r = new Bounds(new Vector3(a.x, 0f, a.z), Vector3.zero);
        r.Encapsulate(new Vector3(b.x, 0f, b.z));
        return r;
    }

    // ===== Напівпрозора заливка на рельєфі =====
    // Сітка клітинок по зоні; клітинка, центр якої всередині, малюється квадратом по висотах terrain.
    // Розмір клітинки росте з площею, щоб велика зона не гальмувала (≈ до 1500 клітинок).
    static void DrawFill(Bounds b, System.Func<Vector3, bool> inside, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        float cell = Mathf.Max(1f, Mathf.Sqrt(b.size.x * b.size.z / 1500f));
        int nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / cell));
        int nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / cell));

        // висоти у вузлах сітки — рахуємо один раз
        var h = new Vector3[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
                h[i, j] = Ground(b.min.x + i * cell, b.min.z + j * cell);

        var oldZTest = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;   // заливку ховають пагорби й об'єкти
        Handles.color = color;
        var quad = new Vector3[4];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var center = new Vector3(b.min.x + (i + 0.5f) * cell, 0f, b.min.z + (j + 0.5f) * cell);
                if (!inside(center)) continue;
                quad[0] = h[i, j]; quad[1] = h[i + 1, j]; quad[2] = h[i + 1, j + 1]; quad[3] = h[i, j + 1];
                Handles.DrawAAConvexPolygon(quad);
                System.Array.Reverse(quad);                // і з іншого боку — щоб не залежати від напрямку обходу
                Handles.DrawAAConvexPolygon(quad);
            }
        Handles.zTest = oldZTest;
    }

    // Точка на землі + 0.15 м, щоб заливку було видно над terrain
    static Vector3 Ground(float x, float z)
    {
        var p = new Vector3(x, 0f, z);
        if (ScatterTerrain.SampleGround(p, out float y, out _)) p.y = y;
        return p + Vector3.up * 0.15f;
    }

    // ===== Мишка: Shift + клік / Shift + тягни / Cmd(Ctrl) + клік =====
    void HandleMouse(ScatterZone zone, Event e)
    {
        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button != 0 || e.alt) break; // Alt — це обертання камери, не чіпаємо

                // Cmd/Ctrl + клік — видалити найближчу точку
                if (e.command || e.control)
                {
                    int index = FindPointUnderMouse(zone, e.mousePosition);
                    if (index >= 0)
                    {
                        Undo.RecordObject(zone, "Scatter Remove Point");
                        zone.points.RemoveAt(index);
                        e.Use();
                    }
                }
                // Shift — почати: клік це чи протягування, стане зрозуміло далі
                else if (e.shift)
                {
                    if (MouseToGround(e.mousePosition, out lassoStart))
                    {
                        lassoPressed = true;
                        lassoDrawing = false;
                    }
                    else Debug.LogWarning("[Scatter] Клік не влучив у terrain");
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (!lassoPressed) break;
                if (Mode == DrawMode.Rectangle)
                {
                    // Прямокутник: лише запам'ятовуємо другий кут, зона зміниться, коли відпустиш мишку
                    if (MouseToGround(e.mousePosition, out Vector3 corner)) { dragCurrent = corner; lassoDrawing = true; }
                    SceneView.RepaintAll();
                    e.Use();
                    break;
                }
                // Ласо: збираємо точки окремо; зона зміниться, коли відпустиш мишку
                if (MouseToGround(e.mousePosition, out Vector3 dragPoint))
                {
                    if (!lassoDrawing) { lassoPoints.Clear(); lassoPoints.Add(lassoStart); lassoDrawing = true; }
                    if (DistXZ(lassoPoints[lassoPoints.Count - 1], dragPoint) >= LassoStep) lassoPoints.Add(dragPoint);
                    SceneView.RepaintAll();
                }
                e.Use();
                break;

            case EventType.MouseUp:
                if (!lassoPressed) break;
                if (!lassoDrawing)
                {
                    // мишка не рухалась — звичайний клік: додаємо одну точку
                    Undo.RecordObject(zone, "Scatter Add Point");
                    InsertPoint(zone, zone.transform.InverseTransformPoint(lassoStart));
                }
                else if (Mode == DrawMode.Rectangle)
                {
                    SetRectangle(zone, lassoStart, dragCurrent);
                }
                else if (lassoPoints.Count >= 3)
                {
                    ApplyContour(zone, lassoPoints, "Контур намальовано");
                }
                else Debug.LogWarning("[Scatter] Ласо закоротке — обведи край зони повністю");
                lassoPressed = false;
                lassoDrawing = false;
                Repaint();               // оновити Inspector (точки і площа)
                SceneView.RepaintAll();
                e.Use();
                break;
        }
    }

    // ===== Точки, які можна тягнути мишкою =====
    void DrawPointHandles(ScatterZone zone)
    {
        Handles.color = OutlineColor;
        for (int i = 0; i < zone.points.Count; i++)
        {
            Vector3 world = zone.GetWorldPoint(i);
            float size = HandleUtility.GetHandleSize(world) * 0.08f;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                // точку завжди кладемо на землю під курсором
                if (MouseToGround(Event.current.mousePosition, out Vector3 groundHit)) moved = groundHit;
                else if (ScatterTerrain.SampleGround(moved, out float h, out _)) moved.y = h;

                Undo.RecordObject(zone, "Scatter Move Point");
                zone.points[i] = zone.transform.InverseTransformPoint(moved);
            }
        }
    }

    // ===== Допоміжне =====

    // Точка на terrain під курсором: спершу через колайдер, якщо не вийшло — «крокуємо» променем по висотах terrain
    static bool MouseToGround(Vector2 mouse, out Vector3 point)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mouse);
        return RaycastTerrainCollider(ray, out point) || MarchTerrain(ray, out point);
    }

    static bool RaycastTerrainCollider(Ray ray, out Vector3 point)
    {
        point = Vector3.zero;
        float best = float.MaxValue;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!(hit.collider is TerrainCollider)) continue;
            if (hit.distance < best) { best = hit.distance; point = hit.point; }
        }
        return best < float.MaxValue;
    }

    // Запасний спосіб без колайдера: йдемо променем кроками по 2 м, поки не опинимось під землею, потім уточнюємо
    static bool MarchTerrain(Ray ray, out Vector3 point)
    {
        point = Vector3.zero;
        const float step = 2f;
        for (float d = step; d < 5000f; d += step)
        {
            if (!IsUnderGround(ray.GetPoint(d))) continue;
            float lo = d - step, hi = d;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (IsUnderGround(ray.GetPoint(mid))) hi = mid; else lo = mid;
            }
            point = ray.GetPoint(hi);
            return true;
        }
        return false;
    }

    static bool IsUnderGround(Vector3 p)
    {
        return ScatterTerrain.SampleGround(p, out float h, out _) && p.y <= h;
    }

    // Найближча до курсора точка (у пікселях екрана), або -1
    static int FindPointUnderMouse(ScatterZone zone, Vector2 mouse)
    {
        int best = -1;
        float bestDist = PickPixels;
        for (int i = 0; i < zone.points.Count; i++)
        {
            float d = Vector2.Distance(HandleUtility.WorldToGUIPoint(zone.GetWorldPoint(i)), mouse);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    // Нова точка вставляється в найближчу сторону контуру, а не в кінець —
    // так контур можна «доточувати» в будь-якому місці.
    static void InsertPoint(ScatterZone zone, Vector3 local)
    {
        int n = zone.points.Count;
        if (n < 3) { zone.points.Add(local); return; }
        int bestIndex = n;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            float d = DistanceToSegmentXZ(local, zone.points[i], zone.points[(i + 1) % n]);
            if (d < bestDist) { bestDist = d; bestIndex = i + 1; }
        }
        zone.points.Insert(bestIndex, local);
    }

    static float DistanceToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 pp = new Vector2(p.x, p.z), aa = new Vector2(a.x, a.z), bb = new Vector2(b.x, b.z);
        Vector2 ab = bb - aa;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(pp - aa, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(pp, aa + ab * t);
    }

    static float DistXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
