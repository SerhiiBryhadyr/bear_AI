using UnityEngine;

// =====================================================================
// BearPlayerAnalysis - phase 1 of canon 03 (D-15): while stalking, the
// bear "reads" the player. It counts only what the bear SEES.
//
// What it measures (PlayerReadout below):
//   - looking back: the look is lookBackAngle+ away from the route
//     (the route = his movement over the last seconds, not this frame:
//     W follows the camera, so a quick turn would otherwise hide it);
//   - left / right: how long he checks each side -> "blind side";
//   - camera: average turning speed and share of "chaotic" turning;
//   - movement: run / walk / stand / crouch;
//   - route: straightness and the longest straight stretch;
//   - spotted: how many times the bear believed it was noticed.
//
// Window: ends when the bear has watched the player minObservedTime
// seconds (enough data) or after maxWindowTime. Then
// BearPatternChooser picks a pattern - for now only a log line - and a
// new window starts.
// No choice while the bear believes it is spotted: it waits until that
// opinion fades (D-43, BearKnowledge).
// Memory (D-38): data and the last choice live memoryTime seconds, even
// if the player was lost for a while.
//
// Lives on the bear. States do not call it: it reads the current state.
// =====================================================================
[DefaultExecutionOrder(-30)]
public class BearPlayerAnalysis : MonoBehaviour
{
    [Header("Window (D-15)")]
    [Tooltip("Enough data: the bear has watched the player this long (s).")]
    public float minObservedTime = 10f;
    [Tooltip("The window ends after this long anyway (s).")]
    public float maxWindowTime = 30f;
    [Tooltip("Less watched than this (s) at the end of the window -> no choice, new window.")]
    public float minObservedForChoice = 3f;

    [Header("Memory (D-38)")]
    [Tooltip("Data and the last choice are kept this long (s), even if the player was lost.")]
    public float memoryTime = 120f;

    [Header("Measures")]
    [Tooltip("The look is this far (deg) from the route = looking back.")]
    public float lookBackAngle = 120f;
    [Tooltip("The look is this far (deg) to a side = checking that side.")]
    public float sideLookAngle = 45f;
    [Tooltip("Route = the player's movement smoothed over this many seconds.")]
    public float routeSmoothing = 3f;
    [Tooltip("Camera faster than this (deg/s) = chaotic (same threshold as dominance).")]
    public float chaoticCameraSpeed = 130f;
    public float runSpeed = 6f;
    public float standSpeed = 0.3f;
    [Tooltip("The route turned more than this (deg) = a new straight stretch.")]
    public float straightTurnAngle = 30f;

    public PlayerReadout Current { get; } = new PlayerReadout();
    public bool HasChoice { get; private set; }
    public BearPattern LastChoice { get; private set; }
    public float TimeSinceChoice => HasChoice ? Time.time - lastChoiceTime : float.PositiveInfinity;

    private BearAI bearAI;
    private BearKnowledge knowledge;
    private PlayerBehaviorTracker tracker;
    private PlayerController controller;
    private Transform cachedPlayer;

    private float lastChoiceTime;
    private float lastObservationTime = -999f;
    private bool wasStalking;
    private bool wasBelieving;
    private bool waitingForFade;
    private int noticedAtWindowStart;

    private Vector3 smoothedVelocity;
    private Vector3 route;
    private Vector3 lastPosition;
    private Vector3 windowStartPosition;
    private bool hasLastPosition;
    private bool wasLookingBack;
    private Vector3 stretchHeading;
    private float stretchTime;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        knowledge = GetComponent<BearKnowledge>();

