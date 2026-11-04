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
//    A good point: the player cannot see it (bush, out of his view), it
//    is alongside-behind his route, on the bear's own side of the route,
//    at a comfortable distance (closer behind cover, farther in the
//    open). The bear commits to it: a new choice only when the place
//    went bad or after repickTime - no twitching every second. It
//    crosses to the other side only when its own side has no cover,
//    and never right under the player's eyes.
// 2. Rhythm: walks walkTime, stops pauseTime and turns to the player,
//    walks again - not a gliding ghost. Speed follows the player.
// 3. The bear sees the player looking its way -> freezes.
// 4. Without sight (D-44): follows by ear and prediction
//    (BearKnowledge.EstimatePlayerPosition). After peekAfter seconds it
//    must check: steps out to a place with a view to where the player
//    should be. Nothing there -> BearKnowledge loses the player after
//    stalkLoseContactTime -> SearchLastKnown.
// 5. Spotted (D-42, simplified): close -> Encounter (Tracking); far ->
//    stands and reads the player: he runs -> "he saw me" -> follows
//    faster and closer, still hidden; he walks on -> "he did not see
//    me". BearKnowledge forgets being spotted after ~10 s (D-43).
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

    [Header("Rhythm (not a gliding ghost)")]
    public Vector2 walkTime = new Vector2(3f, 8f);
    public Vector2 pauseTime = new Vector2(1f, 3f);
    public float turnSpeed = 120f;

    [Header("Speed")]
    [Tooltip("Speed = the player's seen speed x speedFactor + speedBonus, clamped (m/s).")]
    public float speedFactor = 1.2f;
    public float speedBonus = 0.5f;
    public float minSpeed = 2f;
    public float maxSpeed = 9.5f;
    [Tooltip("Farther than this from the player -> no pauses, maxSpeed (m).")]
    public float catchUpDistance = 80f;

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

    [Header("Checks")]
    [Tooltip("Height of the bear's body above a point when checking whether the player could see it (m).")]
    public float bodyHeight = 0.9f;
    [Tooltip("The player's eye height above his feet, if he has no CharacterController (m).")]
    public float playerEyeHeight = 1.6f;

    [Header("Debug")]
    public bool logChoices = true;

    private enum Phase { Walk, Pause, Freeze, Read, Peek }

    // Out of the player's view a place is still a bit risky: he may turn.
    private const float OutOfViewExposure = 0.35f;
    // Keeping the current place gets a bonus - no switching for a tiny gain.
    private const float KeepBonus = 0.5f;
    // Exposure above this = the current place went bad.
    private const float GoodExposureLimit = 0.6f;

    private BearVision vision;
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

    private Vector3 peekPoint;
    private bool peekArrived;
    private int peekAttempts;

    private Vector3 route;              // the player's movement direction (flat), last seen
    private int side;                   // +1 right of the route, -1 left, 0 unknown
    private int knownNoticedCount;
    private float pursuitUntil = -999f;

    // ---------------------------------------------------------------
    // Setup / cleanup.
    // ---------------------------------------------------------------
    protected override void Awake()
    {
        base.Awake();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        Knowledge.SetLoseContactTime(stalkLoseContactTime);
        knownNoticedCount = Knowledge.NoticedCount;
        hasTarget = false;
        arrived = false;
        side = 0;
        peekAttempts = 0;
        StartWalk();

        Debug.Log("Bear entered STALKING (contact: " + Knowledge.LastContactSource + ")");
    }

    private void OnDisable()
    {
        if (bearAI == null)
            return;

        if (Knowledge != null)
            Knowledge.ResetLoseContactTime();

        if (Movement != null)
            Movement.SetSpeedMultiplier(1f);
    }

    // ---------------------------------------------------------------
    // Main loop. Order matters: spotted -> reading -> freeze -> peek ->
    // hiding place.
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

        if (CheckSpotted(distance))
            return;

        if (phase == Phase.Read)
        {
            UpdateRead(player);
            return;
        }

        if (UpdateFreeze())
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
        Vector3 velocity = Knowledge.LastKnownVelocity;
        velocity.y = 0f;

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
        Vector3 velocity = Knowledge.LastKnownVelocity;
        velocity.y = 0f;

        if (velocity.magnitude < 1f)
            return false;

        Vector3 toBear = transform.position - Knowledge.LastSeenPosition;
        toBear.y = 0f;
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
        Vector3 toBear = transform.position - Knowledge.LastSeenPosition;
        toBear.y = 0f;

        if (look == Vector3.zero || toBear.sqrMagnitude < 0.01f)
            return false;

        return Vector3.Angle(look, toBear) <= maxAngle;
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
            Vector3 toPlayer = player - transform.position;
            toPlayer.y = 0f;
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
    // ---------------------------------------------------------------
    private void UpdateHiding(Vector3 player, float distance)
    {
        float dt = Time.deltaTime;
        bool inPursuit = Time.time < pursuitUntil;
        float scale = inPursuit ? pursuitDistanceFactor : 1f;
        bool tooFar = distance > catchUpDistance;

        repickTimer -= dt;
        checkTimer -= dt;

        bool targetBad = false;
        if (hasTarget && checkTimer <= 0f)
        {
            checkTimer = 1f;
            targetBad = !IsStillGood(target, player, scale);
        }

        if (!hasTarget || repickTimer <= 0f || targetBad)
            PickHidingPlace(player, scale, targetBad);

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

        // Pause, or waiting at the place: stand and face the player.
        if ((phase == Phase.Pause && !tooFar) || arrived)
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

        Movement.SetSpeed(WalkSpeed(tooFar, inPursuit));

        if (!destinationSet)
        {
            Movement.MoveTo(target);
            destinationSet = true;
        }

        phaseTimer -= dt;
        if (phaseTimer <= 0f && !tooFar)
            StartPause();
    }

    private void PickHidingPlace(Vector3 player, float scale, bool currentWentBad)
    {
        float minD = minDistance * scale;
        if (Knowledge.BelievesNoticed)
            minD = Mathf.Max(minD, spottedMinDistance);
        float maxD = Mathf.Max(minD + 5f, searchRadius * scale);

        if (side == 0)
            side = SideOf(transform.position - player);

        bool found = false;
        float bestScore = float.NegativeInfinity;
        Vector3 best = transform.position;

        if (hasTarget && !currentWentBad)
        {
            bestScore = Score(target, player, minD, maxD, scale) + KeepBonus;
            best = target;
            found = bestScore > -50f;
        }

        for (int i = 0; i < candidateCount; i++)
        {
            Vector3 candidate = player + CandidateDirection(i) * Random.Range(minD, maxD);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                continue;

            float score = Score(hit.position, player, minD, maxD, scale);
            if (score > bestScore)
            {
                bestScore = score;
                best = hit.position;
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

        int newSide = SideOf(best - player);
        bool crossing = side != 0 && newSide != 0 && newSide != side;
        if (newSide != 0)
            side = newSide;

        target = best;
        hasTarget = true;
        arrived = false;
        destinationSet = false;

        if (logChoices)
        {
            float open = VisibleFromPlayer(best, player);
            Debug.Log("[Stalk] hiding place " + FlatDistance(best, player).ToString("F0") + " m from the player, open to him " +
                      Mathf.RoundToInt(open * 100f) + "%" +
                      (crossing ? ", CROSSES to the other side" : "") +
                      (currentWentBad ? " (the old place went bad)" : ""));
        }
    }

    // Most points on the bear's own side, alongside-behind the route; the
    // rest anywhere (so it can still cross when its side has no cover).
    private Vector3 CandidateDirection(int index)
    {
        bool ownSide = side != 0 && route != Vector3.zero &&
                       index < Mathf.RoundToInt(candidateCount * ownSideShare);

        if (ownSide)
        {
            float angle = Random.Range(40f, 170f) * side;
            return Quaternion.AngleAxis(angle, Vector3.up) * route;
        }

        Vector2 random = Random.insideUnitCircle.normalized;
        return new Vector3(random.x, 0f, random.y);
    }

    // ---------------------------------------------------------------
    // How good a hiding place is (higher = better).
    // Hidden from the player counts most, then distance and own side,
    // then "alongside-behind"; a long trip there costs a bit.
    // ---------------------------------------------------------------
    private float Score(Vector3 point, Vector3 player, float minD, float maxD, float scale)
    {
        Vector3 fromPlayer = point - player;
        fromPlayer.y = 0f;
        float distance = fromPlayer.magnitude;

        if (distance < minD - 1f || distance > maxD + 10f)
            return -100f;

        float visible = VisibleFromPlayer(point, player);
        bool inView = InPlayerView(fromPlayer);
        float exposure = visible * (inView ? 1f : OutOfViewExposure);

        // Crossing the route right under the player's eyes - never.
        int pointSide = SideOf(fromPlayer);
        bool otherSide = side != 0 && pointSide != 0 && pointSide != side;
        if (otherSide && inView)
            return -100f;

        float ideal = Mathf.Clamp(Mathf.Lerp(coverDistance, openDistance, visible) * scale, minD, maxD);
        float distanceScore = 1f - Mathf.Clamp01(Mathf.Abs(distance - ideal) / 30f);

        float behindScore = 0.5f;
        if (route != Vector3.zero)
            behindScore = 1f - Mathf.Clamp01(Mathf.Abs(Vector3.Angle(route, fromPlayer) - behindAngle) / 130f);

        float sideScore = otherSide ? 0f : 1f;
        float trip = Mathf.Clamp01(FlatDistance(transform.position, point) / 40f);

        return (1f - exposure) * 3f + distanceScore * 1.5f + behindScore + sideScore * 1.5f - trip;
    }

    private bool IsStillGood(Vector3 point, Vector3 player, float scale)
    {
        Vector3 fromPlayer = point - player;
        fromPlayer.y = 0f;
        float distance = fromPlayer.magnitude;

        float minD = minDistance * scale;
        if (Knowledge.BelievesNoticed)
            minD = Mathf.Max(minD, spottedMinDistance);

        if (distance < minD - 2f || distance > searchRadius * scale + 15f)
            return false;

        float exposure = VisibleFromPlayer(point, player) * (InPlayerView(fromPlayer) ? 1f : OutOfViewExposure);
        return exposure < GoodExposureLimit;
    }

    // How well the player (at his estimated position) could see a bear
    // standing at this point: 1 open, 0.5 one bush, 0 rock. The same
    // rule as the eyes use (LineOfSight, D-31).
    private float VisibleFromPlayer(Vector3 point, Vector3 player)
    {
        Vector3 eye = player + Vector3.up * PlayerEyeHeight();
        Vector3 body = point + Vector3.up * bodyHeight;

        return LineOfSight.PointVisibility(eye, body, transform, Knowledge.Player,
                                           vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
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
    // Small helpers.
    // ---------------------------------------------------------------
    private float WalkSpeed(bool tooFar, bool inPursuit)
    {
        if (tooFar)
            return maxSpeed;

        Vector3 velocity = Knowledge.LastKnownVelocity;
        velocity.y = 0f;
        float playerSpeed = velocity.magnitude;
        float speed = Mathf.Clamp(playerSpeed * speedFactor + speedBonus, minSpeed, maxSpeed);

        if (inPursuit)
            speed = Mathf.Min(maxSpeed, Mathf.Max(speed, playerSpeed * 1.1f + 1f));

        return speed;
    }

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

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
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
