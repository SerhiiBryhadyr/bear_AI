using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// =====================================================================
// BearThreatStatesBuilder - builds the "Threat" group in the bear's
// Animator Controller (25.11, D-76). Editor only.
//
// The face-off shows threats with whole-body clips that play once:
// rearing up, the angry stance, a paw slam, a lunge... Clicking them
// into the Animator by hand is slow and easy to get wrong, so this
// builds them from the list below (Actions). Run it again after
// changing the list: the old "Threat" group is removed and built anew.
//
// Inside the group every action is a state named after what it does
// (Lunge, PawSlam...). The code plays it by that name, never by the
// clip's name (D-08: another clip = a change in this list, not in the
// code). When an action ends, it goes on by itself: to the next action
// (HindUp -> HindLook -> HindDown), to the angry stance (AngryIdle,
// it loops there until the code ends it) or back to Locomotion.
//
// Speed: all clips already play x1.26 longer (BearAnimator, the bear is
// bigger than the asset's). The parameter ThreatSpeed (default 1)
// multiplies only these states: 1 = as now, 1.26 = the clip's own speed.
// Set by BearThreatAnimator - tune it in Play after the test (25.11).
//
// Use: select the bear in the scene, Tools -> Bear -> Build Threat
// States. Not in Play mode. Commit first - the controller file changes.
// =====================================================================
public static class BearThreatStatesBuilder
{
    private const string GroupName = "Threat";
    private const string Locomotion = "Locomotion";
    private const string SpeedParam = "ThreatSpeed";
    private const float Blend = 0.25f;                      // crossfade into the next state (s)

    // One action: the state's name, its clip, where it goes when the clip
    // ends ("" = plays again - a loop), and where in the clip (s) it hands
    // over (0 = at the end).
    private struct Action
    {
        public readonly string state, clip, next;
        public readonly float endAt;
        public Action(string state, string clip, string next, float endAt = 0f)
        {
            this.state = state;
            this.clip = clip;
            this.next = next;
            this.endAt = endAt;
        }
    }

    private static readonly Action[] Actions =
    {
        // Level 0 - rears up: to see him better and to look bigger. Stands
        // ~2 s (Idle_01 looks straight; Idle_02 turns the body away), goes
        // to Locomotion as soon as the stance is back (~2.4 s of 3.33).
        new Action("HindUp",    "Trans_Stand_to_StandHind",  "HindLook"),
        new Action("HindLook",  "StandHind_Idle_01",         "HindDown", 2.0f),
        new Action("HindDown",  "Trans_StandHind_to_Stand",  Locomotion, 2.4f),
        // Startled (close encounter, later)
        new Action("Startle",   "Hit_Stand_F01",             Locomotion),
        // Level 1 - the angry stance: stays in AngryIdle until the code ends it
        new Action("AngryIn",   "Trans_Stand_to_StandAngry", "AngryIdle"),
        new Action("AngryIdle", "StandAngry_Breathing_01",   ""),
        new Action("AngryPeer", "StandAngry_Breathing_02",   "AngryIdle"),
        new Action("AngryOut",  "Trans_StandAngry_to_Stand", Locomotion),
        // Levels 2-3 - threats; they end in the angry stance.
        // (02_High = 01_High in our FBX - checked 25.11, not used.)
        new Action("PawSlam",   "Attack_StandAngry_01_High", "AngryIdle"),
        new Action("Lunge",     "Attack_StandAngry_01_Low",  "AngryIdle"),
    };

    [MenuItem("Tools/Bear/Build Threat States")]
    private static void Build()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[Threat] Stop Play mode first.");
            return;
        }

        Animator animator = BearClipSpeeds.FindAnimator(Selection.activeGameObject);
        AnimatorController controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        if (controller == null)
        {
            Debug.LogWarning("[Threat] Select the bear (the object with the Animator and its controller) in the Hierarchy.");
            return;
        }

        AnimatorStateMachine root = controller.layers[0].stateMachine;
        AnimatorState locomotion = root.states.Select(s => s.state).FirstOrDefault(s => s.name == Locomotion);
        if (locomotion == null)
        {
            Debug.LogError("[Threat] State '" + Locomotion + "' not found in the Base Layer of " + controller.name + ".");
            return;
        }

        if (!EditorUtility.DisplayDialog("Build Threat States",
                "Rebuild the '" + GroupName + "' group in " + controller.name + "?\n\nThe controller file changes - commit first.",
                "Build", "Cancel"))
            return;

        Dictionary<string, AnimationClip> clips = ModelClips(controller);

        // The speed knob of these states (default 1 = as the walk).
        if (!controller.parameters.Any(p => p.name == SpeedParam))
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = SpeedParam,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f
            });

        // 1. The old group out (a rebuild starts clean).
        foreach (ChildAnimatorStateMachine child in root.stateMachines)
            if (child.stateMachine.name == GroupName)
                root.RemoveStateMachine(child.stateMachine);

        // 2. One state per action.
        AnimatorStateMachine group = root.AddStateMachine(GroupName, new Vector3(450f, 250f, 0f));
        var states = new Dictionary<string, AnimatorState>();
        var missing = new List<string>();

        foreach (Action action in Actions)
        {
            if (!clips.TryGetValue(action.clip, out AnimationClip clip))
            {
                missing.Add(action.clip);
                continue;
            }

            int n = states.Count;
            AnimatorState state = group.AddState(action.state, new Vector3(250f + (n % 3) * 230f, 60f + (n / 3) * 70f, 0f));
            state.motion = clip;
            state.writeDefaultValues = locomotion.writeDefaultValues;
            state.speedParameterActive = true;
            state.speedParameter = SpeedParam;
            states[action.state] = state;
        }

        // 3. Where each one goes when its clip ends.
        foreach (Action action in Actions)
        {
            if (!states.TryGetValue(action.state, out AnimatorState from))
                continue;

            AnimatorState to;
            if (action.next == "")
                to = from;                                  // loop
            else if (!states.TryGetValue(action.next, out to))
                to = locomotion;                            // Locomotion, or the next one is missing

            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = ExitTime((AnimationClip)from.motion, action.endAt);
            transition.hasFixedDuration = true;
            transition.duration = Blend;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("[Threat] built '" + GroupName + "' in " + controller.name + ": " + states.Count + " of " + Actions.Length +
                  " actions (" + string.Join(", ", states.Keys) + ")" +
                  (missing.Count > 0 ? ". Clips NOT found: " + string.Join(", ", missing) : ""));
    }

    // The blend into the next state ends exactly at endAt (0 = the clip's end).
    private static float ExitTime(AnimationClip clip, float endAt)
    {
        if (clip == null || clip.length <= 0f)
            return 0.9f;
        float end = endAt > 0f ? Mathf.Min(endAt, clip.length) : clip.length;
        return Mathf.Clamp((end - Blend) / clip.length, 0.05f, 0.99f);
    }

    // All clips of the model file the controller's clips come from, by name.
    private static Dictionary<string, AnimationClip> ModelClips(AnimatorController controller)
    {
        var result = new Dictionary<string, AnimationClip>();
        AnimationClip any = controller.animationClips.FirstOrDefault(c => c != null);
        if (any == null)
            return result;

        foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(any)).OfType<AnimationClip>())
            if (!clip.name.StartsWith("__preview__") && !result.ContainsKey(clip.name))
                result[clip.name] = clip;

        return result;
    }
}
