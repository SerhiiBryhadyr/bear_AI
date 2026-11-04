using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearStalkState - mode Knows, branch "stalking" (D-15, D-35, D-44).
// The bear knows where the player is, the player does not know where
// the bear is. The bear follows him hidden, so BearPlayerAnalysis can
// read his behaviour (and later pick a pattern).
//
// 1. Hiding place (D-35): a few RANDOM points around the player, the
//    best one the bear found wins (it may miss the ideal spot - fair).
//    The points are taken around the place where the player WILL be
//    when the bear gets there (his last seen velocity x travel time),
//    otherwise the bear always arrives where he used to be (tests 2.11:
//    places 16-43 m, the bear itself 50-65 m behind).
//    A good point: the player cannot see it (bush, out of his view;
//    3 rays - head, body, rear - so a trunk hides a third of the bear,
//    a bush half), the bear watches him from there FROM COVER (open
//    ground gets no bonus for watching), it is alongside-behind his
//    route, on the bear's own side of the route, at a comfortable
//    distance (closer behind cover, farther in the open). The bear
//    commits to it: a new choice only when the place went bad or after
//    repickTime - no twitching every second. Its own side gets a bonus:
//    it crosses only when the other side has clearly better cover, and
//    never right under the player's eyes.
// 2. Rhythm: walks walkTime, stops pauseTime and turns to the player,
//    walks again - not a gliding ghost. Speed follows the player.
//    Fallen behind (farther than the hiding ring, or far behind its own
//    chosen place) -> no pauses and faster, until it has caught up.
// 3. Head (D-32 logic): while walking its own way the bear turns its
//    head (BearVision) to where it thinks the player is. The visible
//    head turn comes with D-32.
// 4. The bear sees the player looking its way -> freezes.
// 5. Without sight (D-44): follows by ear and prediction
//    (BearKnowledge.EstimatePlayerPosition). After peekAfter seconds it
//    must check: steps out to a place with a view to where the player
//    should be. Nothing there -> BearKnowledge loses the player after
//    stalkLoseContactTime -> SearchLastKnown.
// 6. Spotted (D-42, simplified): close -> Encounter (Tracking); far ->
//    stands and reads the player: he runs -> "he saw me" -> follows
//    faster and closer, still hidden; he walks on -> "he did not see
//    me". BearKnowledge forgets being spotted after ~10 s (D-43).
// 7. STUB until the patterns exist (open question 2.11): a pattern is
//    chosen (BearPlayerAnalysis) and the bear does not believe it is
//    spotted -> it walks openly to the player -> Encounter. Every choice
//    is used once; an unused one is still valid when the bear finds the
//    player again (D-38). Switch off: stubPatternToEncounter.
// =====================================================================
public class BearStalkState : BearStateBehaviour
{
    public override BearState Id => BearState.Stalk;

    [Header("Hiding place (D-35)")]
    [Tooltip("Random points are taken in a ring around the player, from minDistance to searchRadius (m).")]
    public float minDistance = 12f;
    public float searchRadius = 50f;
    [Tooltip("Random points checked per choice. Few on purpose: the bear may miss the ideal spot.")]
    public int candidateCount = 12;
    [Tooltip("Share of the points taken on the bear's own side of the player's route.")]
    [Range(0f, 1f)]
    public float ownSideShare = 0.75f;
    [Tooltip("Preferred distance to the player behind cover (m).")]
    public float coverDistance = 20f;
    [Tooltip("Preferred distance in the open (m): nothing hides the bear, so farther.")]
    public float openDistance = 45f;
    [Tooltip("Best angle from the player's route (deg): 90 = alongside, 180 = straight behind.")]
    public float behindAngle = 130f;
    [Tooltip("A new hiding place not more often than this (s, random between x and y), unless the current one went bad.")]
    public Vector2 repickTime = new Vector2(5f, 8f);
    public float arriveDistance = 1.5f;
    [Tooltip("Places are picked around where the player WILL be when the bear gets there: his last seen velocity x travel time, at most this long (s).")]
    public float maxLeadTime = 5f;
    [Tooltip("Bonus for a place from which the bear sees the player FROM COVER (canon D-35): bigger the more hidden the place is, none in the open. Higher = the bear watches more.")]
    public float seeFromHereWeight = 1.5f;

    [Header("Rhythm (not a gliding ghost)")]
    public Vector2 walkTime = new Vector2(3f, 8f);
    public Vector2 pauseTime = new Vector2(1f, 3f);
    public float turnSpeed = 120f;

