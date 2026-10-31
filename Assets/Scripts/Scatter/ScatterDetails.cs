using UnityEngine;

// ============================================================
// Трава для зони Scatter. Кладеться в terrain як Paint Details (не об'єктами),
// тож коштує стільки ж, скільки трава, намальована пензлем terrain.
// Живе на тому самому об'єкті, що й ScatterZone, і бере з неї контур.
// Сама нічого не садить — це робить ScatterDetailsGenerator (кнопки в Inspector).
// У грі нічого не робить.
// ============================================================
[RequireComponent(typeof(ScatterZone))]
[DisallowMultipleComponent]
public class ScatterDetails : MonoBehaviour
{
    [Tooltip("Яку траву і як густо садити")]
    public ScatterDetailPreset preset;

    [Tooltip("Випадковість трави. Окремо від seed зони: «Нове seed» трави не рухає кущі й камені")]
    public int seed = 1;

    [Tooltip("«Прибрати траву під об'єктами»: яку частку розміру об'єкта очищати (0.7 — трохи менше за сам об'єкт)")]
    [Range(0.2f, 1.5f)] public float clearUnderObjectsScale = 0.7f;

    public ScatterZone Zone => GetComponent<ScatterZone>();
}
