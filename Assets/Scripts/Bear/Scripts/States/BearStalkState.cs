using System.Text;
using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearStalkState - mode Knows, branch "stalking" (D-15, D-35, D-44).
// The bear knows where the player is, the player does not know where
// the bear is. The bear follows him hidden, so BearPlayerAnalysis can
// read his behaviour (and later pick a pattern).
//
// 1. Hiding place (D-35): RANDOM points around the player, the best one
//    the bear found wins (it may miss the ideal spot - fair). Points go
//    to both sides of his route, more to the bear's own side (8.11:
//    14 / 10; before - 9 own, 3 anywhere, so the other side got ~1 and
//    the bear never found the bushes across the field), only
//    alongside-behind him (pointSector), nothing ahead.
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
//    never right under the player's eyes. The side is "left / right of
//    his route": when the player turns more than sideTurnAngle, it is
//    read again from where the bear actually stands (8.11: after a
//    turn the old side sent the bear across the open field).
// 2. Rhythm: walks walkTime, stops pauseTime and turns to the player,
//    walks again - not a gliding ghost. It stops only in cover
//    (pauseMaxExposure); in the open it walks on (8.11: 9 of 25 pauses
//    were in the open). Speed follows the player.
//    Fallen behind (farther than the hiding ring, or far behind its own
//    chosen place) -> no pauses and faster, until it has caught up.
// 3. Head (D-32 logic): while walking its own way the bear turns its
//    head (BearVision) to where it thinks the player is. The visible
//    head turn comes with D-32.
// 4. The bear sees the player looking its way -> freezes.
// 5. Without sight (D-44): follows by ear and prediction
//    (BearKnowledge.EstimatePlayerPosition). No contact at all - no
//    sight AND no sound - for noContactPeekTime seconds -> it does not
//    wait, it peeks: steps out to a place with a view toward where the
//    player probably is (where it last knew him + a short way along his
//    route: he either stopped or walked on, the view cone covers both).
//    Not more often than peekInterval (dense bushes block its own view
//    all the time). Hears him -> knows where he is, no peek (8.11:
//    before - 10 s by sight only, the bear waited at the old spot while
//    the player quietly walked on, and lost him). Nothing there ->
//    BearKnowledge loses the player after stalkLoseContactTime ->
//    SearchLastKnown.
// 6. Spotted (D-42, simplified): close -> Encounter (Tracking); far ->
//    stands and reads the player: he runs -> "he saw me" -> follows
//    faster and closer, still hidden; he walks on -> "he did not see
//    me". BearKnowledge forgets being spotted after ~10 s (D-43).
// 7. Pattern (D-03, D-52): BearPlayerAnalysis has made a fresh choice,
//    the bear does not believe it is spotted and saw the player a
//    moment ago -> hands over to the pattern's state (now only
//    RearPursuit). forcePattern says WHICH pattern, not WHEN: the bear
//    first stalks until the analysis chooses. Every choice is used
//    once; an unused one is still valid when the bear finds the player
//    again (D-38). A new pattern not sooner than patternCooldown after
//    the last one ended.
// 8. DEBUG LOGS - TEMPORARY (8.11, tuning the side rule): [Candidates]
//    on every choice, [StalkTick] every second, [Rhythm] on pauses and
//    arrivals. Behaviour does not depend on them. Remove when the
//    algorithm is approved.
// =====================================================================
public class BearStalkState : BearStateBehaviour
{
    public override BearState Id => BearState.Stalk;

    [Header("Hiding place (D-35)")]
    [Tooltip("Random points are taken in a ring around the player, from minDistance to searchRadius (m).")]
    public float minDistance = 12f;
    public float searchRadius = 50f;
    [Tooltip("Random points per choice on the bear's own side of the player's route. Side not known yet -> all points split half / half.")]
    public int ownSidePoints = 14;
    [Tooltip("Random points per choice on the other side - so it can find better cover there (crossing still needs the side bonus to pay off).")]
    public int otherSidePoints = 10;
    [Tooltip("Where the points go on each side, deg from the player's route: 40 = alongside-ahead, 180 = straight behind. Nothing straight ahead.")]
    public Vector2 pointSector = new Vector2(40f, 180f);
    [Tooltip("The player's route turned more than this (deg) since the bear's side was set -> the side is read again from where the bear actually stands.")]
    public float sideTurnAngle = 60f;
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
    [Tooltip("Stops for a pause only where the player could see it less than this (visible x 1 in his view, x 0.5 out of it - the same rule as for hiding places). Otherwise walks on and checks again every 0.5 s.")]
    [Range(0f, 1f)]
    public float pauseMaxExposure = 0.5f;
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
    [Tooltip("No contact at all - no sight and no sound - this long (s) -> the bear peeks out to find the player again.")]
    public float noContactPeekTime = 4f;
    [Tooltip("A new peek not sooner than this after the last one (s): in dense bushes the bear loses sight all the time.")]
    public float peekInterval = 8f;
    [Tooltip("Where to look when peeking: the player may have walked on along his route for up to this long (s) - the bear looks halfway (stood still / walked on).")]
    public float peekLeadTime = 4f;
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

