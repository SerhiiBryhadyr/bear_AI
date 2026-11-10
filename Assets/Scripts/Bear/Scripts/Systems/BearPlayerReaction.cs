using UnityEngine;

// What the player does relative to the bear - as the bear SEES it.
public enum PlayerAction
{
    Unknown,        // the bear does not see the player
    Stands,         // stands still
    Approaches,     // walks or runs TOWARD the bear
    RunsAway,       // runs (not toward the bear)
    WalksAway,      // walks with his back or side to the bear (also "pretends he did not see")
    BacksAway,      // walks backward facing the bear, normal speed (S)
    BacksAwaySlow   // walks backward facing the bear, slowly (S + Shift, or crouched)
}

// =====================================================================
// BearPlayerReaction - reads WHAT THE PLAYER DOES relative to the bear:
// comes toward it, runs, walks away, backs away (normal / slow), stands.
// Works the whole game, in every state and mood. Pattern reactions
// (D-52) read Current; later other systems can read it too.
//
// Honest (the rule of BearKnowledge): only what the bear SEES - the
// player's velocity and body direction from BearKnowledge, never his
// keys or camera. Does not see him for lostAfter seconds -> Unknown.
//
// How it decides, every frame while the bear sees the player:
//   slower than standSpeed                      -> Stands
//   moves toward the bear (within towardAngle)  -> Approaches
//   faster than runSpeed                        -> RunsAway
//   his body faces the bear (within facingAngle) -> BacksAway, or
//                                                  BacksAwaySlow below slowBackSpeed
//   otherwise (back or side to the bear)        -> WalksAway
// A new action counts only after it lasted confirmTime: one frame of
// noise (a key tap, a bump) does not flip the reading.
// Also: Velocity (smoothed) and TurnRate - how fast he turns his body /
// camera (a jerk = he reacts to something, 10.11).
// =====================================================================
[DefaultExecutionOrder(-45)]    // after BearKnowledge (-50), before the states
public class BearPlayerReaction : MonoBehaviour
{
    [Header("Thresholds")]
    [Tooltip("Slower than this (m/s) = stands.")]
    public float standSpeed = 0.3f;
    [Tooltip("Faster than this (m/s) = runs. Walk 4, run 8.")]
    public float runSpeed = 6f;
    [Tooltip("Backing away slower than this (m/s) = slow retreat. S 3, S + Shift 2, crouch 1.8.")]
    public float slowBackSpeed = 2.5f;
    [Tooltip("Moving within this angle (deg) of the direction to the bear = comes toward it.")]
    public float towardAngle = 60f;
    [Tooltip("The player's body points within this angle (deg) of the bear = faces it.")]
    public float facingAngle = 60f;

    [Header("Stability")]
    [Tooltip("A new action counts only after it lasted this long (s).")]
    public float confirmTime = 0.3f;
    [Tooltip("Velocity smoothing (s): the player's speed is measured every frame and jumps.")]
    public float velocitySmoothing = 0.15f;
    [Tooltip("The bear does not see the player this long (s) -> Unknown.")]
    public float lostAfter = 1f;
    [Tooltip("Smoothing of the turn rate (s).")]
    public float turnSmoothing = 0.15f;

    [Header("Debug")]
    public bool logChanges = true;

    // --- What other scripts read ---
    public PlayerAction Current { get; private set; } = PlayerAction.Unknown;
    public float TimeInCurrent => Time.time - currentSince;     // s since Current started
    public float Speed { get; private set; }                    // smoothed, m/s
    public float FacingAngle { get; private set; } = 180f;      // 0 = his body points straight at the bear
    public float Distance { get; private set; } = float.PositiveInfinity;
    public Vector3 Velocity => smoothVelocity;                  // smoothed, flat, m/s
    public float TurnRate { get; private set; }                 // deg/s his body (camera) turns, smoothed

    private BearKnowledge knowledge;
    private Vector3 smoothVelocity;
    private bool wasSeeing;
    private Vector3 lastLook;

    private PlayerAction candidate = PlayerAction.Unknown;
    private float candidateSince;
    private float currentSince;

    private void Awake()
    {
        knowledge = GetComponent<BearKnowledge>();

        if (knowledge == null)
            Debug.LogError("BearPlayerReaction: BearKnowledge is missing on the bear.");
    }

    // ---------------------------------------------------------------
    // Every frame: measure -> raw action -> confirm -> Current.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (knowledge == null)
            return;

        if (!knowledge.IsSeeingPlayer)
        {
            wasSeeing = false;

            if (Current != PlayerAction.Unknown && knowledge.TimeSinceSeen >= lostAfter)
                SetCurrent(PlayerAction.Unknown);

            candidate = Current;
            candidateSince = Time.time;
            return;
        }

        Measure();

        PlayerAction raw = Classify();

        if (raw != candidate)
        {
            candidate = raw;
            candidateSince = Time.time;
        }

        if (candidate != Current && Time.time - candidateSince >= confirmTime)
            SetCurrent(candidate);
    }

    // Speed (smoothed), distance and where his body points.
    private void Measure()
    {
        bool continuing = wasSeeing;
        Vector3 velocity = knowledge.LastKnownVelocity;

        if (!wasSeeing)
            smoothVelocity = velocity;      // seen again: start from the fresh value
        else
            smoothVelocity = Vector3.Lerp(smoothVelocity, velocity, 1f - Mathf.Exp(-Time.deltaTime / velocitySmoothing));

        wasSeeing = true;
        Speed = smoothVelocity.magnitude;

        Vector3 toBear = Flat(transform.position - knowledge.LastSeenPosition);
        Distance = toBear.magnitude;

        Vector3 look = knowledge.PlayerLookDirection;
        FacingAngle = look == Vector3.zero || toBear.sqrMagnitude < 0.01f ? 180f : Vector3.Angle(look, toBear);

        // Turn rate: only between two frames the bear saw him in.
        if (continuing && lastLook != Vector3.zero && look != Vector3.zero && Time.deltaTime > 0f)
        {
            float rate = Vector3.Angle(lastLook, look) / Time.deltaTime;
            TurnRate = Mathf.Lerp(TurnRate, rate, 1f - Mathf.Exp(-Time.deltaTime / turnSmoothing));
        }
        else
        {
            TurnRate = 0f;
        }

        lastLook = look;
    }

    // The rules from the header, in this order.
    private PlayerAction Classify()
    {
        if (Speed < standSpeed)
            return PlayerAction.Stands;

        Vector3 toBear = Flat(transform.position - knowledge.LastSeenPosition);
        if (toBear.sqrMagnitude > 0.01f && Vector3.Angle(smoothVelocity, toBear) <= towardAngle)
            return PlayerAction.Approaches;

        if (Speed >= runSpeed)
            return PlayerAction.RunsAway;

        if (FacingAngle <= facingAngle)
            return Speed < slowBackSpeed ? PlayerAction.BacksAwaySlow : PlayerAction.BacksAway;

        return PlayerAction.WalksAway;
    }

    private void SetCurrent(PlayerAction action)
    {
        Current = action;
        currentSince = Time.time;

        if (!logChanges)
            return;

        if (action == PlayerAction.Unknown)
            Debug.Log("[Reaction] Unknown (the bear does not see the player)");
        else
            Debug.Log("[Reaction] " + action + " (speed " + Speed.ToString("F1") + " m/s, facing " +
                      FacingAngle.ToString("F0") + " deg, " + Distance.ToString("F1") + " m)");
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
