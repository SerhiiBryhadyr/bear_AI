using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// BearHeadLook - the visible head turn (D-32, 25.11).
//
// BearVision decides WHERE the bear looks: a state follows the player
// (stalking, face-off...) or the bear glances at a heard sound. This
// script turns the neck and the head there, so the player sees where
// the bear looks. Eyes sit under RigHead and turn with it - and while
// this script is on, BearVision takes the angle of view from Eyes:
// the bear sees exactly where its muzzle points.
//
// How: every frame, after the animation (like BearJawLock), the head
// and the neck bones above it turn around the vertical, each by its
// share - the neck bends in an arc, the head is not a hinge. Only
// rotation, the bones do not move - nothing stretches. Side to side
// only, no up / down. At most BearVision.maxHeadTurn from the body;
// the turn speeds up and slows down like a real head.
//
// No look point -> the head goes back smoothly to the animation as is.
// Untick the component -> the old way (the eyes turn only in the logic).
// Shake(): the bear shakes its head side to side for a moment (a stress
// sign in the face-off, BearThreatDisplay, 25.11), on top of the rest.
// =====================================================================
[DefaultExecutionOrder(500)]                                // after the Animator and BearGroundAlign
public class BearHeadLook : MonoBehaviour
{
    [Header("Bones")]
    [Tooltip("The head bone (Eyes must be under it).")]
    public string headBoneName = "RigHead";
    [Tooltip("How many bones above the head turn too (the neck). The log at Start shows which. If the shoulders move - make it smaller.")]
    [Range(0, 3)]
    public int neckBones = 2;
    [Tooltip("Share of the turn for each bone, from the base of the neck to the head. The head takes the last one; the sum becomes 100% by itself.")]
    public float[] shares = { 0.3f, 0.3f, 0.4f };

    [Header("Speed")]
    [Tooltip("About how long one turn takes (s). Smaller - sharper.")]
    public float turnTime = 0.25f;
    [Tooltip("Fastest head turn (deg/s).")]
    public float maxTurnSpeed = 240f;

    [Header("Debug")]
    [Tooltip("At Start: which bones turn and their shares.")]
    public bool logBones = true;

    private BearVision vision;
    private Transform eyes;                                 // shows where the muzzle points
    private readonly List<Transform> chain = new List<Transform>();   // base of the neck -> head
    private readonly List<float> weights = new List<float>();
    private Quaternion[] animatedPose;                      // local rotations before our turn
    private Quaternion[] turnedPose;                        // ... and after it

    private float turn;                                     // degrees on top of the animation (+ = right)
    private float turnSpeed;                                // for SmoothDampAngle
    private float animatedYaw;                              // where the animation points the head

    private float shakeStart = -1f;
    private float shakeTime;
    private float shakeAngle;
    private const float ShakeFrequency = 1.5f;              // side to side per second

    // ---------------------------------------------------------------
    // Setup: the head bone, the neck above it, the shares.
    // ---------------------------------------------------------------
    private void Start()
    {
        vision = GetComponent<BearVision>();
        eyes = vision != null ? vision.eyes : null;

        if (eyes == null)
        {
            Debug.LogWarning("BearHeadLook: BearVision or its Eyes is missing - the head does not turn.");
            enabled = false;
            return;
        }

        Transform head = System.Array.Find(GetComponentsInChildren<Transform>(true), t => t.name == headBoneName);
        if (head == null)
        {
            Debug.LogWarning("BearHeadLook: bone " + headBoneName + " not found - the head does not turn.");
            enabled = false;
            return;
        }

        // The head and neckBones bones above it, stored from the base of the neck.
        for (Transform bone = head; bone != null && bone != transform && chain.Count <= neckBones; bone = bone.parent)
            chain.Insert(0, bone);

        // Shares: the head takes the last one, the neck the ones before it.
        float sum = 0f;
        for (int i = 0; i < chain.Count; i++)
        {
            int s = Mathf.Clamp(shares.Length - chain.Count + i, 0, shares.Length - 1);
            float w = shares.Length > 0 ? Mathf.Max(0f, shares[s]) : 1f;
            weights.Add(w);
            sum += w;
        }
        for (int i = 0; i < weights.Count; i++)
            weights[i] = sum > 0f ? weights[i] / sum : 1f / weights.Count;

        animatedPose = new Quaternion[chain.Count];
        turnedPose = new Quaternion[chain.Count];

        if (logBones)
        {
            string text = "[HeadLook] bones:";
            for (int i = 0; i < chain.Count; i++)
                text += " " + chain[i].name + " " + Mathf.RoundToInt(weights[i] * 100f) + "%";
            Debug.Log(text + ", limit " + vision.maxHeadTurn + " deg");
        }
    }

