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
