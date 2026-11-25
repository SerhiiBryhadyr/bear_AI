using UnityEngine;

// =====================================================================
// BearBoundaries - the bear's boundary (D-70, close part; D-75).
// The player SEES the bear (his body points at it, lookAngle for
// lookTime), WALKS AT it (really came approachGain closer) and is
// closer than boundaryDistance from its eyes -> provocation:
//   Good           -> the last warning: a roar and the face-off. It
//                     stands; he keeps coming -> attack (the face-off
//                     already does that); he stops / backs away -> it
//                     leaves.
//   Medium / Angry -> a roar and an attack (chase without mercy).
// Works in stalking, Tracking (the Encounter), the back-off / flee /
// calm phases of the close encounter and WHILE THE BEAR LETS HIM GO:
// ignoring does not protect against walking at the bear. The face-off,
// pattern 1 and the chase check this themselves.
// While it ignores him BearKnowledge does not look, so here the bear's
// eyes are asked directly (only when he is near); the attack then ends
// the ignoring.
// Later (D-70 / D-71): farther than the boundary by mood, the bluff
// charge, bearPressure.
// =====================================================================
[DefaultExecutionOrder(-30)]    // after BearKnowledge (-50) and BearPlayerReaction (-45), before the states
public class BearBoundaries : MonoBehaviour
{
    [Header("Boundary (D-70 / D-75)")]
    [Tooltip("Closer than this (m, from the eyes) and walking at the bear = provocation.")]
    public float boundaryDistance = 12f;
    [Tooltip("He looks at the bear: his body points at it within this angle (deg)...")]
    public float lookAngle = 45f;
    [Tooltip("...for this long (s).")]
    public float lookTime = 0.3f;
    [Tooltip("'Walks at the bear': at least this fast (m/s) toward it...")]
    public float approachSpeed = 1f;
    [Tooltip("...within this angle of the direction to the bear (deg)...")]
    public float approachAngle = 60f;
    [Tooltip("...and really came this much closer (m) since he started (turning on the move is no reason, 11.11).")]
    public float approachGain = 2f;
    [Tooltip("A stop / turn shorter than this (s) does not reset the approach.")]
    public float approachGrace = 0.3f;

    [Header("Sound")]
    [Tooltip("Roar before the warning / attack. Empty -> the warning roar of BearRearPursuitState.")]
    public AudioClip roarClip;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Debug")]
    [Tooltip("Log [BoundaryTick] every tickInterval s while he is near and seen: distance, look, approach.")]
    public bool logTick = true;
    public float tickInterval = 0.5f;

    private BearAI bearAI;
    private BearKnowledge knowledge;
    private BearVision vision;
    private BearBodyPoints body;
    private BearMood mood;
    private BearFaceOffState faceOff;
    private BearChaseState chase;
    private BearCloseEncounterState closeEncounter;
    private BearRearPursuitState rearPursuit;
    private BearVoice voice;
    private PlayerAwareness awareness;

    private float lookTimer;
    private float approachFrom = -1f;   // distance when he started walking at the bear (-1 = he does not)
    private float notTowardTimer;
    private float tickTimer;

    private AudioClip Roar => roarClip != null ? roarClip : (rearPursuit != null ? rearPursuit.warningRoarClip : null);

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        knowledge = GetComponent<BearKnowledge>();
        vision = GetComponent<BearVision>();
        body = GetComponent<BearBodyPoints>();
        mood = GetComponent<BearMood>();
        faceOff = GetComponent<BearFaceOffState>();
        chase = GetComponent<BearChaseState>();
        closeEncounter = GetComponent<BearCloseEncounterState>();
        rearPursuit = GetComponent<BearRearPursuitState>();
        voice = BearVoice.On(gameObject);

