using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// =====================================================================
// BearFootMonitor - debug: do the paws match the movement? (15.11)
//
// Once a second, line [Feet]:
//   bear speed | planted paws slide X m/s (Y% of the speed) |
//   paws on the ground Z% | clips with weights (Walk 60% + Trot 40%)
// A paw that stands on the ground must not move over it: slide ~0 =
// good, slide close to the bear's speed = the paws skate.
// At once:
//   [Anim] state Locomotion -> Sneak   - switch between Animator states
//   [Feet] POP ...                     - the body jumped in one frame
//
// Runs after the animation and after BearGroundAlign, so it sees what
// the camera sees. Debug only: turn the component off when not needed.
// =====================================================================
[DefaultExecutionOrder(1000)]
public class BearFootMonitor : MonoBehaviour
{
    [Tooltip("Empty = the Animator on this object or on a child.")]
    public Animator animator;
    [Tooltip("Ground for the paws: terrain and solid objects. The bear's own colliders are skipped.")]
    public LayerMask groundLayers = ~0;

    [Header("On the ground")]
    [Tooltip("A paw within this height (m) of its lowest point of the last seconds = stands on the ground.")]
    public float groundBand = 0.05f;

    [Header("Pop (a jump of the body in one frame)")]
    public float popDistance = 0.15f;   // m
    public float popAngle = 12f;        // degrees

    private const float PawSpacing = 0.25f;

    private List<Transform> paws = new List<Transform>();
    private Transform body;             // skeleton root bone
    private Transform pelvis;           // its pelvis child (rotation pops)

    private Vector3[] lastPaw;
    private bool[] lastDown;
    private float[] lowest;             // lowest height above ground lately, per paw

    private Vector3 lastBodyLocal;
    private Quaternion lastPelvisLocal;
    private bool hasLast;
    private float lastPopTime = -999f;

    private int lastStateHash;
    private float timer;
    private float slideDistance;        // planted paws, over the second
    private float plantedTime;
    private float downFrames;
    private float allFrames;
    private Vector3 secondStart;

    private readonly RaycastHit[] hits = new RaycastHit[8];

    private void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        SkinnedMeshRenderer skin = GetComponentInChildren<SkinnedMeshRenderer>();
        if (animator == null || skin == null || skin.rootBone == null)
        {
            Debug.LogWarning("BearFootMonitor: no Animator / skinned mesh found - off.");
            enabled = false;
            return;
        }

