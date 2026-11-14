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
// =====================================================================
[RequireComponent(typeof(BearMovement))]
public class BearAnimator : MonoBehaviour
{
    [Tooltip("Empty = the Animator on this object or on a child.")]
    public Animator animator;

    [Header("Speed")]
    [Tooltip("Smoothing of Speed, s: clips blend softly instead of switching at once.")]
    public float speedDampTime = 0.15f;

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

        // Speed -> blend tree. Damped: the value slides to the new speed
        // over ~speedDampTime instead of jumping.
        animator.SetFloat(SpeedId, speed, speedDampTime, Time.deltaTime);

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
            LogSpeed(speed);
    }

    private void LogSpeed(float speed)
    {
        logTimer -= Time.deltaTime;
        if (logTimer > 0f)
            return;

        logTimer = 1f;
        Debug.Log($"[Anim] speed {speed:F2} m/s, sneak {(sneakShown ? 1 : 0)}");
    }
}
