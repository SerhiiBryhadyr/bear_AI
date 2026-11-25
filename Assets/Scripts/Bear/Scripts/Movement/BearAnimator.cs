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
// 25.11: it crouches only after CREEPING for sneakHoldTime. Before, every
// stop after a trot (and every start) passed through the sneak band for
// a moment: the Sneak state showed the crouched clip while the damped
// Speed fell, then Stand_Idle - a ~0.5 s neck dip, "a twitch".
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
    [Tooltip("Sneak shows only up to this speed, m/s. It turns off above this + 0.5, so it does not flicker at the border. 0 = sneak off (21.11, until D-63; before 25.11 it still came on at a full stop).")]
    public float sneakMaxSpeed = 2.5f;
    [Tooltip("Slower than this (m/s) = it stands, does not creep.")]
    public float sneakMinSpeed = 0.15f;
    [Tooltip("It crouches only after creeping this long (s) and goes back to the normal state after standing this long. A quick stop or start passes through without a dip (25.11).")]
    public float sneakHoldTime = 0.5f;

    [Header("Debug")]
    [Tooltip("Once a second: [Anim] speed and sneak - to tune the blend tree thresholds.")]
    public bool logSpeed = false;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int SneakId = Animator.StringToHash("Sneak");

    private BearMovement movement;
    private bool sneakShown;
    private float sneakChangeTimer;     // how long the wanted posture differs from the shown one
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

        UpdateSneak(speed);
        animator.SetBool(SneakId, sneakShown);

        if (logSpeed)
            LogSpeed(speed, k);
    }

    // Sneak (25.11). Off at once when the state stops sneaking or the bear
    // runs (a trot in the sneak clip slides). Otherwise the posture
    // changes only when the wanted one holds sneakHoldTime: creeping ->
    // crouch; standing -> the normal state (both show Stand_Idle at 0,
    // so that switch is not seen, and a start into a trot stays upright).
    // Between sneakMaxSpeed and +0.5 - keeps what it shows (no flicker).
    private void UpdateSneak(float speed)
    {
        if (!movement.Sneaking || speed > sneakMaxSpeed + 0.5f)
        {
            sneakShown = false;
            sneakChangeTimer = 0f;
            return;
        }

        bool want;
        if (speed <= sneakMinSpeed)
            want = false;
        else if (speed <= sneakMaxSpeed)
            want = true;
        else
            want = sneakShown;

        if (want == sneakShown)
        {
            sneakChangeTimer = 0f;
            return;
        }

        sneakChangeTimer += Time.deltaTime;
        if (sneakChangeTimer < sneakHoldTime)
            return;

        sneakShown = want;
        sneakChangeTimer = 0f;
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
