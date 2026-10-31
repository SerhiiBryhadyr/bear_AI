using UnityEditor;
using UnityEngine;

// ============================================================
// Inspector для ScatterDetails: кнопки трави — «Засадити», «Нове seed», «Прибрати»,
// «Прибрати під об'єктами». Контур малюється в самій зоні (ScatterZone вище).
// ============================================================
[CustomEditor(typeof(ScatterDetails))]
public class ScatterDetailsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var details = (ScatterDetails)target;

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
    }

    // ===== Підказка: що під зоною (terrain, розмір клітинки, режим) =====
    static string Info(ScatterZone zone)
    {
        string text = "Трава кладеться в terrain як Paint Details, не об'єктами. Контур — той самий, що в зоні вище.\n" +
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
