using UnityEngine;

// =====================================================================
// BearChaseState - mode Knows, the player runs away
// (canon 03: "the player runs from the bear -> chase").
// Tracking switches here when the bear SEES the player moving away
// faster than chaseStartSpeed.
//
// 1. Runs at chaseSpeed (a bit faster than the player's run, 8 m/s):
//    in the open you cannot outrun the bear - only cover and silence help.
// 2. Target (04, sections 19 and 21 - Direct Pursuit):
//    sees the player  -> the point where he will be in predictionTime;
//    does not see him -> the last known point from BearKnowledge
//                        (by ear it is only an area, D-20).
// 3. Caught up (closer than catchDistance): no attack yet - stops,
//    faces the player and logs it.
// 4. The player is seen moving slower than calmSpeed for calmTime
//    seconds -> back to Tracking (reaction by dominance).
//    Losing the player is decided by BearKnowledge (D-20) -> BearBrain
//    -> SearchLastKnown, as from Tracking.
// =====================================================================
public class BearChaseState : BearStateBehaviour
{
    public override BearState Id => BearState.Chase;

    [Header("Speed")]
    [Tooltip("Chase speed, m/s. The player runs 8.")]
    public float chaseSpeed = 9.5f;

    [Header("Target")]
    [Tooltip("Run to where the player will be in this many seconds (only while seeing him).")]
    public float predictionTime = 0.6f;
    [Tooltip("How often the target is updated (s).")]
    public float repathInterval = 0.2f;

    [Header("Caught up")]
    [Tooltip("No attack yet: the bear stops this close (m) and waits.")]
    public float catchDistance = 2f;
    public float turnSpeed = 360f;          // deg/s when facing the player in place

    [Header("End of chase")]
    [Tooltip("The player is seen moving slower than this (m/s)...")]
    public float calmSpeed = 5f;
    [Tooltip("...for this many seconds -> back to Tracking.")]
    public float calmTime = 1.5f;

    private float repathTimer;
    private float calmTimer;
    private bool caught;

    // ---------------------------------------------------------------
    // Start / end: fast running only inside this state.
    // ---------------------------------------------------------------
    private void OnEnable()
    {
        caught = false;
        calmTimer = 0f;
        repathTimer = 0f;

        Movement.SetSpeed(chaseSpeed);
        Debug.Log("[Chase] started: the player runs away, " + DistanceToPlayer().ToString("F1") + " m");
    }

    private void OnDisable()
    {
        if (bearAI != null && Movement != null)
            Movement.SetSpeedMultiplier(1f);
    }

    // ---------------------------------------------------------------
    // Every frame: stopped running? -> caught up? -> run.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!Knowledge.HasLastKnownPosition)
            return;

        if (PlayerCalmedDown())
        {
            Debug.Log("[Chase] the player stopped running -> Tracking");
            ChangeState(BearState.Tracking);
            return;
        }

        if (HandleCaughtUp())
            return;

        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f)
            return;

        repathTimer = repathInterval;
        RunToTarget();
    }

    // Seen slower than calmSpeed long enough. Not seeing = unknown, timer resets.
    private bool PlayerCalmedDown()
    {
        bool seenSlow = Knowledge.IsSeeingPlayer && Knowledge.LastKnownVelocity.magnitude < calmSpeed;
        calmTimer = seenSlow ? calmTimer + Time.deltaTime : 0f;
        return calmTimer >= calmTime;
    }

    // ---------------------------------------------------------------
    // Close enough: stop and face. +1 m margin so the bear does not
    // twitch between "run" and "stop" on the border.
    // ---------------------------------------------------------------
    private bool HandleCaughtUp()
    {
        float distance = DistanceToPlayer();
        bool close = caught ? distance <= catchDistance + 1f : distance <= catchDistance;

        if (!close)
        {
            caught = false;
            return false;
        }

        if (!caught)
        {
            caught = true;
            Movement.Stop();
            Debug.Log("[Chase] CAUGHT UP: " + distance.ToString("F1") + " m (no attack yet)");
        }

        Movement.FaceTowards(Knowledge.LastKnownPosition, turnSpeed);
        return true;
    }

    // Seen -> predicted point; not seen -> last known point (no direction).
    private void RunToTarget()
    {
        Vector3 velocity = Knowledge.IsSeeingPlayer ? Knowledge.LastKnownVelocity : Vector3.zero;
        Movement.MoveTo(Knowledge.LastKnownPosition + velocity * predictionTime);
    }

    private float DistanceToPlayer()
    {
        Vector3 offset = Knowledge.LastKnownPosition - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }
}
