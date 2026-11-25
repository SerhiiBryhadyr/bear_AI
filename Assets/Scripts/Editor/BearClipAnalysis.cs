using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// =====================================================================
// BearClipAnalysis - what every clip of the bear's model does, in
// numbers (24.11; for Bear_Animations.md and D-76). Editor only. The
// scene is not changed: clips are sampled in Animation Mode and the
// pose comes back.
//
// For EVERY clip in the model's FBX (not only those in the controller):
//   - body (root bone): how far it moves forward / sideways / up inside
//     the clip and whether it comes back to the start. Motion baked into
//     the bone: without Apply Root Motion the mesh drifts away from the
//     object and jumps back when the clip ends;
//   - head (Eyes / RigHead): lowest and highest point and when;
//   - front paws: exact intervals when both are off the ground (on the
//     hind legs, a jump) - where a clip must not be cut;
//   - turn: how the body heading (pelvis -> head) turns (Trans_TurnL/R);
//   - muzzle reach: how far ahead of its start the head gets - a lunge
//     done by the spine, not by the root bone (Attack_StandAngry_*).
// Clips whose names contain one of DetailNames also get a timeline
// every DetailStep s: h head height, f body (root bone) forward,
// r muzzle reach, t turn, UP front paws up.
//
// Use: select the bear in the scene, Tools -> Bear -> Analyze All Clips.
// Not in Play mode. Result - the Console (Claude reads Editor.log).
// =====================================================================
public static class BearClipAnalysis
{
    private const float SampleRate = 30f;       // the clips are 30 frames per second
    private const float PawUp = 0.3f;           // a front paw this high above its lowest point (m) = off the ground
    private const float MinInterval = 0.1f;     // shorter "paws up" moments are steps, not rearing
    private const float DetailStep = 0.2f;
    private static readonly string[] DetailNames = { "StandHind", "StandAngry", "Hit_Stand", "Attack", "Trans_Turn" };

    // One sampled clip.
    private class Samples
    {
        public AnimationClip clip;
        public float dt;
        public Vector3[] body;      // root bone, in the bear's own space (x side, y up, z forward)
        public Vector3[] root;      // the bear object itself (moves only if the clip animates it)
        public float[] head;        // head height above the bear's origin (m)
        public float[] paw0;        // front paw heights (m)
        public float[] paw1;
        public float[] heading;     // body heading, deg (pelvis -> head, flat), 0 = forward
        public float[] reach;       // head forward of the bear's origin (m)
    }

