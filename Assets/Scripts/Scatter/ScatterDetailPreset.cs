using System.Collections.Generic;
using UnityEngine;

// ===== Одна рослина в пресеті (трава, квіти, плющ, маленькі камені, корені) =====
[System.Serializable]
public class ScatterDetailItem
{
    [Tooltip("Звичайний префаб із Project. Scatter сам зробить і садитиме його копію SC_<назва> — " +
             "трава пензля й стара трава цього префаба не змінюються. Меш не на корені (LOD, кілька частин) — копія з меша LOD0 / першої частини")]
    public GameObject prefab;

    [Tooltip("Частка цієї рослини в суміші (2 = вдвічі більше, ніж 1). Усі рослини ростуть разом у кожній клітинці")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Ставити розмір з пресету (Scale Range) у копію SC_. Вимкнено — розмір копії такий, як у оригіналу в terrain (Edit Details). " +
             "Діє на всю траву Scatter цього префаба, але не на траву пензля")]
    public bool sizeFromPreset = false;

    [Tooltip("Випадковий розмір: від X до Y (з галочкою Size From Preset або якщо оригіналу ще немає в terrain)")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    [Tooltip("Максимальний нахил землі (градуси)")]
    [Range(0f, 90f)] public float maxSlope = 40f;

    [Tooltip("Росте купками (квіти): загальна кількість приблизно та сама, але зібрана в купки розміром Clump Size")]
    public bool clumped = false;
}

// ===== Пресет трави: що садити, як густо і що обходити =====
// Трава кладеться в terrain як Paint Details, не об'єктами (ScatterDetails на зоні).
[CreateAssetMenu(menuName = "Scatter/Grass Preset", fileName = "GrassPreset")]
public class ScatterDetailPreset : ScriptableObject
{
    // Стеля густоти копій SC_ (їхній Detail Density у terrain). Ставиться один раз, коли копія з'являється в terrain.
    public const float MaxDensity = 8f;

    public List<ScatterDetailItem> items = new List<ScatterDetailItem>();

    [Tooltip("Густота в цій зоні — у тих самих одиницях, що Detail Density пензля на повну силу (0.7 у пресеті ≈ пензель 0.7). " +
             "Діє лише всередині зони: інші зони, трава пензля й стара трава не змінюються. Максимум — стеля копій (8)")]
    [Range(0f, MaxDensity)] public float density = 1f;

    [Header("Плями")]
    [Tooltip("0 — рівний килим, 1 — трава росте плямами з прогалинами")]
    [Range(0f, 1f)] public float patchiness = 0.5f;

    [Tooltip("Розмір плям (м)")]
    [Min(1f)] public float patchSize = 8f;

    [Tooltip("Розмір купок для позицій з галочкою Clumped (м)")]
    [Min(1f)] public float clumpSize = 4f;

    [Tooltip("На скільки метрів від краю зони трава поступово рідшає (0 — різкий край)")]
    [Min(0f)] public float edgeFade = 2f;

    [Header("Що обходимо")]
    [Tooltip("Не садити на шар terrain стежки")]
    public bool avoidPathLayer = false;
    public TerrainLayer pathLayer;
    [Range(0f, 1f)] public float pathThreshold = 0.3f;

    [Header("Лише для terrain у режимі Instance Count")]
    [Tooltip("Скільки травинок у клітинці при густоті 1")]
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
