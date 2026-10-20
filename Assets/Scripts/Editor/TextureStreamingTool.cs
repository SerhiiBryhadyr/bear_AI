using UnityEditor;
using UnityEngine;

// =====================================================================
// TextureStreamingTool (editor only - not included in the game build)
// Menu: Tools -> Textures -> Enable Streaming Mipmaps
// Turns on "Streaming Mipmaps" for every texture in Assets where it
// makes sense, so Texture Streaming (Quality settings) can keep only the
// needed detail in memory: close objects stay sharp, far ones use less.
// =====================================================================
public static class TextureStreamingTool
{
    [MenuItem("Tools/Textures/Enable Streaming Mipmaps")]
    private static void EnableStreamingMipmaps()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });

        int changed = 0;
        int alreadyOn = 0;
        int skipped = 0;
        bool cancelled = false;

        // ---------------------------------------------------------------
        // Batch mode: Unity collects all changes and reimports them at
        // the end in one go (much faster than one by one).
        // ---------------------------------------------------------------
        AssetDatabase.StartAssetEditing();

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Enable Streaming Mipmaps",
                        $"{i + 1} / {guids.Length}  {path}",
                        (float)i / guids.Length))
                {
                    cancelled = true;
                    break;
                }

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (!ShouldStream(importer))
                {
                    skipped++;
                    continue;
                }

                if (importer.streamingMipmaps)
                {
                    alreadyOn++;
                    continue;
                }

                importer.streamingMipmaps = true;
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();   // reimport of changed textures happens here
            EditorUtility.ClearProgressBar();
        }

        Debug.Log(
            $"[TextureStreamingTool] {(cancelled ? "CANCELLED. " : "")}" +
            $"Changed: {changed}, already on: {alreadyOn}, skipped: {skipped}, total: {guids.Length}. " +
            "Remember: Project Settings -> Quality -> Texture Streaming must be enabled."
        );
    }

    // ---------------------------------------------------------------
    // Only normal 3D-world textures with mipmaps. Sprites, UI, cursors,
    // cookies, lightmaps, embedded textures (no importer) are left as is.
    // ---------------------------------------------------------------
    private static bool ShouldStream(TextureImporter importer)
    {
        if (importer == null)
            return false;

        if (!importer.mipmapEnabled)
            return false;

        switch (importer.textureType)
        {
            case TextureImporterType.Default:
            case TextureImporterType.NormalMap:
            case TextureImporterType.SingleChannel:
                return true;

            default:
                return false;
        }
    }
}