    public enum PatternPick { Auto, RearPursuit }

    [Header("Patterns (D-03, D-52)")]
    [Tooltip("Which pattern starts once the analysis has chosen. RearPursuit = always pattern 1 (the only one made). Auto = the analysis choice; a pattern not made yet -> keeps stalking. Changes WHICH, not WHEN.")]
    public PatternPick forcePattern = PatternPick.RearPursuit;
    [Tooltip("A new pattern not sooner than this after the previous one ended (s).")]
    public float patternCooldown = 60f;
    [Tooltip("The player looked the bear's way (FREEZE) -> no pattern starts for this long (s): he may have seen it (10.11).")]
    public float patternAfterLook = 5f;

    [Header("Checks")]
    [Tooltip("Height of the bear's body above a point when checking whether the player could see it (m). Used only without BearBodyPoints (13.11: taken from the bones).")]
    public float bodyHeight = 0.9f;
    [Tooltip("Half the bear's body length (m): head and rear points of the 3-ray check. Used only without BearBodyPoints (13.11: taken from the bones).")]
    public float bodyHalfLength = 0.9f;
    [Tooltip("The player's eye height above his feet, if he has no CharacterController (m).")]
    public float playerEyeHeight = 1.6f;

    [Header("Debug")]
    public bool logChoices = true;
    [Tooltip("TEMPORARY (8.11): every choice of a hiding place -> [Candidates]: the bear's side, how many points on each side, the best ones with every part of their score.")]
    public bool logCandidates = true;
    [Tooltip("How many of the best points [Candidates] lists.")]
    public int logCandidatesTop = 6;
    [Tooltip("TEMPORARY (8.11): every tickInterval s -> [StalkTick] (where the bear is against the REAL player), plus [Rhythm] on pauses and arrivals.")]
    public bool logTick = true;
    public float tickInterval = 1f;

    private enum Phase { Walk, Pause, Freeze, Read, Peek }

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
    // A pattern starts only if the bear saw the player not longer ago than this (s).
    private const float PatternFreshSight = 3f;

    private BearVision vision;
    private BearPlayerAnalysis analysis;
    private BearRearPursuitState rearPursuit;
    private BearBodyPoints bodyPoints;
    private CharacterController playerBody;

    private Phase phase;
    private float phaseTimer;
    private float repickTimer;
    private float checkTimer;
    private float freezeReleaseTimer;
    private int pauseSkips;             // pause checks skipped in the open during this walk

    private bool hasTarget;
    private bool arrived;
    private bool destinationSet;
    private Vector3 target;
    private bool lagging;

    private Vector3[] candidatePoints;
    private float[] candidateScores;
    private bool[] candidateOtherSide;

    // Debug (TEMPORARY, 8.11): what each candidate's score is made of.
    private struct CandidateParts
    {
        public int side;              // +1 right of the route, -1 left, 0 behind / ahead
        public float angle;           // deg from the player's route: 0 = ahead, 180 = straight behind
        public float distance;        // m from the anchor (where the player will be)
        public float ideal;           // preferred distance for this much cover (m)
        public float visible;         // 0..1: how much of the bear the player could see there
        public bool inView;           // inside the player's view cone (freezeViewAngle)
        public float hidden;          // score part: (1 - exposure) x 3
        public float distanceScore;   // score part: closeness to ideal x 1.5
        public float behind;          // score part: alongside-behind (0..1)
        public float watch;           // score part: sees him from cover x seeFromHereWeight
        public float trip;            // m from the bear (score part: -trip / 40, at most -1)
        public string rejected;       // why the point is out (-100), or null
    }

    private CandidateParts[] candidateParts;
    private float[] candidateFinal;       // score + side bonus
    private bool[] candidateIsCurrent;    // the current place (keep bonus)
    private float tickTimer;
    private Vector3 lastTickBear;
    private Vector3 lastTickPlayer;

    private Vector3 peekPoint;
    private Vector3 peekAim;            // where the bear looks from the peek point
    private bool peekArrived;
    private int peekAttempts;
    private float lastPeekTime = -999f;

