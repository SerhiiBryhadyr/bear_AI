using System.Collections.Generic;
using UnityEngine;

// ===== Як об'єкт сідає на землю =====
// Rigid — цілісний об'єкт (камінь, кущ, стовбур): нахил під схил + вкопування, щоб жоден край не висів.
// Drape — груповий префаб: кожна дочірня частина окремо сідає на рельєф, як ковдра.
public enum ScatterFitMode { Rigid, Drape }

// ===== Один асет у пресеті =====
[System.Serializable]
public class ScatterItem
{
    [Tooltip("Префаб або FBX")]
    public GameObject prefab;

    [Tooltip("Як часто трапляється відносно інших (2 = вдвічі частіше, ніж 1)")]
    [Min(0f)] public float weight = 1f;

    [Tooltip("Rigid — цілісний об'єкт; Drape — груповий префаб, кожна частина сідає на рельєф окремо")]
    public ScatterFitMode fitMode = ScatterFitMode.Rigid;

    [Tooltip("Радіус, який займає об'єкт (м). Два об'єкти не стануть ближче, ніж сума їхніх радіусів")]
    [Min(0.1f)] public float footprintRadius = 1f;

    [Tooltip("Випадковий розмір: від X до Y")]
    public Vector2 scaleRange = new Vector2(0.9f, 1.15f);

    [Tooltip("0 — стоїть вертикально, 1 — повністю лягає по схилу")]
    [Range(0f, 1f)] public float alignToSlope = 0.5f;

    [Tooltip("Наскільки ще вкопати після посадки (м)")]
    [Min(0f)] public float sinkDepth = 0.05f;

    [Tooltip("Максимальна глибина вкопування (м). Якщо, щоб краї не висіли, треба глибше — місце відкидається (у Drape — вимикається частина групи)")]
    [Min(0f)] public float maxBury = 0.4f;

    [Tooltip("Максимальний нахил землі (градуси)")]
    [Range(0f, 90f)] public float maxSlope = 35f;
}

// ===== Пресет: що ставити, скільки і що обходити =====
[CreateAssetMenu(menuName = "Scatter/Preset", fileName = "ScatterPreset")]
public class ScatterPreset : ScriptableObject
{
    public List<ScatterItem> items = new List<ScatterItem>();

    [Tooltip("Скільки об'єктів на 100 м² (10×10 м). Реально може стати менше, якщо не вистачить місця")]
    [Min(0f)] public float density = 2f;

    [Header("Що обходимо")]
    [Tooltip("Мінімальна відстань від дерев Paint Trees (м)")]
    [Min(0f)] public float treeClearance = 1.2f;

    [Tooltip("Обходити об'єкти з колайдерами (берези-префаби, ручні групи, інші зони)")]
    public bool avoidColliders = true;
    public LayerMask obstacleLayers = ~0;

    [Tooltip("Не ставити на шар terrain стежки")]
    public bool avoidPathLayer = false;
    public TerrainLayer pathLayer;
    [Range(0f, 1f)] public float pathThreshold = 0.3f;

    // ===== Значення за замовчуванням для нових рядків =====
    // Unity створює новий рядок списку з нулями (вага 0, розмір 0, схил 0) — з такими
    // нічого не ставиться. Рядок з розміром 0–0 вважаємо новим і заповнюємо стартовими числами.
    void OnValidate()
    {
        for (int i = 0; i < items.Count; i++)
        {
            ScatterItem item = items[i];
            if (item == null || item.scaleRange == Vector2.zero)
                items[i] = new ScatterItem { prefab = item != null ? item.prefab : null };
        }
    }
}