    [Header("Speed")]
    [Tooltip("Speed = the player's speed x speedFactor + speedBonus, clamped (m/s).")]
    public float speedFactor = 1.2f;
    public float speedBonus = 0.5f;
    public float minSpeed = 2f;
    public float maxSpeed = 9.5f;
    [Tooltip("Farther from the player than searchRadius + this (m) -> fallen behind: no pauses, faster.")]
    public float catchUpMargin = 5f;
    [Tooltip("Catching up: the player's speed + this (m/s), at most maxSpeed.")]
    public float catchUpSpeedBonus = 2.5f;
    [Tooltip("Not seen longer than this (s) -> the bear assumes the player walks on at assumedPlayerSpeed or faster, even if it last saw him standing.")]
    public float assumeMovingAfter = 2f;
    public float assumedPlayerSpeed = 4f;

    [Header("Freeze")]
    [Tooltip("The bear sees the player looking within this angle (deg) of it -> stands still.")]
    public float freezeViewAngle = 50f;
    public float unfreezeDelay = 0.5f;

    [Header("Without sight (D-44)")]
    [Tooltip("Without seeing the player this long (s) the bear steps out to check.")]
    public float peekAfter = 10f;
    [Tooltip("While stalking the player is lost only after this long without sight and sound (s). Normal - BearKnowledge.loseContactTime.")]
    public float stalkLoseContactTime = 15f;
    [Tooltip("The last seen velocity is used for prediction at most this long (s).")]
    public float maxPredictionTime = 6f;
    public float peekSearchRadius = 10f;
    [Tooltip("Looks from the peek point this long (s) before trying once more.")]
    public float peekLookTime = 2f;

    [Header("Spotted (D-42, simplified)")]
    [Tooltip("Spotted closer than this, or the player walks toward the bear this close -> Encounter (Tracking), m.")]
    public float encounterDistance = 20f;
    [Tooltip("While it believes it is spotted, hiding places are at least this far (m). Tracking lets the player go at 25 m.")]
    public float spottedMinDistance = 25f;
    [Tooltip("Stands and reads the player this long after being spotted (s).")]
    public float readTime = 2.5f;
    [Tooltip("The player is seen moving faster than this (m/s) -> 'he saw me, he runs'.")]
    public float playerRunSpeed = 6f;
    [Tooltip("'He saw me': distances x this (closer) for pursuitTime seconds.")]
    public float pursuitDistanceFactor = 0.6f;
    public float pursuitTime = 15f;

    [Header("Stub until the patterns exist (open question 2.11)")]
    [Tooltip("A pattern is chosen and the bear does not believe it is spotted -> walks openly to the player -> Encounter at encounterDistance. Off = stalking only.")]
    public bool stubPatternToEncounter = true;
    [Tooltip("Walking openly to the player: his speed + this (m/s).")]
    public float stubApproachSpeedBonus = 1.5f;

    [Header("Checks")]
    [Tooltip("Height of the bear's body above a point when checking whether the player could see it (m).")]
    public float bodyHeight = 0.9f;
    [Tooltip("Half the bear's body length (m): head and rear points of the 3-ray check.")]
    public float bodyHalfLength = 0.9f;
    [Tooltip("The player's eye height above his feet, if he has no CharacterController (m).")]
    public float playerEyeHeight = 1.6f;

    [Header("Debug")]
    public bool logChoices = true;

    private enum Phase { Walk, Pause, Freeze, Read, Peek, Approach }

    // Out of the player's view an open place is still risky: he may turn
    // (canon D-35: a bear right behind is caught by the first look back).
    private const float OutOfViewExposure = 0.5f;
    // Own side (or straight behind / ahead) gets this bonus: crossing must pay off.
    private const float SideBonus = 1.5f;
    // Keeping the current place gets a bonus - no switching for a tiny gain.
    private const float KeepBonus = 0.5f;
    // Exposure above this = the current place went bad.
    private const float GoodExposureLimit = 0.6f;
    // Farther from the player than its own place + this (m) = behind its plan: no pauses.
    private const float BehindPlanMargin = 10f;
    // The stub starts only if the bear saw the player not longer ago than this (s).
    private const float StubFreshSight = 3f;

    private BearVision vision;
    private BearPlayerAnalysis analysis;
    private CharacterController playerBody;

    private Phase phase;
    private float phaseTimer;
    private float repickTimer;
    private float checkTimer;
    private float freezeReleaseTimer;

    private bool hasTarget;
    private bool arrived;
    private bool destinationSet;
    private Vector3 target;
    private bool lagging;

    private Vector3[] candidatePoints;
    private float[] candidateScores;
    private bool[] candidateOtherSide;

    private Vector3 peekPoint;
    private bool peekArrived;
    private int peekAttempts;

    private Vector3 route;              // the player's movement direction (flat), last seen
    private int side;                   // +1 right of the route, -1 left, 0 unknown
    private int knownNoticedCount;
    private float pursuitUntil = -999f;

