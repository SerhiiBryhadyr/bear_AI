using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · Резервна копія і Restore.
    // Перед кожною зміною копіює файл (.mat, .meta текстур і моделей,
    // згенеровані PNG, якщо вони вже були) у
    //     <проєкт>/AssetSetupBackups/<дата-час>_<папка>/
    // Ця папка лежить ПОЗА Assets — Unity її не бачить і не імпортує.
    // Restore: повертає назви, прибирає створені файли (у Кошик macOS),
    // кладе старі файли на місце і переімпортовує лише їх.
    // ============================================================

    [Serializable] class BackupEntry { public string assetPath; public string file; public string copy; }
    [Serializable] class RenameEntry { public string oldPath; public string newPath; }
    [Serializable] class BackupManifest
    {
        public string folder;
        public string created;
        public List<BackupEntry> files = new List<BackupEntry>();
        public List<string> createdAssets = new List<string>();
        public List<RenameEntry> renames = new List<RenameEntry>();
    }

    public class AssetSetupBackup
    {
        public static string Root => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "AssetSetupBackups");

        readonly BackupManifest manifest;
        readonly string dir;
        readonly HashSet<string> saved = new HashSet<string>();

        public AssetSetupBackup(string folder)
        {
            manifest = new BackupManifest { folder = folder, created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            dir = Path.Combine(Root, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Path.GetFileName(folder).Replace(' ', '_'));
            Directory.CreateDirectory(dir);
            Write();
        }

        // ===== Зберегти .meta асета (і сам файл, якщо withFile) — ДО зміни =====
        public void Save(string assetPath, bool withFile)
        {
            CopyIn(assetPath + ".meta", assetPath);
            if (withFile) CopyIn(assetPath, assetPath);
        }

        public void Created(string assetPath) { manifest.createdAssets.Add(assetPath); Write(); }
        public void Renamed(string oldPath, string newPath) { manifest.renames.Add(new RenameEntry { oldPath = oldPath, newPath = newPath }); Write(); }

        void CopyIn(string file, string assetPath)
        {
            if (!File.Exists(file) || !saved.Add(file)) return;
            string copy = manifest.files.Count.ToString("D4") + "_" + Path.GetFileName(file);
            File.Copy(file, Path.Combine(dir, copy));
            manifest.files.Add(new BackupEntry { assetPath = assetPath, file = file, copy = copy });
            Write();
        }

        // Маніфест пишемо після кожного кроку — Restore спрацює, навіть якщо Apply впав посередині
        void Write() => File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonUtility.ToJson(manifest, true));

        // =====================================================================
        // Restore: остання ще не відновлена копія для цієї папки.
        // Після відновлення копія позначається "_restored"; наступний Restore
        // поверне на крок раніше (якщо Apply запускали кілька разів).
        // =====================================================================
        public static bool RestoreLast(string folder, out string message)
        {
            string dir = !Directory.Exists(Root) ? null : Directory.GetDirectories(Root)
                .Where(d => !d.EndsWith("_restored") && File.Exists(Path.Combine(d, "manifest.json")))
                .OrderByDescending(d => Path.GetFileName(d))
                .FirstOrDefault(d => Read(d).folder == folder);
            if (dir == null) { message = "для цієї папки немає резервної копії"; return false; }
            var man = Read(dir);

            // 1) Повернути назви (з кінця)
            for (int i = man.renames.Count - 1; i >= 0; i--)
            {
                var r = man.renames[i];
                if (File.Exists(r.newPath)) AssetDatabase.RenameAsset(r.newPath, Path.GetFileNameWithoutExtension(r.oldPath));
            }

            // 2) Створені файли — у Кошик macOS (їх можна дістати звідти)
            foreach (string p in man.createdAssets)
                if (File.Exists(p)) AssetDatabase.MoveAssetToTrash(p);

            // 3) Старі файли — на місце
            foreach (var e in man.files) File.Copy(Path.Combine(dir, e.copy), e.file, true);

            // 4) Переімпорт лише відновлених асетів
            AssetDatabase.Refresh();
            foreach (string p in man.files.Select(e => e.assetPath).Distinct())
                if (File.Exists(p)) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);

            Directory.Move(dir, dir + "_restored");
            message = $"повернуто файлів: {man.files.Count}, прибрано створених: {man.createdAssets.Count}, " +
                      $"назв повернуто: {man.renames.Count} (копія від {man.created})";
            return true;
        }

        static BackupManifest Read(string dir) =>
            JsonUtility.FromJson<BackupManifest>(File.ReadAllText(Path.Combine(dir, "manifest.json")));
    }
}
