using UnityEngine;

public enum DominanceLevel
{
    Low,
    Medium,
    High
}

public class PlayerDominance : MonoBehaviour
{
    [Header("Dominance")]
    [Range(0f, 1f)]
    public float playerDominance = 0.5f;

    public DominanceLevel CurrentLevel { get; private set; }

    [Header("References")]
    [SerializeField] private Camera playerCamera;

    private PlayerAwareness playerAwareness;
    private Transform bear;

    [Header("Looking")]
    [Range(0f, 180f)]
    public float lookAngle = 45f;

    [Range(0f, 180f)]
    public float turnedAwayAngle = 120f;

    [Header("Movement")]
    public float movementThreshold = 0.15f;
    public float retreatDirectionThreshold = 0.5f;

    [Header("Dominance Thresholds")]
    [Range(0f, 1f)]
    public float lowThreshold = 0.35f;

    [Range(0f, 1f)]
    public float highThreshold = 0.65f;

    [Header("Behavior Factors")]
    public float lookingAtBearBonus = 0.1f;
    public float controlledRetreatBonus = 0.15f;
    public float turningAwayPenalty = 0.25f;
    public float lookingAwayPenalty = 0.1f;
    public float movingTowardBearBonus = 0.05f;

    public bool IsLookingAtBear { get; private set; }
    public bool IsBackingAwayWhileWatching { get; private set; }
    public bool IsTurningAwayAndRetreating { get; private set; }

    private void Awake()
    {
        playerAwareness = GetComponent<PlayerAwareness>();

        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    private void Start()
    {
        GameObject bearObject = GameObject.Find("Bear Animated");

        if (bearObject != null)
        {
            bear = bearObject.transform;
        }
        else
        {
            Debug.LogWarning(
                "PlayerDominance: Bear Animated was not found. " +
                "Make sure the BearAI scene is loaded and the bear is active."
            );
        }

        if (playerAwareness == null)
        {
            Debug.LogError(
                "PlayerDominance: PlayerAwareness is missing from Player."
            );
        }

        if (playerCamera == null)
        {
            Debug.LogError(
                "PlayerDominance: Player camera was not found. " +
                "Assign Main Camera in the Inspector."
            );
        }

        UpdateDominanceLevel();
    }

    private void Update()
    {
        if (playerAwareness == null || bear == null || playerCamera == null)
            return;

        CalculateDominance();
    }

    private void CalculateDominance()
    {
        float score = 0.5f;

        Vector3 directionToBear = bear.position - transform.position;
        directionToBear.y = 0f;

        Vector3 cameraForward = playerCamera.transform.forward;
        cameraForward.y = 0f;

        if (directionToBear.sqrMagnitude < 0.001f ||
            cameraForward.sqrMagnitude < 0.001f)
        {
            return;
        }

        directionToBear.Normalize();
        cameraForward.Normalize();

        float viewAngle = Vector3.Angle(
            cameraForward,
            directionToBear
        );

        IsLookingAtBear = viewAngle <= lookAngle;
        IsBackingAwayWhileWatching = false;
        IsTurningAwayAndRetreating = false;

        Vector3 movementDirection = playerAwareness.Direction;
        float speed = playerAwareness.Speed;

        bool isMoving = speed >= movementThreshold;
        float movementDot = 0f;

        if (isMoving && movementDirection.sqrMagnitude > 0.001f)
        {
            movementDirection.y = 0f;
            movementDirection.Normalize();

            // Positive: moving away from the bear.
            // Negative: moving toward the bear.
            Vector3 directionAwayFromBear = -directionToBear;

            movementDot = Vector3.Dot(
                movementDirection,
                directionAwayFromBear
            );
        }

        if (IsLookingAtBear)
        {
            score += lookingAtBearBonus;
        }
        else
        {
            score -= lookingAwayPenalty;
        }

        if (isMoving && movementDot > retreatDirectionThreshold)
        {
            if (IsLookingAtBear)
            {
                // The player retreats while maintaining visual contact.
                IsBackingAwayWhileWatching = true;
                score += controlledRetreatBonus;
            }
            else if (viewAngle >= turnedAwayAngle)
            {
                // The player turns their back and moves away.
                IsTurningAwayAndRetreating = true;
                score -= turningAwayPenalty;
            }
            else
            {
                // The player retreats without clearly watching the bear.
                score -= lookingAwayPenalty;
            }
        }
        else if (isMoving && movementDot < -retreatDirectionThreshold)
        {
            // Moving toward the bear is not the same as fleeing.
            // Running toward the bear will be handled separately later.
            if (IsLookingAtBear)
                score += movingTowardBearBonus;
        }

        playerDominance = Mathf.Clamp01(score);

        UpdateDominanceLevel();
    }

    private void UpdateDominanceLevel()
    {
        if (playerDominance < lowThreshold)
        {
            CurrentLevel = DominanceLevel.Low;
        }
        else if (playerDominance <= highThreshold)
        {
            CurrentLevel = DominanceLevel.Medium;
        }
        else
        {
            CurrentLevel = DominanceLevel.High;
        }
    }
}