    private Vector3 route;              // the player's movement direction (flat), last seen
    private int side;                   // +1 right of the route, -1 left, 0 unknown
    private Vector3 sideRoute;          // the player's route when "side" was set
    private int knownNoticedCount;
    private float pursuitUntil = -999f;

    private float usedChoiceTime = -999f;   // the analysis choice already used (kept between stalkings)
    private float lastWatchedTime = -999f;  // the last moment the player looked the bear's way

    // ---------------------------------------------------------------
    // Setup / cleanup.
    // ---------------------------------------------------------------
    protected override void Awake()
    {
        base.Awake();
        vision = GetComponent<BearVision>();
        analysis = GetComponent<BearPlayerAnalysis>();
        rearPursuit = GetComponent<BearRearPursuitState>();
        bodyPoints = GetComponent<BearBodyPoints>();
    }

    private void OnEnable()
    {
        Knowledge.SetLoseContactTime(stalkLoseContactTime);
        Movement.SetSneak(true);      // crouched walk while stalking (BearAnimator, 14.11)
        knownNoticedCount = Knowledge.NoticedCount;
        hasTarget = false;
        arrived = false;
        lagging = false;
        side = 0;
        sideRoute = Vector3.zero;
        peekAttempts = 0;
        lastPeekTime = -999f;
        StartWalk();

        tickTimer = 0f;
        lastTickBear = transform.position;
        lastTickPlayer = Knowledge.Player != null ? Knowledge.Player.position : transform.position;

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
        {
            Movement.SetSpeedMultiplier(1f);
            Movement.SetSneak(false);
        }
    }

    // ---------------------------------------------------------------
    // Main loop. Order matters: spotted -> reading -> freeze ->
    // pattern start -> peek -> hiding place.
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

        LogTick();                                          // debug, temporary

        if (CheckSpotted(distance))
            return;

        if (phase == Phase.Read)
        {
            UpdateRead(player);
            return;
        }

        if (UpdateFreeze())
            return;

        if (TryStartPattern())
            return;

        // No sight and no sound for a while -> peek (D-44, 8.11). Hears him
        // -> TimeSinceContact stays small -> it knows where he is, no peek.
        if (phase != Phase.Peek && Knowledge.TimeSinceContact >= noContactPeekTime &&
            Time.time - lastPeekTime >= peekInterval)
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
            lastWatchedTime = Time.time;

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
    // Pattern start (D-03, D-52). A fresh, unused choice of
    // BearPlayerAnalysis + the bear does not believe it is spotted
    // (D-43) + it saw the player a moment ago + the last pattern ended
    // at least patternCooldown ago -> the pattern's state.
    // ---------------------------------------------------------------
    private bool TryStartPattern()
    {
        if (analysis == null || !analysis.HasChoice)
            return false;

        if (analysis.TimeSinceChoice > analysis.memoryTime)
            return false;

        float choiceTime = Time.time - analysis.TimeSinceChoice;
        if (choiceTime <= usedChoiceTime + 0.01f)
            return false;                                   // this choice was already used

        if (Knowledge.BelievesNoticed || Knowledge.TimeSinceSeen > PatternFreshSight)
            return false;

        if (rearPursuit != null && rearPursuit.TimeSinceEnded < patternCooldown)
            return false;                                   // pause between patterns

        if (Time.time - lastWatchedTime < patternAfterLook)
            return false;                                   // he has just looked its way

        usedChoiceTime = choiceTime;

        BearPattern chosen = analysis.LastChoice;
        BearPattern pattern = forcePattern == PatternPick.RearPursuit ? BearPattern.RearPursuit : chosen;

        if (pattern != BearPattern.RearPursuit || rearPursuit == null)
        {
            Debug.Log("[Stalk] pattern " + BearPatternChooser.Name(pattern) + " chosen - not made yet" +
                      (rearPursuit == null ? " (BearRearPursuitState is missing on the bear)" : "") + ", keeps stalking");
            return false;
        }

        Debug.Log("[Stalk] pattern " + BearPatternChooser.Name(pattern) +
                  (forcePattern == PatternPick.Auto ? " (analysis)" : " (forced; the analysis chose " + BearPatternChooser.Name(chosen) + ")") +
                  " - starts it, " + FlatDistance(transform.position, Knowledge.LastKnownPosition).ToString("F0") + " m from the player");
        ChangeState(BearState.RearPursuit);
        return true;
    }

