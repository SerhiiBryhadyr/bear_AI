using UnityEngine;

// =====================================================================
// BearVision - the bear's eyes (D-17, base). Answers one question:
// "do I see the player right now?" BearKnowledge asks it every frame.
//
// How far the bear sees depends on three things:
//   1. Angle from where the eyes look (layered FOV, 04 section 7):
//      inside fullRangeHalfAngle - full range; further to the side the
//      range drops to edgeRange; beyond maxHalfAngle - not seen at all.
//   2. How much of the player is visible: rays to 5 body points
//      (head, two shoulders, hips, knees). Half the body behind a
//      rock = half the range; a bush hides partly (LineOfSight - the
//      same rules as the player's eyes, D-31).
//   3. How the player moves: running is the most noticeable, standing
//      still less, crouching much less.
//
//   Seen  =  distance <= maxViewDistance * angle * visible share * movement
//
// Closer than closeRange the angle does not matter: the bear notices a
// player right next to it from any side (obstacles still count).
//
// Example: player runs in the open straight ahead -> 100 m;
// stands still -> 60 m; crouches with half the body behind a rock -> ~17 m.
//
// Eyes: an empty "Eyes" object under the head bone, blue axis (Z)
// looking forward. Without it - body position + eyeHeight, body forward.
// Not yet: flashlight, grass (only bushes with a Foliage collider hide).
// =====================================================================
public class BearVision : MonoBehaviour
{
    public Transform player { get; private set; }

    [Header("Eyes")]
    [Tooltip("Empty object under the head bone, Z axis looking forward. Empty = body + eyeHeight, body forward.")]
    public Transform eyes;
    [Tooltip("Used only when Eyes is empty.")]
    public float eyeHeight = 1.2f;

    [Header("Range and angle (04, section 7)")]
    [Tooltip("Range on open ground when the player is fully visible and running (m).")]
    public float maxViewDistance = 100f;
    [Tooltip("Full range inside this angle from the eyes' direction (deg, to each side).")]
    public float fullRangeHalfAngle = 45f;
    [Tooltip("Beyond this angle the bear does not see at all (deg, to each side).")]
    public float maxHalfAngle = 65f;
    [Tooltip("Range at the very edge of the view (m).")]
    public float edgeRange = 3f;
    [Tooltip("Closer than this (m) the bear notices the player at any angle (hears, smells, side vision). Obstacles still count.")]
    public float closeRange = 2.5f;

    [Header("How noticeable the player is (0-1)")]
    public float runFactor = 1f;
    public float walkFactor = 0.8f;
    public float standFactor = 0.6f;
    public float backStepFactor = 0.6f;
    public float crouchFactor = 0.35f;

    [Header("Obstacles")]
    [Tooltip("What blocks the view. Everything by default; remove layers you can see through (chain-link fences).")]
    public LayerMask blockingLayers = ~0;
    [Tooltip("Bushes: partial cover. Empty = the layer named 'Foliage'.")]
    public LayerMask foliageLayers = 0;
    [Tooltip("Every bush on the line of sight hides this share of what is left (0.5: one bush - 50% visible, two - 25%).")]
    [Range(0f, 1f)]
    public float foliageBlock = 0.5f;

    [Header("Debug")]
    [Tooltip("Log when the bear starts / stops seeing the player.")]
    public bool logChanges = true;

    // --- Last result (for logs and other scripts) ---
    public float LastDistance { get; private set; }
    public float LastVisibleShare { get; private set; }     // 0..1, part of the body not hidden
    public float LastEffectiveRange { get; private set; }  // how far the bear could see the player right now

    private const int PointCount = 5;

    private CharacterController playerBody;
    private PlayerController playerController;

    private readonly Vector3[] points = new Vector3[PointCount];
    private readonly float[] pointVisibility = new float[PointCount];
    private Vector3 lastEyePosition;
    private bool wasSeeing;

    // ---------------------------------------------------------------
    // Setup: find the player and his body (size changes when crouching).
    // ---------------------------------------------------------------
    private void Start()
    {
        if (foliageLayers.value == 0)
            foliageLayers = LayerMask.GetMask("Foliage");

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (playerObject == null)
        {
            Debug.LogError("BearVision: Player with tag 'Player' was not found.");
            return;
        }

        player = playerObject.transform;
        playerBody = playerObject.GetComponent<CharacterController>();
        playerController = playerObject.GetComponent<PlayerController>();

        if (playerBody == null)
            Debug.LogWarning("BearVision: the player has no CharacterController - body points use a fixed 1.8 m height.");
    }

    // ---------------------------------------------------------------
    // Main question. Order: cheap checks first (angle, distance),
    // rays only when the player could be seen at all.
    // ---------------------------------------------------------------
    public bool CanSeePlayer()
    {
        if (player == null)
            return Report(false, "no player");

        LastVisibleShare = 0f;
        LastEffectiveRange = 0f;

        Vector3 eye = EyePosition();
        lastEyePosition = eye;

        BuildBodyPoints(eye);
        Vector3 bodyCenter = points[3];                     // hips
        LastDistance = Vector3.Distance(eye, bodyCenter);

        // 1. Angle -> how much of the full range is left at this angle.
        //    Right next to the bear the angle does not matter.
        float angleFactor = LastDistance <= closeRange ? 1f : AngleFactor(bodyCenter - eye);
        if (angleFactor <= 0f)
            return Report(false, "outside the view angle");

        // Even fully visible and running - too far for this angle: no rays needed.
        if (LastDistance > maxViewDistance * angleFactor)
            return Report(false, "too far for this angle");

        // 2. How much of the body is visible.
        LastVisibleShare = VisibleShare(eye);
        if (LastVisibleShare <= 0f)
            return Report(false, "fully hidden");

        // 3. How noticeable the movement is.
        float movement = MovementFactor();

        LastEffectiveRange = maxViewDistance * angleFactor * LastVisibleShare * movement;
        return Report(LastDistance <= LastEffectiveRange, "range " + LastEffectiveRange.ToString("F1") + " m");
    }