        if (bearAI == null || knowledge == null || vision == null)
            Debug.LogError("BearBoundaries: needs BearAI, BearKnowledge and BearVision on the bear.");
        if (chase == null || faceOff == null)
            Debug.LogError("BearBoundaries: needs BearChaseState and BearFaceOffState on the bear.");
    }

    // ---------------------------------------------------------------
    // Every frame: does the boundary apply now -> does he look at the
    // bear -> does he walk at it -> closer than the boundary?
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!Applies() || knowledge.Player == null)
        {
            ResetReading();
            return;
        }

        Transform player = knowledge.Player;
        float distance = DistanceFromEyes(player.position);

        // Far away there is nothing to check (no extra eye rays).
        if (distance > boundaryDistance + 10f)
        {
            ResetReading();
            return;
        }

        bool sees = knowledge.IsIgnoringPlayer ? vision.CanSeePlayer() : knowledge.IsSeeingPlayer;
        if (!sees)
        {
            ResetReading();
            return;
        }

        Vector3 toBear = Flat(transform.position - player.position);
        float facing = Vector3.Angle(Flat(player.forward), toBear);
        lookTimer = facing <= lookAngle ? lookTimer + Time.deltaTime : 0f;

        bool comes = ComesAtBear(player, toBear, distance);
        Tick(distance, facing);

        if (lookTimer >= lookTime && comes && distance <= boundaryDistance)
            Cross(distance);
    }

    // [BoundaryTick]: why it did or did not cross, every tickInterval s.
    private void Tick(float distance, float facing)
    {
        if (!logTick)
            return;

        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f)
            return;

        tickTimer = tickInterval;
        float speed = awareness != null ? Flat(awareness.Velocity).magnitude : 0f;
        Debug.Log("[BoundaryTick] " + bearAI.StateMachine.CurrentState + (knowledge.IsIgnoringPlayer ? " (lets him go)" : "") +
                  " | " + distance.ToString("F1") + "/" + boundaryDistance.ToString("F0") + " m" +
                  " | facing " + facing.ToString("F0") + " deg, looks " + lookTimer.ToString("F1") + "/" + lookTime.ToString("F1") + " s" +
                  " | his speed " + speed.ToString("F1") + " m/s, came closer " +
                  (approachFrom >= 0f ? (approachFrom - distance).ToString("F1") : "-") + "/" + approachGain.ToString("F0") + " m");
    }

    // Where the boundary works (see the header).
    private bool Applies()
    {
        if (bearAI == null || knowledge == null || vision == null || chase == null || faceOff == null)
            return false;

        BearState state = bearAI.StateMachine.CurrentState;

        if (state == BearState.Chase || state == BearState.FaceOff || state == BearState.RearPursuit || state == BearState.Hunt)
            return false;   // they check it themselves

        if (state == BearState.CloseEncounter)
            return closeEncounter != null && closeEncounter.AllowsBoundaries;

        if (knowledge.IsIgnoringPlayer)
            return true;    // letting him go does not protect him

        return state == BearState.Stalk || state == BearState.Tracking;
    }

    // He walks at the bear AND has really come approachGain closer
    // since he started. A short stop or turn (approachGrace) is no reset.
    private bool ComesAtBear(Transform player, Vector3 toBear, float distance)
    {
        if (awareness == null)
            awareness = player.GetComponent<PlayerAwareness>();

        Vector3 velocity = awareness != null ? Flat(awareness.Velocity) : Vector3.zero;
        bool toward = velocity.magnitude >= approachSpeed &&
                      toBear.sqrMagnitude > 0.01f &&
                      Vector3.Angle(velocity, toBear) <= approachAngle;

        if (toward)
        {
            notTowardTimer = 0f;
            if (approachFrom < 0f)
                approachFrom = distance;
        }
        else
        {
            notTowardTimer += Time.deltaTime;
            if (notTowardTimer >= approachGrace)
                approachFrom = -1f;
        }

        return approachFrom >= 0f && approachFrom - distance >= approachGain;
    }

    // ---------------------------------------------------------------
    // He crossed the boundary: Good - the last warning (face-off),
    // Medium / Angry - an attack. Both with a roar.
    // ---------------------------------------------------------------
    private void Cross(float distance)
    {
        BearMoodLevel level = mood != null ? mood.Level : BearMoodLevel.Good;
        bool ignoring = knowledge.IsIgnoringPlayer;
        string why = "he walks at the bear, " + distance.ToString("F1") + " m (boundary " + boundaryDistance.ToString("F0") +
                     " m), in " + bearAI.StateMachine.CurrentState + (ignoring ? ", while it let him go" : "");

        if (voice != null)
            voice.Play(Roar, volume);

        if (level == BearMoodLevel.Good)
        {
            Debug.Log("[Boundaries] Good | LAST WARNING (roar" + (Roar == null ? ", no clip" : "") + "): " + why);
            faceOff.Begin(distance, "boundary - last warning: " + why, -1f, true);
            bearAI.StateMachine.ChangeState(BearState.FaceOff);
        }
        else
        {
            Debug.Log("[Boundaries] " + level + " | ATTACK (roar" + (Roar == null ? ", no clip" : "") + "): " + why);
            chase.Prepare(0f, "boundary - " + why);
            bearAI.StateMachine.ChangeState(BearState.Chase);
        }

        // After the switch: BearBrain keeps the new state when the contact comes back.
        knowledge.StopIgnoring("he crossed the boundary");
        ResetReading();
    }

    private void ResetReading()
    {
        lookTimer = 0f;
        approachFrom = -1f;
        notTowardTimer = 0f;
    }

    private float DistanceFromEyes(Vector3 point)
    {
        return body != null && body.Active ? body.DistanceFromMuzzle(point) : Flat(transform.position - point).magnitude;
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