    // ---------------------------------------------------------------
    // Peek (D-44): no sight and no sound for a while -> step out to a
    // place with a view toward where he probably is, look, hide again.
    // ---------------------------------------------------------------
    private void StartPeek(Vector3 player)
    {
        peekAttempts++;
        lastPeekTime = Time.time;
        phase = Phase.Peek;
        phaseTimer = peekLookTime;
        peekArrived = false;
        destinationSet = false;

        peekAim = PeekAim(player);

        float bestView = -1f;
        peekPoint = transform.position;
        Vector3 playerChest = peekAim + Vector3.up * PlayerChestHeight();

        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = Random.insideUnitCircle * peekSearchRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                continue;

            Vector3 eye = hit.position + Vector3.up * BearEyeHeight();
            float view = LineOfSight.PointVisibility(eye, playerChest, Knowledge.Player, transform,
                                                     vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
            if (view > bestView)
            {
                bestView = view;
                peekPoint = hit.position;
            }
        }

        // No view from around here - step toward where the player probably is.
        if (bestView < 0.25f)
        {
            Vector3 toPlayer = Flat(peekAim - transform.position);
            Vector3 step = transform.position + toPlayer.normalized * Mathf.Min(peekSearchRadius, toPlayer.magnitude * 0.5f);

            if (NavMesh.SamplePosition(step, out NavMeshHit stepHit, 3f, NavMesh.AllAreas))
                peekPoint = stepHit.position;
        }

        float unseen = Knowledge.TimeSinceSeen;
        string since = float.IsInfinity(unseen) ? "has not seen the player yet" : unseen.ToString("F0") + " s without seeing him";
        Debug.Log("[Stalk] PEEK #" + peekAttempts + ": " + Knowledge.TimeSinceContact.ToString("F0") + " s without sight and sound (" + since +
                  ") - steps out " + FlatDistance(transform.position, peekPoint).ToString("F0") + " m to look where he probably is (" +
                  FlatDistance(player, peekAim).ToString("F0") + " m on along his route from the bear's estimate), view from there " +
                  Pct(Mathf.Max(bestView, 0f)) + " (D-44)");
    }

    // Where to look from the peek point (8.11): where the bear last knew
    // the player + half the way he could have walked on along his route
    // (he either stood still or walked on - the view cone, +-45 deg,
    // covers both from 20-50 m). Seen walking -> the estimate already
    // moves on with his velocity.
    private Vector3 PeekAim(Vector3 estimate)
    {
        if (route == Vector3.zero || Flat(Knowledge.LastKnownVelocity).magnitude >= 1f)
            return estimate;

        float time = Mathf.Min(Knowledge.TimeSinceContact, peekLeadTime);
        return estimate + route * (assumedPlayerSpeed * time * 0.5f);
    }

    private void UpdatePeek(Vector3 player)
    {
        // Contact again - sees him, or hears him (then it knows where he
        // is too; 8.11: before only sight ended a peek, after hearing him
        // the bear stood at the peek point).
        if (Knowledge.IsSeeingPlayer || Knowledge.TimeSinceContact < 0.5f)
        {
            Debug.Log("[Stalk] peek: " + (Knowledge.IsSeeingPlayer ? "sees" : "hears") + " the player (" +
                      FlatDistance(player, transform.position).ToString("F0") + " m) - back to hiding");
            peekAttempts = 0;
            hasTarget = false;
            StartWalk();
            return;
        }

        // The head looks where the player probably is, not at the old estimate.
        vision.SetLookTarget(peekAim + Vector3.up * PlayerChestHeight());

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

        Movement.FaceTowards(peekAim, turnSpeed);

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
            LogRhythm("ARRIVED at its place");              // debug, temporary
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

        // Time for a pause - only in cover (8.11). In the open it walks on
        // and looks again in 0.5 s, until it is hidden or at its place.
        phaseTimer -= dt;
        if (phaseTimer <= 0f && mayPause)
        {
            float exposure = Exposure(transform.position, player);
            if (exposure < pauseMaxExposure)
            {
                StartPause();
            }
            else
            {
                if (pauseSkips == 0)
                    LogRhythm("no pause here (exposure " + exposure.ToString("F2") + ") - walks on to cover");   // debug, temporary
                pauseSkips++;
                phaseTimer = 0.5f;
            }
        }
    }

