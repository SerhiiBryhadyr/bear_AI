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
//      turns his course, jerks the camera) -> FACE-OFF; walks on as
//      before -> "he did not see me", the pattern goes on.
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
//    goes - does not count)
//    hides             lets him go      listens: hears a     attack
//                                       walk / run -> chase
//    Good / Medium let him go when he is backOffGain farther than where
//    the face-off began (not closer than releaseDistance).
//    (*) hunt - BearHuntState, step 4. Until it exists: Medium - 50%
//        attack / 50% lets him go, Angry - attack.
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
    public Vector2 followDistance = new Vector2(8f, 15f);
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
    public Vector2 closeDistance = new Vector2(6f, 8f);
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
    [Tooltip("...or he jerked the camera faster than this (deg/s).")]
    public float jerkTurnRate = 150f;
    [Tooltip("The close rule works up to this distance (m); farther - the normal opinion of BearKnowledge.")]
    public float closeNoticeRange = 18f;
    [Tooltip("Noticed farther than this (m) during the approach -> back to stalking (D-42), not a face-off.")]
    public float faceOffRange = 20f;
    [Tooltip("The freeze lasts this long after he looks away (s).")]
    public float unfreezeDelay = 0.7f;

    [Header("Face-off (D-52)")]
    [Tooltip("After being noticed the bear watches this long (s) before it reads what the player does.")]
    public float reactionDelay = 0.8f;
    [Tooltip("'Comes at the bear' counts only this fast (m/s)...")]
    public float approachAttackSpeed = 1f;
    [Tooltip("...for this long (s): one small step is no reason to attack.")]
    public float approachAttackTime = 0.5f;
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
    [Tooltip("Medium, slow backing / standing: he must hide from the bear within this time (s) from the face-off, or it attacks. Only cover saves (10.11).")]
    public float mediumEscapeTime = 15f;
    [Tooltip("Hidden from the bear this long (s) -> Good lets him go, Medium listens first.")]
    public float hiddenTime = 2f;
    [Tooltip("Medium: listens this long (s) after he hid; hears a loud step -> chase.")]
    public float listenTime = 4f;
    [Tooltip("A step at least this loud (hearing radius, m) counts: walk 10, run 20 (back 7, slow back 4, crouch 2.5).")]
    public float loudStep = 10f;

    [Header("Leaving")]
    [Tooltip("Good, no reaction to the warning: runs to the nearest cover this far (m)...")]
    public Vector2 coverSearch = new Vector2(8f, 18f);
    public float coverRunSpeed = 6f;
    [Tooltip("Leaving calmly: walks this far from the player (m), then lets him go.")]
    public float leaveDistance = 30f;
    [Tooltip("Retreat / leaving at most this long (s).")]
    public float leaveMaxTime = 8f;

    [Header("Contact")]
    [Tooltip("During the pattern the player is lost after this long without sight and sound (s).")]
    public float loseContactTime = 10f;

    [Header("Movement")]
    public float turnSpeed = 180f;
    public float repathInterval = 0.25f;

    [Header("Debug")]
    [Tooltip("Light sounds and threats every few seconds (logs instead of sounds / animations).")]
    public bool logCues = true;

    private enum Phase { Approach, Pursuit, FaceOff, Leave, Retreat }
    private enum Answer { None, Watch, Threat, Follow, Hesitate }

    // --- Read by BearStalkState (pause between patterns) ---
    public float TimeSinceEnded => Time.time - endedAt;

    private BearVision vision;
    private BearPlayerReaction reaction;
    private BearMood mood;
    private BearChaseState chase;
    private PlayerSightOfBear playerSight;

    private Phase phase;
    private float phaseStart;
    private bool started;
    private float endedAt = -999f;

    private float noLookTimer;
    private float centerTimer;
    private float screenTimer;
    private bool suspicious;
    private float suspicionEnd;
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
    private float listenUntil;
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
        reaction = GetComponent<BearPlayerReaction>();
        mood = GetComponent<BearMood>();
        chase = GetComponent<BearChaseState>();

        if (reaction == null)
            Debug.LogError("BearRearPursuitState: BearPlayerReaction is missing on the bear.");
        if (mood == null)
            Debug.LogWarning("BearRearPursuitState: BearMood is missing on the bear - the mood is Good.");
        if (chase == null)
            Debug.LogError("BearRearPursuitState: BearChaseState is missing on the bear - attacks and chases will not work.");
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
        listenUntil = 0f;
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
            case Phase.FaceOff: UpdateFaceOff(player, distance); break;
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

            if (screenTimer >= lookTime)
            {
                if (Mood != BearMoodLevel.Good && distance <= assumeSeenDistance)
                {
                    StartFaceOff(distance, "uncertain look (" + angle.ToString("F0") + " deg), only " + distance.ToString("F0") +
                                           " m - assumes the worst (" + Mood + ")");
                    return true;
                }

                suspicious = true;
                suspicionEnd = Time.time + suspicionTime;
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
    // backs away, comes at it; his course turned; he jerked the camera
    // -> he saw it. Walks on as before for suspicionTime -> did not.
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
        else if (reaction.TurnRate >= jerkTurnRate)
            change = "he jerked the camera " + reaction.TurnRate.ToString("F0") + " deg/s";

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
            Log("he walks on as before - did not see it, the pattern goes on");
        }
        return false;
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
    private void StartFaceOff(float distance, string why)
    {
        SetPhase(Phase.FaceOff);
        Movement.Stop();
        answer = Answer.None;
        hadRetreat = false;
        hiddenTimer = 0f;
        listenUntil = 0f;
        releaseAt = Mathf.Max(releaseDistance, distance + backOffGain);
        escapeDeadline = Time.time + reactionDelay + mediumEscapeTime;

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

        if (act == PlayerAction.Unknown)
        {
            HandleHidden();
            return;
        }

        hiddenTimer = 0f;
        listenUntil = 0f;

        // Comes at the bear - attack in any mood (one small step does not count).
        if (act == PlayerAction.Approaches && reaction.Speed >= approachAttackSpeed && reaction.TimeInCurrent >= approachAttackTime)
        {
            Attack("he comes at the bear");
            return;
        }

        // Only real backing counts (10.11: standing a second after turning
        // around also counted - then walking away was not a bluff).
        if ((act == PlayerAction.BacksAway || act == PlayerAction.BacksAwaySlow) &&
            reaction.TimeInCurrent >= backedAwayTime)
            hadRetreat = true;

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
                if (act == PlayerAction.WalksAway && reaction.TimeInCurrent < turnAwayGrace)
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
                Bluff();
            else if (reaction.TimeInCurrent >= turnAwayGrace)       // a short look where he goes does not count
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
                Bluff();
            else if (reaction.TimeInCurrent >= turnAwayGrace)       // a short look where he goes does not count
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

        // Medium: listens - hears him walk or run away -> chase.
        if (listenUntil <= 0f)
        {
            listenUntil = Time.time + listenTime;
            Log("he hid - listens " + listenTime + " s (medium)");
        }

        if (Knowledge.TimeSinceHeard < 0.5f && Knowledge.LastHeardLoudness >= loudStep)
        {
            ChaseWithMercy(mediumMercy, "heard him " + (Knowledge.LastHeardLoudness >= 15f ? "run" : "walk") + " after hiding (medium)");
            return;
        }

        if (Time.time >= listenUntil)
            StartLeave("silence after he hid (medium)");
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

        for (int i = 0; i < 12; i++)
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
        if (reaction.Current == PlayerAction.Approaches && reaction.Speed >= approachAttackSpeed &&
            reaction.TimeInCurrent >= approachAttackTime && distance < releaseDistance)
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
        Vector3 body = point + Vector3.up * 0.9f;
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
