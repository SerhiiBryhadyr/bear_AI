using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// =====================================================================
// BearSoundsSetup - puts all the bear's sounds into its components in
// one click (26.11). Clips by name from Assets/Audio/Bear (made 25-26.11,
// Audio_Licenses.md). Missing components (BearThreatDisplay,
// BearPresenceSounds) are added. Undo works; save the scene after.
//
// Use: select the bear in the scene, Tools -> Bear -> Assign Sounds.
// Not in Play mode (changes in Play are lost).
// =====================================================================
public static class BearSoundsSetup
{
    private const string Folder = "Assets/Audio/Bear/";

    [MenuItem("Tools/Bear/Assign Sounds")]
    private static void Assign()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[Sounds] Stop Play mode first - changes in Play are lost.");
            return;
        }

        GameObject bear = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<BearAI>()?.gameObject : null;
        if (bear == null)
        {
            Debug.LogWarning("[Sounds] Select the bear (the object with BearAI) in the Hierarchy.");
            return;
        }

        var missing = new List<string>();
        AudioClip C(string name)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + name + ".wav");
            if (clip == null)
                missing.Add(name);
            return clip;
        }
        AudioClip[] Many(params string[] names) => names.Select(C).Where(c => c != null).ToArray();

        // Threats in the face-off (D-76).
        BearThreatDisplay display = Get<BearThreatDisplay>(bear);
        display.huffClips = Many("Bear_Huff_01", "Bear_Huff_02");
        display.growlClips = Many("Bear_GrowlLow_01", "Bear_Growl_01", "Bear_Growl_02");
        display.roarClips = Many("Bear_Roar_01", "Bear_Roar_02", "Bear_GrowlRoar_01");
        display.attackRoarClips = ByPrefix("Bear_RoarAttack_");      // sharp roars (26.11); none - ordinary
        display.jawClip = C("Bear_JawClack_01");
        display.thudClip = C("Bear_PawThud_01");

        // The face-off's last warning; the close encounter.
        BearFaceOffState faceOff = bear.GetComponent<BearFaceOffState>();
        if (faceOff != null) { Undo.RecordObject(faceOff, "Assign bear sounds"); faceOff.warningRoarClip = C("Bear_Roar_01"); }
        BearCloseEncounterState close = bear.GetComponent<BearCloseEncounterState>();
        if (close != null)
        {
            Undo.RecordObject(close, "Assign bear sounds");
            close.huffClip = C("Bear_FearWoof_Low80");      // startled - an involuntary woof
            close.fearClip = C("Bear_Startle_01");          // startled and runs off
            close.warningClip = C("Bear_Roar_02");
        }

        // Breathing, sniffing, panting (D-34). Single breaths - every file
        // with the prefix, so new variants are picked up by running it again.
        BearPresenceSounds presence = Get<BearPresenceSounds>(bear);
        presence.breaths = ByPrefix("Bear_Breath_");
        presence.deepBreaths = ByPrefix("Bear_BreathDeep_");
        presence.rumbleBreaths = ByPrefix("Bear_BreathRumble_");
        presence.breathLoop = C("Bear_BreathCalm_Loop_01");
        presence.pant = C("Bear_Pant_01");
        presence.afterRoar = C("Bear_BreathAfterRoar_01");
        presence.calmSounds = Many("Bear_Sniff_01", "Bear_Grumble_01");
        presence.calmRoars = Many("Bear_Roar_01", "Bear_Roar_02", "Bear_GrowlRoar_01");

        EditorUtility.SetDirty(bear);
        EditorSceneManager.MarkSceneDirty(bear.scene);
        Debug.Log("[Sounds] attack roars " + display.attackRoarClips.Length + ", breaths " + presence.breaths.Length + ", deep " + presence.deepBreaths.Length + ", rumble " + presence.rumbleBreaths.Length);
        Debug.Log("[Sounds] assigned to " + bear.name + (missing.Count > 0 ? " - NOT found in " + Folder + ": " + string.Join(", ", missing) : " - all found") + ". Save the scene.");
    }

    // All clips in Folder whose names start with the prefix, sorted.
    private static AudioClip[] ByPrefix(string prefix)
    {
        return AssetDatabase.FindAssets(prefix + " t:AudioClip", new[] { Folder.TrimEnd('/') })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith(prefix))
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
            .Where(c => c != null)
            .ToArray();
    }

    // The component on the bear (added if missing), ready for Undo.
    private static T Get<T>(GameObject bear) where T : Component
    {
        T c = bear.GetComponent<T>();
        if (c == null)
            c = Undo.AddComponent<T>(bear);
        Undo.RecordObject(c, "Assign bear sounds");
        EditorUtility.SetDirty(c);
        return c;
    }
}
