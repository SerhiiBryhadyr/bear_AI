using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearCloseEncounterState - the close encounter (D-75, replaces D-57).
// The bear did not know where the player was and suddenly SEES him
// close by (triggerDistance from the eyes). BearBrain asks TryStart();
// this state decides itself whether it applies.
//
// 1. INSTINCT - only when STARTLED (it had not seen him for
//    surpriseAfter seconds). Above every other decision, whether he
//    sees the bear or not:
//      flee (chance by mood) -> fear sound, runs fleeDistance away,
//                               calms down, then stalking (it comes back
//                               to look; lost by then -> it searches);
//      otherwise             -> maybe a HUFF right at the moment of
//                               sight (involuntary, not a call), then it
//                               FREEZES for freezeTime.
//    Not startled (it saw him a moment ago - it was looking for him):
//    no instinct, only a short read.
// 2. READING - does he see the bear? Only what the bear sees: his body
//    points at it (seesAngle) for seesTime (BearPlayerReaction).
//      he sees it     -> face-off by mood (BearFaceOffState);
//      he does not    -> a roll by mood (goodChoice / mediumChoice):
//        back off     -> RETREATS at a trot (24.11: no show - he does not
//                        see it, so it hurries not to lose him) ->
//                        stalking; he turns and looks at it -> face-off
//        roar         -> roars from behind -> face-off ("settle it here")
//        attack       -> a warning sound, a short pause -> chase without
//                        mercy (never without a warning, D-50)
// 3. After any close encounter the patterns wait patternPause seconds
//    (BearStalkState.PausePatterns): the bear is near, but does not
//    press. "Lets him go" is not used: the player must not stay without
//    the bear for long (D-75).
// Walking at the bear closer than its boundary - BearBoundaries (works
// in the back-off, flee and calm phases of this state too).
// Contact (25.11): he is contactDistance from the bear's body -> attack
// at once, in any phase.
// Angry: a concept for later - uses the Medium numbers for now.
// =====================================================================
public class BearCloseEncounterState : BearStateBehaviour
{
    public override BearState Id => BearState.CloseEncounter;

    [Header("Start (D-75)")]
    [Tooltip("First sight closer than this (m, from the eyes) = close encounter.")]
    public float triggerDistance = 12f;
    [Tooltip("Had not seen him at least this long (s) = startled: the instinct works. For tests ~5.")]
    public float surpriseAfter = 30f;

    [Header("Instinct (startled only)")]
    [Range(0f, 1f)] public float goodFlee = 0.25f;    // 25.11 (D-83): was 0.5 - a Good bear is not a coward
    [Range(0f, 1f)] public float mediumFlee = 0.3f;
    [Range(0f, 1f)] public float angryFlee = 0.1f;
    [Tooltip("Huff right at the moment of sight, if it does not flee.")]
    [Range(0f, 1f)] public float goodHuff = 0.6f;
    [Range(0f, 1f)] public float mediumHuff = 0.3f;
    [Tooltip("Freezes this long (s, random) before it decides.")]
    public Vector2 freezeTime = new Vector2(0.5f, 1.5f);

    [Header("Reading the player")]
    [Tooltip("His body points at the bear within this angle (deg)...")]
    public float seesAngle = 45f;
    [Tooltip("...for this long (s) = he sees the bear.")]
    public float seesTime = 0.3f;
    [Tooltip("Not startled: reads him this long (s) before it decides.")]
    public float readTime = 0.4f;

    [Header("He does not see it: x back off, y roar -> face-off, z attack from behind")]
    public Vector3 goodChoice = new Vector3(0.85f, 0.12f, 0.03f);
    public Vector3 mediumChoice = new Vector3(0.60f, 0.25f, 0.15f);
    [Tooltip("Attack from behind: the warning sound, then this pause (s), then the chase.")]
    public Vector2 attackWarningPause = new Vector2(0.5f, 1f);

    [Header("Retreat when he does not see it (24.11: at a trot, then stalking)")]
    public Vector2 retreatDistance = new Vector2(20f, 30f);
    [Tooltip("m/s; 5.5 = clean trot.")]
    public float retreatSpeed = 5.5f;
    [Tooltip("Good backs off quietly at a walk (m/s) - a confident bear does not trot away from him (D-83, 25.11).")]
    public float goodRetreatSpeed = 2.2f;
    public float retreatMaxTime = 10f;