    // How much the player could see the bear at this point: visible x 1
    // in his view, x OutOfViewExposure out of it (the rule of the places).
    private float Exposure(Vector3 point, Vector3 player)
    {
        Vector3 fromPlayer = Flat(point - player);
        return VisibleFromPlayer(point, player) * (InPlayerView(fromPlayer) ? 1f : OutOfViewExposure);
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

        // The player turned (8.11): "own side" is left / right of HIS ROUTE,
        // so after a big turn the old side may lie across his path (the
        // bear walked through the open field to get there). Read the side
        // again from where the bear actually stands; straight behind /
        // ahead (0) -> a free choice, like the first one.
        if (side != 0 && route != Vector3.zero && sideRoute != Vector3.zero)
        {
            float turned = Vector3.Angle(route, sideRoute);
            if (turned > sideTurnAngle)
            {
                int was = side;
                side = SideOf(transform.position - player);
                sideRoute = route;

                if (logChoices)
                    Debug.Log("[Stalk] the player turned " + turned.ToString("F0") + " deg - own side re-read from where the bear stands: " +
                              SideName(side) + " (was " + SideName(was) + ")" + (side == 0 ? " - free choice" : ""));
            }
        }

        bool firstPick = side == 0;                         // debug: the side is set right now
        if (side == 0)
        {
            side = SideOf(transform.position - player);
            sideRoute = route;
        }
        int sideBefore = side;                              // debug

        // 1. Score the current place and the random points: ownSidePoints
        //    on its own side, otherSidePoints on the other one (side not
        //    known yet -> half right, half left).
        int total = Mathf.Max(0, ownSidePoints) + Mathf.Max(0, otherSidePoints);
        EnsureBuffers(total + 1);
        int count = 0;

        if (hasTarget && !currentWentBad)
            count = AddCandidate(target, KeepBonus, count, anchor, minD, maxD, scale);

        int askedOwnSide = 0;                               // debug (side unknown: right)
        int askedOtherSide = 0;                             // debug (side unknown: left)
        int noNavMesh = 0;                                  // debug: points with no NavMesh near

        for (int i = 0; i < total; i++)
        {
            int pointSide;                                  // +1 right of the route, -1 left
            if (side != 0)
                pointSide = i < ownSidePoints ? side : -side;
            else
                pointSide = i % 2 == 0 ? 1 : -1;

            if (pointSide == side || (side == 0 && pointSide > 0))
                askedOwnSide++;
            else
                askedOtherSide++;

            Vector3 candidate = anchor + CandidateDirection(pointSide) * Random.Range(minD, maxD);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                noNavMesh++;
                continue;
            }

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
        int bestIndex = -1;                                 // debug

        for (int i = 0; i < count; i++)
        {
            float score = candidateScores[i];
            if (score <= -50f)
            {
                candidateFinal[i] = score;
                continue;                                   // outside the ring / crossing under his eyes
            }

            if (!candidateOtherSide[i])
                score += SideBonus;
            candidateFinal[i] = score;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidatePoints[i];
                bestIndex = i;
                found = true;
            }
        }

        repickTimer = Random.Range(repickTime.x, repickTime.y);

        if (logCandidates)
            LogCandidates(count, bestIndex, firstPick, sideBefore, anchor, player, bearDistance, minD, maxD,
                          askedOwnSide, askedOtherSide, noNavMesh, currentWentBad);

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
        if (newSide != 0 && newSide != side)
        {
            side = newSide;
            sideRoute = route;                              // the side means "of THIS route" from now on
        }

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
        float score = Score(point, anchor, minD, maxD, scale, out bool otherSide, out CandidateParts parts);