    private float usedChoiceTime = -999f;   // the analysis choice the stub already used (kept between stalkings)
    private float approachTimer;

    // ---------------------------------------------------------------
    // Setup / cleanup.
    // ---------------------------------------------------------------
    protected override void Awake()
    {
        base.Awake();
        vision = GetComponent<BearVision>();
        analysis = GetComponent<BearPlayerAnalysis>();
    }

    private void OnEnable()
    {
        Knowledge.SetLoseContactTime(stalkLoseContactTime);
        knownNoticedCount = Knowledge.NoticedCount;
        hasTarget = false;
        arrived = false;
        lagging = false;
        side = 0;
        peekAttempts = 0;
        StartWalk();

        Debug.Log("Bear entered STALKING (contact: " + Knowledge.LastContactSource + ")");
    }

    private void OnDisable()
    {
        if (vision != null)
            vision.ClearLookTarget();

        if (bearAI == null)
            return;

        if (Knowledge != null)
            Knowledge.ResetLoseContactTime();

        if (Movement != null)
            Movement.SetSpeedMultiplier(1f);
    }

    // ---------------------------------------------------------------
    // Main loop. Order matters: stub approach -> spotted -> reading ->
    // freeze -> stub start -> peek -> hiding place.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!Knowledge.HasLastKnownPosition || Knowledge.Player == null || vision == null)
            return;

        UpdateRoute();

        if (Knowledge.IsSeeingPlayer)
            peekAttempts = 0;

        Vector3 player = Knowledge.EstimatePlayerPosition(maxPredictionTime);
        float distance = FlatDistance(player, transform.position);

        // The head follows where the bear thinks the player is (D-32 logic).
        vision.SetLookTarget(player + Vector3.up * PlayerChestHeight());

        if (phase == Phase.Approach)
        {
            UpdateApproach(player, distance);
            return;
        }

        if (CheckSpotted(distance))
            return;

        if (phase == Phase.Read)
        {
            UpdateRead(player);
            return;
        }

        if (UpdateFreeze())
            return;

        if (TryStartStub(distance))
            return;

        if (phase != Phase.Peek && Knowledge.TimeSinceSeen >= peekAfter)
            StartPeek(player);

        if (phase == Phase.Peek)
        {
            UpdatePeek(player);
            return;
        }