    [Header("Fleeing")]
    public Vector2 fleeDistance = new Vector2(30f, 40f);
    [Tooltip("m/s; 7 = clean trot (row Trot 7.18).")]
    public float fleeSpeed = 7f;
    public float fleeMaxTime = 10f;
    [Tooltip("After running away it calms down this long (s), then comes back to stalk him.")]
    public Vector2 calmTime = new Vector2(20f, 30f);

    [Header("After the encounter")]
    [Tooltip("Patterns wait this long (s) after any close encounter.")]
    public float patternPause = 60f;
    [Tooltip("While this state runs the player is lost only after this long without sight and sound (s).")]
    public float loseContactTime = 60f;

    [Header("Sounds (empty = only a log line)")]
    public AudioClip huffClip;
    public AudioClip fearClip;
    [Tooltip("Roar from behind / warning before the attack. Empty -> the warning roar of BearRearPursuitState.")]
    public AudioClip warningClip;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Movement")]
    public float turnSpeed = 180f;

    [Header("Contact (25.11)")]
    [Tooltip("He is this close to the bear's body (m, BearBodyPoints) -> attack at once, in any phase.")]
    public float contactDistance = 1f;

    [Header("Debug")]
    [Tooltip("Log [CloseTick] every tickInterval s: phase, distance, what he does, where he looks.")]
    public bool logTick = true;
    public float tickInterval = 0.5f;

    // BearBoundaries works only in the phases where the bear moves away.
    public bool AllowsBoundaries => enabled && (phase == Phase.BackOff || phase == Phase.Flee || phase == Phase.Calm);

    private enum Phase { Freeze, Read, Warn, BackOff, Flee, Calm }

    private BearVision vision;
    private BearPlayerReaction reaction;
    private BearMood mood;
    private BearBodyPoints body;
    private BearCoverFinder cover;
    private BearFaceOffState faceOff;
    private BearChaseState chase;
    private BearStalkState stalk;
    private BearRearPursuitState rearPursuit;
    private BearVoice voice;

    // TryStart() -> OnEnable: what started it.
    private bool hasPending;
    private bool pendingStartled;
    private float pendingDistance;
    private string pendingWhy = "";

    private bool started;
    private bool startled;
    private Phase phase;
    private float phaseStart;
    private float phaseDuration;
    private float seesTimer;
    private float lookedLongest;        // longest look at the bear in this phase (s) - for the log
    private Vector3 target;
    private float tickTimer;

    private float PhaseTime => Time.time - phaseStart;
    private bool PlayerSeesBear => seesTimer >= seesTime;
    private BearMoodLevel Mood => mood != null ? mood.Level : BearMoodLevel.Good;
    private AudioClip Warning => warningClip != null ? warningClip : (rearPursuit != null ? rearPursuit.warningRoarClip : null);

    protected override void Awake()
    {
        base.Awake();
        vision = GetComponent<BearVision>();
        reaction = GetComponent<BearPlayerReaction>();
        mood = GetComponent<BearMood>();
        body = GetComponent<BearBodyPoints>();
        faceOff = GetComponent<BearFaceOffState>();
        chase = GetComponent<BearChaseState>();
        stalk = GetComponent<BearStalkState>();
        rearPursuit = GetComponent<BearRearPursuitState>();
        voice = BearVoice.On(gameObject);

        if (reaction == null)
            Debug.LogError("BearCloseEncounterState: BearPlayerReaction is missing - it cannot tell whether he sees the bear.");
        if (mood == null)
            Debug.LogWarning("BearCloseEncounterState: BearMood is missing - the mood is Good.");
        if (faceOff == null || chase == null || stalk == null)
            Debug.LogError("BearCloseEncounterState: needs BearFaceOffState, BearChaseState and BearStalkState on the bear.");
    }

    // ===============================================================
    // Start: BearBrain asks on the first sight. Applies only close by.
    // ===============================================================
    public bool TryStart(string why)
    {
        if (!Knowledge.IsSeeingPlayer)
            return false;

        float distance = DistanceFromEyes(Knowledge.LastKnownPosition);
        if (distance > triggerDistance)
        {
            Debug.Log("[Close] first sight at " + distance.ToString("F1") + " m from the eyes (" + why + ") - farther than " +
                      triggerDistance.ToString("F0") + " m: no close encounter");
            return false;
        }

        hasPending = true;
        pendingStartled = Knowledge.UnseenBeforeSight >= surpriseAfter;
        pendingDistance = distance;
        pendingWhy = why;

        ChangeState(BearState.CloseEncounter);
        return true;
    }

