using System.Collections.Generic;
using UnityEngine;

// ============================================================
// Зона розстановки: контур на землі + пресет + seed.
// Сама нічого не ставить — це робить ScatterGenerator (кнопка Generate в Inspector).
// Об'єкт зони не обертай і не масштабуй (Rotation 0, Scale 1).
// ============================================================
public class ScatterZone : MonoBehaviour
{
    [Tooltip("Що і як розставляти")]
    public ScatterPreset preset;

    [Tooltip("Від цього числа залежить випадковість. Той самий seed — той самий результат")]
    public int seed = 1;

    [Tooltip("Показувати контур у Scene view")]
    public bool showOutline = true;

    // Точки контуру в локальних координатах зони. Редагуються мишкою в Scene view.
    [HideInInspector] public List<Vector3> points = new List<Vector3>();

    public const string GeneratedName = "Generated";

    // ===== Точка контуру у світових координатах =====
    public Vector3 GetWorldPoint(int i)
    {
        return transform.TransformPoint(points[i]);
    }

    // ===== Чи точка всередині контуру (дивимось зверху: лише X і Z) =====
    // Промінь від точки вбік: непарна кількість перетинів зі сторонами = всередині.
    public bool ContainsXZ(Vector3 world)
    {
        int n = points.Count;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector3 a = GetWorldPoint(i);
            Vector3 b = GetWorldPoint(j);
            bool crosses = (a.z > world.z) != (b.z > world.z);
            if (crosses && world.x < (b.x - a.x) * (world.z - a.z) / (b.z - a.z) + a.x)
                inside = !inside;
        }
        return inside;
    }

    // ===== Площа контуру зверху (м²) =====
    public float AreaXZ()
    {
        int n = points.Count;
        if (n < 3) return 0f;
        float sum = 0f;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            Vector3 a = GetWorldPoint(i);
            Vector3 b = GetWorldPoint(j);
            sum += (b.x * a.z) - (a.x * b.z);
        }
        return Mathf.Abs(sum) * 0.5f;
    }

    // ===== Прямокутник, що охоплює контур =====
    public Bounds GetBoundsXZ()
    {
        Bounds b = new Bounds(GetWorldPoint(0), Vector3.zero);
        for (int i = 1; i < points.Count; i++) b.Encapsulate(GetWorldPoint(i));
        return b;
    }

#if UNITY_EDITOR
    // ===== Контур у Scene view — лінія по рельєфу =====
    void OnDrawGizmos()
    {
        if (!showOutline || points.Count < 2) return;
        Gizmos.color = new Color(1f, 0.6f, 0f);
        int segments = points.Count >= 3 ? points.Count : points.Count - 1; // 2 точки — просто відрізок
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = GetWorldPoint(i);
            Vector3 b = GetWorldPoint((i + 1) % points.Count);
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b)), 1, 200); // крок ~1 м
            Vector3 prev = OnGround(a);
            for (int s = 1; s <= steps; s++)
            {
                Vector3 next = OnGround(Vector3.Lerp(a, b, s / (float)steps));
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }

    // Точка на землі + 0.3 м, щоб лінію було видно
    Vector3 OnGround(Vector3 p)
    {
        if (ScatterTerrain.SampleGround(p, out float h, out _)) p.y = h;
        return p + Vector3.up * 0.3f;
    }
#endif
}
