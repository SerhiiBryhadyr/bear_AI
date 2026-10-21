using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · Розпізнавання текстур за назвами файлів.
    // Кожна текстура отримує РОЛЬ (BaseColor, Normal, Roughness…) і ПРЕФІКС.
    // Текстури з однаковим префіксом — один НАБІР (= один матеріал):
    //   Bucket_low_wood_BaseColor + Bucket_low_wood_Normal → набір "Bucket_low_wood".
    // Нічого в проєкті не змінює — лише читає назви.
    // ============================================================

    public enum TexRole { Unknown, BaseColor, Normal, Bump, Roughness, Smoothness, Metallic, AO, Opacity, Height, Specular, Emission, PackedORM }

    public class TexInfo
    {
        public string path;        // шлях у проєкті: Assets/...
        public string prefixName;  // префікс як у назві файлу: "Bucket_low_wood"
        public string key;         // префікс для порівняння: "bucketlowwood"
        public TexRole role;
    }

    public class TextureSet
    {
        public string key;
        public string name;   // з нього — назва матеріалу і згенерованих PNG
        public readonly Dictionary<TexRole, TexInfo> maps = new Dictionary<TexRole, TexInfo>();
        public readonly List<TexInfo> extra = new List<TexInfo>();   // друга текстура з тією ж роллю — не використовується
        public readonly List<string> notes = new List<string>();     // що варто перевірити очима

        public bool Has(TexRole r) => maps.ContainsKey(r);
        public string PathOf(TexRole r) => maps.TryGetValue(r, out var t) ? t.path : null;
        public bool IsMaterial => Has(TexRole.BaseColor);   // без кольору матеріал не робимо
    }

    public class ClassifyResult
    {
        public readonly List<TextureSet> sets = new List<TextureSet>();
        public readonly List<TexInfo> unknown = new List<TexInfo>();   // назву не впізнано
        public readonly List<string> ignored = new List<string>();     // свідомо пропущені
    }

    public static class TextureClassifier
    {
        public const string MaskSuffix = "_Mask";            // згенерована: Metallic / AO / Smoothness
        public const string BaseAlphaSuffix = "_BaseAlpha";  // згенерована: колір + Opacity

        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr", ".bmp", ".hdr", ".gif" };

        // ===== Суфікси → роль. Довші перевіряються першими ("normal_opengl" раніше за "normal") =====
        static readonly (string token, TexRole role)[] Tokens = new (string, TexRole)[]
        {
            ("ao_metallic_roughness", TexRole.PackedORM), ("occlusion_roughness_metallic", TexRole.PackedORM),
            ("occlusionroughnessmetallic", TexRole.PackedORM), ("orm", TexRole.PackedORM), ("arm", TexRole.PackedORM),

            ("basecolor", TexRole.BaseColor), ("base_color", TexRole.BaseColor), ("albedom", TexRole.BaseColor),
            ("albedo", TexRole.BaseColor), ("diffuse", TexRole.BaseColor), ("diff", TexRole.BaseColor),
            ("color", TexRole.BaseColor), ("col", TexRole.BaseColor),

            ("normalogl16", TexRole.Normal), ("normalgl16", TexRole.Normal), ("normal16", TexRole.Normal),
            ("normalogl", TexRole.Normal), ("normal_ogl", TexRole.Normal),
            ("normal_opengl", TexRole.Normal), ("normal_directx", TexRole.Normal), ("normal_gl", TexRole.Normal),
            ("normal_dx", TexRole.Normal), ("normalgl", TexRole.Normal), ("normaldx", TexRole.Normal),
            ("normal", TexRole.Normal), ("norm", TexRole.Normal), ("nrm", TexRole.Normal), ("nor", TexRole.Normal), ("n", TexRole.Normal),

            ("bump", TexRole.Bump),
            ("roughness", TexRole.Roughness), ("rough", TexRole.Roughness),
            ("smoothness", TexRole.Smoothness), ("glossiness", TexRole.Smoothness), ("gloss", TexRole.Smoothness),
            ("metallic", TexRole.Metallic), ("metalness", TexRole.Metallic), ("metal", TexRole.Metallic),
            ("ambient_occlusion", TexRole.AO), ("ambientocclusion", TexRole.AO), ("occlusion", TexRole.AO), ("ao", TexRole.AO),
            ("opacity", TexRole.Opacity), ("alpha", TexRole.Opacity), ("transparency", TexRole.Opacity),
            ("displacement", TexRole.Height), ("height", TexRole.Height), ("disp", TexRole.Height),
            ("specular", TexRole.Specular), ("spec", TexRole.Specular),
            ("emission", TexRole.Emission), ("emissive", TexRole.Emission),
        }.OrderByDescending(t => t.Item1.Length).ToArray();

        // Суфікси, які впізнаємо і без розділювача, за великою літерою: "BochkaAO", "RoofNormal".
        // Короткі й неоднозначні ("n", "col", "metal") — лише з розділювачем.
        static readonly HashSet<string> CamelTokens = new HashSet<string>
        { "ao", "basecolor", "albedo", "diffuse", "normal", "roughness", "metallic", "metalness",
          "occlusion", "opacity", "height", "smoothness", "glossiness", "specular", "emission" };

        // UDIM-номер тайла і копії Unity в кінці назви: "_BaseColor.1001", "_BaseColor.1001 2"
        static readonly Regex Udim = new Regex(@"[._]1\d{3}( \d+)?$");

        public static bool IsImage(string path) => ImageExt.Contains(Path.GetExtension(path).ToLowerInvariant());

        // "PicnicTable_AO.tga.png" → "PicnicTable_AO" (знімає всі розширення картинок)
        public static string BareName(string path)
        {
            string name = Path.GetFileName(path);
            string ext;
            while ((ext = Path.GetExtension(name).ToLowerInvariant()) != "" && ImageExt.Contains(ext))
                name = name.Substring(0, name.Length - ext.Length);
            return Udim.Replace(name, "");
        }

        // Для порівняння назв: лише літери й цифри, малими ("Bucket_low_wood" → "bucketlowwood")
        public static string Key(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        // ===== Роль і префікс однієї текстури =====
        public static TexInfo Classify(string path)
        {
            string bare = BareName(path);
            string lower = bare.ToLowerInvariant();
            foreach (var (token, role) in Tokens)
            {
                if (lower == token) return Make(path, "", role);                  // файл "normal.jpg"
                if (lower.Length > token.Length && lower.EndsWith(token))
                {
                    int cut = lower.Length - token.Length;
                    char sep = lower[cut - 1];                                     // перед суфіксом — розділювач
                    if (sep == '_' || sep == '-' || sep == ' ' || sep == '.')
                        return Make(path, bare.Substring(0, cut - 1), role);
                    if (CamelTokens.Contains(token) && char.IsUpper(bare[cut]) &&  // або межа CamelCase: "Bochka|AO"
                        (char.IsLower(bare[cut - 1]) || char.IsDigit(bare[cut - 1])))
                        return Make(path, bare.Substring(0, cut), role);
                }
            }
            return Make(path, bare, TexRole.Unknown);
        }

        static TexInfo Make(string path, string prefix, TexRole role)
        {
            prefix = prefix.Trim('_', '-', ' ', '.');
            return new TexInfo { path = path, prefixName = prefix, key = Key(prefix), role = role };
        }

        // ===== Розкласти всі текстури папки на набори =====
        // forcedBase — текстури, які наявні матеріали вже використовують як колір.
        // Рятує файли без суфікса: "iron.jpg", "TrashCan.png".
        public static ClassifyResult Build(IEnumerable<string> paths, HashSet<string> forcedBase)
        {
            var result = new ClassifyResult();
            var byKey = new Dictionary<string, TextureSet>();
            var unknown = new List<TexInfo>();

            foreach (string p in paths)
            {
                string bare = BareName(p);
                if (bare.ToLowerInvariant().Contains("internal_ground") ||          // підкладка-тінь зі Sketchfab
                    Path.GetExtension(p).ToLowerInvariant() == ".hdr" ||             // HDRI-небо, не матеріал
                    bare.EndsWith(MaskSuffix) || bare.EndsWith(BaseAlphaSuffix))     // уже згенеровані AssetSetup
                { result.ignored.Add(p); continue; }

                TexInfo t = Classify(p);
                if (t.role == TexRole.Unknown && forcedBase.Contains(p)) t.role = TexRole.BaseColor;
                if (t.role == TexRole.Unknown) { unknown.Add(t); continue; }
                Add(byKey, t);
            }

            // Файл без суфікса з назвою = префікс набору:
            //   у набору немає кольору → це і є колір ("TrashCan" + "TrashCan_N");
            //   колір уже є → це копія кольору (".jpg" і ".jpeg" грядки) — у запас.
            foreach (TexInfo t in unknown)
            {
                if (!byKey.TryGetValue(t.key, out var s)) { result.unknown.Add(t); continue; }
                t.role = TexRole.BaseColor;
                Put(s, t);
            }

            // Набір без кольору приєднати до наборів, з якими збігається префікс:
            //   "GrayscaleTire01_Low_Tire01_AO" → "Tire01_Low_Tire01";
            //   "Wood1_Normal" → і "Wood1a", і "Wood1b" (спільна карта на два кольори).
            // Набір отримує лише ті ролі, яких у нього ще немає. Без збігів — лишається окремо.
            var bases = byKey.Values.Where(s => s.IsMaterial).ToList();
            foreach (var orphan in byKey.Values.Where(s => !s.IsMaterial).ToList())
            {
                if (orphan.key == "") continue;
                var match = bases.Where(b => b.key != "" && (orphan.key.Contains(b.key) || b.key.Contains(orphan.key))).ToList();
                if (match.Count == 0) continue;
                foreach (var b in match)
                    foreach (var t in orphan.maps.Values)
                        if (!b.Has(t.role)) b.maps[t.role] = t;
                byKey.Remove(orphan.key);
            }

            // Лишився набір без кольору, а колір у папці один, і ролі не перетинаються →
            // скоріше за все друкарська помилка автора ("Bockha_BaseColor" + "Bochka_Normal"). Приєднуємо з позначкою.
            bases = byKey.Values.Where(s => s.IsMaterial).ToList();
            if (bases.Count == 1)
                foreach (var orphan in byKey.Values.Where(s => !s.IsMaterial).ToList())
                {
                    if (orphan.maps.Keys.Any(bases[0].Has)) continue;      // скло лампи: Roughness у обох — не чіпаємо
                    foreach (var t in orphan.maps.Values) bases[0].maps[t.role] = t;
                    bases[0].notes.Add($"ℹ приєднано «{orphan.maps.Values.First().prefixName}» (назва відрізняється) — перевір очима");
                    byKey.Remove(orphan.key);
                }

            foreach (var s in byKey.Values)
                s.name = (s.maps.TryGetValue(TexRole.BaseColor, out var b) ? b : s.maps.Values.First()).prefixName;
            result.sets.AddRange(byKey.Values.OrderBy(s => s.key));
            return result;
        }

        static void Add(Dictionary<string, TextureSet> byKey, TexInfo t)
        {
            if (!byKey.TryGetValue(t.key, out var s)) byKey[t.key] = s = new TextureSet { key = t.key };
            Put(s, t);
        }

        static void Put(TextureSet s, TexInfo t)
        {
            if (!s.maps.TryGetValue(t.role, out var cur)) { s.maps[t.role] = t; return; }
            // Кілька Normal: краща — OpenGL і 8-біт; DirectX і 16-біт — у запас
            if (t.role == TexRole.Normal && NormalRank(t.path) < NormalRank(cur.path)) { s.maps[t.role] = t; s.extra.Add(cur); }
            else s.extra.Add(t);
        }

        static int NormalRank(string path) =>
            (IsDirectX(path) ? 2 : 0) + (BareName(path).EndsWith("16") ? 1 : 0);

        // Normal у форматі DirectX: зелений канал перевернутий — рельєф у Unity виглядатиме навиворіт
        public static bool IsDirectX(string path)
        {
            string l = BareName(path).ToLowerInvariant();
            return l.EndsWith("normaldx") || l.EndsWith("normal_dx") || l.EndsWith("normal_directx");
        }
    }
}