    // ===============================================================
    // 1. INSTINCT - the first moment.
    // ===============================================================
    private void OnEnable()
    {
        started = true;
        Knowledge.SetLoseContactTime(loseContactTime);
        Movement.SetSneak(false);
        Movement.Stop();
        seesTimer = 0f;

        float distance = hasPending ? pendingDistance : DistanceFromEyes(Knowledge.LastKnownPosition);
        string why = hasPending ? pendingWhy : "enabled without TryStart()";
        startled = hasPending && pendingStartled;
        hasPending = false;
        phase = startled ? Phase.Freeze : Phase.Read;     // for the first log line

        float unseen = Knowledge.UnseenBeforeSight;
        Log("CLOSE ENCOUNTER at " + distance.ToString("F1") + " m from the eyes (" + why + ") - " +
            (startled
                ? "STARTLED (had not seen him " + (float.IsInfinity(unseen) ? "ever" : unseen.ToString("F0") + " s") + ")"
                : "not startled: it saw him " + unseen.ToString("F0") + " s ago, it was looking for him"));

        tickTimer = 0f;

        if (startled && Roll("flee", Chance(goodFlee, mediumFlee, angryFlee)))
        {
            StartFlee();
            return;
        }

        if (startled && Roll("huff", Chance(goodHuff, mediumHuff, 0f)))
            Play(huffClip, "HUFF - startled, involuntary");

        if (startled)
            SetPhase(Phase.Freeze, Random.Range(freezeTime.x, freezeTime.y));
        else
            SetPhase(Phase.Read, readTime);

        Log((startled ? "FREEZES " : "reads him ") + phaseDuration.ToString("F1") + " s");
    }

    private void OnDisable()
    {
        if (!started || bearAI == null)
            return;

        started = false;

        if (vision != null)
            vision.ClearLookTarget();
        if (Knowledge != null)
            Knowledge.ResetLoseContactTime();
        if (Movement != null)
            Movement.SetSpeedMultiplier(1f);
    }

    // ---------------------------------------------------------------
    // Main loop.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!Knowledge.HasLastKnownPosition)
            return;

        Vector3 player = Knowledge.EstimatePlayerPosition(3f);

        // Contact (25.11): he ran into the bear - attack, whatever it was doing.
        if (TouchesPlayer(out float touch))
        {
            Attack("CONTACT at " + touch.ToString("F1") + " m from its body - he ran into it");
            return;
        }

        UpdateSeesTimer();
        Tick(player);

