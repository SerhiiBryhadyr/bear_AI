using UnityEngine;

// =====================================================================
// BearBodyPoints - three points of the bear's body: head, body, rear
// (D-16). Used to tell how much of the bear can be seen
// (PlayerSightOfBear: "does the player see the bear?").
//
// Best: drag bones into the fields (Head, Spine, Pelvis) - the points
// then follow the animation (lying down, standing on hind legs).
// Empty field -> the point is taken from the bear's root with the
// offsets below (x = side, y = up, z = forward). Empty Head -> the
// "Eyes" object of BearVision, if it is set.
//
// Select the bear in the Scene view: yellow spheres = the points.
// Check that they sit inside the model.
//
// Close checks (13.11): the muzzle is ~1.8 m ahead of the root, so
// "how close is the player" counts from the muzzle (chase, Tracking)
// or from the body line head -> rear (side view, BearVision), and the
// bear's height / length for cover checks come from these points, not
// from fixed numbers. They follow the bones, so they also follow the
// model's scale. Use For Checks off = the old behaviour everywhere.
// =====================================================================
public class BearBodyPoints : MonoBehaviour
{
    public const int Count = 3;     // 0 = head, 1 = body, 2 = rear

    [Header("Bones (optional)")]
    public Transform head;
    public Transform body;
    public Transform rear;

    [Header("Without bones: offsets from the root (m)")]
    public Vector3 headOffset = new Vector3(0f, 1.0f, 1.1f);
    public Vector3 bodyOffset = new Vector3(0f, 0.9f, 0f);
    public Vector3 rearOffset = new Vector3(0f, 0.9f, -0.9f);

    [Header("Close checks (13.11)")]
    [Tooltip("On: close distances count from the muzzle / the body and cover checks take the bear's height and length from these points. Off: old behaviour - from the root and the fixed numbers in the states.")]
    public bool useForChecks = true;

    // States ask this before using the methods below.
    public bool Active => isActiveAndEnabled && useForChecks;

    private Transform eyes;

    private void Awake()
    {
        BearVision vision = GetComponent<BearVision>();
        if (vision != null)
            eyes = vision.eyes;
    }

    // ---------------------------------------------------------------
    // World position of point 0..2. Offsets ignore the root's scale,
    // so a scaled model does not throw the points away.
    // ---------------------------------------------------------------
    public Vector3 GetPoint(int index)
    {
        switch (index)
        {
            case 0:
                if (head != null) return head.position;
                if (eyes != null) return eyes.position;
                return FromRoot(headOffset);

            case 1:
                return body != null ? body.position : FromRoot(bodyOffset);

            default:
                return rear != null ? rear.position : FromRoot(rearOffset);
        }
    }

    private Vector3 FromRoot(Vector3 offset)
    {
        return transform.position + transform.rotation * offset;
    }

    // ---------------------------------------------------------------
    // Distances and sizes from the body (13.11). Distances are flat
    // (on the ground plane), the same way the states measure them.
    // ---------------------------------------------------------------
    public Vector3 Muzzle => GetPoint(0);

    // From the muzzle (head point = Eyes) to a point.
    public float DistanceFromMuzzle(Vector3 point)
    {
        return FlatDistance(Muzzle, point);
    }

    // From the nearest point of the body line head -> rear to a point:
    // "right next to the bear" from any side, not only in front.
    public float DistanceFromBody(Vector3 point)
    {
        Vector3 a = Flat(GetPoint(0));
        Vector3 b = Flat(GetPoint(2));
        Vector3 p = Flat(point);
        Vector3 ab = b - a;

        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    // How far the muzzle is ahead of the root (m).
    public float MuzzleAhead => FlatDistance(transform.position, Muzzle);

    // Above the root (= the ground under the bear), m.
    public float EyeHeight => GetPoint(0).y - transform.position.y;
    public float BodyHeight => GetPoint(1).y - transform.position.y;

    // Half the length head -> rear (m): the silhouette in cover checks.
    public float HalfLength => FlatDistance(GetPoint(0), GetPoint(2)) * 0.5f;

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
        {
            BearVision vision = GetComponent<BearVision>();
            eyes = vision != null ? vision.eyes : null;
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < Count; i++)
            Gizmos.DrawWireSphere(GetPoint(i), 0.15f);
    }
}
