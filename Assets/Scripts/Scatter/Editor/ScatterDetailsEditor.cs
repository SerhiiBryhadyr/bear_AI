using System.Text;
using UnityEditor;
using UnityEngine;

// ============================================================
// Inspector для ScatterDetails: кнопки трави — «Засадити», «Нове seed», «Прибрати»,
// «Прибрати під об'єктами». Контур малюється в самій зоні (ScatterZone вище).
// Показує, що буде з позиціями пресету (пропуск, копія з частини, без інстансингу),
// і звіт останнього засаджування.
// ============================================================
[CustomEditor(typeof(ScatterDetails))]
public class ScatterDetailsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var details = (ScatterDetails)target;

        // ===== Що буде з позиціями пресету — видно одразу, без Console =====
        string notes = Notes(details.preset);
        if (notes != "")
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(notes, MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(Info(details.Zone), MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Засадити траву", GUILayout.Height(28))) ScatterDetailsGenerator.Generate(details);
        if (GUILayout.Button("Нове seed", GUILayout.Height(28)))
        {
            Undo.RecordObject(details, "Scatter Grass New Seed");
            details.seed = Random.Range(1, 100000);
            ScatterDetailsGenerator.Generate(details);
        }
        if (GUILayout.Button("Прибрати траву", GUILayout.Height(28))) ScatterDetailsGenerator.Clear(details);
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Прибрати траву під об'єктами зони"))
            ScatterDetailsGenerator.ClearUnderObjects(details);

        // ===== Звіт останнього «Засадити траву» =====
        if (!string.IsNullOrEmpty(details.lastReport))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Останнє засаджування", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(details.lastReport, MessageType.None);
        }
    }

    // ===== Позиції, які пропускаються, збираються з однієї частини або малюються без інстансингу =====
    static string Notes(ScatterDetailPreset preset)
    {
        if (preset == null) return "";
        var sb = new StringBuilder();
        foreach (ScatterDetailItem item in preset.items)
        {
            string problem = ScatterDetailsGenerator.ItemProblem(item);
            if (problem != null) { Line(sb, problem); continue; }
            if (!ScatterDetailsGenerator.MeshOnRoot(item.prefab))
                Line(sb, $"{item.prefab.name}: меш не на корені префаба — копія SC_ буде з меша LOD0 / першої частини (лише одна частина)");
            if (!ScatterDetailsGenerator.SupportsInstancing(item.prefab, out string bad))
                Line(sb, $"{item.prefab.name}: у матеріалах {bad} вимкнено Enable GPU Instancing — малюю без інстансингу (лише текстура _MainTex). Краще увімкнути галочку в матеріалах");
        }
        return sb.ToString();
    }

    static void Line(StringBuilder sb, string text)
    {
        if (sb.Length > 0) sb.Append('\n');
        sb.Append("• ").Append(text);
    }

    // ===== Підказка: як працює і що під зоною (terrain, розмір клітинки, режим) =====
    static string Info(ScatterZone zone)
    {
        string text = "Трава кладеться в terrain як Paint Details, не об'єктами. Контур — той самий, що в зоні вище.\n" +
                      $"Scatter садить свої копії ({ScatterDetailsGenerator.CopyPrefix}<назва>, папка Scatter_Copies): трава пензля й стара трава не змінюються.\n" +
                      "Density — як Detail Density пензля на повну силу, лише в цій зоні. Стерти траву Scatter пензлем — вибери в Paint Details копію SC_.\n" +
                      "Після роботи — Ctrl+S (Cmd+S), щоб зберегти terrain.";

        if (zone == null || zone.points.Count < 3)
            return text + "\n\nСпершу намалюй контур зони.";

        Bounds area = zone.GetBoundsXZ();
        int count = 0;
        string cell = "", mode = "";
        foreach (Terrain t in Terrain.activeTerrains)
        {
            if (!ScatterDetailsGenerator.CellRect(t, area, out _)) continue;
            count++;
            TerrainData d = t.terrainData;
            cell = (d.size.x / d.detailWidth).ToString("0.00");
            mode = ScatterDetailsGenerator.IsCoverageMode(d) ? "Coverage" : "Instance Count";
        }

        return count == 0
            ? text + "\n\nПід зоною немає terrain із сіткою деталей."
            : text + $"\n\nTerrain під зоною: {count} · клітинка ~{cell} м · режим: {mode}";
    }
}
