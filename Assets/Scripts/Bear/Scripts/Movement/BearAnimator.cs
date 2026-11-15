using UnityEngine;

// =====================================================================
// BearAnimator - shows the bear's movement in the Animator (14.11).
//
// Speed (Float) = how fast the bear really moves, m/s. The blend tree
// picks the clip by it: Idle / WalkSlow / Walk / Trot / Run / Sprint.
// States only set the speed (BearMovement) and never name clips, so a
// new clip = a change in the Animator, not in the code (D-08).
//
// Sneak (Bool) = a state asked to sneak (BearMovement.SetSneak) AND
// the bear is slow enough. Faster -> normal trot / run, so the bear
// does not "sneak" at 6 m/s while it runs between covers.
//
// Needs in the Animator Controller: parameters "Speed" (Float) and
// "Sneak" (Bool). On the Animator: Apply Root Motion OFF - the
// NavMeshAgent moves the bear, the clips only play in place.
//
// Size (15.11): our bear is 1.8x bigger than the asset's bear. Bigger
// animals move their legs slower (pace ~ 1 / sqrt(size)), so all clips
// play slower by Size Time Scale (1.8 -> 0.745) and the speed given to
// the blend tree is divided by it. The thresholds stay the clips' own
// speeds, measured by Tools -> Bear -> Measure Clip Speeds:
// WalkSlow 0.8, Walk 1.66, Trot 8.08, Run 13.3, Sprint 23.6, Sneak 0.33.
// Real speeds then: Walk 1.24, Trot 6.0, Run 9.9, Sprint 17.6 m/s.
// =====================================================================
[RequireComponent(typeof(BearMovement))]
public class BearAnimator : MonoBehaviour
{
    [Tooltip("Empty = the Animator on this object or on a child.")]
    public Animator animator;

    [Header("Speed")]
    [Tooltip("Smoothing of Speed, s: clips blend softly instead of switching at once.")]
    public float speedDampTime = 0.15f;

    [Header("Size (15.11)")]
    [Tooltip("How much slower all clips play because the bear is bigger than the asset's bear. 0 = auto: 1 / sqrt(scale), 1.8 -> 0.745. Can be changed in Play mode.")]
    public float sizeTimeScale = 0f;

    [Header("Sneak")]
    [Tooltip("Sneak shows only up to this speed, m/s. It turns off above this + 0.5, so it does not flicker at the border.")]
    public float sneakMaxSpeed = 2.5f;

    [Header("Debug")]
    [Tooltip("Once a second: [Anim] speed and sneak - to tune the blend tree thresholds.")]
    public bool logSpeed = false;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int SneakId = Animator.StringToHash("Sneak");

    private BearMovement movement;
    private bool sneakShown;
    private float logTimer;

    private void Awake()
    {
        movement = GetComponent<BearMovement>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
            Debug.LogWarning("BearAnimator: no Animator found on the bear.");
    }

    private void Update()
    {
        if (animator == null)
            return;

        float speed = movement.CurrentSpeed;

        // Size: clips slower by k, the blend tree gets speed / k, so a
        // clip plays exactly at the speed its paws need (no sliding).
        float k = TimeScale();
        animator.speed = k;

        // Speed -> blend tree. Damped: the value slides to the new speed
        // over ~speedDampTime instead of jumping.
        animator.SetFloat(SpeedId, speed / k, speedDampTime, Time.deltaTime);

        // Sneak with a small gap: on below sneakMaxSpeed,
        // off above sneakMaxSpeed + 0.5.
        if (!movement.Sneaking)
            sneakShown = false;
        else if (speed <= sneakMaxSpeed)
            sneakShown = true;
        else if (speed > sneakMaxSpeed + 0.5f)
            sneakShown = false;

        animator.SetBool(SneakId, sneakShown);

        if (logSpeed)
            LogSpeed(speed, k);
    }

    // Size Time Scale, or the auto value 1 / sqrt(scale).
    private float TimeScale()
    {
        if (sizeTimeScale > 0f)
            return sizeTimeScale;

        float scale = Mathf.Max(0.01f, animator.transform.lossyScale.x);
        return 1f / Mathf.Sqrt(scale);
    }

    private void LogSpeed(float speed, float k)
    {
        logTimer -= Time.deltaTime;
        if (logTimer > 0f)
            return;

        logTimer = 1f;
        Debug.Log($"[Anim] speed {speed:F2} m/s (blend {speed / k:F2}, clips x{k:F3}), sneak {(sneakShown ? 1 : 0)}");
    }
}