        if (knowledge == null)
            Debug.LogError("BearPlayerAnalysis: BearKnowledge is missing on the bear.");
    }

    // ---------------------------------------------------------------
    // Every frame while stalking: watch (if the bear sees the player),
    // then maybe choose.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (bearAI == null || bearAI.StateMachine == null || knowledge == null)
            return;

        bool stalking = bearAI.StateMachine.CurrentState == BearState.Stalk;

        if (stalking && !wasStalking)
            OnStalkingStarted();
        wasStalking = stalking;

        ReportBeliefFaded();

        if (!stalking)
            return;

        FindPlayerParts();
        Current.Window += Time.deltaTime;

        if (knowledge.IsSeeingPlayer)
            Observe(Time.deltaTime);

        TryChoose();
    }

    // ---------------------------------------------------------------
    // Back to stalking: fresh memory -> continue (D-38), old -> restart.
    // ---------------------------------------------------------------
    private void OnStalkingStarted()
    {
        if (HasChoice && TimeSinceChoice <= memoryTime)
        {
            Debug.Log("[Analysis] back to stalking: pattern " + BearPatternChooser.Name(LastChoice) + " chosen " +
                      TimeSinceChoice.ToString("F0") + " s ago is still valid - would start it " +
                      (knowledge.BelievesNoticed ? "when the bear stops believing it is spotted" : "right away") + " (D-38)");
            return;
        }

        if (Time.time - lastObservationTime <= memoryTime && Current.Observed > 0f)
        {
            Debug.Log("[Analysis] back to stalking: continues the window (" + Current.Observed.ToString("F1") + " s watched)");
            return;
        }

        HasChoice = false;
        ResetWindow();
        Debug.Log("[Analysis] new analysis");
    }

    // D-43: the opinion "spotted" faded -> a remembered pattern is allowed again.
    private void ReportBeliefFaded()
    {
        bool believing = knowledge.BelievesNoticed;

        if (wasBelieving && !believing && HasChoice && TimeSinceChoice <= memoryTime)
            Debug.Log("[Analysis] the bear thinks it was not seen - pattern " + BearPatternChooser.Name(LastChoice) + " allowed again (D-43)");

        wasBelieving = believing;
    }

    // ---------------------------------------------------------------
    // One frame of watching the player.
    // ---------------------------------------------------------------
    private void Observe(float dt)
    {
        lastObservationTime = Time.time;
        Current.Observed += dt;

        Vector3 position = knowledge.LastSeenPosition;
        Vector3 velocity = knowledge.LastKnownVelocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;

        // Route: movement smoothed over routeSmoothing seconds; kept while he stands.
        float blend = 1f - Mathf.Exp(-dt / Mathf.Max(routeSmoothing, 0.01f));
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, blend);
        if (smoothedVelocity.magnitude > 0.5f)
            route = smoothedVelocity.normalized;

        // Path: length walked and how far from the start of the window.
        if (hasLastPosition)
            Current.PathLength += FlatDistance(lastPosition, position);
        else
            windowStartPosition = position;

        lastPosition = position;
        hasLastPosition = true;
        Current.Displacement = FlatDistance(windowStartPosition, position);

        ObserveLook(dt);
        ObserveStraightness(dt);

        // Camera (the bear sees the head turning).
        if (tracker != null)
        {
            float cameraSpeed = tracker.CameraAngularSpeed;
            Current.CameraSum += cameraSpeed * dt;
            if (cameraSpeed >= chaoticCameraSpeed)
                Current.Chaotic += dt;
        }

        // Movement.
        if (controller != null && controller.IsCrouching)
            Current.Crouch += dt;
        else if (speed >= runSpeed)
            Current.Run += dt;
        else if (speed < standSpeed)
            Current.Stand += dt;

        Current.Spotted = knowledge.NoticedCount - noticedAtWindowStart;
    }

    // Back / left / right relative to the route.
    private void ObserveLook(float dt)
    {
        Vector3 look = knowledge.PlayerLookDirection;

        if (route == Vector3.zero || look == Vector3.zero)
            return;

        float angle = Vector3.SignedAngle(route, look, Vector3.up);     // + right, - left
        float absolute = Mathf.Abs(angle);
        bool back = absolute >= lookBackAngle;

        if (back)
        {
            Current.LookBack += dt;
            if (!wasLookingBack)
                Current.LookBackCount++;
        }
        else if (absolute >= sideLookAngle)
        {
            if (angle < 0f)
                Current.Left += dt;
            else
                Current.Right += dt;
        }

        wasLookingBack = back;
    }

    // Longest stretch without turning the route more than straightTurnAngle.
    private void ObserveStraightness(float dt)
    {
        if (route == Vector3.zero)
            return;

        if (stretchHeading == Vector3.zero || Vector3.Angle(stretchHeading, route) > straightTurnAngle)
        {
            stretchHeading = route;
            stretchTime = 0f;
            return;
        }

        stretchTime += dt;
        Current.LongestStraight = Mathf.Max(Current.LongestStraight, stretchTime);
    }

    // ---------------------------------------------------------------
    // Enough data -> choose (unless the bear believes it is spotted).
    // ---------------------------------------------------------------
    private void TryChoose()
    {
        bool enough = Current.Observed >= minObservedTime;
        bool timeUp = Current.Window >= maxWindowTime;

        if (!enough && !timeUp)
            return;

        if (Current.Observed < minObservedForChoice)
        {
            Debug.Log("[Analysis] not enough data: watched the player " + Current.Observed.ToString("F1") + " s of " +
                      Current.Window.ToString("F0") + " s - new window");
            ResetWindow();
            return;
        }

        if (knowledge.BelievesNoticed)
        {
            if (!waitingForFade)
            {
                waitingForFade = true;
                Debug.Log("[Analysis] data ready, but the bear believes it is spotted - the pattern waits (D-43)");
            }
            return;
        }

        waitingForFade = false;

        BearPatternChooser.Result result = BearPatternChooser.Choose(Current);
        HasChoice = true;
        LastChoice = result.Pattern;
        lastChoiceTime = Time.time;

        Debug.Log("[Analysis] " + Current.Describe());
        Debug.Log("[Analysis] -> would choose " + result.Describe());

        ResetWindow();
    }

    private void ResetWindow()
    {
        Current.Reset();
        hasLastPosition = false;
        wasLookingBack = false;
        stretchHeading = Vector3.zero;
        stretchTime = 0f;
        waitingForFade = false;
        noticedAtWindowStart = knowledge != null ? knowledge.NoticedCount : 0;
    }

    private void FindPlayerParts()
    {
        Transform player = knowledge.Player;
        if (player == null || player == cachedPlayer)
            return;

        cachedPlayer = player;
        tracker = player.GetComponent<PlayerBehaviorTracker>();
        controller = player.GetComponent<PlayerController>();
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}

