using System.IO;
using UnityEditor;
using UnityEngine;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · Пакування каналів у нові PNG.
    // URP Lit не має слота Roughness: Smoothness він бере з АЛЬФИ Metallic-текстури,
    // а AO — із ЗЕЛЕНОГО каналу Occlusion-текстури. Тому збираємо одну "Mask":
    //     R = Metallic   G = AO   B = 0   A = Smoothness (= 1 − Roughness)
    // і ставимо її в обидва слоти — одна текстура замість трьох.
    // Opacity кладемо в альфу кольорової текстури ("BaseAlpha").
    // Оригінальні файли не змінюються — пікселі читаються прямо з диска.
    // ============================================================
    public static class TexturePacker
    {
        // ===== Джерело: пікселі однієї текстури; уміє читати, навіть якщо розмір інший =====
        class Src
        {
            readonly Texture2D tex;
            readonly Color32[] px;
            public readonly int w, h;

            public Src(Texture2D t) { tex = t; px = t.GetPixels32(); w = t.width; h = t.height; }

            // Значення каналу (0 = R … 3 = A) у точці x,y картинки розміром W×H
            public float Get(int x, int y, int W, int H, int ch)
            {
                if (W == w && H == h) return px[y * w + x][ch] / 255f;
                return tex.GetPixelBilinear((x + 0.5f) / W, (y + 0.5f) / H)[ch];   // інший розмір — беремо найближче
            }

            public void Free() => Object.DestroyImmediate(tex);
        }

        // ===== Mask: R = Metallic, G = AO, A = Smoothness =====
        // fallbackSmooth / fallbackMetal — якщо карти немає, беремо число з матеріалу
        public static void WriteMask(TextureSet set, string outPath, float fallbackSmooth, float fallbackMetal)
        {
            Src metal = Load(set.PathOf(TexRole.Metallic));
            Src rough = Load(set.PathOf(TexRole.Roughness));
            Src smooth = Load(set.PathOf(TexRole.Smoothness));
            Src ao = Load(set.PathOf(TexRole.AO));
            Src orm = Load(set.PathOf(TexRole.PackedORM));       // glTF: R = AO, G = Roughness, B = Metallic
            var all = new[] { metal, rough, smooth, ao, orm };

            int W = 0, H = 0;                                    // розмір — найбільший серед джерел
            foreach (var s in all) if (s != null && s.w * s.h > W * H) { W = s.w; H = s.h; }

            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float m = metal != null ? metal.Get(x, y, W, H, 0)
                        : orm != null ? orm.Get(x, y, W, H, 2) : fallbackMetal;
                float a = ao != null ? ao.Get(x, y, W, H, 0)
                        : orm != null ? orm.Get(x, y, W, H, 0) : 1f;          // 1 = без затінення
                float sm = rough != null ? 1f - rough.Get(x, y, W, H, 0)        // головне: Roughness → Smoothness
                         : smooth != null ? smooth.Get(x, y, W, H, 0)
                         : orm != null ? 1f - orm.Get(x, y, W, H, 1) : fallbackSmooth;
                outPx[y * W + x] = new Color32(To8(m), To8(a), 0, To8(sm));
            }
            Save(outPx, W, H, outPath);
            foreach (var s in all) s?.Free();
        }

        // ===== BaseAlpha: колір з BaseColor, альфа з Opacity =====
        public static void WriteBaseAlpha(TextureSet set, string outPath)
        {
            Src col = Load(set.PathOf(TexRole.BaseColor));
            Src op = Load(set.PathOf(TexRole.Opacity));
            int W = col.w, H = col.h;

            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                outPx[y * W + x] = new Color32(To8(col.Get(x, y, W, H, 0)), To8(col.Get(x, y, W, H, 1)),
                                               To8(col.Get(x, y, W, H, 2)), To8(op.Get(x, y, W, H, 0)));
            Save(outPx, W, H, outPath);
            col.Free(); op.Free();
        }

        // ===== Читання пікселів =====
        static Src Load(string assetPath)
        {
            if (assetPath == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            string ext = Path.GetExtension(assetPath).ToLowerInvariant();

            // PNG / JPG — прямо з файлу, без жодних змін у проєкті
            if ((ext == ".png" || ext == ".jpg" || ext == ".jpeg") && t.LoadImage(File.ReadAllBytes(assetPath)))
                return new Src(t);

            // Інші формати (tga, tif, psd…): на мить робимо імпорт читабельним, потім повертаємо як було
            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            bool wasReadable = imp.isReadable;
            var wasCompression = imp.textureCompression;
            imp.isReadable = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            try
            {
                var src = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                t.Reinitialize(src.width, src.height);
                t.SetPixels32(src.GetPixels32());
                t.Apply();
                return new Src(t);
            }
            finally
            {
                imp.isReadable = wasReadable;
                imp.textureCompression = wasCompression;
                imp.SaveAndReimport();
            }
        }

        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        static void Save(Color32[] px, int w, int h, string outPath)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            t.SetPixels32(px);
            t.Apply();
            File.WriteAllBytes(outPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }
    }
}
