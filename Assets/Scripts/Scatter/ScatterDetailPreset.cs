using System.Collections.Generic;
using UnityEngine;

// ===== Одна трава в пресеті =====
[System.Serializable]
public class ScatterDetailItem
{
    [Tooltip("Префаб трави: один меш (MeshFilter + MeshRenderer на корені префаба), без груп")]
    public GameObject prefab;

    [Tooltip("Як часто трапляється відносно інших (2 = вдвічі частіше, ніж 1)")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Випадковий розмір: від X до Y. Це налаштування трави в terrain — діє на цю траву по всьому terrain, не лише в зоні")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    [Tooltip("Максимальний нахил землі (градуси)")]
    [Range(0f, 90f)] public float maxSlope = 40f;
}

// ===== Пресет трави: що садити, як густо і що обходити =====
// Трава кладеться в terrain як Paint Details, не об'єктами (ScatterDetails на зоні).
[CreateAssetMenu(menuName = "Scatter/Grass Preset", fileName = "GrassPreset")]
public class ScatterDetailPreset : ScriptableObject
{
    public List<ScatterDetailItem> items = new List<ScatterDetailItem>();

    [Tooltip("Наскільки густо, 0–1. 1 — максимум: у режимі terrain Coverage клітинка повністю вкрита, у режимі Instance Count — maxPerCell травинок у клітинці")]
    [Range(0f, 1f)] public float density = 0.6f;

    [Header("Плями")]
    [Tooltip("0 — рівний килим, 1 — трава росте плямами з прогалинами")]
    [Range(0f, 1f)] public float patchiness = 0.5f;

    [Tooltip("Розмір плям (м)")]
    [Min(1f)] public float patchSize = 8f;

    [Tooltip("На скільки метрів від краю зони трава поступово рідшає (0 — різкий край)")]
    [Min(0f)] public float edgeFade = 2f;

    [Header("Що обходимо")]
    [Tooltip("Не садити на шар terrain стежки")]
    public bool avoidPathLayer = false;
    public TerrainLayer pathLayer;
    [Range(0f, 1f)] public float pathThreshold = 0.3f;

    [Header("Лише для terrain у режимі Instance Count")]
    [Tooltip("Скільки травинок у клітинці при щільності 1")]
    [Range(1, 16)] public int maxPerCell = 8;

    // ===== Значення за замовчуванням для нових рядків =====
    // Unity створює новий рядок списку з нулями — з такими нічого не садиться.
    // Рядок з розміром 0–0 вважаємо новим і заповнюємо стартовими числами (як у ScatterPreset).
    void OnValidate()
    {
        for (int i = 0; i < items.Count; i++)
        {
            ScatterDetailItem item = items[i];
            if (item == null || item.scaleRange == Vector2.zero)
                items[i] = new ScatterDetailItem { prefab = item != null ? item.prefab : null };
        }
    }
}
