using UnityEngine;

// =====================================================================
// BearGroundAlign - tilts the bear's body along a slope (14.11,
// open question 26.10, variant A).
//
// Two rays go down: one ahead of the root (under the chest / front
// paws), one behind it (under the pelvis / hind paws). The height
// difference between the two hits gives the slope along the bear;
// the body turns by this angle around the root (= the ground under
// the bear's centre), smoothly. Sideways the bear stays level.
//
// Only the skeleton turns, after the animation (LateUpdate). The root
// object - NavMeshAgent, distances, states - is not touched. Eyes sit
// on the head bone, so on a hill the bear also looks a bit up / down
// the slope, like a real bear.
//
// Setup (Inspector):
//   Skeleton Root - the topmost bone of the rig, the one all the
//                   others hang on (usually RigPelvis or its parent).
//   Ground Layers - terrain and solid objects. Not Foliage, not the
//                   player. The bear's own colliders are skipped.
// Select the bear in Play mode: green spheres = where the rays hit
// the ground, the line between them = the slope the bear follows.
// =====================================================================
public class BearGroundAlign : MonoBehaviour
{
    [Header("Setup")]
    public Transform skeletonRoot;
    public LayerMask groundLayers = ~0;

    [Header("Rays")]
    [Tooltip("How far ahead / behind the root the rays go down, m. 0 = from BearBodyPoints (body point and rear point), or 0.9 m if that does not work.")]
    public float frontOffset = 0f;
    public float backOffset = 0f;
    [Tooltip("Rays start this high above the ground point, m, and go twice as far down.")]
    public float rayHeight = 2f;

    [Header("Tilt")]
    [Tooltip("Steeper slopes are clamped to this angle, degrees.")]
    public float maxPitch = 25f;
    [Tooltip("How fast the body follows a new slope, s. More = softer, but later on a sharp edge.")]
    public float smoothTime = 0.25f;

    [Header("Height (14.11)")]
    [Tooltip("Lower / raise the body so the paws stand on the real ground (the front and back ray hits), not at the NavMesh height. Off = as before (the bear floated 10-20 cm on ridges).")]
    public bool snapToGround = true;
    [Tooltip("At most this far up or down, m.")]
    public float maxSnap = 0.4f;
    [Tooltip("How fast the height follows the ground, s.")]
    public float heightSmoothTime = 0.1f;

    [Header("Debug")]
    [Tooltip("Once a second: [Slope] current and target angle, lift.")]
    public bool logPitch = false;

    // Current tilt, degrees: + = nose up (uphill), - = nose down.
    public float Pitch => pitch;

    private float pitch;
    private float pitchVelocity;
    private float lift;             // m, + = body up, - = body down
    private float liftVelocity;
    private float front;
    private float back;
    private float logTimer;

    // The bone's un-tilted pose and what we wrote last frame (local).
    // If the Animator did not rewrite the position or the rotation this
    // frame (no clip animates it, the bear is culled off-screen), our
    // tilt is still on it - then the saved un-tilted value is used, so
    // tilts never stack up. Checked separately: a clip may animate only
    // the position (14.11: the bear rolled like a wheel because of it).
    private bool applied;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 writtenLocalPosition;
    private Quaternion writtenLocalRotation;

    private readonly RaycastHit[] hits = new RaycastHit[8];

    // For gizmos.
    private Vector3 frontHit;
    private Vector3 backHit;
    private bool frontFound;
    private bool backFound;

    // ---------------------------------------------------------------
    // Setup: check the bone, find where the rays go down.
    // ---------------------------------------------------------------
    private void Start()
    {
        if (skeletonRoot == null || skeletonRoot == transform || !skeletonRoot.IsChildOf(transform))
        {
            Debug.LogWarning("BearGroundAlign: set Skeleton Root to the top bone of the bear's rig (a child of the bear). Tilt is off.");
            enabled = false;
            return;
        }

        front = frontOffset > 0f ? frontOffset : OffsetFromBody(1, 1f, 0.9f);
        back = backOffset > 0f ? backOffset : OffsetFromBody(2, -1f, 0.9f);

        Debug.Log($"[Slope] rays: {front:F2} m ahead, {back:F2} m behind the root");
    }

    // Distance along the bear from the root to a body point of
    // BearBodyPoints (1 = body, 2 = rear). sign: +1 ahead, -1 behind.
    // Too small (no bones, offsets mode) -> fallback.
    private float OffsetFromBody(int index, float sign, float fallback)
    {
        BearBodyPoints points = GetComponent<BearBodyPoints>();
        if (points == null)
            return fallback;

        float along = Vector3.Dot(points.GetPoint(index) - transform.position, transform.forward) * sign;
        return along > 0.3f ? along : fallback;
    }

    private void OnDisable()
    {
        // Put the bone back un-tilted: the Animator may never rewrite it.
        if (applied && skeletonRoot != null)
        {
            skeletonRoot.localPosition = baseLocalPosition;
            skeletonRoot.localRotation = baseLocalRotation;
        }

        applied = false;
    }

