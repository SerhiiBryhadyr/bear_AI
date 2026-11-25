using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearFaceOffState - the Encounter face to face, by mood (D-52,
// Ideas/Rear_Pursuit_Moods.md). Moved out of BearRearPursuitState on
// 21.11 without changing the behaviour (G-12: the face-off got a second
// user - the surprise meeting at close range, D-57).
//
// Who starts it: whoever calls Begin() and then ChangeState(FaceOff),
// like BearChaseState.Prepare(). Now - pattern 1 when the player has
// noticed the bear; the close encounter (BearCloseEncounterState, D-75:
// they saw each other, a roar from behind, he turned while it backed
// off); BearBoundaries (Good: the last warning, D-75). Later - being
// spotted while stalking (D-41).
//
// 1. FACE-OFF: the bear stops, watches reactionDelay seconds, then
//    reads what the player does (BearPlayerReaction) and answers by
//    the mood table (D-52):
//                      Good             Medium               Angry
//    comes at it       roar, last       attack               attack
//                      warning; comes
//                      on -> attack (25.11, D-75)
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
//    hides             lets him go      LOST (2)             attack
//    Good / Medium let him go when he is backOffGain farther than where
//    the face-off began (not closer than releaseDistance).
//    (*) hunt - BearHuntState, step 4. Until it exists: Medium - 50%
//        attack / 50% lets him go, Angry - attack.
// 2. LOST behind cover (Medium, 11.11): from the moment he is out of
//    sight the bear listens harder (hearingBoost, BearHearing); a walk /
//    run heard -> chase. Hidden hiddenTime -> SEARCH (BearAreaSearch):
//    the place where it saw him last, then X, Y, Z, looking around at
//    each; a quiet step -> the next point is there. Not found -> it
//    REMEMBERS him memoryTime seconds, wandering (patrol points) with
//    normal ears. Search or memory: sees him walk / run away or hears a
//    walk / run -> chase; sees him stand / back away / come -> FACE-OFF
//    again with only refindEscapeTime to hide; found findsToAttack
//    times -> attack. Memory over -> forgets him (no long ignoring).
// 3. END: chase or attack (BearChaseState), LEAVES for show and stalks
//    him again (D-75, 24.11: a calm walk away so he thinks it is over;
//    after leaveDeceiveTime, or as soon as it hears him walk / run away,
//    stalking - the patterns wait leavePatternPause), back to stalking,
//    or the player is lost (D-20). TimeSinceEnded - read by pattern 1
//    for the pause between patterns.
// 4. WHAT THE PLAYER SEES (D-76, 25.11): BearThreatDisplay shows threat
//    levels 0-3 (stares / rears up -> angry stance + huff -> paw slam +
//    roar -> lunge + roar), one step at a time (WantedLevel). An action
//    is never cut: the decisions above wait until it ends. Contact - he
//    is contactDistance from the bear's body - is an attack at once,
//    from any phase, even in the middle of an action.
// =====================================================================
public class BearFaceOffState : BearStateBehaviour
{
    public override BearState Id => BearState.FaceOff;

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
    [Tooltip("Good: watches the backing / standing player this long (s, random), then turns and leaves. 8-12 s from 25.11 (D-76): time for 2-3 threat actions.")]
    public Vector2 goodWatchTime = new Vector2(8f, 12f);
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

    [Header("Good: the last warning (D-75, 25.11)")]
    [Tooltip("Roar before the attack when he comes at a Good bear. Empty -> the warning roar of BearRearPursuitState.")]
    public AudioClip warningRoarClip;
    [Range(0f, 1f)] public float roarVolume = 1f;

    [Header("Leaving (D-75, 24.11: for show, then stalking)")]
    [Tooltip("Leaving calmly toward a point this far from the player (m) - a show: he should think it is over.")]
    public float leaveDistance = 30f;
    [Tooltip("The show lasts this long (s), then it stalks him. Sooner if it hears him walk / run away.")]
    public float leaveDeceiveTime = 4f;
    [Tooltip("After the face-off the patterns wait this long (s).")]
    public float leavePatternPause = 60f;

    [Header("Contact")]
    [Tooltip("During the face-off the player is lost after this long without sight and sound (s) - the same as in pattern 1.")]
    public float loseContactTime = 15f;
    [Tooltip("He is this close to the bear's body (m, BearBodyPoints) -> attack at once, from any phase, even in the middle of a threat action (25.11). The lunge moves the muzzle ~1 m forward - it reaches him too.")]
    public float contactDistance = 1f;

    [Header("Threat the player sees (D-76, 25.11)")]
    [Tooltip("Good: he stands (or walks off pretending) this long (s) -> level 1 'go away' (the angry stance). Backing away brings it down.")]
    public float goodLevel1After = 3f;

    [Header("Movement")]
    public float turnSpeed = 180f;
    public float repathInterval = 0.25f;

    private enum Phase { FaceOff, Leave, Search, Lost }
    private enum Answer { None, Watch, Threat, Follow, Hesitate }

    // --- Read by BearRearPursuitState (pause between patterns) ---
    public float TimeSinceEnded => enabled ? 0f : Time.time - endedAt;

    private BearVision vision;
    private BearPlayerReaction reaction;
    private BearMood mood;
    private BearChaseState chase;
    private BearHearing hearing;
    private BearAreaSearch search;
    private BearRearPursuitState rearPursuit;
    private BearVoice voice;
    private BearPatrolSystem patrol;
    private BearStalkState stalk;
    private BearThreatDisplay display;
    private BearBodyPoints bodyPoints;

    private Phase phase;
    private float phaseStart;
    private bool started;
    private float endedAt = -999f;
    private float repathTimer;

    // Begin() -> OnEnable: what started the face-off.
    private bool hasPending;
    private float pendingDistance;
    private string pendingWhy = "";
    private float pendingEscapeTime = -1f;

    private Answer answer;
    private float answerEnd;
    private bool hadRetreat;
    private bool warned;                // Good: the last warning was given (roar)
    private bool pendingWarned;
    private float hiddenTimer;
    private float turnedAwayAt = -1f;   // he turned his back after backing away (-1 = he did not)
    private bool hearingBoosted;
    private int finds;                  // Medium: found him again after losing him
    private float memoryEnd;
    private float approachFrom = -1f;   // distance when he started walking toward the bear (-1 = he does not)
    private float releaseAt;            // Good: lets him go at this distance
    private float escapeDeadline;       // Medium: must be hidden by then
    private float escapeTotal;          // ... out of this many seconds (threat levels)
    private float hesitateTotal;        // Angry: the hesitation, s (threat levels)
    private float goodImpatience;       // Good: how long he has stood (s) - threat level 1

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
        hearing = GetComponent<BearHearing>();
        search = GetComponent<BearAreaSearch>();
        patrol = GetComponent<BearPatrolSystem>();
        stalk = GetComponent<BearStalkState>();
        rearPursuit = GetComponent<BearRearPursuitState>();
        voice = BearVoice.On(gameObject);
        bodyPoints = GetComponent<BearBodyPoints>();

        display = GetComponent<BearThreatDisplay>();
        if (display == null)
        {
            display = gameObject.AddComponent<BearThreatDisplay>();
            Debug.Log("BearFaceOffState: BearThreatDisplay added in Play - add it to the bear to keep its settings.");
        }

        if (reaction == null)
            Debug.LogError("BearFaceOffState: BearPlayerReaction is missing on the bear.");
        if (mood == null)
            Debug.LogWarning("BearFaceOffState: BearMood is missing on the bear - the mood is Good.");
        if (chase == null)
            Debug.LogError("BearFaceOffState: BearChaseState is missing on the bear - attacks and chases will not work.");
        if (search == null)
            Debug.LogError("BearFaceOffState: BearAreaSearch is missing on the bear - Medium cannot search for him (add the component).");
    }

    // Who starts the face-off calls this first, then ChangeState(FaceOff).
    // distance / why - for the log and the release distance; escapeTime -
    // Medium, how long he has to hide (-1 = mediumEscapeTime); warned -
    // the caller has already roared the last warning (BearBoundaries).
    public void Begin(float distance, string why, float escapeTime = -1f, bool warned = false)
    {
        pendingWarned = warned;
        pendingDistance = distance;
        pendingWhy = why;
        pendingEscapeTime = escapeTime;
        hasPending = true;
    }

    // Mercy chance in a chase, by mood. Pattern 1 reads it too (he runs
    // after its warning), so the numbers live in one place.
    public float MercyFor(BearMoodLevel level)
    {
        switch (level)
        {
            case BearMoodLevel.Good: return goodMercy;
            case BearMoodLevel.Medium: return mediumMercy;
            default: return 0f;
        }
    }

    private void OnEnable()
    {
        started = true;
        Knowledge.SetLoseContactTime(loseContactTime);
        finds = 0;
        repathTimer = 0f;

        float distance = hasPending ? pendingDistance : DistanceToPlayer();
        string why = hasPending ? pendingWhy : "enabled without Begin()";
        float escapeTime = hasPending ? pendingEscapeTime : -1f;
        hasPending = false;

        StartFaceOff(distance, why, escapeTime);
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
        if (display != null)
            display.Stop();
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

        // Contact (25.11): he ran into the bear - attack at once, from any
        // phase, even in the middle of a threat action (it is cut).
        if (TouchesPlayer(out float touch))
        {
            if (display != null)
                display.Abort();
            Attack("CONTACT at " + touch.ToString("F1") + " m from its body - he ran into it");
            return;
        }

        // Searching, the bear does not know where he is: the head goes with the body.
        bool searching = phase == Phase.Search || phase == Phase.Lost;
        if (vision != null && !searching)
            vision.SetLookTarget(player + Vector3.up * 1.2f);

        switch (phase)
        {
            case Phase.FaceOff: UpdateFaceOff(player, distance); break;
            case Phase.Search: UpdateSearch(); break;
            case Phase.Lost: UpdateLost(); break;
            default: UpdateLeaving(distance); break;
        }
    }

    // ===============================================================
    // 1. FACE-OFF by mood (D-52)
    // ===============================================================
    // escapeTime: Medium, how long he has to hide (found again - shorter).
    private void StartFaceOff(float distance, string why, float escapeTime = -1f)
    {
        SetPhase(Phase.FaceOff);
        Movement.Stop();
        answer = Answer.None;
        hadRetreat = false;
        warned = pendingWarned;
        pendingWarned = false;
        hiddenTimer = 0f;
        turnedAwayAt = -1f;
        SetHearingBoost(false);
        if (search != null)
            search.Stop();
        releaseAt = Mathf.Max(releaseDistance, distance + backOffGain);
        escapeTotal = escapeTime > 0f ? escapeTime : mediumEscapeTime;
        escapeDeadline = Time.time + reactionDelay + escapeTotal;
        goodImpatience = 0f;
        if (display != null)
            display.Begin();

        Knowledge.MarkNoticed("face-off - noticed at " + distance.ToString("F0") + " m");
        Log("NOTICED at " + distance.ToString("F0") + " m (" + why + ") - FACE-OFF");
    }

    private void UpdateFaceOff(Vector3 player, float distance)
    {
        // Stands and turns to him - unless it follows him or steps toward
        // him (threat level 1). In the middle of an action it turns slowly.
        if (answer != Answer.Follow && (display == null || !display.Moving))
        {
            Movement.Stop();
            Movement.FaceTowards(player, display != null && display.IsBusy ? turnSpeed * 0.3f : turnSpeed);
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

        // What the player sees (D-76). An action is never cut (25.11):
        // every decision below waits until it ends.
        if (display != null)
        {
            display.Tick(WantedLevel(act), distance, player);
            if (display.IsBusy)
                return;
        }

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

        // Comes at the bear (one small step does not count): Good warns
        // once with a roar, the others attack.
        if (ComesAtBear(distance))
        {
            ComesAtIt("he comes at the bear");
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
            hesitateTotal = answerEnd - Time.time;
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
    // 2. LOST behind cover (Medium, 11.11): search, then memory.
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
            Knowledge.Release("face-off - forgot him after the search", forgetIgnoreTime);
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
    // 3. ENDINGS
    // ===============================================================
    private void Attack(string why)
    {
        Log("ATTACK -> chase without mercy: " + why);
        StartChase(0f, why);
    }

    // He comes at the bear. Good: the first time - a roar, the last
    // warning (it stands its ground; he must come approachGain closer
    // again to be attacked); after that - attack. Others - attack.
    private void ComesAtIt(string why)
    {
        if (Mood != BearMoodLevel.Good || warned)
        {
            Attack(why + (warned ? " after the last warning" : ""));
            return;
        }

        if (phase != Phase.FaceOff)
            StartFaceOff(DistanceToPlayer(), "the last warning while leaving");   // turns back to him (resets warned)

        warned = true;
        approachFrom = -1f;     // he has to come closer again
        AudioClip clip = warningRoarClip != null ? warningRoarClip : (rearPursuit != null ? rearPursuit.warningRoarClip : null);
        if (voice != null)
            voice.Play(clip, roarVolume);
        if (display != null)
            display.WarnLunge();                            // the lunge with the roar (25.11)
        Log("LAST WARNING - roar" + (clip == null ? " (no clip)" : "") + ": " + why + "; comes on -> attack");
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

    // Turns and leaves calmly - for show - then stalks him (D-75).
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
        Log("LEAVES calmly (for show " + leaveDeceiveTime.ToString("F0") + " s): " + why);
    }

    private void UpdateLeaving(float distance)
    {
        // Comes at the bear while it leaves - still an attack.
        if (ComesAtBear(distance) && distance < releaseDistance)
        {
            ComesAtIt("he comes at the bear while it leaves");
            return;
        }

        // It does not look back, but it hears: a walk / run away -> stalking at once.
        bool heardHim = Knowledge.TimeSinceHeard < 0.5f && Knowledge.LastHeardLoudness >= loudStep;
        bool arrived = FlatDistance(transform.position, leavePoint) <= 2f;
        if (!heardHim && !arrived && PhaseTime < leaveDeceiveTime)
            return;

        if (stalk == null)
        {
            Knowledge.Release("face-off - " + leaveReason + " (no BearStalkState)");
            return;
        }

        stalk.PausePatterns(leavePatternPause, "after the face-off");
        Log("-> STALKING: " + leaveReason + "; " +
            (heardHim ? "heard him go away (" + StepName(Knowledge.LastHeardLoudness) + ")" : "the show is over after " + PhaseTime.ToString("F1") + " s"));
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
        approachFrom = -1f;

        // Leaves, searches, wanders: no standing threats.
        if (newPhase != Phase.FaceOff && display != null)
            display.Stop();
    }

    // ---------------------------------------------------------------
    // Threat level the player should see now (D-76, 25.11):
    // 0 "I see you", 1 "go away", 2 "last warning", 3 "I'll attack",
    // -1 = it walks after him (no standing threats).
    //   Good:   he stands or walks off pretending goodLevel1After s -> 1;
    //           backing away brings it back to 0. Never above 1.
    //   Medium: counts down to the attack: the first third of the time
    //           to hide - 1, the second - 2, the last - 3.
    //   Angry:  the same, until its charge.
    // ---------------------------------------------------------------
    private int WantedLevel(PlayerAction act)
    {
        if (answer == Answer.Follow)
            return -1;

        if (Mood == BearMoodLevel.Good)
        {
            if (act == PlayerAction.Stands || answer == Answer.Threat)
                goodImpatience += Time.deltaTime;
            else if (act == PlayerAction.BacksAway || act == PlayerAction.BacksAwaySlow)
                goodImpatience = Mathf.Max(0f, goodImpatience - 2f * Time.deltaTime);
            return goodImpatience >= goodLevel1After ? 1 : 0;
        }

        if (answer == Answer.Threat)
            return LevelByTime(escapeDeadline, escapeTotal);
        if (answer == Answer.Hesitate)
            return LevelByTime(answerEnd, hesitateTotal);
        return 0;
    }

    private static int LevelByTime(float deadline, float total)
    {
        float left = (deadline - Time.time) / Mathf.Max(1f, total);
        return left > 0.66f ? 1 : left > 0.33f ? 2 : 3;
    }

    // Contact (25.11): to where he really is, from the body line (bones -
    // the lunge carries the muzzle forward, the contact zone goes with it).
    private bool TouchesPlayer(out float distance)
    {
        Vector3 player = Knowledge.Player.position;
        distance = bodyPoints != null && bodyPoints.Active
            ? bodyPoints.DistanceFromBody(player)
            : FlatDistance(player, transform.position);
        return distance <= contactDistance;
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

    private float DistanceToPlayer()
    {
        return FlatDistance(Knowledge.LastKnownPosition, transform.position);
    }

    private void Log(string text)
    {
        Debug.Log("[FaceOff] " + Mood + " | " + phase + " | " + text);
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

    // Selected bear in Play: magenta - where it leaves to.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !enabled || phase != Phase.Leave)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(leavePoint, 0.8f);
        Gizmos.DrawLine(transform.position, leavePoint);
    }
}
