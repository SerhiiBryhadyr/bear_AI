using UnityEngine;

// =====================================================================
// BearTrackingState
// The bear sees the player and decides HOW CLOSE to come, based on the
// player's dominance level (PlayerDominance, D-01). It never retreats.
//   HIGH   -> stands still, faces the player and watches
//   MEDIUM -> walks slowly toward the player, stops at mediumStopDistance
//   LOW    -> walks toward the player, stops at lowStopDistance (no pushing)
// A chosen reaction is kept at least minReactionTime, so short spikes of
// dominance do not make the bear jerk back and forth.
// Cycle: Tracking -> lost player -> SearchLastKnown -> Patrol.
// =====================================================================
public class BearTrackingState : MonoBehaviour
{
    [Header("Tracking")]
    public float predictionTime = 0.6f;
    public float decisionInterval = 0.3f;
    [Tooltip("A reaction is kept at least this long before switching.")]
    public float minReactionTime = 2f;
    [Tooltip("How long the player may be out of sight before the bear counts it as lost.")]
    public float lostSightGraceTime = 1f;

    [Header("LOW - approach")]
    public float lowStopDistance = 2.5f;
    public float lowSpeedMultiplier = 1f;

    [Header("MEDIUM - cautious approach")]
    public float mediumStopDistance = 6f;
    public float mediumSpeedMultiplier = 0.5f;

    [Header("Facing")]
    public float turnSpeed = 180f;          // deg/s when turning to the player in place

    private enum Reaction { None, Approach, CautiousApproach, Watch }

    private BearAI bearAI;
    private BearVision vision;
    private PlayerAwareness playerAwareness;
    private PlayerDominance playerDominance;

    private Reaction currentReaction;
    private float reactionTime;             // how long the current reaction lasts
    private float decisionTimer;
    private float lostSightTimer;

    // ---------------------------------------------------------------
    // Setup / cleanup.
    // ---------------------------------------------------------------
    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        currentReaction = Reaction.None;
        reactionTime = 0f;
        decisionTimer = 0f;
        lostSightTimer = 0f;

        FindPlayerComponents();

        Debug.Log("Bear entered TRACKING");
    }

    private void OnDisable()
    {
        // Other states walk at normal speed.
        if (bearAI != null && bearAI.Movement != null)
            bearAI.Movement.SetSpeedMultiplier(1f);
    }

    private bool FindPlayerComponents()
    {
        if (vision == null || vision.player == null)
            return false;

        if (playerAwareness == null)
            playerAwareness = vision.player.GetComponent<PlayerAwareness>();

        if (playerDominance == null)
            playerDominance = vision.player.GetComponent<PlayerDominance>();

        return playerAwareness != null;
    }

    // ---------------------------------------------------------------
    // Main loop: vision -> what the reaction does now -> new decision.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!FindPlayerComponents())
            return;

        if (HandleLostSight())
            return;

        reactionTime += Time.deltaTime;
        UpdateCurrentReaction();

        decisionTimer -= Time.deltaTime;
        if (decisionTimer > 0f)
            return;

        decisionTimer = decisionInterval;
        Decide();
    }

    // ---------------------------------------------------------------
    // Vision. The player counts as lost only after lostSightGraceTime
    // without seeing him (the bear may be turning on a curved path).
    // Returns true if the state was changed.
    // ---------------------------------------------------------------
    private bool HandleLostSight()
    {
        if (vision.CanSeePlayer())
        {
            lostSightTimer = 0f;
            bearAI.SetLastKnownPlayerPosition(playerAwareness.CurrentPosition);
            return false;
        }

        lostSightTimer += Time.deltaTime;
        if (lostSightTimer < lostSightGraceTime)
            return false;

        Debug.Log("[Tracking] lost player. Last known position: " + bearAI.LastKnownPlayerPosition);
        bearAI.StateMachine.ChangeState(BearState.SearchLastKnown);
        return true;
    }

    // ---------------------------------------------------------------
    // Decision: reaction for the current level. Switches only after
    // minReactionTime (except the very first choice).
    // Without PlayerDominance the bear approaches, as before.
    // ---------------------------------------------------------------
    private void Decide()
    {
        DominanceLevel level = playerDominance != null ? playerDominance.CurrentLevel : DominanceLevel.Low;
        Reaction wanted = ReactionFor(level);

        bool canSwitch = currentReaction == Reaction.None || reactionTime >= minReactionTime;

        if (wanted != currentReaction && canSwitch)
            SetReaction(wanted, level);

        if (currentReaction == Reaction.Approach)
            UpdateApproachTarget(lowStopDistance, predictionTime);
        else if (currentReaction == Reaction.CautiousApproach)
            UpdateApproachTarget(mediumStopDistance, 0f);
    }

    private Reaction ReactionFor(DominanceLevel level)
    {
        switch (level)
        {
            case DominanceLevel.High: return Reaction.Watch;
            case DominanceLevel.Medium: return Reaction.CautiousApproach;
            default: return Reaction.Approach;
        }
    }

    private void SetReaction(Reaction reaction, DominanceLevel level)
    {
        currentReaction = reaction;
        reactionTime = 0f;

        switch (reaction)
        {
            case Reaction.Watch:
                bearAI.Movement.Stop();
                break;

            case Reaction.CautiousApproach:
                bearAI.Movement.SetSpeedMultiplier(mediumSpeedMultiplier);
                break;

            case Reaction.Approach:
                bearAI.Movement.SetSpeedMultiplier(lowSpeedMultiplier);
                break;
        }

        Debug.Log($"[Tracking] {level} -> {reaction}");
    }

    // ---------------------------------------------------------------
    // Every frame: stop at the right distance and face the player.
    // ---------------------------------------------------------------
    private void UpdateCurrentReaction()
    {
        Vector3 playerPosition = playerAwareness.CurrentPosition;

        switch (currentReaction)
        {
            case Reaction.Watch:
                bearAI.Movement.FaceTowards(playerPosition, turnSpeed);
                break;

            case Reaction.CautiousApproach:
                StopAndFaceIfClose(mediumStopDistance, playerPosition);
                break;

            case Reaction.Approach:
                StopAndFaceIfClose(lowStopDistance, playerPosition);
                break;
        }
    }

    private void StopAndFaceIfClose(float stopDistance, Vector3 playerPosition)
    {
        if (DistanceToPlayer() > stopDistance)
            return;

        bearAI.Movement.Stop();
        bearAI.Movement.FaceTowards(playerPosition, turnSpeed);
    }

    // ---------------------------------------------------------------
    // Approach: walk to a point stopDistance before the (predicted)
    // player position. +0.5 m margin so the bear does not twitch
    // between "walk" and "stop" on the border.
    // ---------------------------------------------------------------
    private void UpdateApproachTarget(float stopDistance, float prediction)
    {
        if (DistanceToPlayer() <= stopDistance + 0.5f)
            return;

        Vector3 predicted = playerAwareness.CurrentPosition + playerAwareness.Velocity * prediction;

        Vector3 fromPlayerToBear = transform.position - predicted;
        fromPlayerToBear.y = 0f;

        Vector3 target = predicted;
        if (fromPlayerToBear.sqrMagnitude > 0.01f)
            target = predicted + fromPlayerToBear.normalized * stopDistance;

        bearAI.Movement.MoveTo(target);
    }

    private float DistanceToPlayer()
    {
        Vector3 offset = playerAwareness.CurrentPosition - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }
}