    // ---------------------------------------------------------------
    // Every frame, after the Animator: angle -> smooth -> skeleton.
    // ---------------------------------------------------------------
    private void LateUpdate()
    {
        // 1. Target angle from the two rays. No ground under one of
        //    them (edge of the map, a bridge) -> level.
        float target = 0f;

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude > 0.0001f)
        {
            forward.Normalize();
            frontFound = GroundPoint(transform.position + forward * front, out frontHit);
            backFound = GroundPoint(transform.position - forward * back, out backHit);

            if (frontFound && backFound)
                target = Mathf.Atan2(frontHit.y - backHit.y, front + back) * Mathf.Rad2Deg;
        }

        target = Mathf.Clamp(target, -maxPitch, maxPitch);

        // 2. Smooth: the body follows the slope, it does not snap.
        pitch = Mathf.SmoothDamp(pitch, target, ref pitchVelocity, smoothTime);

        // 2b. Height: the paws go on the line front hit -> back hit. The
        //     root stands on the NavMesh, which is a rough copy of the
        //     ground (on a ridge it can be above it); the rays hit the
        //     real ground. Lift = that line's height under the root.
        float targetLift = 0f;
        if (snapToGround && frontFound && backFound)
        {
            float lineY = backHit.y + (frontHit.y - backHit.y) * (back / (front + back));
            targetLift = Mathf.Clamp(lineY - transform.position.y, -maxSnap, maxSnap);
        }

        lift = Mathf.SmoothDamp(lift, targetLift, ref liftVelocity, heightSmoothTime);

        // 3. Put the tilt on the skeleton.
        ApplyTilt();

        if (logPitch)
            LogPitch(target);
    }

    // Nearest ground straight below a point, skipping the bear itself.
    private bool GroundPoint(Vector3 point, out Vector3 hitPoint)
    {
        Vector3 origin = point + Vector3.up * rayHeight;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, rayHeight * 2f,
                                            groundLayers, QueryTriggerInteraction.Ignore);

        hitPoint = point;
        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform))
                continue;   // the bear's own colliders

            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                hitPoint = hits[i].point;
                found = true;
            }
        }

        return found;
    }

    // ---------------------------------------------------------------
    // The tilt itself. Works in the bear root's space: turn around the
    // root's right axis, through the root (the ground under the bear),
    // so front and hind paws both land on the slope line.
    // ---------------------------------------------------------------
    private void ApplyTilt()
    {
        // 1. The un-tilted pose. A value the Animator rewrote this frame
        //    is fresh; a value still exactly what we wrote last frame was
        //    not rewritten - take the saved un-tilted one instead.
        Vector3 localPosition = skeletonRoot.localPosition;
        Quaternion localRotation = skeletonRoot.localRotation;

        if (!applied || !Same(localPosition, writtenLocalPosition))
            baseLocalPosition = localPosition;

        if (!applied || !Same(localRotation, writtenLocalRotation))
            baseLocalRotation = localRotation;

        // 2. Back to the un-tilted pose, then tilt once.
        skeletonRoot.localPosition = baseLocalPosition;
        skeletonRoot.localRotation = baseLocalRotation;

        // + pitch = nose up = turn around the right axis by -pitch.
        RotateBone(Quaternion.AngleAxis(-pitch, Vector3.right));
        skeletonRoot.position += Vector3.up * lift;

        writtenLocalPosition = skeletonRoot.localPosition;
        writtenLocalRotation = skeletonRoot.localRotation;
        applied = true;
    }

    // Exact comparison: Unity's == allows a small difference, and a
    // slowly changing animation would look "not rewritten" by it.
    private static bool Same(Vector3 a, Vector3 b)
    {
        return a.x == b.x && a.y == b.y && a.z == b.z;
    }

    private static bool Same(Quaternion a, Quaternion b)
    {
        return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    }

    private void RotateBone(Quaternion rotationInRootSpace)
    {
        Vector3 position = transform.InverseTransformPoint(skeletonRoot.position);
        Quaternion rotation = Quaternion.Inverse(transform.rotation) * skeletonRoot.rotation;

        skeletonRoot.position = transform.TransformPoint(rotationInRootSpace * position);
        skeletonRoot.rotation = transform.rotation * (rotationInRootSpace * rotation);
    }

    private void LogPitch(float target)
    {
        logTimer -= Time.deltaTime;
        if (logTimer > 0f)
            return;

        logTimer = 1f;
        Debug.Log($"[Slope] pitch {pitch:F1} deg (target {target:F1}), lift {lift * 100f:F0} cm, front {(frontFound ? 1 : 0)} back {(backFound ? 1 : 0)}");
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        Gizmos.color = Color.green;
        if (frontFound) Gizmos.DrawSphere(frontHit, 0.08f);
        if (backFound) Gizmos.DrawSphere(backHit, 0.08f);
        if (frontFound && backFound) Gizmos.DrawLine(frontHit, backHit);
    }
}
