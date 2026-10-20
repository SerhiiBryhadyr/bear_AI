using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ============================================================
// Меню Tools → Scatter: знайти або прибрати все, що поставив Scatter, у всіх зонах сцени.
// Scatter кладе об'єкти лише в "<зона> → Generated", тож усе інше в сцені не чіпається.
// ============================================================
public static class ScatterMenu
{
    // ===== Виділити все згенероване (щоб побачити, що і де стоїть) =====
    [MenuItem("Tools/Scatter/Виділити все згенероване")]
    static void SelectAllGenerated()
    {
        var found = new List<Object>();
        foreach (ScatterZone zone in Object.FindObjectsOfType<ScatterZone>(true))
        {
            Transform root = zone.transform.Find(ScatterZone.GeneratedName);
            if (root == null) continue;
            foreach (Transform child in root) found.Add(child.gameObject);
            Debug.Log($"[Scatter] {zone.name}: {root.childCount} об'єктів у Generated");
        }
        Selection.objects = found.ToArray();
        Debug.Log($"[Scatter] Всього згенерованих об'єктів: {found.Count}");
    }

    // ===== Прибрати все згенероване у всіх зонах (Undo працює) =====
    [MenuItem("Tools/Scatter/Видалити все згенероване")]
    static void DeleteAllGenerated()
    {
        Undo.SetCurrentGroupName("Scatter Delete All");
        int count = 0;
        foreach (ScatterZone zone in Object.FindObjectsOfType<ScatterZone>(true))
        {
            Transform root = zone.transform.Find(ScatterZone.GeneratedName);
            if (root == null) continue;
            count += root.childCount;
            Undo.DestroyObjectImmediate(root.gameObject);
        }
        Debug.Log($"[Scatter] Видалено згенерованих об'єктів: {count}");
    }
}
