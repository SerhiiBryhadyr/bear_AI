using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · Виконання плану (кроки Apply по порядку).
    // Перед кожною зміною — запис у резервну копію.
    // ============================================================
    public static class AssetSetupRunner
    {
        // Повертає кількість текстур, у яких виправлено імпорт
        public static int Run(AssetSetupPlan plan, AssetSetupBackup backup)
        {
            // ===== Крок 1. Резервна копія всього, що змінюватимемо =====
            foreach (var s in plan.textures.sets)
                foreach (var t in s.maps.Values) backup.Save(t.path, false);
            foreach (var mp in plan.materials)
                if (mp.existing != null) backup.Save(mp.path, true);
            foreach (var m in plan.models) backup.Save(m.path, false);

            // ===== Крок 2. Імпорт текстур: «дані» без sRGB, Normal — тип Normal =====
            int fixedCount = 0;
            AssetDatabase.StartAssetEditing();          // усі переімпорти разом в кінці — швидше
            try
            {
                foreach (var s in plan.textures.sets)
                    foreach (var t in s.maps.Values)
                        if (FixImport(t, s)) fixedCount++;
            }
            finally { AssetDatabase.StopAssetEditing(); }

            // ===== Крок 3. Згенерувати Mask / BaseAlpha і налаштувати матеріали =====
            // "Наші" текстури — усі текстури наборів цієї папки. Лише їх можна прибрати зі слота.
            var ours = new HashSet<string>(plan.textures.sets.SelectMany(x => x.maps.Values).Select(t => t.path));
            bool IsOurs(Material om, string slotName) =>
                om != null && om.GetTexture(slotName) != null && ours.Contains(AssetDatabase.GetAssetPath(om.GetTexture(slotName)));
            var masks = new Dictionary<TextureSet, Texture2D>();       // один набір — одна Mask, навіть якщо матеріалів кілька
            var baseAlphas = new Dictionary<TextureSet, Texture2D>();
            int i = 0;
            foreach (var mp in plan.materials)
            {
                var s = mp.set;
                EditorUtility.DisplayProgressBar("Asset Setup", s.name, (float)i++ / plan.materials.Count);

                // Числа зі старого матеріалу — на випадок, коли карти для них немає
                var old = mp.existing;
                float oldSmooth = old != null && old.GetTexture("_MetallicGlossMap") == null ? old.GetFloat("_Smoothness") : 0.3f;
                float oldMetal = old != null && old.GetTexture("_MetallicGlossMap") == null ? old.GetFloat("_Metallic") : 0f;

                Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(mp.keepBase ?? s.PathOf(TexRole.BaseColor));
                Texture2D mask = null;

                if (masks.TryGetValue(s, out var ready)) mask = ready;
                else if (AssetSetupPlan.NeedsMask(s))
                {
                    string path = AssetSetupPlan.GeneratedPath(s, TextureClassifier.MaskSuffix);
                    BeforeWrite(path, backup, out bool existed);
                    TexturePacker.WriteMask(s, path, oldSmooth, oldMetal);
                    if (!existed) backup.Created(path);
                    mask = masks[s] = ImportGenerated(path, false, false, s);
                }
                if (mp.keepBase == null && baseAlphas.TryGetValue(s, out var readyAlpha)) baseMap = readyAlpha;
                else if (s.Has(TexRole.Opacity) && mp.keepBase == null)
                {
                    string path = AssetSetupPlan.GeneratedPath(s, TextureClassifier.BaseAlphaSuffix);
                    BeforeWrite(path, backup, out bool existed);
                    TexturePacker.WriteBaseAlpha(s, path);
                    if (!existed) backup.Created(path);
                    baseMap = baseAlphas[s] = ImportGenerated(path, true, true, s);
                }

                string normalPath = s.PathOf(TexRole.Normal) ?? s.PathOf(TexRole.Bump);
                Texture2D normal = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) : null;

                Material mat = mp.existing;
                if (mat == null)
                {
                    mat = new Material(Shader.Find(AssetSetupPlan.LitShader)) { name = Path.GetFileNameWithoutExtension(mp.path) };
                    AssetDatabase.CreateAsset(mat, mp.path);
                    backup.Created(mp.path);
                }
                bool hasAO = mask != null && AssetSetupPlan.HasAO(s);
                MaterialBuilder.Configure(mat, baseMap,
                    normal, normal == null && IsOurs(old, "_BumpMap"),
                    mask, mask == null && IsOurs(old, "_MetallicGlossMap"),
                    hasAO, !hasAO && IsOurs(old, "_OcclusionMap"),
                    s.Has(TexRole.Opacity) && mp.keepBase == null);
                mp.result = mat;
            }
            AssetDatabase.SaveAssets();

            // ===== Крок 4. Перейменувати наявні матеріали ("New Material" → назва набору) =====
            foreach (var mp in plan.materials.Where(m => m.renameTo != null))
            {
                string err = AssetDatabase.RenameAsset(mp.path, mp.renameTo);
                if (string.IsNullOrEmpty(err)) backup.Renamed(mp.path, $"{AssetSetupPlan.Dir(mp.path)}/{mp.renameTo}.mat");
                else Debug.LogWarning($"[AssetSetup] Не вдалось перейменувати {mp.path}: {err}");
            }

            // ===== Крок 5. Призначити матеріали слотам моделей =====
            foreach (var m in plan.models.Where(x => x.skipReason == null))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(m.path);
                if (m.enableMaterialImport)
                {
                    imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                    imp.SaveAndReimport();
                    var again = AssetSetupPlan.PlanModel(m.path, imp, plan);
                    m.remaps.AddRange(again.remaps);
                    m.unmatched.AddRange(again.unmatched);
                    m.enableMaterialImport = false;
                }
                if (m.remaps.Count == 0) continue;
                foreach (var (slot, mp) in m.remaps)
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mp.result);
                imp.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            return fixedCount;
        }

        // ===== Налаштування імпорту однієї текстури. true — якщо щось змінено =====
        static bool FixImport(TexInfo t, TextureSet set)
        {
            if (!(AssetImporter.GetAtPath(t.path) is TextureImporter imp)) return false;
            bool changed = false;
            switch (t.role)
            {
                case TexRole.BaseColor:
                    if (!imp.sRGBTexture) { imp.sRGBTexture = true; changed = true; }
                    break;
                case TexRole.Normal:
                    if (imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; changed = true; }
                    break;
                case TexRole.Bump when !set.Has(TexRole.Normal):    // карта висоти замість normal (криниця)
                    if (imp.textureType != TextureImporterType.NormalMap || !imp.convertToNormalmap)
                    {
                        imp.textureType = TextureImporterType.NormalMap;
                        imp.convertToNormalmap = true;                // Unity сам робить normal з відтінків сірого
                        imp.heightmapScale = 0.25f;
                        changed = true;
                    }
                    break;
                default:                                              // Roughness, Metallic, AO, Opacity… — це дані, не колір
                    if (imp.sRGBTexture) { imp.sRGBTexture = false; changed = true; }
                    break;
            }
            if (changed) imp.SaveAndReimport();
            return changed;
        }

        static void BeforeWrite(string path, AssetSetupBackup backup, out bool existed)
        {
            existed = File.Exists(path);
            if (existed) backup.Save(path, true);    // повторний Apply: стару Mask теж зберігаємо
        }

        // ===== Імпорт згенерованого PNG =====
        static Texture2D ImportGenerated(string path, bool srgb, bool transparency, TextureSet s)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = transparency;
            imp.mipmapEnabled = true;
            imp.streamingMipmaps = true;                              // як решта текстур проєкту
            imp.maxTextureSize = s.maps.Values                        // Max Size — як у найбільшої з оригіналів
                .Select(t => AssetImporter.GetAtPath(t.path) as TextureImporter)
                .Where(x => x != null).Select(x => x.maxTextureSize).DefaultIfEmpty(2048).Max();
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