        switch (phase)
        {
            case Phase.Freeze:
            case Phase.Read:
                Stand(player);
                if (PhaseTime >= phaseDuration)
                    Decide();
                break;

            case Phase.Warn:
                Stand(player);
                if (PhaseTime >= phaseDuration)
                    Attack("attack from behind after the warning");
                break;

            case Phase.BackOff: UpdateBackOff(); break;
            case Phase.Flee: UpdateFlee(player); break;
            default: UpdateCalm(player); break;
        }
    }

    // Contact: to where he really is, from the body line.
    private bool TouchesPlayer(out float distance)
    {
        distance = float.PositiveInfinity;
        if (Knowledge.Player == null)
            return false;

        Vector3 p = Knowledge.Player.position;
        distance = body != null && body.Active ? body.DistanceFromBody(p) : FlatDistance(p, transform.position);
        return distance <= contactDistance;
    }

    // Stands, turns its muzzle to him, the head looks at him.
    private void Stand(Vector3 player)
    {
        Movement.Stop();
        Movement.FaceTowards(player, turnSpeed);
        if (vision != null)
            vision.SetLookTarget(player + Vector3.up * 1.2f);
    }

    // He sees the bear = his body points at it, seesTime in a row.
    private void UpdateSeesTimer()
    {
        bool looks = Knowledge.IsSeeingPlayer && reaction != null && reaction.FacingAngle <= seesAngle;
        seesTimer = looks ? seesTimer + Time.deltaTime : 0f;
        lookedLongest = Mathf.Max(lookedLongest, seesTimer);
    }

    // [CloseTick]: the whole situation every tickInterval seconds.
    private void Tick(Vector3 player)
    {
        if (!logTick)
            return;

        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f)
            return;

        tickTimer = tickInterval;
        Debug.Log("[CloseTick] " + Mood + " | " + phase + " " + PhaseTime.ToString("F1") + "/" + phaseDuration.ToString("F1") + " s" +
                  " | " + DistanceFromEyes(player).ToString("F1") + " m from the eyes" +
                  " | sees him: " + (Knowledge.IsSeeingPlayer ? "yes" : "no") +
                  " | he: " + (reaction != null ? reaction.Current.ToString() : "?") +
                  ", facing " + (reaction != null ? reaction.FacingAngle.ToString("F0") : "?") + " deg" +
                  ", looks " + seesTimer.ToString("F1") + " s" +
                  " | bear " + Movement.CurrentSpeed.ToString("F1") + " m/s");
    }

    // ===============================================================
    // 2. READING -> the decision.
    // ===============================================================
    private void Decide()
    {
        Log("READ: longest look at the bear " + lookedLongest.ToString("F1") + " s (needs " + seesTime.ToString("F1") +
            " s within " + seesAngle.ToString("F0") + " deg), now facing " +
            (reaction != null ? reaction.FacingAngle.ToString("F0") : "?") + " deg -> " +
            (PlayerSeesBear ? "HE SEES IT" : "he does not see it"));

        if (PlayerSeesBear)
        {
            ToFaceOff(startled ? "they saw each other" : "he sees the bear");
            return;
        }

        Vector3 choice = Mood == BearMoodLevel.Good ? goodChoice : mediumChoice;
        float total = choice.x + choice.y + choice.z;
        float roll = Random.value * total;
        string pick = total <= 0f || roll < choice.x ? "BACK OFF" : roll < choice.x + choice.y ? "ROAR -> face-off" : "ATTACK from behind";
        Log("choice roll " + (total > 0f ? roll / total : 0f).ToString("F2") + " of [back off " + Pct(choice.x / total) +
            ", roar " + Pct(choice.y / total) + ", attack " + Pct(choice.z / total) + "] -> " + pick);

        if (total <= 0f || roll < choice.x)
        {
            StartBackOff();
        }
        else if (roll < choice.x + choice.y)
        {
            Play(Warning, "ROAR from behind - settles it here");
            ToFaceOff("roared from behind, he did not see it");
        }
        else
        {
            Play(Warning, "WARNING before the attack from behind");
            SetPhase(Phase.Warn, Random.Range(attackWarningPause.x, attackWarningPause.y));
        }
    }

    // --- Back off: walks away out of his sight -> stalking ---
    private void StartBackOff()
    {
        // To cover if there is one (25.11, D-77), else just away from him.
        if (cover == null)
            cover = GetComponent<BearCoverFinder>();
        if (cover == null || !cover.Find(Knowledge.LastKnownPosition, out target))
            target = PointAway(Random.Range(retreatDistance.x, retreatDistance.y));
        float speed = Mood == BearMoodLevel.Good ? goodRetreatSpeed : retreatSpeed;   // D-83: Good - quietly, at a walk
        Movement.SetSpeed(speed);
        Movement.MoveTo(target);
        SetPhase(Phase.BackOff, speed < 3f ? retreatMaxTime * 2f : retreatMaxTime);   // at a walk it needs longer
        Log("RETREATS " + (speed < 3f ? "quietly at a walk " : "at a trot ") + speed.ToString("F1") + " m/s - he did not see it, to a point " +
            FlatDistance(transform.position, target).ToString("F0") + " m away");
    }

    private void UpdateBackOff()
    {
        if (PlayerSeesBear)
        {
            ToFaceOff("he turned and saw it while it backed off");
            return;
        }

        bool arrived = PhaseTime > 0.5f && (Movement.HasReachedDestination() || FlatDistance(transform.position, target) <= 2f);
        if (arrived || PhaseTime >= phaseDuration)
            ToStalk("retreated unseen - it thinks he did not see it");
    }

    // --- Flee: fear, runs away, calms down -> stalking ---
    private void StartFlee()
    {
        Play(fearClip, "FEAR sound");
        target = PointAway(Random.Range(fleeDistance.x, fleeDistance.y));
        Movement.SetSpeed(fleeSpeed);
        Movement.MoveTo(target);
        SetPhase(Phase.Flee, fleeMaxTime);
        Log("FLEES - startled, " + fleeSpeed.ToString("F0") + " m/s to a point " + FlatDistance(transform.position, target).ToString("F0") + " m away");
    }

    private void UpdateFlee(Vector3 player)
    {
        // Runs its way, the head looks back at him (within maxHeadTurn).
        if (vision != null)
            vision.SetLookTarget(player + Vector3.up * 1.2f);

        bool arrived = PhaseTime > 0.5f && (Movement.HasReachedDestination() || FlatDistance(transform.position, target) <= 2f);
        if (!arrived && PhaseTime < phaseDuration)
            return;

        Movement.Stop();
        Movement.SetSpeedMultiplier(1f);
        SetPhase(Phase.Calm, Random.Range(calmTime.x, calmTime.y));
        Log("ran away, calms down " + phaseDuration.ToString("F0") + " s");
    }

    // Stopped after running away: turns to where he probably is and
    // watches him (25.11: it stood with its back to him 20-30 s and lost him).
    private void UpdateCalm(Vector3 player)
    {
        Stand(player);
        if (PhaseTime >= phaseDuration)
            ToStalk("calmed down after fleeing - comes back to look at him");
    }

    // ===============================================================
    // 3. ENDINGS - every one pauses the patterns.
    // ===============================================================
    private void ToFaceOff(string why)
    {
        PausePatterns();
        if (faceOff == null)
        {
            ToStalk(why + " (no BearFaceOffState)");
            return;
        }

        Log("-> FACE-OFF: " + why);
        faceOff.Begin(DistanceFromEyes(Knowledge.LastKnownPosition), "close encounter - " + why);
        ChangeState(BearState.FaceOff);
    }

    private void Attack(string why)
    {
        PausePatterns();
        if (chase == null)
        {
            ToStalk(why + " (no BearChaseState)");
            return;
        }

        Log("-> ATTACK, no mercy: " + why);
        chase.Prepare(0f, "close encounter - " + why);
        ChangeState(BearState.Chase);
    }

    private void ToStalk(string why)
    {
        PausePatterns();
        Log("-> STALKING: " + why);
        ChangeState(stalk != null ? BearState.Stalk : BearState.Patrol);
    }

    private void PausePatterns()
    {
        if (stalk != null)
            stalk.PausePatterns(patternPause, "after a close encounter");
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------
    private void SetPhase(Phase newPhase, float duration)
    {
        phase = newPhase;
        phaseStart = Time.time;
        phaseDuration = duration;
        lookedLongest = 0f;
    }

    // Random roll with the numbers in the log.
    private bool Roll(string what, float chance)
    {
        float roll = Random.value;
        bool yes = roll < chance;
        Log(what + " roll " + roll.ToString("F2") + " vs " + Pct(chance) + " -> " + (yes ? "YES" : "no"));
        return yes;
    }

    private static string Pct(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }

    private float Chance(float good, float medium, float angry)
    {
        switch (Mood)
        {
            case BearMoodLevel.Good: return good;
            case BearMoodLevel.Medium: return medium;
            default: return angry;
        }
    }

    // A point on the NavMesh this far from the bear, away from the player
    // (+-35 deg, so it does not always go the same way).
    private Vector3 PointAway(float distance)
    {
        Vector3 away = Flat(transform.position - Knowledge.LastKnownPosition);
        if (away.sqrMagnitude < 0.01f)
            away = -transform.forward;

        Vector3 direction = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;
        Vector3 point = transform.position + direction * distance;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 8f, NavMesh.AllAreas))
            return hit.position;

        return transform.position + direction * Mathf.Min(distance, 10f);
    }

    private float DistanceFromEyes(Vector3 point)
    {
        return body != null && body.Active ? body.DistanceFromMuzzle(point) : FlatDistance(transform.position, point);
    }

    private void Play(AudioClip clip, string what)
    {
        Log("SOUND: " + what + (clip == null ? " (no clip)" : ""));
        if (clip != null && voice != null)
            voice.Play(clip, volume);
    }

    private void Log(string text)
    {
        Debug.Log("[Close] " + Mood + " | " + phase + " | " + text);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        return Flat(a - b).magnitude;
    }
}
