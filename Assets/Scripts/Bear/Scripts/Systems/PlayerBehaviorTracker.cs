using UnityEngine;

// =====================================================================
// PlayerBehaviorTracker
// Observes WHAT the player is doing relative to the bear.
// Only facts (standing, running, looking...), no scoring.
// PlayerDominance turns these facts into a score; later other systems
// (bear patterns, habituation layer) can read the same facts.
// Lives on the Player object, next to PlayerAwareness.
// =====================================================================
public class PlayerBehaviorTracker : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Leave empty if the bear is in another scene - it is found by its BearAI component.")]
    [SerializeField] private Transform bear;

    [Header("Looking")]
    [Tooltip("The bear is farther than this angle from where the camera looks = back turned to it.")]
    [Range(0f, 180f)]
    public float backTurnedAngle = 120f;

    [Header("Movement")]
    public float standingSpeedThreshold = 0.3f;
    [Tooltip("Used only if PlayerController is missing.")]
    public float fallbackRunSpeed = 6f;
    [Range(0f, 1f)]
    public float towardBearDot = 0.5f;       // 0.5 = within ~60 deg of bear direction

    [Header("Camera")]
    [Tooltip("Smoothing of camera rotation speed, seconds. Single twitches are ignored.")]
    public float cameraSmoothingTime = 0.5f;

    // ---------------------------------------------------------------
    // Facts for other systems.
    // ---------------------------------------------------------------
    public bool HasBear => bear != null;
    public float DistanceToBear { get; private set; } = float.MaxValue;

    public bool IsLookingAtBear { get; private set; }
    public bool IsBackTurned { get; private set; }

    public bool IsStanding { get; private set; }
    public bool IsBackingAway { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsMovingTowardBear { get; private set; }

    // Normal movement: not standing, not running, not careful retreat
    // (plain walking 4 m/s, plain S 3 m/s).
    public bool IsWalking => !IsStanding && !IsRunning && !IsBackingAway;

    public float CameraAngularSpeed { get; private set; }   // smoothed, deg/s

    // Stub until the flashlight exists (D-01).
    public bool IsFlashlightOnBear => false;

    private PlayerAwareness playerAwareness;
    private PlayerController playerController;
    private PlayerSightOfBear sight;
    private Vector3 lastCameraForward;

    // ---------------------------------------------------------------
    // Setup: find own components, camera and the bear.
    // ---------------------------------------------------------------
    private void Awake()
    {
        playerAwareness = GetComponent<PlayerAwareness>();
        playerController = GetComponent<PlayerController>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        // "Looking at the bear" now comes from the D-16 fact.
        sight = GetComponent<PlayerSightOfBear>();
        if (sight == null)
        {
            Debug.LogWarning("PlayerBehaviorTracker: PlayerSightOfBear is missing on Player - added automatically. " +
                             "Add it in the Inspector to keep its settings.");
            sight = gameObject.AddComponent<PlayerSightOfBear>();
        }
    }

    private void Start()
    {
        if (bear == null)
        {
            TryFindBear();

            if (bear == null)
                Debug.LogWarning("PlayerBehaviorTracker: BearAI not found yet - will retry every second (bear scene may load later).");
        }

        if (playerAwareness == null)
            Debug.LogError("PlayerBehaviorTracker: PlayerAwareness is missing on Player.");

        if (playerCamera == null)
            Debug.LogError("PlayerBehaviorTracker: player camera not found. Assign it in the Inspector.");
        else
            lastCameraForward = playerCamera.transform.forward;
    }

    private void Update()
    {
        UpdateCameraSpeed();

        if (bear == null)
        {
            RetryFindBear();
            return;
        }

        if (playerAwareness == null || playerCamera == null)
            return;

        Vector3 directionToBear = bear.position - transform.position;
        directionToBear.y = 0f;

        DistanceToBear = directionToBear.magnitude;

        if (DistanceToBear < 0.01f)
            return;

        directionToBear /= DistanceToBear;

        UpdateLooking(directionToBear);
        UpdateMovement(directionToBear);
    }

    // ---------------------------------------------------------------
    // Bear search: BearAI is searched in ALL loaded scenes, so the bear
    // can live in a separate scene (code and assets are split for git).
    // ---------------------------------------------------------------
    private float bearSearchTimer;

    private void TryFindBear()
    {
        BearAI bearAI = FindFirstObjectByType<BearAI>();

        if (bearAI != null)
        {
            bear = bearAI.transform;
            Debug.Log("PlayerBehaviorTracker: bear found - " + bear.name);
        }
    }

    private void RetryFindBear()
    {
        bearSearchTimer -= Time.deltaTime;
        if (bearSearchTimer > 0f)
            return;

        bearSearchTimer = 1f;
        TryFindBear();
    }

    // ---------------------------------------------------------------
    // Camera: how many degrees per second the view turns (smoothed).
    // Counts every movement, back and forth, so small nervous
    // twitching also adds up.
    // ---------------------------------------------------------------
    private void UpdateCameraSpeed()
    {
        if (playerCamera == null || Time.deltaTime <= 0f)
            return;

        Vector3 forward = playerCamera.transform.forward;
        float rawSpeed = Vector3.Angle(lastCameraForward, forward) / Time.deltaTime;
        lastCameraForward = forward;

        float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(cameraSmoothingTime, 0.01f));
        CameraAngularSpeed = Mathf.Lerp(CameraAngularSpeed, rawSpeed, blend);
    }

    // ---------------------------------------------------------------
    // Looking (D-16): the player really SEES the bear - it is in the
    // frame, not hidden by a rock, and he looked long enough to notice
    // it (PlayerSightOfBear). The camera merely pointing that way is
    // not enough any more. Held ~1 s, so a quick glance aside does not
    // flip dominance.
    // Back turned: still by angle - it is about the body, not sight.
    // ---------------------------------------------------------------
    private void UpdateLooking(Vector3 directionToBear)
    {
        IsLookingAtBear = sight != null && sight.IsWatchingBear;

        Vector3 cameraForward = playerCamera.transform.forward;
        cameraForward.y = 0f;

        if (cameraForward.sqrMagnitude < 0.001f)
            return;   // looking straight up/down - keep last value

        float viewAngle = Vector3.Angle(cameraForward.normalized, directionToBear);
        IsBackTurned = viewAngle >= backTurnedAngle;
    }

    // ---------------------------------------------------------------
    // Movement: standing / careful retreat / running / toward the bear.
    // Running and retreat come straight from PlayerController flags.
    // ---------------------------------------------------------------
    private void UpdateMovement(Vector3 directionToBear)
    {
        float speed = playerAwareness.Speed;
        IsStanding = speed < standingSpeedThreshold;

        float dotToBear = Vector3.Dot(playerAwareness.Direction, directionToBear);
        IsMovingTowardBear = !IsStanding && dotToBear > towardBearDot;

        if (playerController != null)
        {
            IsRunning = !IsStanding && playerController.IsRunning;

            // S+Shift counts only if it actually takes the player away from the bear.
            IsBackingAway = !IsStanding && playerController.IsBackingAway && !IsMovingTowardBear;
        }
        else
        {
            IsRunning = speed >= fallbackRunSpeed;
            IsBackingAway = false;
        }
    }
}