    [MenuItem("Tools/Bear/Analyze All Clips")]
    private static void Analyze()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[ClipAnalysis] Stop Play mode first.");
            return;
        }

        Animator animator = BearClipSpeeds.FindAnimator(Selection.activeGameObject);
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("[ClipAnalysis] Select the bear (the object with the Animator and its controller) in the Hierarchy.");
            return;
        }

        Transform root = animator.transform;
        Transform body = BearClipSpeeds.FindBody(root);
        Transform head = FindByName(root, "Eyes") ?? FindByName(root, "RigHead");
        Transform pelvis = FindByName(root, "RigPelvis") ?? body;
        List<Transform> paws = BearClipSpeeds.FindPaws(root);

        if (head == null || paws.Count < 4)
        {
            Debug.LogWarning("[ClipAnalysis] Could not find the head (Eyes / RigHead) or four paws in the bear's skeleton.");
            return;
        }

        // Front paws: the two farthest forward in the current pose.
        List<Transform> front = paws.OrderByDescending(p => Local(root, p.position).z).Take(2).ToList();

        AnimationClip[] clips = ModelClips(animator);
        if (clips.Length == 0)
        {
            Debug.LogWarning("[ClipAnalysis] No clips found in the model file.");
            return;
        }

        // 1. Sample every clip (the pose comes back afterwards).
        var all = new List<Samples>();
        Vector3 startRoot = root.position;
        AnimationMode.StartAnimationMode();
        try
        {
            foreach (AnimationClip clip in clips)
                all.Add(Sample(root, body, pelvis, head, front, clip, startRoot));
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        // 2. The ground under each front paw = its lowest point in all clips.
        float ground0 = all.Min(s => s.paw0.Min());
        float ground1 = all.Min(s => s.paw1.Min());

        // 3. Summary: one line per clip.
        var report = new StringBuilder();
        report.AppendLine("[ClipAnalysis] " + root.name + ", scale " + root.lossyScale.x.ToString("F2") + ", " + all.Count +
                          " clips. body = " + body.name + ", head = " + head.name + ", front paws = " + front[0].name + ", " + front[1].name +
                          ". Distances in m, times in s. fwd = forward along the bear, + ahead.");

        foreach (Samples s in all)
            report.AppendLine("  " + Summary(s, ground0, ground1));

        Debug.Log(report.ToString());

        // 4. Timelines for the clips that matter for the face-off (D-76).
        foreach (Samples s in all.Where(s => DetailNames.Any(n => s.clip.name.Contains(n))))
            Debug.Log(Timeline(s, ground0, ground1));
    }

    // ---------------------------------------------------------------
    // Frame by frame (30 per second): where the body, head and paws are.
    // ---------------------------------------------------------------
    private static Samples Sample(Transform root, Transform body, Transform pelvis, Transform head, List<Transform> front,
                                  AnimationClip clip, Vector3 startRoot)
    {
        int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * SampleRate));
        var s = new Samples
        {
            clip = clip,
            dt = clip.length / frames,
            body = new Vector3[frames + 1],
            root = new Vector3[frames + 1],
            head = new float[frames + 1],
            paw0 = new float[frames + 1],
            paw1 = new float[frames + 1],
            heading = new float[frames + 1],
            reach = new float[frames + 1]
        };

        for (int f = 0; f <= frames; f++)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(root.gameObject, clip, f * s.dt);
            AnimationMode.EndSampling();

            s.body[f] = Local(root, body.position);
            s.root[f] = root.position - startRoot;
            s.head[f] = Local(root, head.position).y;
            s.paw0[f] = Local(root, front[0].position).y;
            s.paw1[f] = Local(root, front[1].position).y;

            Vector3 headLocal = Local(root, head.position);
            Vector3 spine = headLocal - Local(root, pelvis.position);
            s.heading[f] = Mathf.Atan2(spine.x, spine.z) * Mathf.Rad2Deg;
            s.reach[f] = headLocal.z;
        }

        return s;
    }

    // "Trans_Stand_to_StandHind 3.00 s | body fwd +0.12 at the end (+0.30 max at 1.2) ... | front paws up 1.83-3.00"
    private static string Summary(Samples s, float ground0, float ground1)
    {
        int last = s.body.Length - 1;
        Vector3 start = s.body[0];

        float endFwd = s.body[last].z - start.z;
        int maxF = ArgMax(s.body, v => v.z - start.z);
        int minF = ArgMax(s.body, v => -(v.z - start.z));
        float side = s.body.Max(v => Mathf.Abs(v.x - start.x));
        float up = s.body.Max(v => v.y - start.y);
        bool back = (s.body[last] - start).magnitude < 0.15f;
        float rootMoved = s.root.Max(v => v.magnitude);

        int headLow = ArgMax(s.head, h => -h);
        int headHigh = ArgMax(s.head, h => h);
        float turnEnd = Mathf.DeltaAngle(s.heading[0], s.heading[last]);
        int turnMax = ArgMax(s.heading, h => Mathf.Abs(Mathf.DeltaAngle(s.heading[0], h)));
        int reachMax = ArgMax(s.reach, r => r);

        return s.clip.name.PadRight(28) + " " + s.clip.length.ToString("F2") + " s" + (s.clip.isLooping ? " loop" : "     ") +
               " | body fwd " + Signed(endFwd) + " at the end (max " + Signed(s.body[maxF].z - start.z) + " at " + (maxF * s.dt).ToString("F1") +
               ", min " + Signed(s.body[minF].z - start.z) + " at " + (minF * s.dt).ToString("F1") + "), side " + side.ToString("F2") +
               ", up " + Signed(up) + (back ? ", BACK to start" : "") +
               (rootMoved > 0.01f ? " | OBJECT moves " + rootMoved.ToString("F2") : "") +
               " | head " + s.head[headLow].ToString("F2") + " at " + (headLow * s.dt).ToString("F1") +
               " .. " + s.head[headHigh].ToString("F2") + " at " + (headHigh * s.dt).ToString("F1") +
               " | turn " + Signed(turnEnd, "F0") + " deg at the end (max " + Signed(Mathf.DeltaAngle(s.heading[0], s.heading[turnMax]), "F0") +
               " at " + (turnMax * s.dt).ToString("F1") + ")" +
               " | muzzle reach " + Signed(s.reach[reachMax] - s.reach[0]) + " at " + (reachMax * s.dt).ToString("F1") +
               " | front paws up " + Intervals(s, ground0, ground1);
    }

    // Every DetailStep s: t, head height, body forward, front paws up.
    private static string Timeline(Samples s, float ground0, float ground1)
    {
        var line = new StringBuilder();
        line.Append("[ClipTimeline] " + s.clip.name + " (" + s.clip.length.ToString("F2") + " s): ");

        int step = Mathf.Max(1, Mathf.RoundToInt(DetailStep / s.dt));
        for (int f = 0; f < s.body.Length; f += step)
            line.Append(Point(s, f, ground0, ground1) + " | ");

        line.Append(Point(s, s.body.Length - 1, ground0, ground1));
        return line.ToString();
    }

    private static string Point(Samples s, int f, float ground0, float ground1)
    {
        return (f * s.dt).ToString("F1") + " h" + s.head[f].ToString("F2") + " f" + Signed(s.body[f].z - s.body[0].z) +
               " r" + Signed(s.reach[f] - s.reach[0]) + " t" + Signed(Mathf.DeltaAngle(s.heading[0], s.heading[f]), "F0") +
               (PawsUp(s, f, ground0, ground1) ? " UP" : "");
    }

    // Intervals when both front paws are off the ground (at least MinInterval long).
    private static string Intervals(Samples s, float ground0, float ground1)
    {
        var parts = new List<string>();
        int from = -1;

        for (int f = 0; f <= s.body.Length; f++)
        {
            bool up = f < s.body.Length && PawsUp(s, f, ground0, ground1);

            if (up && from < 0)
                from = f;

            if (!up && from >= 0)
            {
                if ((f - from) * s.dt >= MinInterval)
                    parts.Add((from * s.dt).ToString("F2") + "-" + ((f - 1) * s.dt).ToString("F2"));
                from = -1;
            }
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    private static bool PawsUp(Samples s, int f, float ground0, float ground1)
    {
        return s.paw0[f] > ground0 + PawUp && s.paw1[f] > ground1 + PawUp;
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------

    // All clips of the model file the controller's clips come from.
    private static AnimationClip[] ModelClips(Animator animator)
    {
        AnimationClip any = animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c != null);
        if (any == null)
            return new AnimationClip[0];

        string path = AssetDatabase.GetAssetPath(any);
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .OrderBy(c => c.name)
            .ToArray();
    }

    private static Transform FindByName(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
    }

    // A point in the bear's own space: x side, y up, z forward.
    private static Vector3 Local(Transform root, Vector3 point)
    {
        return Quaternion.Inverse(root.rotation) * (point - root.position);
    }

    private static int ArgMax<T>(T[] values, System.Func<T, float> score)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
            if (score(values[i]) > score(values[best]))
                best = i;
        return best;
    }

    private static string Signed(float value, string format = "F2")
    {
        string text = Mathf.Abs(value).ToString(format);
        return (value < 0f && text.Trim('0', '.', ',') != "" ? "-" : "+") + text;
    }
}