    // ---------------------------------------------------------------
    // Eyes: head bone (if set) or body + eyeHeight.
    // ---------------------------------------------------------------
    private Vector3 EyePosition()
    {
        return eyes != null ? eyes.position : transform.position + Vector3.up * eyeHeight;
    }

    private Vector3 LookDirection()
    {
        return eyes != null ? eyes.forward : transform.forward;
    }

    // ---------------------------------------------------------------
    // Layered FOV. Horizontal angle only: a player on a hill above is
    // still "in front". 1 = full range, 0 = not seen.
    // ---------------------------------------------------------------
    private float AngleFactor(Vector3 toTarget)
    {
        Vector3 look = LookDirection();
        look.y = 0f;
        toTarget.y = 0f;

        if (look.sqrMagnitude < 0.0001f || toTarget.sqrMagnitude < 0.0001f)
            return 1f;

        float angle = Vector3.Angle(look, toTarget);

        if (angle <= fullRangeHalfAngle)
            return 1f;
        if (angle >= maxHalfAngle)
            return 0f;

        // Between: the range drops linearly from full to edgeRange.
        float t = (angle - fullRangeHalfAngle) / (maxHalfAngle - fullRangeHalfAngle);
        float range = Mathf.Lerp(maxViewDistance, edgeRange, t);
        return range / maxViewDistance;
    }

    // ---------------------------------------------------------------
    // 5 body points from the player's capsule (it gets lower when he
    // crouches, so a crouched player is automatically easier to hide).
    // Shoulders are placed across the line of sight.
    // ---------------------------------------------------------------
    private void BuildBodyPoints(Vector3 eye)
    {
        float bottom, height, radius;
        Vector3 center;

        if (playerBody != null)
        {
            Bounds b = playerBody.bounds;
            bottom = b.min.y;
            height = b.size.y;
            radius = Mathf.Min(b.extents.x, b.extents.z);
            center = b.center;
        }
        else
        {
            bottom = player.position.y;
            height = 1.8f;
            radius = 0.3f;
            center = player.position + Vector3.up * 0.9f;
        }

        Vector3 toPlayer = center - eye;
        toPlayer.y = 0f;
        Vector3 side = toPlayer.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, toPlayer.normalized) * (radius * 0.8f)
            : Vector3.zero;

        Vector3 column = new Vector3(center.x, 0f, center.z);

        points[0] = column + Vector3.up * (bottom + height * 0.92f);          // head
        points[1] = column + Vector3.up * (bottom + height * 0.75f) + side;   // left shoulder
        points[2] = column + Vector3.up * (bottom + height * 0.75f) - side;   // right shoulder
        points[3] = column + Vector3.up * (bottom + height * 0.50f);          // hips
        points[4] = column + Vector3.up * (bottom + height * 0.20f);          // knees
    }

    // ---------------------------------------------------------------
    // Rays to every body point (LineOfSight): 1 = visible, 0 = hidden,
    // between = behind bushes. The bear's own colliders and other
    // trigger zones are ignored.
    // ---------------------------------------------------------------
    private float VisibleShare(Vector3 eye)
    {
        float sum = 0f;

        for (int i = 0; i < PointCount; i++)
        {
            pointVisibility[i] = LineOfSight.PointVisibility(eye, points[i], player, transform,
                                                             blockingLayers, foliageLayers, foliageBlock);
            sum += pointVisibility[i];
        }

        return sum / PointCount;
    }

    // ---------------------------------------------------------------
    // How noticeable the player's movement is. The bear sees how he
    // moves, so reading it is not cheating.
    // ---------------------------------------------------------------
    private float MovementFactor()
    {
        if (playerController == null)
            return walkFactor;

        switch (playerController.CurrentMode)
        {
            case MoveMode.Run: return runFactor;
            case MoveMode.Walk: return walkFactor;
            case MoveMode.BackWalk: return walkFactor;
            case MoveMode.BackStep: return backStepFactor;
            case MoveMode.Crouch: return crouchFactor;
            default: return standFactor;           // Idle: standing still
        }
    }

    // ---------------------------------------------------------------
    // Log only the moment it changes, not every frame.
    // ---------------------------------------------------------------
    private bool Report(bool seeing, string reason)
    {
        if (seeing != wasSeeing && logChanges)
        {
            string mode = playerController != null ? playerController.CurrentMode.ToString() : "?";
            string share = Mathf.RoundToInt(LastVisibleShare * 100f) + "%";

            Debug.Log(seeing
                ? $"[Vision] SEES player: {LastDistance:F1} m, visible {share}, {mode}, {reason}"
                : $"[Vision] lost sight: {LastDistance:F1} m, {mode}, {reason}");
        }

        wasSeeing = seeing;
        return seeing;
    }

    // ---------------------------------------------------------------
    // Scene view (bear selected): rays of the last check -
    // green = visible point, yellow = behind bushes, red = hidden.
    // ---------------------------------------------------------------
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || player == null)
            return;

        for (int i = 0; i < PointCount; i++)
        {
            float v = pointVisibility[i];
            Gizmos.color = v >= 0.99f ? Color.green : v > 0f ? Color.yellow : Color.red;
            Gizmos.DrawLine(lastEyePosition, points[i]);
        }
    }
}
