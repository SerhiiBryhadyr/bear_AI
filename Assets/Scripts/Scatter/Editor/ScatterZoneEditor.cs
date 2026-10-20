using UnityEditor;
using UnityEngine;

// ============================================================
// Inspector і Scene view для ScatterZone:
// кнопки Generate / New Seed / Clear і малювання контуру мишкою.
// ============================================================
[CustomEditor(typeof(ScatterZone))]
public class ScatterZoneEditor : Editor
{
    static readonly Color OutlineColor = new Color(1f, 0.6f, 0f);
    const float PickPixels = 12f; // наскільки близько (у пікселях) клікнути до точки, щоб її видалити
    const float LassoStep = 2f;   // крок між точками, коли контур малюється протягуванням (м)

    // Стан малювання протягуванням (Shift + тягни)
    bool lassoPressed;   // кнопку натиснуто з Shift
    bool lassoDrawing;   // мишка вже рушила — малюємо новий контур
    Vector3 lassoStart;  // точка, де натиснули

    // ===== Inspector =====
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var zone = (ScatterZone)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Контур у Scene view:\n" +
            "• Shift + тягни мишкою — намалювати контур заново\n" +
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
    void OnEnable()  { SceneView.beforeSceneGui += BeforeSceneGUI; }
    void OnDisable() { SceneView.beforeSceneGui -= BeforeSceneGUI; }

    void BeforeSceneGUI(SceneView view)
    {
        if (target == null) return;
        HandleMouse((ScatterZone)target, Event.current);
    }

    // Точки-ручки малюються у звичайному OnSceneGUI
    void OnSceneGUI()
    {
        if (target == null) return;
        DrawPointHandles((ScatterZone)target);
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
                if (MouseToGround(e.mousePosition, out Vector3 dragPoint))
                {
                    if (!lassoDrawing)
                    {
                        // перший рух — стираємо старий контур і починаємо новий
                        Undo.RecordObject(zone, "Scatter Draw Outline");
                        zone.points.Clear();
                        zone.points.Add(zone.transform.InverseTransformPoint(lassoStart));
                        lassoDrawing = true;
                    }
                    Vector3 last = zone.GetWorldPoint(zone.points.Count - 1);
                    if (DistXZ(last, dragPoint) >= LassoStep)
                        zone.points.Add(zone.transform.InverseTransformPoint(dragPoint));
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
                else
                {
                    Debug.Log($"[Scatter] Контур намальовано: {zone.points.Count} точок, площа {zone.AreaXZ():0} м²");
                }
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
