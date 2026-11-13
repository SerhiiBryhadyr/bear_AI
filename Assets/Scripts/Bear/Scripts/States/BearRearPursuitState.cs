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
// 4. FACE-OFF: the bear stops, watches reactionDelay seconds, then
//    reads what the player does (BearPlayerReaction) and answers by
//    the mood table (D-52):
//                      Good             Medium               Angry
//    comes at it       attack           attack               attack
//    runs              chase, mercy 95% chase, mercy 50%     attack
//    walks away        threat, stays,   hunt (*)             hunt (*)
//    (bluff)           lets him go
//    backs away        watches, lets    follows ~4 s; not    attack
//                      him go           slowed -> attack
//    backs slowly /    watches, lets    aggressive threat;   hesitates,
//    stands            him go           must HIDE within     attacks in 3-8 s
//                                       15 s, else attack
//    turns away after  stalks again     chase, mercy 50%     attack
//    backing (a look away shorter than turnAwayGrace - to see where he
//    goes - does not count; counted from the turn, also while he is out
//    of sight - 11.11)
//    hides             lets him go      LOST (4a)            attack
//    Good / Medium let him go when he is backOffGain farther than where
//    the face-off began (not closer than releaseDistance).
//    (*) hunt - BearHuntState, step 4. Until it exists: Medium - 50%
//        attack / 50% lets him go, Angry - attack.
// 4a. LOST behind cover (Medium, 11.11): from the moment he is out of
//    sight the bear listens harder (hearingBoost, BearHearing); a walk /
//    run heard -> chase. Hidden hiddenTime -> SEARCH (BearAreaSearch):
//    the place where it saw him last, then X, Y, Z, looking around at
//    each; a quiet step -> the next point is there. Not found -> it
//    REMEMBERS him memoryTime seconds, wandering (patrol points) with
//    normal ears. Search or memory: sees him walk / run away or hears a
//    walk / run -> chase; sees him stand / back away / come -> FACE-OFF
//    again with only refindEscapeTime to hide; found findsToAttack
//    times -> attack. Memory over -> forgets him (no long ignoring).
// 5. END: chase or attack (BearChaseState), lets him go
//    (BearKnowledge.Release -> patrol), back to stalking, or the player
//    is lost (D-20). TimeSinceEnded - the pause between patterns.
// Sounds behind him: twig cracks / steps alternate with growls, the
// warning is a loud roar (Angry: no growls, no roar). Sounds and
// animations (growl, hind legs, roar) are logs for now.
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
    [Tooltip("Good / Medium: after the warning the player has this long (s) to turn.")]
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

    [Header("Face-off (D-52)")]
    [Tooltip("After being noticed the bear watches this long (s) before it reads what the player does.")]
    public float reactionDelay = 0.8f;
    [Tooltip("'Comes at the bear' counts only this fast (m/s)...")]
    public float approachAttackSpeed = 1f;
    [Tooltip("...and only after he really got this much closer (m) walking toward it: turning around on the move is no reason to attack (11.11).")]
    public float approachGain = 2f;
    [Tooltip("Backing away (S or S+Shift) this long (s) = 'he has backed away': turning away after it is not a bluff. Standing does not count.")]
    public float backedAwayTime = 1f;
    [Range(0f, 1f)] public float goodMercy = 0.95f;
    [Range(0f, 1f)] public float mediumMercy = 0.5f;
    [Tooltip("Good: watches the backing / standing player this long (s, random), then turns and leaves.")]
    public Vector2 goodWatchTime = new Vector2(4f, 7f);
    [Tooltip("Good, bluff: threatens (growls, hind legs) this long (s), then leaves.")]
    public float threatTime = 4f;
    [Tooltip("Medium, backing at normal speed: follows him this long (s); he does not slow down -> attack.")]
    public float mediumFollowTime = 4f;
    [Tooltip("Medium, until the hunt exists (step 4): chance it attacks after a bluff.")]
    [Range(0f, 1f)] public float mediumBluffAttack = 0.5f;
    [Tooltip("Angry, slow backing / standing: attacks at a random moment in this range (s).")]
    public Vector2 angryHesitation = new Vector2(3f, 8f);
    [Tooltip("Good lets him go when he is at least this far (m)...")]
    public float releaseDistance = 25f;
    [Tooltip("...and this much farther (m) than where the face-off began (10.11: noticed at 26 m must not mean 'already far').")]
    public float backOffGain = 10f;
    [Tooltip("A look away shorter than this (s) - to see where he goes while backing - is not 'turned away'.")]
    public float turnAwayGrace = 2f;
    [Tooltip("Walking away (without backing first) counts as a bluff only after this long (s): a step aside or turning on the spot is not a bluff (11.11).")]
    public float bluffConfirm = 1f;
    [Tooltip("Medium, slow backing / standing: he must hide from the bear within this time (s) from the face-off, or it attacks. Only cover saves (10.11).")]
    public float mediumEscapeTime = 15f;
    [Tooltip("Hidden from the bear this long (s) -> Good lets him go, Medium listens first.")]
    public float hiddenTime = 2f;
    [Tooltip("A step at least this loud (hearing radius, m) counts: walk 10, run 20 (back 7, slow back 4, crouch 2.5).")]
    public float loudStep = 10f;

    [Header("Lost behind cover (Medium, 11.11)")]
    [Tooltip("Out of sight -> listens harder: hearing radius x this (run 50, walk 25, back 17, slow back 10, crouch 6 m) until the search ends.")]
    public float hearingBoost = 2.5f;
    [Tooltip("The search did not find him -> remembers him this long (s): a walk / run heard, or seeing him, still means him.")]
    public float memoryTime = 30f;
    [Tooltip("Wanders at this speed (x normal) while it remembers him.")]
    public float memoryWalkSpeed = 0.7f;
    [Tooltip("Found him again and he stands / backs away: he must hide within this time (s) - shorter than the first time.")]
    public float refindEscapeTime = 8f;
    [Tooltip("Found him again this many times -> attack, he keeps hanging around (2 = the third meeting).")]
    public int findsToAttack = 2;
    [Tooltip("Forgets him: ignores him only this long (s) - just so BearBrain sends it on patrol, not to search again.")]
    public float forgetIgnoreTime = 2f;

    [Header("Leaving")]
    [Tooltip("Good, no reaction to the warning: runs to the nearest cover this far (m)...")]
    public Vector2 coverSearch = new Vector2(8f, 25f);
    public float coverRunSpeed = 6f;
    [Tooltip("Leaving calmly: walks this far from the player (m), then lets him go.")]
    public float leaveDistance = 30f;
    [Tooltip("Retreat / leaving at most this long (s).")]
    public float leaveMaxTime = 8f;

    [Header("Contact")]
    [Tooltip("During the pattern the player is lost after this long without sight and sound (s).")]
    public float loseContactTime = 15f;

    [Header("Movement")]
    public float turnSpeed = 180f;
    public float repathInterval = 0.25f;

    [Header("Debug")]
    [Tooltip("Light sounds and threats every few seconds (logs instead of sounds / animations).")]
    public bool logCues = true;

    private enum Phase { Approach, Pursuit, FaceOff, Leave, Retreat, Search, Lost }
    private enum Answer { None, Watch, Threat, Follow, Hesitate }

    // --- Read by BearStalkState (pause between patterns) ---
    public float TimeSinceEnded => Time.time - endedAt;

    private BearVision vision;
    private BearBodyPoints bodyPoints;
    private BearPlayerReaction reaction;
    private BearMood mood;
    private BearChaseState chase;
    private PlayerSightOfBear playerSight;
    private BearHearing hearing;
    private BearAreaSearch search;
    private BearPatrolSystem patrol;

    private Phase phase;
    private float phaseStart;
    private bool started;
    private float endedAt = -999f;

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

    private Answer answer;
    private float answerEnd;
    private bool hadRetreat;
    private float hiddenTimer;
    private float turnedAwayAt = -1f;   // he turned his back after backing away (-1 = he did not)
    private bool hearingBoosted;
    private int finds;                  // Medium: found him again after losing him
    private float memoryEnd;
    private float approachFrom = -1f;   // distance when he started walking toward the bear (-1 = he does not)
    private float releaseAt;            // Good: lets him go at this distance
    private float escapeDeadline;       // Medium: must be hidden by then

    private Vector3 leavePoint;
    private string leaveReason = "";

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
        hearing = GetComponent<BearHearing>();
        search = GetComponent<BearAreaSearch>();
        patrol = GetComponent<BearPatrolSystem>();

        if (reaction == null)
            Debug.LogError("BearRearPursuitState: BearPlayerReaction is missing on the bear.");
        if (mood == null)
            Debug.LogWarning("BearRearPursuitState: BearMood is missing on the bear - the mood is Good.");
        if (chase == null)
            Debug.LogError("BearRearPursuitState: BearChaseState is missing on the bear - attacks and chases will not work.");
        if (search == null)
            Debug.LogError("BearRearPursuitState: BearAreaSearch is missing on the bear - Medium cannot search for him (add the component).");
    }

    private void OnEnable()
    {
        started = true;
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
        answer = Answer.None;
        hadRetreat = false;
        hiddenTimer = 0f;
        turnedAwayAt = -1f;
        finds = 0;
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
        if (search != null)
            search.Stop();
        SetHearingBoost(false);
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

        // Searching, the bear does not know where he is: the head goes with the body.
        bool searching = phase == Phase.Search || phase == Phase.Lost;
        if (vision != null && !searching)
            vision.SetLookTarget(player + Vector3.up * 1.2f);

        switch (phase)
        {
            case Phase.Approach: UpdateApproach(player, distance); break;
            case Phase.Pursuit: UpdatePursuit(player, distance); break;
            case Phase.FaceOff: UpdateFaceOff(player, distance); break;
            case Phase.Search: UpdateSearch(); break;
            case Phase.Lost: UpdateLost(); break;
            default: UpdateLeaving(distance); break;
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
            warnEnd = Time.time + window;
            Log(angry ? "no warning roar (angry) - " + window + " s for him to turn"
                      : "WARNING: a LOUD ROAR right behind him (placeholder) - " + window + " s for him to turn");
        }
    }

    // A sound the player hears behind him - a log for now; the sound
    // itself comes with the bear's sounds (D-24, D-34).
    private void Cue(string what)
    {
        if (logCues)
            Log("SOUND: " + what + " (placeholder)");
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

    private float MoodMercy()
    {
        switch (Mood)
        {
            case BearMoodLevel.Good: return goodMercy;
            case BearMoodLevel.Medium: return mediumMercy;
            default: return 0f;
        }
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
                StartFaceOff(distance, "the bear's own opinion (BearKnowledge)");
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
                StartFaceOff(distance, "he looked straight at it (" + angle.ToString("F0") + " deg) " + lookTime + " s");
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
                    StartFaceOff(distance, "uncertain look (" + angle.ToString("F0") + " deg), only " + distance.ToString("F0") +
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
            StartFaceOff(distance, "the bear's own opinion (BearKnowledge)");
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
            StartFaceOff(distance, "uncertain look + his behaviour changed: " + change);
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
    // 4. FACE-OFF by mood (D-52)
    // ===============================================================
    // escapeTime: Medium, how long he has to hide (found again - shorter).
    private void StartFaceOff(float distance, string why, float escapeTime = -1f)
    {
        SetPhase(Phase.FaceOff);
        Movement.Stop();
        answer = Answer.None;
        hadRetreat = false;
        hiddenTimer = 0f;
        turnedAwayAt = -1f;
        SetHearingBoost(false);
        if (search != null)
            search.Stop();
        releaseAt = Mathf.Max(releaseDistance, distance + backOffGain);
        escapeDeadline = Time.time + reactionDelay + (escapeTime > 0f ? escapeTime : mediumEscapeTime);

        Knowledge.MarkNoticed("pattern 1 - the player looked back at " + distance.ToString("F0") + " m");
        knownNoticedCount = Knowledge.NoticedCount;
        Log("NOTICED at " + distance.ToString("F0") + " m (" + why + ") - FACE-OFF");
    }

    private void UpdateFaceOff(Vector3 player, float distance)
    {
        if (answer != Answer.Follow)
        {
            Movement.Stop();
            Movement.FaceTowards(player, turnSpeed);
        }

        if (PhaseTime < reactionDelay)
            return;

        PlayerAction act = reaction.Current;

        // Only real backing counts (10.11: standing a second after turning
        // around also counted - then walking away was not a bluff).
        if ((act == PlayerAction.BacksAway || act == PlayerAction.BacksAwaySlow) &&
            reaction.TimeInCurrent >= backedAwayTime)
            hadRetreat = true;

        UpdateTurnedAway(act);

        if (act == PlayerAction.Unknown)
        {
            approachFrom = -1f;

            // He turned his back and went out of sight: the bear saw him
            // leave - that is "turned away", not "hid" (fix 11.11: under
            // trees the bear never saw the 2 s in one piece).
            if (turnedAwayAt >= 0f)
            {
                if (TurnedAwayLongEnough)
                    TurnedAwayOutOfSight();
                return;
            }

            HandleHidden();
            return;
        }

        hiddenTimer = 0f;
        SetHearingBoost(false);

        // Comes at the bear - attack in any mood (one small step does not count).
        if (ComesAtBear(distance))
        {
            Attack("he comes at the bear");
            return;
        }

        switch (Mood)
        {
            case BearMoodLevel.Good: FaceOffGood(act, distance); break;
            case BearMoodLevel.Medium: FaceOffMedium(act, player, distance); break;
            default: FaceOffAngry(act); break;
        }
    }

    // Good: only scares him, no idea to kill.
    private void FaceOffGood(PlayerAction act, float distance)
    {
        if (act == PlayerAction.RunsAway || act == PlayerAction.WalksAway)
        {
            if (hadRetreat)
            {
                if (act == PlayerAction.WalksAway && !TurnedAwayLongEnough)
                    return;                                 // a short look where he goes

                Log("he backed away, then turned away (" + act + ") - the bear does not understand, stalks again");
                ChangeState(BearState.Stalk);
                return;
            }

            if (act == PlayerAction.RunsAway)
            {
                ChaseWithMercy(goodMercy, "he runs (good mood)");
                return;
            }

            if (reaction.TimeInCurrent < bluffConfirm)
                return;                                     // a step aside is not a bluff (11.11)

            if (answer != Answer.Threat)
            {
                answer = Answer.Threat;
                answerEnd = Time.time + threatTime;
                Log("bluff - THREAT: growls, rears up on its hind legs, stays (placeholder)");
            }
        }
        else if (act == PlayerAction.BacksAway || act == PlayerAction.BacksAwaySlow || act == PlayerAction.Stands)
        {
            if (answer != Answer.Watch && answer != Answer.Threat)
            {
                answer = Answer.Watch;
                answerEnd = Time.time + Random.Range(goodWatchTime.x, goodWatchTime.y);
                Log("watches him calmly, no threat");
            }
        }

        if (distance >= releaseAt)
            StartLeave("he is " + distance.ToString("F0") + " m away (good mood)");
        else if (answer != Answer.None && Time.time >= answerEnd)
            StartLeave(answer == Answer.Threat ? "threatened enough (good mood)" : "watched enough (good mood)");
    }

    // Medium: a realistic bear - guards its territory.
    private void FaceOffMedium(PlayerAction act, Vector3 player, float distance)
    {
        if (act == PlayerAction.RunsAway)
        {
            ChaseWithMercy(mediumMercy, hadRetreat ? "turned and ran while backing (medium)" : "he runs (medium)");
            return;
        }

        if (act == PlayerAction.WalksAway)
        {
            if (!hadRetreat)
            {
                if (reaction.TimeInCurrent >= bluffConfirm)     // a step aside is not a bluff (11.11)
                    Bluff();
            }
            else if (TurnedAwayLongEnough)                          // a short look where he goes does not count
                ChaseWithMercy(mediumMercy, "turned away while backing (medium)");
            return;
        }

        if (act == PlayerAction.BacksAway)
        {
            if (answer != Answer.Follow)
            {
                answer = Answer.Follow;
                answerEnd = Time.time + mediumFollowTime;
                Log("he backs away at normal speed - follows him at his pace; not slower in " + mediumFollowTime + " s -> attack");
            }

            if (Time.time >= answerEnd)
            {
                Attack("kept backing away at normal speed (medium)");
                return;
            }

            Movement.SetSpeed(Mathf.Max(reaction.Speed, 1f));
            Repath(player);
            return;
        }

        // Slow backing / standing: aggressive threat, does not follow - but
        // he must hide (HandleHidden) before escapeDeadline (10.11).
        // Later the flashlight etc. (D-01) can change the deadline here.
        if (answer != Answer.Threat)
        {
            answer = Answer.Threat;
            Movement.Stop();
            Log("AGGRESSIVE THREAT: bounces, rears up, huffs (placeholder) - he must hide within " +
                Mathf.Max(0f, escapeDeadline - Time.time).ToString("F0") + " s");
        }

        if (Time.time >= escapeDeadline)
            Attack("he did not hide in time (medium)");
    }

    // Angry: no compromise.
    private void FaceOffAngry(PlayerAction act)
    {
        if (act == PlayerAction.RunsAway || act == PlayerAction.BacksAway)
        {
            Attack(act == PlayerAction.RunsAway ? "he runs (angry)" : "he backs away at normal speed (angry)");
            return;
        }

        if (act == PlayerAction.WalksAway)
        {
            if (!hadRetreat)
            {
                if (reaction.TimeInCurrent >= bluffConfirm)     // a step aside is not a bluff (11.11)
                    Bluff();
            }
            else if (TurnedAwayLongEnough)                          // a short look where he goes does not count
                Attack("turned away (angry)");
            return;
        }

        // Slow backing / standing: hesitates, then charges anyway.
        if (answer != Answer.Hesitate)
        {
            answer = Answer.Hesitate;
            answerEnd = Time.time + Random.Range(angryHesitation.x, angryHesitation.y);
            Log("hesitates - will charge in " + (answerEnd - Time.time).ToString("F1") + " s (angry)");
        }

        if (Time.time >= answerEnd)
            Attack("charged after hesitating (angry)");
    }

    // He pretends he did not see the bear and walks on. Hunt (step 4)
    // is not made yet: Medium - 50% attack / 50% lets him go, Angry - attack.
    private void Bluff()
    {
        if (Mood == BearMoodLevel.Medium && Random.value >= mediumBluffAttack)
        {
            StartLeave("bluff - lets him go this time (medium; the hunt is not made yet)");
            return;
        }

        Attack("bluff - he walks away as if he did not see (" + Mood + "; the hunt is not made yet)");
    }

    // He is out of the bear's sight.
    private void HandleHidden()
    {
        hiddenTimer += Time.deltaTime;

        // Medium listens harder from the moment he is gone (11.11).
        if (Mood == BearMoodLevel.Medium)
        {
            SetHearingBoost(true);

            if (HeardStep(hiddenTimer, out float loudness) && loudness >= loudStep)
            {
                ChaseWithMercy(mediumMercy, "heard him " + StepName(loudness) + " after he hid (medium)");
                return;
            }
        }

        if (hiddenTimer < hiddenTime)
            return;

        switch (Mood)
        {
            case BearMoodLevel.Good:
                StartLeave("he hid (good mood)");
                return;

            case BearMoodLevel.Angry:
                Attack("he hid (angry) - goes for him");
                return;
        }

        StartSearch(Knowledge.LastSeenPosition, "the place where it saw him last", "he hid");
    }

    // --- Turned away after backing (fix 11.11) ---
    // Counted from the moment he turned his back; does NOT reset when the
    // bear loses sight of him. Resets only when the bear sees him facing
    // it again (backing, standing, coming).
    private void UpdateTurnedAway(PlayerAction act)
    {
        if (!hadRetreat)
        {
            turnedAwayAt = -1f;
            return;
        }

        if (act == PlayerAction.WalksAway || act == PlayerAction.RunsAway)
        {
            if (turnedAwayAt < 0f)
                turnedAwayAt = Time.time;
        }
        else if (act != PlayerAction.Unknown)
        {
            turnedAwayAt = -1f;
        }
    }

    private bool TurnedAwayLongEnough => turnedAwayAt >= 0f && Time.time - turnedAwayAt >= turnAwayGrace;

    private void TurnedAwayOutOfSight()
    {
        switch (Mood)
        {
            case BearMoodLevel.Good:
                Log("he backed away, then turned away and went out of sight - the bear does not understand, stalks again");
                ChangeState(BearState.Stalk);
                return;

            case BearMoodLevel.Medium:
                ChaseWithMercy(mediumMercy, "turned away while backing, then out of sight (medium)");
                return;

            default:
                Attack("turned away (angry), then out of sight");
                return;
        }
    }

    // ===============================================================
    // 4a. LOST behind cover (Medium, 11.11): search, then memory.
    // ===============================================================
    private void StartSearch(Vector3 point, string pointName, string why)
    {
        if (search == null)
        {
            StartLeave(why + " - no BearAreaSearch on the bear, lets him go");
            return;
        }

        SetPhase(Phase.Search);
        Knowledge.SetLoseContactTime(9999f);            // stays "knows": the pattern itself decides when it forgets
        SetHearingBoost(true);
        if (vision != null)
            vision.ClearLookTarget();

        Log("SEARCH: " + why + " - checks " + pointName + ", then X, Y, Z; listens x" + hearingBoost);
        search.Begin(point, pointName);
    }

    private void UpdateSearch()
    {
        if (CheckFoundOrHeard("while searching"))
            return;

        // A quiet step (louder ones end in a chase above) -> goes there.
        if (HeardStep(PhaseTime, out float loudness))
            search.Redirect(Knowledge.LastKnownPosition, "a quiet " + StepName(loudness));

        search.Tick();

        if (search.IsDone)
            StartMemory();
    }

    private void StartMemory()
    {
        SetPhase(Phase.Lost);
        SetHearingBoost(false);
        memoryEnd = Time.time + memoryTime;
        Movement.SetSpeedMultiplier(memoryWalkSpeed);
        WanderOn();
        Log("did not find him - remembers him " + memoryTime + " s, wanders off (patrol points), normal ears");
    }

    private void UpdateLost()
    {
        if (CheckFoundOrHeard("while it still remembers him"))
            return;

        // A quiet step close by -> searches again from there.
        if (HeardStep(PhaseTime, out float loudness))
        {
            StartSearch(Knowledge.LastKnownPosition, "where it heard the sound", "heard a quiet " + StepName(loudness) + " close by");
            return;
        }

        if (Time.time >= memoryEnd)
        {
            Log("FORGOT him after " + memoryTime + " s of memory - patrol, no long ignoring");
            Knowledge.Release("pattern 1 - forgot him after the search", forgetIgnoreTime);
            return;
        }

        if (Movement.HasReachedDestination())
            WanderOn();
    }

    // Search or memory: he shows up, or he is heard.
    //   seen walking / running away, or a walk / run heard -> chase;
    //   seen standing, backing away or coming -> FACE-OFF again with only
    //   refindEscapeTime to hide; found findsToAttack times -> attack.
    private bool CheckFoundOrHeard(string where)
    {
        PlayerAction act = reaction.Current;

        if (Knowledge.IsSeeingPlayer && act != PlayerAction.Unknown)
        {
            if (act == PlayerAction.RunsAway || act == PlayerAction.WalksAway)
            {
                ChaseWithMercy(mediumMercy, "saw him " + (act == PlayerAction.RunsAway ? "run" : "walk") + " away " + where + " (medium)");
                return true;
            }

            finds++;
            if (finds >= findsToAttack)
            {
                Attack("found him again " + where + " (meeting " + (finds + 1) + ") - he keeps hanging around (medium)");
                return true;
            }

            StartFaceOff(DistanceToPlayer(), "found him again " + where + ", he " + act, refindEscapeTime);
            return true;
        }

        if (HeardStep(PhaseTime, out float loudness) && loudness >= loudStep)
        {
            ChaseWithMercy(mediumMercy, "heard him " + StepName(loudness) + " " + where + " (medium)");
            return true;
        }

        return false;
    }

    // A step heard less than 0.5 s ago, and not before 'since' seconds ago
    // (a step heard while the bear still saw him does not count).
    private bool HeardStep(float since, out float loudness)
    {
        loudness = Knowledge.LastHeardLoudness;
        float age = Knowledge.TimeSinceHeard;
        return age < 0.5f && age < since;
    }

    private static string StepName(float loudness)
    {
        if (loudness >= 15f) return "run";
        if (loudness >= 10f) return "walk";
        if (loudness >= 5f) return "step back";
        return "slow step";
    }

    private void WanderOn()
    {
        Transform point = patrol != null ? patrol.GetRandomPoint() : null;

        if (point != null)
            Movement.MoveTo(point.position);
        else
            Movement.Stop();
    }

    private void SetHearingBoost(bool on)
    {
        if (hearing == null || on == hearingBoosted)
            return;

        hearingBoosted = on;
        if (on)
            hearing.SetRangeMultiplier(hearingBoost);
        else
            hearing.ResetRangeMultiplier();
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

    // Turns and leaves calmly, then lets the player go (patrol).
    private void StartLeave(string why)
    {
        SetPhase(Phase.Leave);
        leaveReason = why;

        Vector3 player = Knowledge.LastKnownPosition;
        Vector3 away = Flat(transform.position - player);
        if (away.sqrMagnitude < 0.01f)
            away = transform.forward;

        Vector3 direction = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized;
        leavePoint = player + direction * leaveDistance;

        if (NavMesh.SamplePosition(leavePoint, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            leavePoint = hit.position;
        else
            leavePoint = transform.position + direction * 15f;

        Movement.SetSpeedMultiplier(1f);
        Movement.MoveTo(leavePoint);
        Log("LEAVES calmly: " + why);
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

    private void UpdateLeaving(float distance)
    {
        // Comes at the bear while it leaves - still an attack.
        if (ComesAtBear(distance) && distance < releaseDistance)
        {
            Attack("he comes at the bear while it leaves");
            return;
        }

        bool arrived = FlatDistance(transform.position, leavePoint) <= 2f;
        if (!arrived && PhaseTime < leaveMaxTime)
            return;

        if (phase == Phase.Retreat)
        {
            Log("in cover - stalks again");
            ChangeState(BearState.Stalk);
            return;
        }

        Knowledge.Release("pattern 1 - " + leaveReason);
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
    private bool ComesAtBear(float distance)
    {
        if (reaction.Current != PlayerAction.Approaches || reaction.Speed < approachAttackSpeed)
        {
            approachFrom = -1f;
            return false;
        }

        if (approachFrom < 0f)
            approachFrom = distance;

        return approachFrom - distance >= approachGain;
    }

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
    // magenta - where it leaves / retreats to.
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
        else if (phase == Phase.Leave || phase == Phase.Retreat)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(leavePoint, 0.8f);
            Gizmos.DrawLine(transform.position, leavePoint);
        }
    }
}
