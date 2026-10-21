using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · Меню. ПКМ по ПАПЦІ в Project → Asset Setup:
    //   0. Preview усіх асетів — звіт для кожного асета всередині (HumanMakes, IronsObjects…);
    //      нічого не змінює. Підсумок з попередженнями — останнім рядком.
    //   1. Preview — лише друкує в Console, що буде зроблено; нічого не змінює;
    //   2. Apply   — резервна копія + виконання плану для цієї папки;
    //   3. Restore — повертає папку до стану перед останнім Apply (Reset).
    // Працює лише з однією вибраною папкою і тим, що всередині неї.
    // ============================================================
    public static class AssetSetupMenu
    {
        const string Menu = "Assets/Asset Setup/";
        const string PreviewAllItem = Menu + "0. Preview усіх асетів у папці (лише звіт)";
        const string PreviewItem = Menu + "1. Preview (нічого не змінює)";
        const string ApplyItem = Menu + "2. Apply";
        const string RestoreItem = Menu + "3. Restore — повернути як було";
        const int BigFolder = 60;   // більше текстур — перепитати (захист від запуску на цілу BaseFolder)

        static string SelectedFolder()
        {
            if (Selection.assetGUIDs.Length != 1) return null;
            string p = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
            return AssetDatabase.IsValidFolder(p) && p != "Assets" ? p : null;
        }

        [MenuItem(PreviewAllItem, true)]
        [MenuItem(PreviewItem, true)]
        [MenuItem(ApplyItem, true)]
        [MenuItem(RestoreItem, true)]
        static bool IsFolderSelected() => SelectedFolder() != null;

        // ===== 0. Preview усіх асетів у папці =====
        [MenuItem(PreviewAllItem, false, 1899)]
        static void PreviewAll()
        {
            string root = SelectedFolder();
            var folders = new List<string>();
            CollectAssetFolders(root, folders);

            var problems = new StringBuilder();
            int clean = 0;
            try
            {
                for (int i = 0; i < folders.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Asset Setup — Preview", folders[i], (float)i / folders.Count);
                    var plan = AssetSetupPlan.Build(folders[i]);
                    string report = plan.Report(false);
                    Debug.Log(report);
                    int warnings = report.Split('\n').Count(l => l.Contains("⚠"));
                    if (plan.materials.Count == 0) problems.AppendLine($"  без матеріалів  {folders[i]}");
                    else if (warnings > 0) problems.AppendLine($"  ⚠ {warnings}  {folders[i]}");
                    else clean++;
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            Debug.Log($"[AssetSetup] PREVIEW ALL {root}: асетів {folders.Count}, без попереджень {clean}.\n{problems}");
        }

        // ===== Де закінчується «папка-контейнер» і починається «папка асета» =====
        // Контейнер — папка без власних моделей, у якої 2+ підпапки з моделями (HumanMakes, Main_meadow).
        // Усе інше — один асет (bench, old-well: моделі в source/, текстури в textures/).
        static readonly string[] ModelExt = { ".fbx", ".obj", ".dae", ".blend", ".3ds", ".max" };

        static bool HasModelsInside(string folder) => AssetDatabase.FindAssets("t:Model", new[] { folder }).Length > 0;
        static bool HasOwnModel(string folder) =>
            Directory.GetFiles(folder).Any(f => ModelExt.Contains(Path.GetExtension(f).ToLowerInvariant()));

        static void CollectAssetFolders(string folder, List<string> result)
        {
            string[] children = AssetDatabase.GetSubFolders(folder);
            var withModels = children.Where(HasModelsInside).ToList();
            if (withModels.Count < 2 || HasOwnModel(folder)) { result.Add(folder); return; }

            foreach (string c in children)
            {
                if (withModels.Contains(c)) CollectAssetFolders(c, result);
                else if (AssetDatabase.FindAssets("t:Texture2D", new[] { c }).Length > 0) result.Add(c);  // текстури без моделі — теж у звіт
            }
        }

        // ===== 1. Preview =====
        [MenuItem(PreviewItem, false, 1900)]
        static void Preview() => Debug.Log(AssetSetupPlan.Build(SelectedFolder()).Report(false));

        // ===== 2. Apply =====
        [MenuItem(ApplyItem, false, 1901)]
        static void Apply()
        {
            string folder = SelectedFolder();
            var plan = AssetSetupPlan.Build(folder);
            if (plan.materials.Count == 0)
            {
                Debug.Log(plan.Report(false) + "  Наборів з кольоровою текстурою немає — нічого не роблю.");
                return;
            }

            int texCount = plan.textures.sets.Sum(s => s.maps.Count);
            if (texCount > BigFolder && !EditorUtility.DisplayDialog("Asset Setup",
                    $"У папці {texCount} текстур. Це точно папка одного асета?", "Так, продовжити", "Скасувати")) return;

            if (!EditorUtility.DisplayDialog("Asset Setup — Apply",
                    $"{folder}\n\nМатеріалів: {plan.materials.Count} (нових: {plan.materials.Count(m => m.existing == null)}), " +
                    $"моделей: {plan.models.Count}.\nДеталі — у Console (Preview).\n\n" +
                    "Перед змінами буде зроблена резервна копія. «3. Restore» поверне як було.",
                    "Apply", "Скасувати")) return;

            var backup = new AssetSetupBackup(folder);
            try
            {
                int fixedImports = AssetSetupRunner.Run(plan, backup);
                Debug.Log(plan.Report(true) + $"  Виправлено імпорт текстур: {fixedImports}.\n" +
                          "  Щось не так — ПКМ по цій папці → Asset Setup → 3. Restore.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AssetSetup] Помилка під час Apply ({folder}): {e}\n" +
                               "  Зміни могли застосуватись частково — зроби 3. Restore.");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ===== 3. Restore =====
        [MenuItem(RestoreItem, false, 1902)]
        static void Restore()
        {
            string folder = SelectedFolder();
            if (!EditorUtility.DisplayDialog("Asset Setup — Restore",
                    $"Повернути {folder} до стану перед останнім Apply?", "Restore", "Скасувати")) return;

            bool ok = AssetSetupBackup.RestoreLast(folder, out string msg);
            if (ok) Debug.Log($"[AssetSetup] RESTORE {folder}: {msg}");
            else Debug.LogWarning($"[AssetSetup] RESTORE {folder}: {msg}");
        }
    }
}
