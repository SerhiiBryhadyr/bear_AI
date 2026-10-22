using UnityEngine;

// =====================================================================
// BearTrackingState - mode Knows, branch "encounter" (D-15).
// The bear knows where the player is and decides HOW CLOSE to come,
// based on the player's dominance level (PlayerDominance, D-01).
// It never retreats.
//   HIGH   -> stands still, faces the player and watches
//   MEDIUM -> walks slowly toward the player, stops at mediumStopDistance
//   LOW    -> walks toward the player, stops at lowStopDistance (no pushing)
// A chosen reaction is kept at least minReactionTime, so short spikes of
// dominance do not make the bear jerk back and forth.
//
// The player's position comes ONLY from BearKnowledge: while the bear
// sees him - the real position; while he is hidden - the last place he
// was seen or heard. Losing the player is decided by BearKnowledge
// (D-20), the switch to SearchLastKnown - by BearBrain.
// =====================================================================
public class BearTrackingState : BearStateBehaviour
{
    public override BearState Id => BearState.Tracking;

    [Header("Tracking")]
    public float predictionTime = 0.6f;
    public float decisionInterval = 0.3f;
    [Tooltip("A reaction is kept at least this long before switching.")]
    public float minReactionTime = 2f;

    [Header("LOW - approach")]
    public float lowStopDistance = 2.5f;
    public float lowSpeedMultiplier = 1f;

    [Header("MEDIUM - cautious approach")]
    public float mediumStopDistance = 6f;
    public float mediumSpeedMultiplier = 0.5f;

    [Header("Facing")]
    public float turnSpeed = 180f;          // deg/s when turning to the player in place

    private enum Reaction { None, Approach, CautiousApproach, Watch }

    private PlayerDominance playerDominance;

    private Reaction currentReaction;
    private float reactionTime;             // how long the current reaction lasts
    private float decisionTimer;

    // ---------------------------------------------------------------
    // Setup / cleanup.
    // ---------------------------------------------------------------
    private void OnEnable()
    {
        currentReaction = Reaction.None;
        reactionTime = 0f;
        decisionTimer = 0f;

        FindPlayerDominance();

        Debug.Log("Bear entered TRACKING (contact: " + Knowledge.LastContactSource + ")");
    }

    private void OnDisable()
    {
        // Other states walk at normal speed.
        if (bearAI != null && Movement != null)
            Movement.SetSpeedMultiplier(1f);
    }

    private void FindPlayerDominance()
    {
        if (playerDominance == null && Knowledge.Player != null)
            playerDominance = Knowledge.Player.GetComponent<PlayerDominance>();
    }

    // ---------------------------------------------------------------
    // Main loop: what the reaction does now -> new decision.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!Knowledge.HasLastKnownPosition)
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
    // Decision: reaction for the current level. Switches only after
    // minReactionTime (except the very first choice).
    // Without PlayerDominance the bear approaches, as before.
    // ---------------------------------------------------------------
    private void Decide()
    {
        FindPlayerDominance();

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
                Movement.Stop();
                break;

            case Reaction.CautiousApproach:
                Movement.SetSpeedMultiplier(mediumSpeedMultiplier);
                break;

            case Reaction.Approach:
                Movement.SetSpeedMultiplier(lowSpeedMultiplier);
                break;
        }

        Debug.Log($"[Tracking] {level} -> {reaction}");
    }

    // ---------------------------------------------------------------
    // Every frame: stop at the right distance and face the player.
    // ---------------------------------------------------------------
    private void UpdateCurrentReaction()
    {
        Vector3 playerPosition = Knowledge.LastKnownPosition;

        switch (currentReaction)
        {
            case Reaction.Watch:
                Movement.FaceTowards(playerPosition, turnSpeed);
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

        Movement.Stop();
        Movement.FaceTowards(playerPosition, turnSpeed);
    }

    // ---------------------------------------------------------------
    // Approach: walk to a point stopDistance before the (predicted)
    // player position. Prediction only while the bear SEES the player:
    // a hidden player gives no direction. +0.5 m margin so the bear
    // does not twitch between "walk" and "stop" on the border.
    // ---------------------------------------------------------------
    private void UpdateApproachTarget(float stopDistance, float prediction)
    {
        if (DistanceToPlayer() <= stopDistance + 0.5f)
            return;

        Vector3 velocity = Knowledge.IsSeeingPlayer ? Knowledge.LastKnownVelocity : Vector3.zero;
        Vector3 predicted = Knowledge.LastKnownPosition + velocity * prediction;

        Vector3 fromPlayerToBear = transform.position - predicted;
        fromPlayerToBear.y = 0f;

        Vector3 target = predicted;
        if (fromPlayerToBear.sqrMagnitude > 0.01f)
            target = predicted + fromPlayerToBear.normalized * stopDistance;

        Movement.MoveTo(target);
    }

    private float DistanceToPlayer()
    {
        Vector3 offset = Knowledge.LastKnownPosition - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }
}
