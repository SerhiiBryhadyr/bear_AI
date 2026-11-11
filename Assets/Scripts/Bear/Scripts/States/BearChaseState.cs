using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearChaseState - mode Knows, the player runs away
// (canon 03: "the player runs from the bear -> chase").
// Started by Tracking (the bear SEES the player moving away faster than
// chaseStartSpeed) or by a pattern (D-52) - the pattern calls Prepare()
// right before switching here.
//
// 1. Runs at chaseSpeed (a bit faster than the player's run, 8 m/s):
//    in the open you cannot outrun the bear - only cover and silence help.
// 2. Target (04, sections 19 and 21 - Direct Pursuit):
//    sees the player  -> the point where he will be in predictionTime;
//    does not see him -> the last known point from BearKnowledge
//                        (by ear it is only an area, D-20).
// 3. Chase from Tracking (as before): caught up -> stops, faces the
//    player, logs it (no attack yet). Seen slower than calmSpeed for
//    calmTime -> back to Tracking.
// 4. Chase from a pattern (D-52) ends only one of two ways - the
//    decision is made, it does not calm down:
//    - MERCY (chance by mood, given by the pattern): at a random moment
//      or when close, the bear veers off into the forest and lets the
//      player go (BearKnowledge.Release -> patrol);
//    - ATTACK: caught up -> log ATTACK (placeholder: there is no attack
//      yet), stands attackHold seconds, then lets the player go.
//    It loses the player after patternLoseContactTime without sight and
//    sound (not 3 s: 10.11 it gave up when he flashed behind a tree).
// Losing the player is decided by BearKnowledge (D-20) -> BearBrain ->
// SearchLastKnown, in both cases.
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
    [Tooltip("The bear stops this close (m). From Tracking it waits; from a pattern - ATTACK (placeholder).")]
    public float catchDistance = 2f;
    public float turnSpeed = 360f;          // deg/s when facing the player in place

    [Header("End of chase (from Tracking)")]
    [Tooltip("The player is seen moving slower than this (m/s)...")]
    public float calmSpeed = 5f;
    [Tooltip("...for this many seconds -> back to Tracking.")]
    public float calmTime = 1.5f;

    [Header("Chase from a pattern (D-52)")]
    [Tooltip("Mercy comes at a random moment this long after the start (s)...")]
    public Vector2 mercyAfter = new Vector2(4f, 7f);
    [Tooltip("...or earlier, when the bear gets this close (m).")]
    public float mercyDistance = 5f;
    [Tooltip("Mercy: the bear runs this far from the player (m), to its side, into the forest.")]
    public Vector2 mercyRunDistance = new Vector2(25f, 35f);
    [Tooltip("Mercy run at most this long (s), then it lets the player go.")]
    public float mercyRunTime = 6f;
    [Tooltip("Attack placeholder: stands this long (s) after catching up, then lets the player go.")]
    public float attackHold = 2f;
    [Tooltip("A chase from a pattern loses the player after this long without sight and sound (s). From Tracking - the normal 3 s (D-20).")]
    public float patternLoseContactTime = 6f;

    private enum ChaseMode { FromTracking, FromPattern, Mercy, Attacked }

    private ChaseMode mode;
    private float repathTimer;
    private float calmTimer;
    private bool caught;

    // Given by a pattern before the switch (Prepare).
    private bool prepared;
    private float preparedMercy;
    private string reason = "";

    private bool willSpare;
    private float spareAt;
    private Vector3 mercyPoint;
    private float timer;

    // ---------------------------------------------------------------
    // A pattern calls this RIGHT BEFORE ChangeState(Chase):
    // mercyChance 0 = attack for sure, 0.95 = almost always spares.
    // ---------------------------------------------------------------
    public void Prepare(float mercyChance, string why)
    {
        prepared = true;
        preparedMercy = mercyChance;
        reason = why;
    }

    // ---------------------------------------------------------------
    // Start / end: fast running only inside this state.
    // ---------------------------------------------------------------
    private void OnEnable()
    {
        caught = false;
        calmTimer = 0f;
        repathTimer = 0f;
        Movement.SetSpeed(chaseSpeed);

        if (prepared)
        {
            prepared = false;
            mode = ChaseMode.FromPattern;
            Knowledge.SetLoseContactTime(patternLoseContactTime);
            willSpare = Random.value < preparedMercy;
            spareAt = Time.time + Random.Range(mercyAfter.x, mercyAfter.y);

            Debug.Log("[Chase] started by the pattern: " + reason + ", " + DistanceToPlayer().ToString("F1") +
                      " m, mercy chance " + Mathf.RoundToInt(preparedMercy * 100f) + "% -> " +
                      (willSpare ? "will SPARE him" : "will NOT spare him"));
            return;
        }

        mode = ChaseMode.FromTracking;
        reason = "the player runs away";
        Debug.Log("[Chase] started: the player runs away, " + DistanceToPlayer().ToString("F1") + " m");
    }

    private void OnDisable()
    {
        prepared = false;

        if (bearAI == null)
            return;

        if (mode != ChaseMode.FromTracking && Knowledge != null)
            Knowledge.ResetLoseContactTime();
        if (Movement != null)
            Movement.SetSpeedMultiplier(1f);
    }

    // ---------------------------------------------------------------
    // Every frame: mercy run / attack hold -> calmed down (Tracking
    // chase only) -> time to spare (pattern chase) -> caught up -> run.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!Knowledge.HasLastKnownPosition)
            return;

        if (mode == ChaseMode.Mercy)
        {
            UpdateMercy();
            return;
        }

        if (mode == ChaseMode.Attacked)
        {
            UpdateAttackHold();
            return;
        }

        if (mode == ChaseMode.FromTracking && PlayerCalmedDown())
        {
            Debug.Log("[Chase] the player stopped running -> Tracking");
            ChangeState(BearState.Tracking);
            return;
        }

        if (mode == ChaseMode.FromPattern && willSpare &&
            (Time.time >= spareAt || DistanceToPlayer() <= mercyDistance))
        {
            StartMercy();
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
    // Close enough. From Tracking: stop and face (+1 m margin so the
    // bear does not twitch between "run" and "stop"). From a pattern:
    // ATTACK - a placeholder until the attack exists.
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

        if (mode == ChaseMode.FromPattern)
        {
            mode = ChaseMode.Attacked;
            timer = attackHold;
            Movement.Stop();
            Debug.Log("[Chase] ATTACK at " + distance.ToString("F1") + " m (placeholder - there is no attack yet): " + reason);
            return true;
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

    // ---------------------------------------------------------------
    // Mercy (D-52): sharply veers off to its own side of the player's
    // run and a bit back, into the forest; there it lets him go.
    // ---------------------------------------------------------------
    private void StartMercy()
    {
        Vector3 player = Knowledge.LastKnownPosition;
        Vector3 away = Flat(transform.position - player);
        if (away.sqrMagnitude < 0.01f)
            away = -transform.forward;
        away.Normalize();

        Vector3 run = Flat(Knowledge.LastKnownVelocity);
        Vector3 side = Vector3.Cross(Vector3.up, run.sqrMagnitude > 0.25f ? run.normalized : away);
        if (Vector3.Dot(side, away) < 0f)
            side = -side;                                   // its own side, not across the player

        Vector3 direction = (side + away * 0.5f).normalized;
        float distance = Random.Range(mercyRunDistance.x, mercyRunDistance.y);
        mercyPoint = player + direction * distance;

        if (NavMesh.SamplePosition(mercyPoint, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            mercyPoint = hit.position;
        else
            mercyPoint = transform.position + direction * 15f;

        mode = ChaseMode.Mercy;
        timer = mercyRunTime;
        Movement.SetSpeed(chaseSpeed * 0.8f);
        Movement.MoveTo(mercyPoint);

        Debug.Log("[Chase] MERCY at " + DistanceToPlayer().ToString("F1") + " m: veers off into the forest (" + reason + ")");
    }

    private void UpdateMercy()
    {
        timer -= Time.deltaTime;

        if (timer > 0f && Flat(mercyPoint - transform.position).magnitude > 2f)
            return;

        Knowledge.Release("mercy after a chase (" + reason + ")");
    }

    // Attack placeholder: stands, faces the player, then lets him go.
    private void UpdateAttackHold()
    {
        Movement.FaceTowards(Knowledge.LastKnownPosition, turnSpeed);

        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        Knowledge.Release("after the attack (placeholder)");
    }

    private float DistanceToPlayer()
    {
        return Flat(Knowledge.LastKnownPosition - transform.position).magnitude;
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
