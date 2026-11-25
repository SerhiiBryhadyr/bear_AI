using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// =====================================================================
// BearClipSpeeds - measures how fast each clip of the bear "walks"
// (15.11). Editor only.
//
// The clips are recorded in place: the body stays, the paws move.
// While a paw stands on the ground, it slides BACK relative to the
// body exactly as fast as the bear should move forward - that is the
// clip's natural speed. Blend tree thresholds = these speeds -> the
// paws stop sliding.
//
// Use: select the bear (the object with the Animator) in the scene,
// Tools -> Bear -> Measure Clip Speeds. Not in Play mode.
// The scene is not changed: clips are sampled in Animation Mode and the
// pose comes back. Measured on the bear in the scene, so its scale
// (1.8) is already included. Result - a table in the Console.
// =====================================================================
public static class BearClipSpeeds
{
    private const float SampleRate = 60f;     // samples per second of a clip
    private const float StanceBand = 0.04f;   // paw within 4 cm of its lowest point = on the ground
    private const float PawSpacing = 0.25f;   // two paws are at least this far apart (m)

    [MenuItem("Tools/Bear/Measure Clip Speeds")]
    private static void Measure()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[ClipSpeeds] Stop Play mode first.");
            return;
        }

        Animator animator = FindAnimator(Selection.activeGameObject);
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("[ClipSpeeds] Select the bear (the object with the Animator and its controller) in the Hierarchy.");
            return;
        }

        Transform root = animator.transform;
        List<Transform> paws = FindPaws(root);
        if (paws.Count < 2)
        {
            Debug.LogWarning("[ClipSpeeds] Could not find the paws in the bear's skeleton.");
            return;
        }

        Transform body = FindBody(root);

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips
            .Where(c => c != null).Distinct().ToArray();

        var report = new StringBuilder();
        report.AppendLine($"[ClipSpeeds] {root.name}, scale {root.lossyScale.x:F2}, paws: {string.Join(", ", paws.Select(p => p.name))}, body: {body.name}");

        var results = new List<(string name, float speed, float side, float ground, float length, float travel)>();

        AnimationMode.StartAnimationMode();
        try
        {
            foreach (AnimationClip clip in clips)
            {
                MeasureClip(root, paws, body, clip, out float speed, out float side, out float onGround, out float travel);
                results.Add((clip.name, speed, side, onGround, clip.length, travel));
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();   // the pose comes back
        }

        foreach (var r in results.OrderBy(r => r.speed))
            report.AppendLine($"  {r.name,-16} paws vs body {r.speed,5:F2} m/s (sideways {r.side:F2}) | body moves {r.travel:F2} m in the clip ({r.travel / Mathf.Max(r.length, 0.01f):F2} m/s) | on ground {r.ground * 100f:F0}% | clip {r.length:F2} s");

        Debug.Log(report.ToString());
    }

    // ---------------------------------------------------------------
    // One clip: sample it frame by frame, follow each paw in the bear's
    // own space. Frames where a paw is at its lowest = it stands on
    // the ground; its backward speed there = the clip's speed.
    // ---------------------------------------------------------------
    private static void MeasureClip(Transform root, List<Transform> paws, Transform body, AnimationClip clip,
                                    out float speed, out float side, out float onGround, out float travel)
    {
        int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length * SampleRate));
        float dt = clip.length / frames;

        var positions = new Vector3[paws.Count, frames + 1];
        var bodyPositions = new Vector3[frames + 1];

        for (int f = 0; f <= frames; f++)
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(root.gameObject, clip, f * dt);
            AnimationMode.EndSampling();

            for (int p = 0; p < paws.Count; p++)
                positions[p, f] = Quaternion.Inverse(root.rotation) * (paws[p].position - root.position);

            bodyPositions[f] = Quaternion.Inverse(root.rotation) * (body.position - root.position);
        }

        // Does the body itself move forward inside the clip (motion baked
        // into the bone, not "in place")?
        travel = bodyPositions[frames].z - bodyPositions[0].z;

        var speeds = new List<float>();
        var sideSpeeds = new List<float>();
        int groundFrames = 0;

        for (int p = 0; p < paws.Count; p++)
        {
            float minY = float.MaxValue;
            for (int f = 0; f <= frames; f++)
                minY = Mathf.Min(minY, positions[p, f].y);

            for (int f = 0; f < frames; f++)
            {
                bool down = positions[p, f].y <= minY + StanceBand;
                if (down)
                    groundFrames++;

                if (down && positions[p, f + 1].y <= minY + StanceBand)
                {
                    // Paw relative to the body: right for both kinds of clips.
                    Vector3 before = positions[p, f] - bodyPositions[f];
                    Vector3 after = positions[p, f + 1] - bodyPositions[f + 1];
                    speeds.Add(-(after.z - before.z) / dt);          // back = forward speed
                    sideSpeeds.Add(Mathf.Abs(after.x - before.x) / dt);
                }
            }
        }

        speed = speeds.Count > 0 ? Mathf.Abs(Median(speeds)) : 0f;
        side = sideSpeeds.Count > 0 ? Median(sideSpeeds) : 0f;
        onGround = (float)groundFrames / (paws.Count * frames);
    }

    // ---------------------------------------------------------------
    // Helpers (internal: BearClipAnalysis uses them too, 24.11).
    // ---------------------------------------------------------------
    internal static Animator FindAnimator(GameObject selected)
    {
        if (selected == null)
            return null;

        Animator animator = selected.GetComponentInParent<Animator>();
        return animator != null ? animator : selected.GetComponentInChildren<Animator>();
    }

    // The bone the whole body hangs on (RigRoot / RigPelvis).
    internal static Transform FindBody(Transform root)
    {
        SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin != null && skin.rootBone != null)
            return skin.rootBone;

        Transform rig = root.Find("RigRoot");
        return rig != null ? rig : root;
    }

    // Paws = the lowest end bones of the skeleton, one per leg.
    internal static List<Transform> FindPaws(Transform root)
    {
        SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
        HashSet<Transform> bones = skin != null && skin.bones.Length > 0
            ? new HashSet<Transform>(skin.bones.Where(b => b != null))
            : new HashSet<Transform>(root.GetComponentsInChildren<Transform>().Where(t => t != root));

        // End bones: no child of theirs is a bone.
        var ends = bones.Where(b => !b.Cast<Transform>().Any(c => bones.Contains(c)))
                        .OrderBy(b => b.position.y)
                        .ToList();

        var paws = new List<Transform>();
        foreach (Transform end in ends)
        {
            Vector3 flat = new Vector3(end.position.x, 0f, end.position.z);
            bool farFromOthers = paws.All(p =>
                Vector3.Distance(flat, new Vector3(p.position.x, 0f, p.position.z)) > PawSpacing);

            if (farFromOthers)
                paws.Add(end);

            if (paws.Count == 4)
                break;
        }

        return paws;
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) * 0.5f;
    }
}
