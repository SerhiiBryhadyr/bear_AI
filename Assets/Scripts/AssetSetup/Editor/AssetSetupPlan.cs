using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · План: що буде зроблено з папкою асета.
    // Збирає текстури, наявні матеріали й моделі і вирішує:
    //   який матеріал для якого набору текстур, у який слот моделі його поставити.
    // НІЧОГО НЕ ЗМІНЮЄ. Preview лише друкує план, Apply його виконує.
    // ============================================================

    public class MaterialPlan
    {
        public TextureSet set;
        public Material existing;   // наявний матеріал (оновлюємо на місці); null → створюємо новий
        public string path;         // де лежить / де буде .mat
        public string renameTo;     // нова назва наявного матеріалу; null — не перейменовувати
        public Material result;     // заповнюється під час Apply
        public string keepBase;     // матеріал дивиться на КОПІЮ кольору (табличка: 3 копії) — лишаємо його копію
    }

    public class ModelPlan
    {
        public string path;
        public bool enableMaterialImport;   // у моделі вимкнено імпорт матеріалів — слоти видно лише після увімкнення
        public string skipReason;
        public readonly List<(string slot, MaterialPlan mat)> remaps = new List<(string, MaterialPlan)>();
        public readonly List<string> unmatched = new List<string>();
    }

    public class AssetSetupPlan
    {
        public const string LitShader = "Universal Render Pipeline/Lit";

        public string folder;
        public ClassifyResult textures;
        public readonly List<MaterialPlan> materials = new List<MaterialPlan>();
        public readonly List<ModelPlan> models = new List<ModelPlan>();
        public readonly List<string> skippedMaterials = new List<string>();  // не URP Lit — не чіпаємо
        public readonly List<string> unusedMaterials = new List<string>();   // Lit, але набір не знайдено — не чіпаємо

        public static bool HasAO(TextureSet s) => s.Has(TexRole.AO) || s.Has(TexRole.PackedORM);
        public static bool NeedsMask(TextureSet s) =>
            HasAO(s) || s.Has(TexRole.Metallic) || s.Has(TexRole.Roughness) || s.Has(TexRole.Smoothness);

        public static string Dir(string p) => Path.GetDirectoryName(p).Replace('\\', '/');

        public static string SafeName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        // Згенерований PNG лежить поруч із кольоровою текстурою: "<набір>_Mask.png"
        public static string GeneratedPath(TextureSet s, string suffix) =>
            $"{Dir(s.PathOf(TexRole.BaseColor) ?? s.maps.Values.First().path)}/{SafeName(s.name)}{suffix}.png";

        // =====================================================================
        public static AssetSetupPlan Build(string folder)
        {
            var plan = new AssetSetupPlan { folder = folder };

            // ===== 1. Наявні матеріали і їхні кольорові текстури =====
            var lit = new List<Material>();
            var forcedBase = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".mat")) continue;                          // матеріали всередині FBX — не файли, пропускаємо
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                if (m.shader == null || m.shader.name != LitShader)
                { plan.skippedMaterials.Add($"{p} ({(m.shader != null ? m.shader.name : "немає шейдера")})"); continue; }
                lit.Add(m);
                var tex = m.GetTexture("_BaseMap");
                if (tex != null) forcedBase.Add(AssetDatabase.GetAssetPath(tex));
            }

            // ===== 2. Текстури → набори =====
            var texPaths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(TextureClassifier.IsImage)
                .Distinct().ToList();
            plan.textures = TextureClassifier.Build(texPaths, forcedBase);
            foreach (var s in plan.textures.sets)
                if (string.IsNullOrEmpty(s.name)) s.name = Path.GetFileName(folder);   // "base_color.jpg" → назва папки

            // ===== 3. Матеріал для кожного набору з кольором =====
            foreach (var set in plan.textures.sets.Where(s => s.IsMaterial))
            {
                string basePath = set.PathOf(TexRole.BaseColor);
                var copies = new HashSet<string>(set.extra.Where(e => e.role == TexRole.BaseColor).Select(e => e.path));
                var own = new HashSet<string>(copies) { basePath, GeneratedPath(set, TextureClassifier.BaseAlphaSuffix) };
                string BaseOf(Material mat0) => mat0.GetTexture("_BaseMap") != null ? AssetDatabase.GetAssetPath(mat0.GetTexture("_BaseMap")) : null;

                // Усі наявні матеріали цього набору (зазвичай один; у таблички — три)
                var owned = lit.Where(x => own.Contains(BaseOf(x))).OrderBy(x => copies.Contains(BaseOf(x))).ToList();
                if (owned.Count == 0)
                {
                    plan.materials.Add(new MaterialPlan { set = set,
                        path = AssetDatabase.GenerateUniqueAssetPath($"{Dir(basePath)}/{SafeName(set.name)}.mat") });
                    continue;
                }
                foreach (var m in owned)
                {
                    lit.Remove(m);
                    var mp = new MaterialPlan { set = set, existing = m, path = AssetDatabase.GetAssetPath(m) };
                    if (copies.Contains(BaseOf(m))) mp.keepBase = BaseOf(m);
                    string wanted = SafeName(set.name);
                    if (m == owned[0] && m.name != wanted && !File.Exists($"{Dir(mp.path)}/{wanted}.mat"))
                        mp.renameTo = wanted;                                // "New Material" → "Bucket_low_wood"
                    plan.materials.Add(mp);
                }
            }
            plan.unusedMaterials.AddRange(lit.Select(AssetDatabase.GetAssetPath));

            // ===== 4. Моделі =====
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(p) is ModelImporter imp) plan.models.Add(PlanModel(p, imp, plan));
            }
            return plan;
        }

        // ===== Модель: який матеріал у який слот =====
        public static ModelPlan PlanModel(string path, ModelImporter imp, AssetSetupPlan plan)
        {
            var mp = new ModelPlan { path = path };
            if (plan.materials.Count == 0) { mp.skipReason = "немає матеріалів для призначення"; return mp; }
            if (imp.materialLocation == ModelImporterMaterialLocation.External)
            { mp.skipReason = "старий режим матеріалів (External) — не чіпаю"; return mp; }
            if (imp.materialImportMode == ModelImporterMaterialImportMode.None) { mp.enableMaterialImport = true; return mp; }

            // Слоти = назви матеріалів усередині моделі + ті, що вже перепризначені
            var remapped = new Dictionary<string, Material>();
            foreach (var kv in imp.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) remapped[kv.Key.name] = kv.Value as Material;
            var slots = new List<string>(remapped.Keys);
            foreach (var m in AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<Material>())
                if (!slots.Contains(m.name)) slots.Add(m.name);

            foreach (string slot in slots)
            {
                remapped.TryGetValue(slot, out var current);
                var target = MatchSlot(slot, current, plan);
                if (target != null) mp.remaps.Add((slot, target)); else mp.unmatched.Add(slot);
            }
            return mp;
        }

        static MaterialPlan MatchSlot(string slot, Material current, AssetSetupPlan plan)
        {
            // 1) слот уже дивиться на матеріал, який ми оновлюємо → лишаємо його
            if (current != null)
            {
                var same = plan.materials.FirstOrDefault(m => m.existing == current || m.result == current);
                if (same != null) return same;
            }

            // 2) за назвою слота: точний збіг > закінчується на > містить; нічия → не вгадуємо
            string slotKey = TextureClassifier.Key(slot);
            MaterialPlan best = null; int bestScore = 0; bool tie = false;
            foreach (var m in plan.materials)
            {
                int score = Score(m.set.key, slotKey);
                if (score > bestScore) { best = m; bestScore = score; tie = false; }
                else if (score > 0 && score == bestScore && m.set != best.set) tie = true;
            }
            if (bestScore > 0) return tie ? null : best;

            // 3) один матеріал на модель → у всі слоти. Але якщо в папці є набір без кольору (Glass_rough лампи),
            //    значить, у моделі є ще одна поверхня, і вгадувати, котрий слот її, не можна → без пари, вручну.
            if (plan.materials.Count == 1 && !plan.textures.sets.Any(s => !s.IsMaterial))
                return plan.materials[0];
            return null;
        }

        static int Score(string setKey, string slotKey)
        {
            if (setKey == "" || slotKey == "") return 0;
            if (setKey == slotKey) return 3;
            if (setKey.EndsWith(slotKey) || slotKey.EndsWith(setKey)) return 2;
            if (setKey.Contains(slotKey) || slotKey.Contains(setKey)) return 1;
            return 0;
        }

        // ===== Звіт у Console (Claude читає його в Editor.log) =====
        public string Report(bool applied)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[AssetSetup] {(applied ? "APPLY" : "PREVIEW")} {folder} — матеріалів: {materials.Count} " +
                          $"(нових: {materials.Count(m => m.existing == null)}), моделей: {models.Count}");
            foreach (var mp in materials)
            {
                var s = mp.set;
                string what = mp.existing != null
                    ? $"оновити {mp.path}" + (mp.renameTo != null ? $" → перейменувати в {mp.renameTo}" : "")
                    : $"створити {mp.path}";
                sb.AppendLine($"  Матеріал «{s.name}»: {what}");
                sb.AppendLine("    текстури: " + string.Join(", ", s.maps.Select(kv => $"{kv.Key}={Path.GetFileName(kv.Value.path)}")));

                var notes = new List<string>();
                if (NeedsMask(s)) notes.Add($"Mask (Metallic + AO + Smoothness) → {Path.GetFileName(GeneratedPath(s, TextureClassifier.MaskSuffix))}");
                else notes.Add("Smoothness без карти — лишається числом");
                if (s.Has(TexRole.Opacity)) notes.Add("Opacity → альфа кольору, Alpha Clip, дві сторони");
                if (!s.Has(TexRole.Normal) && s.Has(TexRole.Bump)) notes.Add("Bump → Normal (з відтінків сірого)");
                if (s.Has(TexRole.Normal) && TextureClassifier.IsDirectX(s.PathOf(TexRole.Normal)))
                    notes.Add("⚠ Normal у форматі DirectX — рельєф може бути навиворіт");
                if (s.Has(TexRole.Height)) notes.Add("Height пропущено (parallax)");
                if (s.Has(TexRole.Specular)) notes.Add("Specular пропущено");
                if (s.Has(TexRole.Emission)) notes.Add("Emission пропущено");
                if (mp.keepBase != null) notes.Add($"колір лишаю свій: {Path.GetFileName(mp.keepBase)}");
                notes.AddRange(s.notes);
                foreach (var e in s.extra) notes.Add($"запасна {e.role}: {Path.GetFileName(e.path)}");
                sb.AppendLine("    " + string.Join("; ", notes));
            }
            foreach (var s in textures.sets.Where(x => !x.IsMaterial))
                sb.AppendLine($"  ⚠ Набір «{s.name}» без кольору — пропущено: {string.Join(", ", s.maps.Values.Select(v => Path.GetFileName(v.path)))}");
            foreach (var t in textures.unknown) sb.AppendLine($"  ⚠ Не впізнав: {Path.GetFileName(t.path)}");
            foreach (var p in skippedMaterials) sb.AppendLine($"  Пропущено матеріал, не URP Lit: {p}");
            foreach (var p in unusedMaterials) sb.AppendLine($"  ⚠ Матеріал без набору текстур (не чіпаю): {p}");
            foreach (var m in models)
            {
                string name = Path.GetFileName(m.path);
                if (m.skipReason != null) sb.AppendLine($"  Модель {name}: {m.skipReason}");
                else if (m.enableMaterialImport) sb.AppendLine($"  Модель {name}: увімкну імпорт матеріалів, слоти призначу після цього");
                else sb.AppendLine($"  Модель {name}: " + (m.remaps.Count > 0 ? string.Join(", ", m.remaps.Select(r => $"{r.slot} → {r.mat.set.name}")) : "слотів не знайдено") +
                                   (m.unmatched.Count > 0 ? $"; ⚠ без пари: {string.Join(", ", m.unmatched)}" : ""));
            }
            sb.AppendLine("  Імпорт текстур: sRGB / Normal виправляю там, де треба. Max Size не змінюю.");
            return sb.ToString();
        }
    }
}