        body = skin.rootBone;
        pelvis = body.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.Contains("Pelvis")) ?? body;
        paws = FindPaws(skin);

        lastPaw = new Vector3[paws.Count];
        lastDown = new bool[paws.Count];
        lowest = Enumerable.Repeat(float.MaxValue, paws.Count).ToArray();
        secondStart = transform.position;

        Debug.Log($"[Feet] monitor on: paws {string.Join(", ", paws.Select(p => p.name))}, body {body.name}, pelvis {pelvis.name}");
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        CheckState();
        CheckPaws(dt);
        CheckPop();

        timer += dt;
        if (timer >= 1f)
        {
            LogSecond();
            timer = 0f;
        }
    }

    // ---------------------------------------------------------------
    // Paws: height above the ground; planted paws must not move.
    // ---------------------------------------------------------------
    private void CheckPaws(float dt)
    {
        for (int i = 0; i < paws.Count; i++)
        {
            Vector3 p = paws[i].position;
            float height = HeightAboveGround(p);

            // Lowest height lately: follows a new minimum at once, rises slowly.
            lowest[i] = Mathf.Min(lowest[i] + 0.05f * dt, height);
            bool down = height <= lowest[i] + groundBand;

            if (down && lastDown[i] && hasLast)
            {
                Vector3 move = p - lastPaw[i];
                move.y = 0f;
                slideDistance += move.magnitude;
                plantedTime += dt;
            }

            if (down)
                downFrames++;
            allFrames++;

            lastPaw[i] = p;
            lastDown[i] = down;
        }
    }

    private float HeightAboveGround(Vector3 point)
    {
        int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.5f, Vector3.down, hits, 3f,
                                            groundLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform))
                continue;
            best = Mathf.Min(best, hits[i].distance);
        }

        return best < float.MaxValue ? best - 0.5f : 0f;
    }

    // ---------------------------------------------------------------
    // Pops: the body bone jumps relative to the bear in one frame.
    // ---------------------------------------------------------------
    private void CheckPop()
    {
        Vector3 bodyLocal = transform.InverseTransformPoint(body.position);
        Quaternion pelvisLocal = Quaternion.Inverse(transform.rotation) * pelvis.rotation;

        if (hasLast)
        {
            float jump = Vector3.Distance(bodyLocal, lastBodyLocal) * transform.lossyScale.x;
            float turn = Quaternion.Angle(pelvisLocal, lastPelvisLocal);

            if ((jump > popDistance || turn > popAngle) && Time.time - lastPopTime > 0.2f)
            {
                lastPopTime = Time.time;
                Debug.Log($"[Feet] POP: body {jump * 100f:F0} cm, pelvis {turn:F0} deg in one frame | {Clips()}");
            }
        }

        lastBodyLocal = bodyLocal;
        lastPelvisLocal = pelvisLocal;
        hasLast = true;
    }

    // ---------------------------------------------------------------
    // Animator state switches (Locomotion <-> Sneak).
    // ---------------------------------------------------------------
    private void CheckState()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (lastStateHash != 0 && state.shortNameHash != lastStateHash)
            Debug.Log($"[Anim] state -> {StateName(state)} | {Clips()}");
        lastStateHash = state.shortNameHash;
    }

    private static string StateName(AnimatorStateInfo state)
    {
        if (state.IsName("Locomotion")) return "Locomotion";
        if (state.IsName("Sneak")) return "Sneak";
        return state.shortNameHash.ToString();
    }

    // ---------------------------------------------------------------
    // Once a second.
    // ---------------------------------------------------------------
    private void LogSecond()
    {
        Vector3 moved = transform.position - secondStart;
        moved.y = 0f;
        float speed = moved.magnitude / timer;
        float slide = plantedTime > 0f ? slideDistance / plantedTime : 0f;
        float share = speed > 0.1f ? slide / speed * 100f : 0f;
        float onGround = allFrames > 0f ? downFrames / allFrames * 100f : 0f;

        Debug.Log($"[Feet] bear {speed:F2} m/s | planted paws slide {slide:F2} m/s ({share:F0}% of speed) | on ground {onGround:F0}% | {Clips()}");

        secondStart = transform.position;
        slideDistance = 0f;
        plantedTime = 0f;
        downFrames = 0f;
        allFrames = 0f;
    }

    // "Walk 62% + Trot 38%", plus the next state during a transition.
    private string Clips()
    {
        var text = new StringBuilder();
        AppendClips(text, animator.GetCurrentAnimatorClipInfo(0));

        if (animator.IsInTransition(0))
        {
            text.Append(" -> ");
            AppendClips(text, animator.GetNextAnimatorClipInfo(0));
        }

        return text.ToString();
    }

    private static void AppendClips(StringBuilder text, AnimatorClipInfo[] clips)
    {
        bool first = true;
        foreach (AnimatorClipInfo info in clips.OrderByDescending(c => c.weight))
        {
            if (info.weight < 0.05f)
                continue;
            if (!first) text.Append(" + ");
            text.Append($"{info.clip.name} {info.weight * 100f:F0}%");
            first = false;
        }
    }

    // Paws = the lowest end bones of the skeleton, one per leg
    // (the same rule as Tools -> Bear -> Measure Clip Speeds).
    private static List<Transform> FindPaws(SkinnedMeshRenderer skin)
    {
        var bones = new HashSet<Transform>(skin.bones.Where(b => b != null));
        var ends = bones.Where(b => !b.Cast<Transform>().Any(c => bones.Contains(c)))
                        .OrderBy(b => b.position.y)
                        .ToList();

        var result = new List<Transform>();
        foreach (Transform end in ends)
        {
            Vector3 flat = new Vector3(end.position.x, 0f, end.position.z);
            if (result.All(p => Vector3.Distance(flat, new Vector3(p.position.x, 0f, p.position.z)) > PawSpacing))
                result.Add(end);
            if (result.Count == 4)
                break;
        }

        return result;
    }
}