    // Shakes its head: angle (deg) to each side, for seconds.
    public void Shake(float seconds, float angle)
    {
        shakeStart = Time.time;
        shakeTime = Mathf.Max(0.1f, seconds);
        shakeAngle = angle;
    }

    // Starts and ends softly (no jump): the swing grows and fades.
    private float ShakeOffset()
    {
        float t = Time.time - shakeStart;
        if (shakeStart < 0f || t >= shakeTime)
            return 0f;
        return shakeAngle * Mathf.Sin(2f * Mathf.PI * ShakeFrequency * t) * Mathf.Sin(Mathf.PI * t / shakeTime);
    }

    // Switched off and on again - start from the animation, no jump.
    private void OnDisable()
    {
        turn = 0f;
        turnSpeed = 0f;
    }

    // ---------------------------------------------------------------
    // Every frame after the animation: measure, aim, turn.
    // ---------------------------------------------------------------
    private void LateUpdate()
    {
        if (chain.Count == 0)
            return;                                         // set up failed (see the warning at Start)

        // The Animator did not write the bones this frame (paused, culled):
        // our turn from the last frame is still on them - take it off,
        // otherwise it adds up every frame and spins the head.
        for (int i = 0; i < chain.Count; i++)
        {
            if (chain[i].localRotation == turnedPose[i])
                chain[i].localRotation = animatedPose[i];
            animatedPose[i] = chain[i].localRotation;
        }

        // 1. Where the animation points the head (deg from the body, + = right).
        //    Head straight down (eating) - keep the last value.
        Vector3 body = Flat(transform.forward);
        Vector3 muzzle = Flat(eyes.forward);
        if (muzzle.sqrMagnitude > 0.04f)
            animatedYaw = Vector3.SignedAngle(body, muzzle, Vector3.up);

        // 2. Where it should point: the look point of BearVision,
        //    not further than maxHeadTurn from the body. None -> 0 (animation).
        float goal = 0f;
        if (vision.HasLookPoint)
        {
            Vector3 toPoint = Flat(vision.LookPoint - eyes.position);
            if (toPoint.sqrMagnitude > 0.01f)
            {
                float wanted = Mathf.Clamp(Vector3.SignedAngle(body, toPoint, Vector3.up),
                                           -vision.maxHeadTurn, vision.maxHeadTurn);
                goal = Mathf.DeltaAngle(animatedYaw, wanted);
            }
        }

        // 3. Smoothly, like a real head: speeds up, slows down.
        turn = Mathf.SmoothDampAngle(turn, goal, ref turnSpeed, turnTime, maxTurnSpeed);

        // 4. Shared between the neck and the head, around the vertical.
        //    A turned bone carries its children, so the head gets the whole turn.
        //    A head shake goes on top, not smoothed (it would fade away).
        float total = turn + ShakeOffset();
        for (int i = 0; i < chain.Count; i++)
        {
            chain[i].rotation = Quaternion.AngleAxis(total * weights[i], Vector3.up) * chain[i].rotation;
            turnedPose[i] = chain[i].localRotation;
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
