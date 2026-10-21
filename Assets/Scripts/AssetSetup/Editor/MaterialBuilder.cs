using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AssetSetupTool
{
    // ============================================================
    // AssetSetup · Налаштування матеріалу URP Lit.
    // Ставить текстури в слоти і вмикає keywords — те саме, що робить
    // Inspector, коли перетягуєш текстури руками.
    // Слот, для якого нової текстури немає, НЕ чіпаємо — крім випадку, коли
    // там стоїть наша ж текстура не на своєму місці (Bump у слоті Metallic) → clear.
    // ============================================================
    public static class MaterialBuilder
    {
        public static void Configure(Material m, Texture2D baseMap, Texture2D normal, bool clearNormal,
                                     Texture2D mask, bool clearMask, bool maskHasAO, bool clearOcclusion, bool alphaClip)
        {
            var lit = Shader.Find(AssetSetupPlan.LitShader);
            if (m.shader != lit) m.shader = lit;

            // ===== Колір =====
            m.SetTexture("_BaseMap", baseMap);
            m.SetTexture("_MainTex", baseMap);                 // старе ім'я слота — деякі системи читають його

            // ===== Normal =====
            if (normal != null || clearNormal)
            {
                m.SetTexture("_BumpMap", normal);
                SetKeyword(m, "_NORMALMAP", normal != null);
            }

            // ===== Metallic + Smoothness (альфа Mask) =====
            if (mask != null || clearMask)
            {
                m.SetFloat("_WorkflowMode", 1f);               // режим Metallic, не Specular
                SetKeyword(m, "_SPECULAR_SETUP", false);
                m.SetTexture("_MetallicGlossMap", mask);       // null прибирає неправильну текстуру (як Bump у криниці)
                SetKeyword(m, "_METALLICSPECGLOSSMAP", mask != null);
            }
            if (mask != null)
            {
                m.SetFloat("_Smoothness", 1f);                 // множник альфи: 1 = рівно як у текстурі
                m.SetFloat("_SmoothnessTextureChannel", 0f);   // 0 = брати з альфи Metallic
                SetKeyword(m, "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A", false);
            }

            // ===== AO (зелений канал тієї ж Mask) =====
            bool setAO = mask != null && maskHasAO;
            if (setAO || clearOcclusion)
            {
                m.SetTexture("_OcclusionMap", setAO ? mask : null);
                SetKeyword(m, "_OCCLUSIONMAP", setAO);
            }
            if (setAO) m.SetFloat("_OcclusionStrength", 1f);

            // ===== Вирізання прозорих місць (Opacity: мітла, листя) =====
            if (alphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                SetKeyword(m, "_ALPHATEST_ON", true);
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetFloat("_Cull", (float)CullMode.Off);      // видно з обох боків
            }
            EditorUtility.SetDirty(m);
        }

        static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }
    }
}