        UpdateHiding(player, distance);
    }

    // The player's route = his movement while the bear last saw him.
    private void UpdateRoute()
    {
        Vector3 velocity = Flat(Knowledge.LastKnownVelocity);

        if (velocity.magnitude > 0.5f)
            route = velocity.normalized;
        else if (route == Vector3.zero)
            route = Knowledge.PlayerLookDirection;
    }

    // ---------------------------------------------------------------
    // Spotted (D-42, simplified). Returns true when the state changed.
    // ---------------------------------------------------------------
    private bool CheckSpotted(float distance)
    {
        bool newlySpotted = Knowledge.NoticedCount != knownNoticedCount;
        knownNoticedCount = Knowledge.NoticedCount;

        if (newlySpotted)
        {
            if (distance <= encounterDistance)
            {
                Debug.Log("[Stalk] spotted at " + distance.ToString("F0") + " m - too close to hide: ENCOUNTER");
                ChangeState(BearState.Tracking);
                return true;
            }

            Debug.Log("[Stalk] spotted at " + distance.ToString("F0") + " m - stands and reads the player");
            phase = Phase.Read;
            phaseTimer = readTime;
            Movement.Stop();
            destinationSet = false;
            return false;
        }

        // It believes it is spotted and the player walks right at it.
        if (Knowledge.BelievesNoticed && Knowledge.IsSeeingPlayer && distance <= encounterDistance && PlayerComesToMe())
        {
            Debug.Log("[Stalk] the player comes toward the bear (" + distance.ToString("F0") + " m) - ENCOUNTER");
            ChangeState(BearState.Tracking);
            return true;
        }

        return false;
    }

    private bool PlayerComesToMe()
    {
        Vector3 velocity = Flat(Knowledge.LastKnownVelocity);

        if (velocity.magnitude < 1f)
            return false;

        Vector3 toBear = Flat(transform.position - Knowledge.LastSeenPosition);
        return Vector3.Dot(velocity.normalized, toBear.normalized) > 0.7f;
    }

    // ---------------------------------------------------------------
    // Reading the player after being spotted far away (D-42).
    // He runs -> "he saw me": follow faster and closer, still hidden.
    // He walks on -> "he probably did not see me".
    // ---------------------------------------------------------------
    private void UpdateRead(Vector3 player)
    {
        Movement.Stop();
        Movement.FaceTowards(player, turnSpeed);

        if (Knowledge.IsSeeingPlayer && Knowledge.LastKnownVelocity.magnitude >= playerRunSpeed)
        {
            pursuitUntil = Time.time + pursuitTime;
            Debug.Log("[Stalk] the player RUNS - he saw me: follows faster and closer, still hidden (" + pursuitTime + " s)");
            hasTarget = false;
            StartWalk();
            return;
        }

        phaseTimer -= Time.deltaTime;
        if (phaseTimer > 0f)
            return;

        Debug.Log("[Stalk] the player walks on - he probably did not see me");
        hasTarget = false;
        StartWalk();
    }

    // ---------------------------------------------------------------
    // Freeze: the bear SEES the player looking its way -> stands still
    // until he looks away (+ unfreezeDelay).
    // ---------------------------------------------------------------
    private bool UpdateFreeze()
    {
        bool watched = Knowledge.IsSeeingPlayer && PlayerLooksAtMe(freezeViewAngle);

        if (watched)
        {
            if (phase != Phase.Freeze)
            {
                phase = Phase.Freeze;
                Movement.Stop();
                destinationSet = false;

                if (logChoices)
                    Debug.Log("[Stalk] FREEZE: the player looks this way");
            }

            freezeReleaseTimer = unfreezeDelay;
            return true;
        }

        if (phase != Phase.Freeze)
            return false;

        freezeReleaseTimer -= Time.deltaTime;
        if (freezeReleaseTimer > 0f)
            return true;

        StartWalk();
        return false;
    }

    private bool PlayerLooksAtMe(float maxAngle)
    {
        Vector3 look = Knowledge.PlayerLookDirection;
        Vector3 toBear = Flat(transform.position - Knowledge.LastSeenPosition);

        if (look == Vector3.zero || toBear.sqrMagnitude < 0.01f)
            return false;

        return Vector3.Angle(look, toBear) <= maxAngle;
    }

    // ---------------------------------------------------------------
    // STUB (open question 2.11) - until the patterns exist.
    // A fresh, unused choice of BearPlayerAnalysis + the bear does not
    // believe it is spotted (D-43) + it saw the player a moment ago ->
    // walks openly to him; at encounterDistance -> Encounter (Tracking).
    // ---------------------------------------------------------------
    private bool TryStartStub(float distance)
    {
        if (!stubPatternToEncounter || analysis == null || !analysis.HasChoice)
            return false;

        if (analysis.TimeSinceChoice > analysis.memoryTime)
            return false;

        float choiceTime = Time.time - analysis.TimeSinceChoice;
        if (choiceTime <= usedChoiceTime + 0.01f)
            return false;                                   // this choice was already used

        if (Knowledge.BelievesNoticed || Knowledge.TimeSinceSeen > StubFreshSight)
            return false;

        usedChoiceTime = choiceTime;
        phase = Phase.Approach;
        approachTimer = 0f;
        destinationSet = false;

        Debug.Log("[Stalk] STUB: pattern " + BearPatternChooser.Name(analysis.LastChoice) + " chosen - walks openly to the player (" +
                  distance.ToString("F0") + " m), Encounter at " + encounterDistance.ToString("F0") + " m (patterns are not made yet)");
        return true;
    }

    private void UpdateApproach(Vector3 player, float distance)
    {
        if (distance <= encounterDistance)
        {
            Debug.Log("[Stalk] STUB: " + distance.ToString("F0") + " m from the player - ENCOUNTER");
            ChangeState(BearState.Tracking);
            return;
        }

        Movement.SetSpeed(Mathf.Clamp(PlayerSpeed() + stubApproachSpeedBonus, minSpeed, maxSpeed));

        approachTimer -= Time.deltaTime;
        if (approachTimer <= 0f)
        {
            Movement.MoveTo(player);
            approachTimer = 0.5f;
        }
    }

    // ---------------------------------------------------------------
    // Peek (D-44): too long without seeing the player -> step out to a
    // place with a view to where he should be, look, hide again.
    // ---------------------------------------------------------------
    private void StartPeek(Vector3 player)
    {
        peekAttempts++;
        phase = Phase.Peek;
        phaseTimer = peekLookTime;
        peekArrived = false;
        destinationSet = false;

        float bestView = -1f;
        peekPoint = transform.position;
        Vector3 playerChest = player + Vector3.up * PlayerChestHeight();

        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = Random.insideUnitCircle * peekSearchRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                continue;

            Vector3 eye = hit.position + Vector3.up * vision.eyeHeight;
            float view = LineOfSight.PointVisibility(eye, playerChest, Knowledge.Player, transform,
                                                     vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
            if (view > bestView)
            {
                bestView = view;
                peekPoint = hit.position;
            }
        }

        // No view from around here - step toward where the player should be.
        if (bestView < 0.25f)
        {
            Vector3 toPlayer = Flat(player - transform.position);
            Vector3 step = transform.position + toPlayer.normalized * Mathf.Min(peekSearchRadius, toPlayer.magnitude * 0.5f);

            if (NavMesh.SamplePosition(step, out NavMeshHit stepHit, 3f, NavMesh.AllAreas))
                peekPoint = stepHit.position;
        }

        float unseen = Knowledge.TimeSinceSeen;
        string since = float.IsInfinity(unseen) ? "has not seen the player yet" : unseen.ToString("F0") + " s without seeing the player";
        Debug.Log("[Stalk] PEEK: " + since + " - steps out to check (D-44)");
    }

    private void UpdatePeek(Vector3 player)
    {
        if (Knowledge.IsSeeingPlayer)
        {
            Debug.Log("[Stalk] peek: the player is there (" + FlatDistance(player, transform.position).ToString("F0") +
                      " m) - back to hiding");
            hasTarget = false;
            StartWalk();
            return;
        }

        if (!peekArrived)
        {
            Movement.SetSpeed(WalkSpeed(false, false));

            if (!destinationSet)
            {
                Movement.MoveTo(peekPoint);
                destinationSet = true;
            }

            if (FlatDistance(transform.position, peekPoint) <= arriveDistance)
            {
                peekArrived = true;
                Movement.Stop();
                destinationSet = false;
            }
            return;
        }

        Movement.FaceTowards(player, turnSpeed);

        phaseTimer -= Time.deltaTime;
        if (phaseTimer > 0f)
            return;

        // One more try a bit further. After that it waits: BearKnowledge
        // loses the player at stalkLoseContactTime (search by vector D-39 - later).
        if (peekAttempts < 2)
            StartPeek(player);
    }

    // ---------------------------------------------------------------
    // Hiding place + rhythm.
    // Places are checked / picked around "anchor" = where the player
    // will be when the bear gets there.
    // ---------------------------------------------------------------
    private void UpdateHiding(Vector3 player, float distance)
    {
        float dt = Time.deltaTime;
        bool inPursuit = Time.time < pursuitUntil;
        float scale = inPursuit ? pursuitDistanceFactor : 1f;

        UpdateLagging(distance, scale);
        float speed = WalkSpeed(lagging, inPursuit);

        repickTimer -= dt;
        checkTimer -= dt;

        bool needCheck = hasTarget && checkTimer <= 0f;
        bool needPick = !hasTarget || repickTimer <= 0f;

        if (needCheck || needPick)
        {
            Vector3 anchor = Anchor(player, speed);
            bool targetBad = false;

            if (needCheck)
            {
                checkTimer = 1f;
                targetBad = !IsStillGood(target, anchor, scale);
            }

            if (needPick || targetBad)
                PickHidingPlace(anchor, player, distance, scale, targetBad);
        }

        if (!hasTarget)
        {
            StandAndFace(player);
            return;
        }

        if (!arrived && FlatDistance(transform.position, target) <= arriveDistance)
        {
            arrived = true;
            repickTimer = Mathf.Min(repickTimer, 1.5f);     // the player moves on - next place soon
        }

        // Fallen behind, or much farther from the player than its own
        // place -> no pauses, the bear has to catch up first.
        bool behindPlan = distance > FlatDistance(target, player) + BehindPlanMargin;
        bool mayPause = !lagging && !behindPlan;

        if (arrived && lagging)
            repickTimer = Mathf.Min(repickTimer, 0.5f);     // its place is left behind - pick a new one now

        // Pause, or waiting at the place: stand and face the player.
        if (arrived || (phase == Phase.Pause && mayPause))
        {
            StandAndFace(player);

            if (phase == Phase.Pause)
            {
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                    StartWalk();
            }
            return;
        }

        if (phase == Phase.Pause)
            StartWalk();                                    // fell behind during a pause - go on at once

        Movement.SetSpeed(speed);

        if (!destinationSet)
        {
            Movement.MoveTo(target);
            destinationSet = true;
        }

        phaseTimer -= dt;
        if (phaseTimer <= 0f && mayPause)
            StartPause();
    }

    // Fallen behind = farther than the hiding ring + catchUpMargin
    // (5 m hysteresis, so it does not blink on the border).
    private void UpdateLagging(float distance, float scale)
    {
        float limit = searchRadius * scale + catchUpMargin;
        bool now = lagging ? distance > limit - 5f : distance > limit;

        if (now == lagging)
            return;

        lagging = now;

        if (logChoices)
            Debug.Log(lagging
                ? "[Stalk] fell behind (" + distance.ToString("F0") + " m) - catches up without pauses"
                : "[Stalk] caught up (" + distance.ToString("F0") + " m)");
    }

    // Where the player will be when the bear gets to its place: his last
    // seen velocity x the time the bear needs (at most maxLeadTime).
    // Follows the ground (NavMesh) so the rays do not start inside a hill.
    private Vector3 Anchor(Vector3 player, float bearSpeed)
    {
        Vector3 velocity = Flat(Knowledge.LastKnownVelocity);
        if (velocity.magnitude < 0.5f)
            return player;                                  // he stands - the place is around him

        float lead = Mathf.Min(maxLeadTime, FlatDistance(transform.position, player) / Mathf.Max(bearSpeed, 1f));
        Vector3 ahead = player + velocity * lead;

        if (NavMesh.SamplePosition(ahead, out NavMeshHit aheadHit, 5f, NavMesh.AllAreas) &&
            NavMesh.SamplePosition(player, out NavMeshHit playerHit, 5f, NavMesh.AllAreas))
        {
            ahead.y = player.y + (aheadHit.position.y - playerHit.position.y);
        }

        return ahead;
    }

    private void PickHidingPlace(Vector3 anchor, Vector3 player, float bearDistance, float scale, bool currentWentBad)
    {
        float minD = minDistance * scale;
        if (Knowledge.BelievesNoticed)
            minD = Mathf.Max(minD, spottedMinDistance);
        float maxD = Mathf.Max(minD + 5f, searchRadius * scale);

        if (side == 0)
            side = SideOf(transform.position - player);

        // 1. Score the current place and the random points.
        EnsureBuffers(candidateCount + 1);
        int count = 0;

        if (hasTarget && !currentWentBad)
            count = AddCandidate(target, KeepBonus, count, anchor, minD, maxD, scale);

        int ownCount = Mathf.RoundToInt(candidateCount * ownSideShare);

        for (int i = 0; i < candidateCount; i++)
        {
            Vector3 candidate = anchor + CandidateDirection(i < ownCount) * Random.Range(minD, maxD);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                continue;

            count = AddCandidate(hit.position, 0f, count, anchor, minD, maxD, scale);
        }

        // 2. Side rule (D-35): its own side (or straight behind / ahead)
        //    always gets SideBonus. It crosses only when the other side is
        //    clearly better - noticeably better cover there.
        //    (4.11: "no bonus when its own side has no cover" made it cross
        //    away from a row of bushes with gaps - reverted.)
        bool found = false;
        float bestScore = -50f;
        Vector3 best = transform.position;

        for (int i = 0; i < count; i++)
        {
            float score = candidateScores[i];
            if (score <= -50f)
                continue;                                   // outside the ring / crossing under his eyes

            if (!candidateOtherSide[i])
                score += SideBonus;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidatePoints[i];
                found = true;
            }
        }

        repickTimer = Random.Range(repickTime.x, repickTime.y);

        if (!found)
        {
            if (logChoices)
                Debug.Log("[Stalk] no hiding place found around the player - stays");
            return;
        }

        bool changed = !hasTarget || FlatDistance(best, target) > 0.5f;
        if (!changed)
            return;

        int newSide = SideOf(best - anchor);
        bool crossing = side != 0 && newSide != 0 && newSide != side;
        if (newSide != 0)
            side = newSide;

        target = best;
        hasTarget = true;
        arrived = false;
        destinationSet = false;

        if (logChoices)
        {
            float open = VisibleFromPlayer(best, anchor);
            bool sees = SeesPlayerFrom(best, anchor);
            Debug.Log("[Stalk] hiding place " + FlatDistance(best, anchor).ToString("F0") + " m from where the player will be (" +
                      FlatDistance(anchor, player).ToString("F0") + " m ahead of him), bear now " + bearDistance.ToString("F0") +
                      " m from him | open to him " + Mathf.RoundToInt(open * 100f) + "%, sees him from there: " + (sees ? "yes" : "no") +
                      (crossing ? ", CROSSES to the other side" : "") +
                      (currentWentBad ? " (the old place went bad)" : ""));
        }
    }

    // Scores one point and stores it for the side rule.
    private int AddCandidate(Vector3 point, float bonus, int count, Vector3 anchor, float minD, float maxD, float scale)
    {
        float score = Score(point, anchor, minD, maxD, scale, out bool otherSide);

        candidatePoints[count] = point;
        candidateScores[count] = score + bonus;
        candidateOtherSide[count] = otherSide;
        return count + 1;
    }

    private void EnsureBuffers(int size)
    {
        if (candidatePoints != null && candidatePoints.Length >= size)
            return;

        candidatePoints = new Vector3[size];
        candidateScores = new float[size];
        candidateOtherSide = new bool[size];
    }

    // Own-side points: alongside-behind the route on the bear's side;
    // the rest anywhere (so it can still cross when the other side is better).
    private Vector3 CandidateDirection(bool wantOwnSide)
    {
        bool ownSide = wantOwnSide && side != 0 && route != Vector3.zero;

        if (ownSide)
        {
            float angle = Random.Range(40f, 170f) * side;
            return Quaternion.AngleAxis(angle, Vector3.up) * route;
        }

        Vector2 random = Random.insideUnitCircle.normalized;
        return new Vector3(random.x, 0f, random.y);
    }

    // ---------------------------------------------------------------
    // How good a hiding place is (higher = better). "player" here is
    // the anchor - where he will be. The side rule is added later
    // (PickHidingPlace), it depends on the other points.
    // Hidden from the player counts most, then distance, "the bear
    // watches him from cover", then "alongside-behind"; a long trip
    // there costs a bit.
    // ---------------------------------------------------------------
    private float Score(Vector3 point, Vector3 player, float minD, float maxD, float scale,
                        out bool otherSide)
    {
        otherSide = false;

        Vector3 fromPlayer = Flat(point - player);
        float distance = fromPlayer.magnitude;

        if (distance < minD - 1f || distance > maxD + 10f)
            return -100f;

        float visible = VisibleFromPlayer(point, player);
        bool inView = InPlayerView(fromPlayer);
        float exposure = visible * (inView ? 1f : OutOfViewExposure);

        // Crossing the route right under the player's eyes - never.
        int pointSide = SideOf(fromPlayer);
        otherSide = side != 0 && pointSide != 0 && pointSide != side;
        if (otherSide && inView)
            return -100f;

        float ideal = Mathf.Clamp(Mathf.Lerp(coverDistance, openDistance, visible) * scale, minD, maxD);
        float distanceScore = 1f - Mathf.Clamp01(Mathf.Abs(distance - ideal) / 30f);

        float behindScore = 0.5f;
        if (route != Vector3.zero)
            behindScore = 1f - Mathf.Clamp01(Mathf.Abs(Vector3.Angle(route, fromPlayer) - behindAngle) / 130f);

        // Watching FROM COVER (D-35: "behind a wall of bushes"): the more
        // hidden the place, the bigger the bonus; open ground gets none
        // (tests 4.11: the bonus in the open made it walk right behind
        // the player across a field).
        float cover = 1f - visible;
        float watch = cover > 0f && SeesPlayerFrom(point, player) ? cover : 0f;
        float trip = Mathf.Clamp01(FlatDistance(transform.position, point) / 40f);

        return (1f - exposure) * 3f + distanceScore * 1.5f + behindScore
             + watch * seeFromHereWeight - trip;
    }

    private bool IsStillGood(Vector3 point, Vector3 player, float scale)
    {
        Vector3 fromPlayer = Flat(point - player);
        float distance = fromPlayer.magnitude;

        float minD = minDistance * scale;
        if (Knowledge.BelievesNoticed)
            minD = Mathf.Max(minD, spottedMinDistance);

        if (distance < minD - 2f || distance > searchRadius * scale + 15f)
            return false;

        float exposure = VisibleFromPlayer(point, player) * (InPlayerView(fromPlayer) ? 1f : OutOfViewExposure);
        return exposure < GoodExposureLimit;
    }

    // How well the player (at "player") could see a bear standing at
    // this point: 3 rays - head, body, rear - and the average. The bear
    // is taken broadside to him (it walks alongside his route): the
    // longest silhouette. Same rule as the eyes (LineOfSight, D-31):
    // open 1, one bush 0.5, rock 0; a trunk covers one ray of three.
    private float VisibleFromPlayer(Vector3 point, Vector3 player)
    {
        Vector3 eye = player + Vector3.up * PlayerEyeHeight();
        Vector3 toPoint = Flat(point - player);
        Vector3 along = toPoint.sqrMagnitude > 0.0001f
            ? Vector3.Cross(Vector3.up, toPoint.normalized)
            : Vector3.right;

        Vector3 body = point + Vector3.up * bodyHeight;
        Vector3 head = body + along * bodyHalfLength + Vector3.up * 0.1f;
        Vector3 rear = body - along * bodyHalfLength;

        float sum = RayFromPlayer(eye, head) + RayFromPlayer(eye, body) + RayFromPlayer(eye, rear);
        return sum / 3f;
    }

    private float RayFromPlayer(Vector3 eye, Vector3 bearPoint)
    {
        return LineOfSight.PointVisibility(eye, bearPoint, transform, Knowledge.Player,
                                           vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
    }

    // Canon D-35: "the bear sees the player from there". Same rule as
    // BearVision for a walking player in front (the head turns to him):
    // range = maxViewDistance x visible x walkFactor.
    private bool SeesPlayerFrom(Vector3 point, Vector3 player)
    {
        Vector3 eye = point + Vector3.up * vision.eyeHeight;
        Vector3 chest = player + Vector3.up * PlayerChestHeight();
        float distance = Vector3.Distance(eye, chest);

        if (distance > vision.maxViewDistance)
            return false;

        float visible = LineOfSight.PointVisibility(eye, chest, Knowledge.Player, transform,
                                                    vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
        return distance <= vision.maxViewDistance * visible * vision.walkFactor;
    }

    private bool InPlayerView(Vector3 fromPlayer)
    {
        Vector3 look = Knowledge.PlayerLookDirection;
        if (look == Vector3.zero)
            return true;                                // unknown - assume he could see it

        return Vector3.Angle(look, fromPlayer) <= freezeViewAngle;
    }

    // +1 = right of the player's route, -1 = left, 0 = ahead / behind or unknown.
    private int SideOf(Vector3 fromPlayer)
    {
        fromPlayer.y = 0f;

        if (route == Vector3.zero || fromPlayer.sqrMagnitude < 0.01f)
            return 0;

        float angle = Vector3.SignedAngle(route, fromPlayer, Vector3.up);
        if (Mathf.Abs(angle) < 15f || Mathf.Abs(angle) > 165f)
            return 0;

        return angle > 0f ? 1 : -1;
    }

    // ---------------------------------------------------------------
    // Speed.
    // ---------------------------------------------------------------

    // The player's speed as the bear knows it. Not seen for a while ->
    // it assumes he walks on (even if it last saw him standing).
    private float PlayerSpeed()
    {
        float speed = Flat(Knowledge.LastKnownVelocity).magnitude;

        if (!Knowledge.IsSeeingPlayer && Knowledge.TimeSinceSeen > assumeMovingAfter)
            speed = Mathf.Max(speed, assumedPlayerSpeed);

        return speed;
    }

    private float WalkSpeed(bool catchingUp, bool inPursuit)
    {
        float playerSpeed = PlayerSpeed();
        float speed = Mathf.Clamp(playerSpeed * speedFactor + speedBonus, minSpeed, maxSpeed);

        if (catchingUp)
            speed = Mathf.Max(speed, Mathf.Min(maxSpeed, playerSpeed + catchUpSpeedBonus));

        if (inPursuit)
            speed = Mathf.Min(maxSpeed, Mathf.Max(speed, playerSpeed * 1.1f + 1f));

        return speed;
    }

    // ---------------------------------------------------------------
    // Small helpers.
    // ---------------------------------------------------------------
    private void StandAndFace(Vector3 player)
    {
        Movement.Stop();
        destinationSet = false;
        Movement.FaceTowards(player, turnSpeed);
    }

    private void StartWalk()
    {
        phase = Phase.Walk;
        phaseTimer = Random.Range(walkTime.x, walkTime.y);
        destinationSet = false;
    }

    private void StartPause()
    {
        phase = Phase.Pause;
        phaseTimer = Random.Range(pauseTime.x, pauseTime.y);
        Movement.Stop();
        destinationSet = false;
    }

    // Eye / chest height of the player above the point the bear tracks
    // (his transform), from his CharacterController if he has one.
    private float PlayerEyeHeight()
    {
        FindPlayerBody();
        if (playerBody == null)
            return playerEyeHeight;

        return playerBody.bounds.max.y - Knowledge.Player.position.y - 0.15f;
    }

    private float PlayerChestHeight()
    {
        FindPlayerBody();
        if (playerBody == null)
            return playerEyeHeight * 0.6f;

        return playerBody.bounds.center.y - Knowledge.Player.position.y;
    }

    private void FindPlayerBody()
    {
        if (playerBody == null && Knowledge.Player != null)
            playerBody = Knowledge.Player.GetComponent<CharacterController>();
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

    // Selected bear in Play: green - hiding place, yellow - peek point,
    // red - walking openly to the player (stub).
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !enabled)
            return;

        if (hasTarget && phase != Phase.Approach)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target, 0.8f);
            Gizmos.DrawLine(transform.position, target);
        }

        if (phase == Phase.Peek)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(peekPoint, 0.8f);
            Gizmos.DrawLine(transform.position, peekPoint);
        }

        if (phase == Phase.Approach && Knowledge != null && Knowledge.HasLastKnownPosition)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, Knowledge.LastKnownPosition);
        }
    }
}