// =====================================================================
// PlayerReadout - what the bear has read about the player in one
// window: seconds of each behaviour + derived shares (0..1 of the time
// the bear actually watched him).
// =====================================================================
public class PlayerReadout
{
    public float Window;            // s since the window started (stalking only)
    public float Observed;          // s the bear actually saw the player
    public float LookBack;
    public int LookBackCount;
    public float Left;
    public float Right;
    public float CameraSum;         // deg/s x s
    public float Chaotic;
    public float Run;
    public float Stand;
    public float Crouch;
    public float PathLength;
    public float Displacement;
    public float LongestStraight;   // s
    public int Spotted;

    public float LookBackShare => Share(LookBack);
    public float LeftShare => Share(Left);
    public float RightShare => Share(Right);
    public float SideShare => Share(Left + Right);
    public float ChaoticShare => Share(Chaotic);
    public float RunShare => Share(Run);
    public float StandShare => Share(Stand);
    public float CrouchShare => Share(Crouch);
    public float CameraAverage => Observed > 0f ? CameraSum / Observed : 0f;

    // 1 = a straight line, lower = loops. Short paths count as straight.
    public float Straightness => PathLength > 3f ? Mathf.Clamp01(Displacement / PathLength) : 1f;

    public void Reset()
    {
        Window = Observed = LookBack = Left = Right = CameraSum = Chaotic = 0f;
        Run = Stand = Crouch = PathLength = Displacement = LongestStraight = 0f;
        LookBackCount = Spotted = 0;
    }

    public string Describe()
    {
        return "watched " + Observed.ToString("F1") + " s of " + Window.ToString("F1") + " s" +
               " | back " + Percent(LookBackShare) + " (" + LookBackCount + "x)" +
               " | sides L " + Percent(LeftShare) + " R " + Percent(RightShare) +
               " | camera " + CameraAverage.ToString("F0") + " deg/s, chaotic " + Percent(ChaoticShare) +
               " | run " + Percent(RunShare) + " stand " + Percent(StandShare) + " crouch " + Percent(CrouchShare) +
               " | straight " + Straightness.ToString("F2") + ", longest " + LongestStraight.ToString("F1") + " s" +
               " | spotted " + Spotted;
    }

    private float Share(float seconds)
    {
        return Observed > 0f ? Mathf.Clamp01(seconds / Observed) : 0f;
    }

    private static string Percent(float share)
    {
        return Mathf.RoundToInt(share * 100f) + "%";
    }
}
