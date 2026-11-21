using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearRearPursuitState - pattern 1 "pursuit from behind" (canon 03,
// phases 1.2-1.5) with reactions by mood (D-52,
// Ideas/Rear_Pursuit_Moods.md). Started by BearStalkState once the
// analysis has chosen a pattern.
//
// 1. APPROACH: the bear steps out onto the path behind the player only
//    after he has not looked its way for noLookBackTime seconds. He
//    looks its way -> it stands still, the count starts again. No
//    teleport (D-34): it comes from its stalking place.
// 2. PURSUIT: followDistance behind him, speed ~0.9-1.05 of his.
//    Presence grows: 0-5 s silence -> 5-10 s light sounds -> from 10 s
//    closes in to closeDistance -> 15 s warning: Good / Medium a loud
//    sound, Angry none. He does not turn within the window ->
//    Good: runs to the nearest cover and stalks again;
//    Medium / Angry: attack.
//    He RUNS before the warning -> the bear runs along and keeps its
//    distance (he may not know about it at all). He runs in the window
//    AFTER the warning instead of turning -> chase, mercy by mood.
//    The warning comes only when the bear is really close behind.
// 3. NOTICED (10.11), within closeNoticeRange:
//    - SURE: the bear in the middle of his screen (sureLookAngle) for
//      lookTime -> FACE-OFF, any mood;
//    - UNCERTAIN: on his screen (screenAngle) for lookTime, not in the
//      middle. Medium / Angry closer than assumeSeenDistance -> assume
//      the worst: FACE-OFF. Otherwise it watches suspicionTime whether
//      he changes his behaviour (runs, stops, faces it, backs away,
//      turns his course, snaps the camera TOWARD it) -> FACE-OFF; walks
//      on as before -> "he did not see me", the pattern goes on.
//      Already in the middle of his screen -> a sure look, not uncertain.
//    A shorter look -> the bear only freezes (canon 1.5). Farther than
//    closeNoticeRange - the normal opinion of BearKnowledge (D-16, D-42).
// 4. FACE-OFF (21.11): handed over to BearFaceOffState - Begin() and
//    ChangeState(FaceOff). The mood table, LOST behind cover (Medium)
//    and leaving calmly live there. Mercy and "comes at the bear" are
//    read from it, so those numbers are set in one place.
// 5. END here: chase or attack (BearChaseState) - he runs after the
//    warning or does not turn; Good, no turn - runs to cover and stalks
//    again; back to stalking; the face-off. TimeSinceEnded - the pause
//    between patterns, counted from the end of the face-off if there
//    was one (as before 21.11).
// Sounds behind him: twig cracks / steps alternate with growls (logs
// for now). The warning roar - warningRoarClip (any clip for now,
// 21.11). Angry: no growls, no roar.
// =====================================================================
public class BearRearPursuitState : BearStateBehaviour
{
    public override BearState Id => BearState.RearPursuit;

    [Header("Approach (canon 1.2)")]
    [Tooltip("The player must not look the bear's way this long (s) before it steps out behind him.")]
    public float noLookBackTime = 3f;
    [Tooltip("The player's body points within this angle (deg) of the bear = the bear is in his view -> it freezes.")]
    public float viewAngle = 60f;
    [Tooltip("Distance behind the player (m): a random one in this range.")]
    public Vector2 followDistance = new Vector2(12f, 30f);
    [Tooltip("Approach speed = the player's speed + this (m/s).")]
    public float approachSpeedBonus = 2.5f;
    [Tooltip("Could not get behind him in this time (s) -> back to stalking.")]
    public float approachTimeout = 30f;

    [Header("Pursuit (canon 1.4)")]
    [Tooltip("Speed = the player's speed x a random factor in this range.")]
    public Vector2 speedFactor = new Vector2(0.9f, 1.05f);
    [Tooltip("Farther than wanted by more than 2 m -> the player's speed + this (m/s).")]
    public float catchUpBonus = 1.5f;
    [Tooltip("Light sounds from this second of the pursuit.")]
    public float lightSoundsAt = 5f;
    [Tooltip("Closes in to closeDistance from this second.")]
    public float closeInAt = 10f;
    public Vector2 closeDistance = new Vector2(8f, 12f);
    [Tooltip("Warning at this second: Good / Medium - loud sound, Angry - none.")]
    public float warningAt = 15f;
    [Tooltip("Good / Medium: after the warning roar ENDS the player has this long (s) to turn.")]
    public float warningWindow = 5f;
    [Tooltip("Angry: no sound, only this long (s).")]
    public float angryWindow = 2.5f;
    [Tooltip("The warning comes only when the bear is at most this much farther (m) than it wants to be - fallen behind, it first catches up.")]
    public float warningMaxExtra = 5f;
    [Tooltip("He runs this long (s) during the approach / pursuit -> chase (mercy by mood).")]
    public float fleeConfirm = 0.5f;

    [Header("Noticed (close, D-52)")]
    [Tooltip("SURE: the bear in the middle of his screen - within this angle (deg) of his look (camera FOV 60: 20 = the middle third)...")]
    public float sureLookAngle = 20f;
    [Tooltip("...for this long (s) -> he saw it, in any mood. The same time for an uncertain look.")]
    public float lookTime = 0.6f;
    [Tooltip("UNCERTAIN: the bear anywhere on his screen - within this angle (deg). FOV 60 at 16:9 = ~46 to the edge.")]
    public float screenAngle = 45f;
    [Tooltip("Uncertain look, Medium / Angry, the bear closer than this (m) -> assumes the worst: he saw it. Farther, or Good -> watches his behaviour.")]
    public float assumeSeenDistance = 15f;
    [Tooltip("Uncertain look -> watches this long (s) whether he changes his behaviour.")]
    public float suspicionTime = 4f;
    [Tooltip("Behaviour change: his course turned more than this (deg)...")]
    public float courseChangeAngle = 45f;
    [Tooltip("...or he snapped the camera TOWARD the bear faster than this (deg/s; turning away does not count).")]
    public float jerkTurnRate = 250f;
    [Tooltip("...or jerked the camera AWAY from the bear faster than this (deg/s): a startled flick. A normal turn back to the path is ~270-370 deg/s (11.11), 180 deg in 0.25 s = 720.")]
    public float jerkAwayRate = 500f;
    [Tooltip("The close rule works up to this distance (m); farther - the normal opinion of BearKnowledge.")]
    public float closeNoticeRange = 32f;
    [Tooltip("Noticed farther than this (m) during the approach -> back to stalking (D-42), not a face-off.")]
    public float faceOffRange = 32f;
    [Tooltip("The freeze lasts this long after he looks away (s).")]
    public float unfreezeDelay = 0.7f;

    [Header("Retreat (Good, no turn after the warning)")]
    [Tooltip("Good, no reaction to the warning: runs to the nearest cover this far (m)...")]
    public Vector2 coverSearch = new Vector2(8f, 25f);
    public float coverRunSpeed = 6f;
    [Tooltip("Retreat at most this long (s).")]
    public float leaveMaxTime = 8f;

    [Header("Contact")]
    [Tooltip("During the pattern the player is lost after this long without sight and sound (s).")]
    public float loseContactTime = 15f;

    [Header("Movement")]
    public float turnSpeed = 180f;
    public float repathInterval = 0.25f;

    [Header("Sounds (temporary, 21.11)")]
    [Tooltip("The warning roar (Good / Medium). Any clip for now - the bear's real sounds come with D-34. Empty = a log only.")]
    public AudioClip warningRoarClip;
    [Range(0f, 1f)] public float roarVolume = 1f;

    [Header("Debug")]
    [Tooltip("Light sounds and threats every few seconds (logs instead of sounds / animations).")]
    public bool logCues = true;

    private enum Phase { Approach, Pursuit, Retreat }

    // --- Read by BearStalkState (pause between patterns) ---
    // Handed over to the face-off -> the pattern counts as ended when the
    // face-off ends (as before 21.11, when the face-off was a phase here).
    public float TimeSinceEnded => handedToFaceOff && faceOff != null ? faceOff.TimeSinceEnded : Time.time - endedAt;

    private BearVision vision;
    private BearBodyPoints bodyPoints;
    private BearPlayerReaction reaction;
    private BearMood mood;
    private BearChaseState chase;
    private PlayerSightOfBear playerSight;
    private BearFaceOffState faceOff;
    private AudioSource voice;

    private Phase phase;
    private float phaseStart;
    private bool started;
    private float endedAt = -999f;
    private bool handedToFaceOff;       // noticed -> the face-off took over (TimeSinceEnded asks it)

    private float noLookTimer;
    private float centerTimer;
    private float screenTimer;
    private bool suspicious;
    private float suspicionEnd;
    private float sampleAngle = -1f;    // snap toward the bear: angle at the start of a 0.1 s window
    private float sampleTime;
    private float towardRate;           // > 0 toward the bear, < 0 away (deg/s)
    private float maxToward;            // the fastest turns while it watches him (for the log)
    private float maxAway;
    private PlayerAction actionBeforeLook;
    private Vector3 routeBeforeLook;
    private bool frozen;
    private float freezeTimer;
    private int knownNoticedCount;

    private float wantDistance;
    private float speedK;
    private float repathTimer;
    private float cueTimer;
    private int cueCount;
    private bool closedIn;
    private bool warned;
    private float warnEnd;
    private float approachFrom = -1f;   // distance when he started walking toward the bear (-1 = he does not)

    private Vector3 leavePoint;

    private float PhaseTime => Time.time - phaseStart;
    private BearMoodLevel Mood => mood != null ? mood.Level : BearMoodLevel.Good;

    // ---------------------------------------------------------------
    // Setup / start / end.
    // ---------------------------------------------------------------
    protected override void Awake()
    {
        base.Awake();
        vision = GetComponent<BearVision>();
        bodyPoints = GetComponent<BearBodyPoints>();
        reaction = GetComponent<BearPlayerReaction>();
        mood = GetComponent<BearMood>();
        chase = GetComponent<BearChaseState>();
        faceOff = GetComponent<BearFaceOffState>();

        if (reaction == null)
            Debug.LogError("BearRearPursuitState: BearPlayerReaction is missing on the bear.");
        if (mood == null)
            Debug.LogWarning("BearRearPursuitState: BearMood is missing on the bear - the mood is Good.");
        if (chase == null)
            Debug.LogError("BearRearPursuitState: BearChaseState is missing on the bear - attacks and chases will not work.");
        if (faceOff == null)
            Debug.LogError("BearRearPursuitState: BearFaceOffState is missing on the bear - noticed = back to stalking (add the component).");
    }

    private void OnEnable()
    {
        started = true;
        handedToFaceOff = false;
        Knowledge.SetLoseContactTime(loseContactTime);
        knownNoticedCount = Knowledge.NoticedCount;

        SetPhase(Phase.Approach);
        noLookTimer = 0f;
        centerTimer = 0f;
        screenTimer = 0f;
        suspicious = false;
        frozen = false;
        closedIn = false;
        warned = false;
        repathTimer = 0f;

        wantDistance = Random.Range(followDistance.x, followDistance.y);
        speedK = Random.Range(speedFactor.x, speedFactor.y);

        Log("STARTED, " + DistanceToPlayer().ToString("F0") + " m from the player - waits until he does not look back for " +
            noLookBackTime + " s, then steps out " + wantDistance.ToString("F0") + " m behind him");
    }

    private void OnDisable()
    {
        if (!started || bearAI == null)
            return;

        started = false;
        endedAt = Time.time;

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
        if (!Knowledge.HasLastKnownPosition || Knowledge.Player == null || reaction == null)
            return;

        Vector3 player = Knowledge.EstimatePlayerPosition(3f);
        float distance = FlatDistance(player, transform.position);

        if (vision != null)
            vision.SetLookTarget(player + Vector3.up * 1.2f);

        switch (phase)
        {
            case Phase.Approach: UpdateApproach(player, distance); break;
            case Phase.Pursuit: UpdatePursuit(player, distance); break;
            default: UpdateRetreat(distance); break;
        }
    }

    // ===============================================================
    // 1. APPROACH
    // ===============================================================
    private void UpdateApproach(Vector3 player, float distance)
    {
        if (CheckNoticed(distance))
            return;

        if (UpdateFreeze(player))
        {
            noLookTimer = 0f;
            return;
        }

        if (PhaseTime > approachTimeout)
        {
            Log("could not get behind him in " + approachTimeout + " s - back to stalking");
            ChangeState(BearState.Stalk);
            return;
        }

        noLookTimer += Time.deltaTime;

        // Waits in its place until he has not looked back long enough.
        if (noLookTimer < noLookBackTime)
        {
            Movement.Stop();
            Movement.FaceTowards(player, turnSpeed);
            return;
        }

        Vector3 behind = BehindPoint(player, wantDistance);
        Movement.SetSpeed(Mathf.Clamp(PlayerSpeed() + approachSpeedBonus, 2f, 9f));
        Repath(behind);

        if (distance <= wantDistance + 2f && IsBehind(player))
        {
            SetPhase(Phase.Pursuit);
            cueTimer = 0f;
            cueCount = 0;
            Log("behind him at " + distance.ToString("F0") + " m - PURSUIT");
        }
    }

    // ===============================================================
    // 2. PURSUIT: follows, presence grows, warning.
    // ===============================================================
    private void UpdatePursuit(Vector3 player, float distance)
    {
        if (CheckNoticed(distance) || (warned && CheckFled(distance)))
            return;

        float t = PhaseTime;
        UpdateEscalation(t, distance);

        if (warned && Time.time >= warnEnd)
        {
            NoTurnAfterWarning();
            return;
        }

        if (UpdateFreeze(player))
            return;

        // Keeps the distance: his speed x factor, catches up if far behind.
        float playerSpeed = PlayerSpeed();
        float speed = playerSpeed * speedK;
        if (distance > wantDistance + 2f)
            speed = playerSpeed + catchUpBonus;
        else if (distance < wantDistance - 1f)
            speed = playerSpeed * speedFactor.x;

        if (playerSpeed < 0.3f && distance <= wantDistance + 1f)
        {
            Movement.Stop();                                // he stands - the bear stands too
            Movement.FaceTowards(player, turnSpeed);
            return;
        }

        Movement.SetSpeed(Mathf.Max(speed, 1f));
        Repath(BehindPoint(player, wantDistance));
    }

    // 0-5 s silence, 5-10 s light cues, 10 s closes in (heavier cues),
    // 15 s warning. Cues alternate: a physical sound (twig, steps) and a
    // growl - the bear talks to the player, a horror element (10.11).
    // Angry - no growls and no warning roar: only physical sounds.
    private void UpdateEscalation(float t, float distance)
    {
        cueTimer -= Time.deltaTime;

        if (t >= lightSoundsAt && !warned && cueTimer <= 0f)
        {
            cueTimer = 2.5f;
            cueCount++;
            bool growl = Mood != BearMoodLevel.Angry && cueCount % 2 == 0;

            if (!closedIn)
                Cue(growl ? "a low growl behind him" : "a twig cracks behind him");
            else
                Cue(growl ? "a warning growl right behind him" : "heavy steps right behind him");
        }

        if (!closedIn && t >= closeInAt)
        {
            closedIn = true;
            wantDistance = Random.Range(closeDistance.x, closeDistance.y);
            Log("closes in to " + wantDistance.ToString("F0") + " m");
        }

        if (!warned && t >= warningAt && distance <= wantDistance + warningMaxExtra)
        {
            warned = true;
            bool angry = Mood == BearMoodLevel.Angry;
            float window = angry ? angryWindow : warningWindow;
            // The window starts when the roar ENDS (21.11: a 6 s clip ate the
            // 5 s window - he turned just as it closed).
            float roar = !angry && warningRoarClip != null ? warningRoarClip.length : 0f;
            warnEnd = Time.time + roar + window;
            if (!angry)
                PlayRoar();                                 // temporary sound (21.11)
            Log(angry ? "no warning roar (angry) - " + window + " s for him to turn"
                      : "WARNING: a LOUD ROAR right behind him (" + roar.ToString("F1") + " s) - then " + window + " s for him to turn");
        }
    }

    // A sound the player hears behind him - a log for now; the sound
    // itself comes with the bear's sounds (D-24, D-34).
    private void Cue(string what)
    {
        if (logCues)
            Log("SOUND: " + what + " (placeholder)");
    }

    // Temporary roar (21.11): any clip until the bear has its own sounds
    // (D-34). A 3D sound from the bear, heard up to ~60 m.
    private void PlayRoar()
    {
        if (warningRoarClip == null)
            return;

        if (voice == null)
        {
            voice = GetComponent<AudioSource>();
            if (voice == null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;                        // 3D: from the bear's side, louder close by
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 5f;
                voice.maxDistance = 60f;
            }
        }

        voice.PlayOneShot(warningRoarClip, roarVolume);
    }

    private void NoTurnAfterWarning()
    {
        if (Mood == BearMoodLevel.Good)
        {
            Log("he did not turn - runs to the nearest cover, stalks again (good mood)");
            StartRetreat();
            return;
        }

        Attack("he did not turn after the warning (" + Mood + ")");
    }

    // ===============================================================
    // 3. NOTICED / FREEZE
    // ===============================================================

    // In the window after the warning he runs instead of turning ->
    // chase, mercy by mood. Before the warning running is no reason:
    // he may not know about the bear at all (10.11).
    private bool CheckFled(float distance)
    {
        if (reaction.Current != PlayerAction.RunsAway || reaction.TimeInCurrent < fleeConfirm)
            return false;

        if (Mood == BearMoodLevel.Angry)
            Attack("he runs, " + distance.ToString("F0") + " m (angry)");
        else
            ChaseWithMercy(MoodMercy(), "he runs after the warning, " + distance.ToString("F0") + " m (" + Mood + ")");
        return true;
    }

    // Mercy by mood - the numbers live in BearFaceOffState (21.11).
    private float MoodMercy()
    {
        return faceOff != null ? faceOff.MercyFor(Mood) : 0f;
    }

    // Close: sure / uncertain look (see the header, point 3). Far: the
    // normal opinion of BearKnowledge. Returns true when the phase changed.
    private bool CheckNoticed(float distance)
    {
        bool knowledgeNoticed = Knowledge.NoticedCount != knownNoticedCount;
        knownNoticedCount = Knowledge.NoticedCount;

        if (distance <= closeNoticeRange)
        {
            if (knowledgeNoticed)
            {
                GoToFaceOff(distance, "the bear's own opinion (BearKnowledge)");
                return true;
            }

            // What he did before he looked - to compare with later.
            if (screenTimer <= 0f && !suspicious)
            {
                actionBeforeLook = reaction.Current;
                routeBeforeLook = Flat(reaction.Velocity);
            }

            bool seen = Knowledge.IsSeeingPlayer && BearVisibleToPlayer();
            float angle = reaction.FacingAngle;
            centerTimer = seen && angle <= sureLookAngle ? centerTimer + Time.deltaTime : 0f;
            screenTimer = seen && angle <= screenAngle ? screenTimer + Time.deltaTime : 0f;

            if (centerTimer >= lookTime)
            {
                GoToFaceOff(distance, "he looked straight at it (" + angle.ToString("F0") + " deg) " + lookTime + " s");
                return true;
            }

            if (suspicious)
                return CheckBehaviourChange(distance);

            // Already in the middle of his screen -> a straight look: the
            // center timer decides (fix 11.11: turning toward the bear passes
            // the screen edge first, so this timer always came first).
            if (screenTimer >= lookTime && angle > sureLookAngle)
            {
                if (Mood != BearMoodLevel.Good && distance <= assumeSeenDistance)
                {
                    GoToFaceOff(distance, "uncertain look (" + angle.ToString("F0") + " deg), only " + distance.ToString("F0") +
                                           " m - assumes the worst (" + Mood + ")");
                    return true;
                }

                suspicious = true;
                suspicionEnd = Time.time + suspicionTime;
                sampleAngle = -1f;
                towardRate = 0f;
                maxToward = 0f;
                maxAway = 0f;
                Log("UNCERTAIN: it was on his screen (" + angle.ToString("F0") + " deg, " + distance.ToString("F0") +
                    " m) - did he see it? Watches his behaviour " + suspicionTime + " s");
            }
            return false;
        }

        centerTimer = 0f;
        screenTimer = 0f;
        suspicious = false;

        if (!knowledgeNoticed)
            return false;

        if (distance <= faceOffRange)
        {
            GoToFaceOff(distance, "the bear's own opinion (BearKnowledge)");
            return true;
        }

        Log("spotted at " + distance.ToString("F0") + " m while approaching - too far for a face-off, back to stalking (D-42)");
        ChangeState(BearState.Stalk);
        return true;
    }

    // Uncertain look: did he change his behaviour? Runs, stops, faces it,
    // backs away, comes at it; his course turned; he snapped the camera
    // toward it -> he saw it. Walks on as before for suspicionTime -> did not.
    private bool CheckBehaviourChange(float distance)
    {
        PlayerAction act = reaction.Current;
        bool telling = act == PlayerAction.RunsAway || act == PlayerAction.Stands || act == PlayerAction.BacksAway ||
                       act == PlayerAction.BacksAwaySlow || act == PlayerAction.Approaches;
        string change = null;

        Vector3 route = Flat(reaction.Velocity);
        float turn = routeBeforeLook.magnitude > 1f && route.magnitude > 1f ? Vector3.Angle(routeBeforeLook, route) : 0f;

        if (telling && act != actionBeforeLook && reaction.TimeInCurrent >= 0.3f)
            change = act.ToString();
        else if (turn > courseChangeAngle)
            change = "his course turned " + turn.ToString("F0") + " deg";
        else
        {
            float rate = TurnRateToBear();
            if (rate >= jerkTurnRate && reaction.FacingAngle <= 90f)     // near 180 a turn past it looks like "toward"
                change = "he snapped the camera toward it " + rate.ToString("F0") + " deg/s";
            else if (-rate >= jerkAwayRate)
                change = "he jerked the camera away from it " + (-rate).ToString("F0") + " deg/s (startled)";
        }

        if (change != null)
        {
            suspicious = false;
            GoToFaceOff(distance, "uncertain look + his behaviour changed: " + change);
            return true;
        }

        if (Time.time >= suspicionEnd)
        {
            suspicious = false;
            screenTimer = 0f;
            Log("he walks on as before - did not see it, the pattern goes on (fastest turn: toward " +
                maxToward.ToString("F0") + ", away " + maxAway.ToString("F0") + " deg/s)");
        }
        return false;
    }

    // How fast his body turns toward (+) or away from (-) the bear, deg/s
    // (11.11). Toward fast = a double take (jerkTurnRate). Away: a normal
    // turn back to the path (~270-370 deg/s) is "I saw nothing"; only a
    // startled flick counts (jerkAwayRate). Measured over 0.1 s windows:
    // per frame it is too noisy.
    private float TurnRateToBear()
    {
        if (!Knowledge.IsSeeingPlayer)
        {
            sampleAngle = -1f;
            towardRate = 0f;
            return 0f;
        }

        float angle = reaction.FacingAngle;
        if (sampleAngle < 0f)
        {
            sampleAngle = angle;
            sampleTime = Time.time;
            return towardRate;
        }

        float window = Time.time - sampleTime;
        if (window >= 0.1f)
        {
            towardRate = (sampleAngle - angle) / window;     // > 0: turns toward the bear
            sampleAngle = angle;
            sampleTime = Time.time;
            maxToward = Mathf.Max(maxToward, towardRate);
            maxAway = Mathf.Max(maxAway, -towardRate);
        }
        return towardRate;
    }

    // Canon 1.5: he looks its way -> stands still; unfreezeDelay after
    // he looks away it goes on.
    private bool UpdateFreeze(Vector3 player)
    {
        bool inView = Knowledge.IsSeeingPlayer && reaction.FacingAngle <= viewAngle;

        if (inView)
        {
            if (!frozen)
            {
                frozen = true;
                Movement.Stop();
                Log("FREEZE: he looks this way (" + reaction.FacingAngle.ToString("F0") + " deg)");
            }

            freezeTimer = unfreezeDelay;
            Movement.FaceTowards(player, turnSpeed);
            return true;
        }

        if (!frozen)
            return false;

        freezeTimer -= Time.deltaTime;
        if (freezeTimer > 0f)
            return true;

        frozen = false;
        repathTimer = 0f;
        return false;
    }

    // ===============================================================
    // 4. FACE-OFF -> BearFaceOffState (21.11)
    // ===============================================================
    private void GoToFaceOff(float distance, string why)
    {
        if (faceOff == null)
        {
            Log("NOTICED at " + distance.ToString("F0") + " m (" + why + ") - BearFaceOffState is missing, back to stalking");
            ChangeState(BearState.Stalk);
            return;
        }

        handedToFaceOff = true;
        faceOff.Begin(distance, why);
        ChangeState(BearState.FaceOff);
    }

    // ===============================================================
    // 5. ENDINGS
    // ===============================================================
    private void Attack(string why)
    {
        Log("ATTACK -> chase without mercy: " + why);
        StartChase(0f, why);
    }

    private void ChaseWithMercy(float mercy, string why)
    {
        Log("CHASE, mercy " + Mathf.RoundToInt(mercy * 100f) + "%: " + why);
        StartChase(mercy, why);
    }

    private void StartChase(float mercy, string why)
    {
        if (chase == null)
        {
            Knowledge.Release("no BearChaseState on the bear");
            return;
        }

        chase.Prepare(mercy, why);
        ChangeState(BearState.Chase);
    }

    // Good, no reaction to the warning: runs to the nearest place the
    // player cannot see, then stalks again.
    private void StartRetreat()
    {
        SetPhase(Phase.Retreat);

        Vector3 player = Knowledge.LastKnownPosition;
        Vector3 away = Flat(transform.position - player);
        if (away.sqrMagnitude < 0.01f)
            away = -transform.forward;

        leavePoint = transform.position + away.normalized * coverSearch.x;
        float best = float.MaxValue;

        for (int i = 0; i < 24; i++)           // 24 random points: the range grew to 25 m
        {
            Vector3 direction = Quaternion.Euler(0f, Random.Range(-100f, 100f), 0f) * away.normalized;
            Vector3 candidate = transform.position + direction * Random.Range(coverSearch.x, coverSearch.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                continue;

            float score = VisibleFromPlayer(hit.position, player) + FlatDistance(hit.position, transform.position) * 0.01f;
            if (score < best)
            {
                best = score;
                leavePoint = hit.position;
            }
        }

        Movement.SetSpeed(coverRunSpeed);
        Movement.MoveTo(leavePoint);
        Log("RETREAT to cover " + FlatDistance(leavePoint, transform.position).ToString("F0") + " m away" +
            (best < float.MaxValue ? ", open to him " + Mathf.RoundToInt(Mathf.Min(best, 1f) * 100f) + "%" : ""));
    }

    private void UpdateRetreat(float distance)
    {
        // Comes at the bear while it retreats - still an attack.
        if (ComesAtBear(distance) && distance < ReleaseDistance)
        {
            Attack("he comes at the bear while it leaves");
            return;
        }

        bool arrived = FlatDistance(transform.position, leavePoint) <= 2f;
        if (!arrived && PhaseTime < leaveMaxTime)
            return;

        Log("in cover - stalks again");
        ChangeState(BearState.Stalk);
    }

    // ===============================================================
    // Helpers.
    // ===============================================================
    private void SetPhase(Phase newPhase)
    {
        phase = newPhase;
        phaseStart = Time.time;
        repathTimer = 0f;
        frozen = false;
        approachFrom = -1f;
    }

    // He walks or runs at the bear AND has really come approachGain closer
    // since he started (11.11: turning around while walking passed 40 deg
    // from the bear for 0.8 s and counted as an attack from 24 m).
    // The thresholds live in BearFaceOffState (21.11).
    private bool ComesAtBear(float distance)
    {
        float minSpeed = faceOff != null ? faceOff.approachAttackSpeed : 1f;
        float gain = faceOff != null ? faceOff.approachGain : 2f;

        if (reaction.Current != PlayerAction.Approaches || reaction.Speed < minSpeed)
        {
            approachFrom = -1f;
            return false;
        }

        if (approachFrom < 0f)
            approachFrom = distance;

        return approachFrom - distance >= gain;
    }

    private float ReleaseDistance => faceOff != null ? faceOff.releaseDistance : 25f;

    private void Repath(Vector3 point)
    {
        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f)
            return;

        repathTimer = repathInterval;
        Movement.MoveTo(point);
    }

    // A point d metres behind the player along his route (standing:
    // behind his back), snapped to the NavMesh.
    private Vector3 BehindPoint(Vector3 player, float d)
    {
        Vector3 point = player - PlayerRoute() * d;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            return hit.position;

        return point;
    }

    private bool IsBehind(Vector3 player)
    {
        Vector3 fromPlayer = Flat(transform.position - player);
        return fromPlayer.sqrMagnitude < 0.01f || Vector3.Angle(PlayerRoute(), fromPlayer) >= 120f;
    }

    // His movement direction as the bear last saw it; standing - where he faces.
    private Vector3 PlayerRoute()
    {
        Vector3 velocity = Flat(Knowledge.LastKnownVelocity);
        if (velocity.magnitude > 0.5f)
            return velocity.normalized;

        Vector3 look = Knowledge.PlayerLookDirection;
        return look != Vector3.zero ? look : transform.forward;
    }

    private float PlayerSpeed()
    {
        return Flat(Knowledge.LastKnownVelocity).magnitude;
    }

    // The bear is not fully hidden from the player (the same check as the
    // opinion in BearKnowledge: the bear knows whether it stands in the open).
    private bool BearVisibleToPlayer()
    {
        if (playerSight == null && Knowledge.Player != null)
            playerSight = Knowledge.Player.GetComponent<PlayerSightOfBear>();

        return playerSight == null || playerSight.VisibleShare > 0f;
    }

    // 0..1 - how well the player could see a bear standing at this point.
    private float VisibleFromPlayer(Vector3 point, Vector3 player)
    {
        if (vision == null)
            return 1f;

        Vector3 eye = player + Vector3.up * 1.6f;
        float bodyHeight = bodyPoints != null && bodyPoints.Active ? bodyPoints.BodyHeight : 0.9f;   // from the bones (13.11)
        Vector3 body = point + Vector3.up * bodyHeight;
        return LineOfSight.PointVisibility(eye, body, transform, Knowledge.Player,
                                           vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
    }

    private float DistanceToPlayer()
    {
        return FlatDistance(Knowledge.LastKnownPosition, transform.position);
    }

    private void Log(string text)
    {
        Debug.Log("[Pattern] " + Mood + " | " + phase + " | " + text);
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Selected bear in Play: cyan - where it wants to be behind him,
    // magenta - where it retreats to.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !enabled || Knowledge == null || !Knowledge.HasLastKnownPosition)
            return;

        if (phase == Phase.Approach || phase == Phase.Pursuit)
        {
            Gizmos.color = Color.cyan;
            Vector3 behind = Knowledge.LastKnownPosition - PlayerRoute() * wantDistance;
            Gizmos.DrawWireSphere(behind, 0.8f);
            Gizmos.DrawLine(transform.position, behind);
        }
        else if (phase == Phase.Retreat)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(leavePoint, 0.8f);
            Gizmos.DrawLine(transform.position, leavePoint);
        }
    }
}