        candidatePoints[count] = point;
        candidateScores[count] = score + bonus;
        candidateOtherSide[count] = otherSide;
        candidateParts[count] = parts;                      // debug
        candidateIsCurrent[count] = bonus > 0f;             // debug
        return count + 1;
    }

    private void EnsureBuffers(int size)
    {
        if (candidatePoints != null && candidatePoints.Length >= size)
            return;

        candidatePoints = new Vector3[size];
        candidateScores = new float[size];
        candidateOtherSide = new bool[size];
        candidateParts = new CandidateParts[size];
        candidateFinal = new float[size];
        candidateIsCurrent = new bool[size];
    }

    // A direction from the anchor on the given side of the player's route
    // (+1 right, -1 left), alongside-behind him (pointSector), never ahead.
    private Vector3 CandidateDirection(int pointSide)
    {
        if (route == Vector3.zero)
        {
            Vector2 random = Random.insideUnitCircle.normalized;   // no route known - anywhere
            return new Vector3(random.x, 0f, random.y);
        }

        float angle = Random.Range(pointSector.x, pointSector.y) * pointSide;
        return Quaternion.AngleAxis(angle, Vector3.up) * route;
    }

    // ---------------------------------------------------------------
    // How good a hiding place is (higher = better). "player" here is
    // the anchor - where he will be. The side rule is added later
    // (PickHidingPlace), it depends on the other points.
    // Hidden from the player counts most, then distance, "the bear
    // watches him from cover", then "alongside-behind"; a long trip
    // there costs a bit.
    // ---------------------------------------------------------------
    // "parts" - the same numbers one by one, only for the debug log.
    private float Score(Vector3 point, Vector3 player, float minD, float maxD, float scale,
                        out bool otherSide, out CandidateParts parts)
    {
        otherSide = false;
        parts = new CandidateParts();

        Vector3 fromPlayer = Flat(point - player);
        float distance = fromPlayer.magnitude;

        parts.distance = distance;
        parts.side = SideOf(fromPlayer);
        parts.angle = route != Vector3.zero ? Vector3.Angle(route, fromPlayer) : -1f;
        parts.trip = FlatDistance(transform.position, point);

        if (distance < minD - 1f || distance > maxD + 10f)
        {
            parts.rejected = "outside the ring";
            return -100f;
        }

        float visible = VisibleFromPlayer(point, player);
        bool inView = InPlayerView(fromPlayer);
        float exposure = visible * (inView ? 1f : OutOfViewExposure);

        parts.visible = visible;
        parts.inView = inView;

        // Crossing the route right under the player's eyes - never.
        int pointSide = parts.side;
        otherSide = side != 0 && pointSide != 0 && pointSide != side;
        if (otherSide && inView)
        {
            parts.rejected = "other side, in his view";
            return -100f;
        }

        float ideal = Mathf.Clamp(Mathf.Lerp(coverDistance, openDistance, visible) * scale, minD, maxD);
        float distanceScore = 1f - Mathf.Clamp01(Mathf.Abs(distance - ideal) / 30f);

        float behindScore = 0.5f;
        if (route != Vector3.zero)
            behindScore = 1f - Mathf.Clamp01(Mathf.Abs(parts.angle - behindAngle) / 130f);

        // Watching FROM COVER (D-35: "behind a wall of bushes"): the more
        // hidden the place, the bigger the bonus; open ground gets none
        // (tests 4.11: the bonus in the open made it walk right behind
        // the player across a field).
        float cover = 1f - visible;
        float watch = cover > 0f && SeesPlayerFrom(point, player) ? cover : 0f;
        float trip = Mathf.Clamp01(parts.trip / 40f);

        parts.ideal = ideal;
        parts.hidden = (1f - exposure) * 3f;
        parts.distanceScore = distanceScore * 1.5f;
        parts.behind = behindScore;
        parts.watch = watch * seeFromHereWeight;

        return parts.hidden + parts.distanceScore + parts.behind + parts.watch - trip;
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

        Vector3 body = point + Vector3.up * BearBodyHeight();
        Vector3 head = body + along * BearHalfLength() + Vector3.up * 0.1f;
        Vector3 rear = body - along * BearHalfLength();

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
        Vector3 eye = point + Vector3.up * BearEyeHeight();
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
        pauseSkips = 0;
    }

    private void StartPause()
    {
        phase = Phase.Pause;
        phaseTimer = Random.Range(pauseTime.x, pauseTime.y);
        Movement.Stop();
        destinationSet = false;

        LogRhythm("PAUSE " + phaseTimer.ToString("F1") + " s on the way, " +
                  FlatDistance(transform.position, target).ToString("F0") + " m to its place" +
                  (pauseSkips > 0 ? ", after walking on " + (pauseSkips * 0.5f).ToString("F1") + " s in the open" : ""));   // debug, temporary
    }

    // Eye / chest height of the player above the point the bear tracks
    // (his transform), from his CharacterController if he has one.
    // ---------------------------------------------------------------
    // The bear's own size from its bones (13.11, BearBodyPoints): eyes,
    // body and length follow the model and its scale. The numbers in
    // the Inspector are used only without BearBodyPoints.
    // ---------------------------------------------------------------
    private bool HasBodyPoints => bodyPoints != null && bodyPoints.Active;

    private float BearEyeHeight()
    {
        return HasBodyPoints ? bodyPoints.EyeHeight : vision.eyeHeight;
    }

    private float BearBodyHeight()
    {
        return HasBodyPoints ? bodyPoints.BodyHeight : bodyHeight;
    }

    private float BearHalfLength()
    {
        return HasBodyPoints ? bodyPoints.HalfLength : bodyHalfLength;
    }

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

    // Side for logs: R / L / mid (straight behind or ahead).
    private static string SideName(int s)
    {
        return s > 0 ? "R" : s < 0 ? "L" : "mid";
    }

    // Share for logs: 0.42 -> "42%".
    private static string Pct(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
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

    // ===============================================================
    // DEBUG LOGS - TEMPORARY (8.11, tuning the side rule).
    // Behaviour does not depend on anything below. Remove when the
    // algorithm is approved (and the struct, arrays and calls marked
    // "debug" above).
    // ===============================================================

    // [Candidates] - one entry per choice of a hiding place: the bear's
    // side, how many points landed on each side, the best ones with
    // every part of their score, the best one on the other side.
    private void LogCandidates(int count, int bestIndex, bool firstPick, int sideBefore, Vector3 anchor, Vector3 player,
                               float bearDistance, float minD, float maxD, int askedOwnSide, int askedOtherSide,
                               int noNavMesh, bool currentWentBad)
    {
        // Count the points by side (R / L / behind-ahead) and the rejected ones.
        int right = 0;
        int left = 0;
        int middle = 0;
        int outsideRing = 0;
        int crossingInView = 0;

        for (int i = 0; i < count; i++)
        {
            if (candidateParts[i].rejected == "outside the ring")
                outsideRing++;
            else if (candidateParts[i].rejected != null)
                crossingInView++;
            else if (candidateParts[i].side > 0)
                right++;
            else if (candidateParts[i].side < 0)
                left++;
            else
                middle++;
        }

        // Order: best final score first (insertion sort, count <= 13).
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
            order[i] = i;

        for (int i = 1; i < count; i++)
        {
            int key = order[i];
            int j = i - 1;
            while (j >= 0 && candidateFinal[order[j]] < candidateFinal[key])
            {
                order[j + 1] = order[j];
                j--;
            }
            order[j + 1] = key;
        }

        StringBuilder text = new StringBuilder();

        text.Append("[Candidates] t=").Append(Time.time.ToString("F1"))
            .Append(" | bear side ").Append(SideName(sideBefore))
            .Append(firstPick ? " (FIRST pick: side set from where the bear stands)" : "")
            .Append(" | bear now ").Append(bearDistance.ToString("F0")).Append(" m from the player, ")
            .Append(SideName(SideOf(transform.position - player))).Append(" of his route")
            .Append(" | player speed (bear's idea) ").Append(PlayerSpeed().ToString("F1")).Append(" m/s, anchor ")
            .Append(FlatDistance(anchor, player).ToString("F0")).Append(" m ahead of him")
            .Append(" | ring ").Append(minD.ToString("F0")).Append("-").Append(maxD.ToString("F0")).Append(" m")
            .Append(currentWentBad ? " | the old place WENT BAD" : "")
            .Append(sideBefore != 0 ? "\n  asked: own side " : "\n  asked (side unknown): R ").Append(askedOwnSide)
            .Append(sideBefore != 0 ? ", other side " : ", L ").Append(askedOtherSide)
            .Append(", no NavMesh ").Append(noNavMesh)
            .Append(" | valid: R ").Append(right).Append(", L ").Append(left).Append(", mid ").Append(middle)
            .Append(" | rejected: outside ring ").Append(outsideRing).Append(", other side in his view ").Append(crossingInView);

        // The best ones.
        int shown = 0;
        bool otherSideShown = false;
        for (int r = 0; r < count && shown < logCandidatesTop; r++)
        {
            int i = order[r];
            if (candidateParts[i].rejected != null)
                continue;

            text.Append("\n").Append(CandidateLine(i, r + 1, i == bestIndex));
            if (candidateOtherSide[i])
                otherSideShown = true;
            shown++;
        }

        // The best one on the other side, if it is not in the list (how far behind it is).
        if (!otherSideShown && sideBefore != 0)
        {
            int otherBest = -1;
            int otherRank = 0;
            for (int r = 0; r < count; r++)
            {
                int i = order[r];
                if (candidateParts[i].rejected == null && candidateOtherSide[i])
                {
                    otherBest = i;
                    otherRank = r + 1;
                    break;
                }
            }

            if (otherBest >= 0)
                text.Append("\n  best on the OTHER side:\n").Append(CandidateLine(otherBest, otherRank, false));
            else
                text.Append("\n  other side: no valid point");
        }

        // The result.
        if (bestIndex < 0)
        {
            text.Append("\n  -> NOTHING found, stays");
        }
        else
        {
            int chosenSide = candidateParts[bestIndex].side;
            bool crossing = sideBefore != 0 && chosenSide != 0 && chosenSide != sideBefore;
            text.Append("\n  -> ")
                .Append(candidateIsCurrent[bestIndex] ? "KEEPS the current place" : "new place")
                .Append(", side ").Append(SideName(chosenSide))
                .Append(crossing ? ", CROSSES" : "");
        }

        Debug.Log(text.ToString());
    }

    // One candidate: total = parts + side bonus (+ keep bonus).
    private string CandidateLine(int i, int rank, bool chosen)
    {
        CandidateParts p = candidateParts[i];
        bool sideBonus = !candidateOtherSide[i];
        float trip = Mathf.Clamp01(p.trip / 40f);

        return "  " + (chosen ? "*" : " ") + rank + ". " + SideName(p.side) +
               (candidateOtherSide[i] ? " OTHER side" : "") +
               (candidateIsCurrent[i] ? " CURRENT" : "") +
               " | total " + candidateFinal[i].ToString("F2") +
               (sideBonus ? " (side +" + SideBonus.ToString("F1") + ")" : "") +
               (candidateIsCurrent[i] ? " (keep +" + KeepBonus.ToString("F1") + ")" : "") +
               " = hidden " + p.hidden.ToString("F2") + " [open " + Pct(p.visible) + (p.inView ? ", IN his view" : ", out of view") + "]" +
               " + dist " + p.distanceScore.ToString("F2") + " [" + p.distance.ToString("F0") + " m, ideal " + p.ideal.ToString("F0") + "]" +
               " + behind " + p.behind.ToString("F2") + " [" + p.angle.ToString("F0") + " deg]" +
               " + watch " + p.watch.ToString("F2") +
               " - trip " + trip.ToString("F2") + " [" + p.trip.ToString("F0") + " m]";
    }

    // [StalkTick] - every tickInterval s: where the bear is against the
    // REAL player (not what the bear believes), its phase and speed.
    private void LogTick()
    {
        if (!logTick)
            return;

        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f)
            return;

        float interval = Mathf.Max(tickInterval, 0.1f);
        tickTimer = interval;

        Transform real = Knowledge.Player;
        Vector3 fromPlayer = Flat(transform.position - real.position);
        float angle = route != Vector3.zero ? Vector3.Angle(route, fromPlayer) : -1f;
        float open = VisibleFromPlayer(transform.position, real.position);
        bool inView = Vector3.Angle(Flat(real.forward), fromPlayer) <= freezeViewAngle;

        float bearSpeed = FlatDistance(transform.position, lastTickBear) / interval;
        float playerSpeed = FlatDistance(real.position, lastTickPlayer) / interval;
        lastTickBear = transform.position;
        lastTickPlayer = real.position;

        float unseen = Knowledge.TimeSinceSeen;
        string sees = Knowledge.IsSeeingPlayer
            ? "yes"
            : "no (" + (float.IsInfinity(unseen) ? "never" : unseen.ToString("F1") + " s") + ")";

        Debug.Log("[StalkTick] t=" + Time.time.ToString("F1") + " " + phase +
                  (lagging ? " LAGGING" : "") + (Time.time < pursuitUntil ? " PURSUIT" : "") +
                  " | " + fromPlayer.magnitude.ToString("F0") + " m from the player, " + SideName(SideOf(fromPlayer)) +
                  " of his route (" + angle.ToString("F0") + " deg), own side " + SideName(side) +
                  " | open to him " + Pct(open) + (inView ? ", IN his view" : "") +
                  " | sees him " + sees +
                  " | speed bear " + bearSpeed.ToString("F1") + " / player " + playerSpeed.ToString("F1") + " m/s" +
                  " | to its place " + (hasTarget ? FlatDistance(transform.position, target).ToString("F0") + " m" : "none") +
                  " | player (" + real.position.x.ToString("F0") + ", " + real.position.z.ToString("F0") + ")" +
                  " bear (" + transform.position.x.ToString("F0") + ", " + transform.position.z.ToString("F0") + ")");
    }

    // [Rhythm] - pauses and arrivals: how open the bear stands to the REAL player.
    private void LogRhythm(string what)
    {
        if (!logTick || Knowledge.Player == null)
            return;

        Transform real = Knowledge.Player;
        Vector3 fromPlayer = Flat(transform.position - real.position);
        bool inView = Vector3.Angle(Flat(real.forward), fromPlayer) <= freezeViewAngle;

        Debug.Log("[Rhythm] " + what + " | open to the player " + Pct(VisibleFromPlayer(transform.position, real.position)) +
                  (inView ? ", IN his view" : "") + " | " + fromPlayer.magnitude.ToString("F0") + " m from him");
    }

    // Selected bear in Play: green - hiding place, yellow - peek point.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !enabled)
            return;

        if (hasTarget)
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
    }
}
