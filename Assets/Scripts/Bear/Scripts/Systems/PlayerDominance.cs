using UnityEngine;

public enum DominanceLevel
{
    Low,
    Medium,
    High
}

// =====================================================================
// PlayerDominance (layer 1 of D-02, rules from D-01)
// Turns facts from PlayerBehaviorTracker into a dominance score 0-1
// and a level Low / Medium / High that the bear reacts to.
// It does not observe the player itself - that is the tracker's job.
// =====================================================================
public class PlayerDominance : MonoBehaviour
{
    [Header("Current value (watch in Play mode)")]
    [Range(0f, 1f)]
    public float playerDominance = 0.5f;

    public DominanceLevel CurrentLevel { get; private set; } = DominanceLevel.Medium;
    public float TargetDominance { get; private set; } = 0.5f;   // instant score this frame

    [Header("Base")]
    [Range(0f, 1f)]
    public float baseScore = 0.5f;

    [Header("Positive factors (D-01)")]
    public float standingBonus = 0.10f;
    public float backingAwayBonus = 0.20f;
    public float lookAtBearBonus = 0.15f;
    public float calmCameraBonus = 0.05f;
    [Tooltip("0 for now: flashlight effect is undecided (D-01).")]
    public float flashlightBonus = 0f;

    [Header("Negative factors (D-01)")]
    public float notLookingPenalty = 0.10f;
    [Tooltip("Normal walking near the bear: High only for standing or careful retreat.")]
    public float walkingPenalty = 0.10f;
    public float backTurnedPenalty = 0.15f;
    public float chaoticCameraPenalty = 0.20f;
    public float runningPenalty = 0.35f;
    public float towardBearPenalty = 0.20f;

    [Header("Camera thresholds, deg/s (calibrated 15.10: calm 0-30, normal turns 40-120, nervous 130-340)")]
    public float calmCameraMaxSpeed = 30f;
    public float chaoticCameraMinSpeed = 130f;

    [Header("Accumulation, per second")]
    public float riseSpeed = 0.15f;          // calm is earned slowly
    public float fallSpeed = 0.40f;          // panic shows almost at once
    public float neutralReturnSpeed = 0.10f; // drift to base when bear is far
    public float awarenessDistance = 25f;

    [Header("Levels")]
    [Range(0f, 1f)]
    public float lowLevelThreshold = 0.3f;
    [Range(0f, 1f)]
    public float highLevelThreshold = 0.6f;
    [Range(0f, 0.1f)]
    public float levelHysteresis = 0.03f;    // stops level flickering on a border

    [Header("Layer hooks (D-02) - layers 2 and 3 will change these")]
    [Tooltip("Layer 2 (habituation) will lower it for repeated tactics.")]
    public float positiveMultiplier = 1f;
    [Tooltip("Layer 3 (anger phase) will turn High off.")]
    public bool allowHighLevel = true;

    [Header("Debug")]
    public bool debugLogs = true;
    public float debugLogInterval = 1f;

    private PlayerBehaviorTracker tracker;
    private float debugTimer;

    // ---------------------------------------------------------------
    // Setup.
    // ---------------------------------------------------------------
    private void Awake()
    {
        tracker = GetComponent<PlayerBehaviorTracker>();

        if (tracker == null)
        {
            Debug.LogWarning(
                "PlayerDominance: PlayerBehaviorTracker is missing on Player - added automatically. " +
                "Add it in the Inspector to keep its settings."
            );

            tracker = gameObject.AddComponent<PlayerBehaviorTracker>();
        }
    }

    private void Start()
    {
        CurrentLevel = LevelWithoutHysteresis(playerDominance);
    }

    // ---------------------------------------------------------------
    // Main loop: near the bear - move toward the instant score;
    // far from the bear - slowly return to neutral.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (!tracker.HasBear)
            return;

        bool bearIsNear = tracker.DistanceToBear <= awarenessDistance;

        if (bearIsNear)
        {
            TargetDominance = CalculateTargetScore();

            float speed = TargetDominance > playerDominance ? riseSpeed : fallSpeed;
            playerDominance = Mathf.MoveTowards(playerDominance, TargetDominance, speed * Time.deltaTime);
        }
        else
        {
            TargetDominance = baseScore;
            playerDominance = Mathf.MoveTowards(playerDominance, baseScore, neutralReturnSpeed * Time.deltaTime);
        }

        UpdateLevel();
        WriteDebugLog(bearIsNear);
    }

    // ---------------------------------------------------------------
    // Instant score: base + everything that is true right now.
    // Plus and minus are summed separately so layer 2 can weaken
    // only the plus part (positiveMultiplier).
    // ---------------------------------------------------------------
    private float CalculateTargetScore()
    {
        float positive = 0f;
        float negative = 0f;

        if (tracker.IsStanding) positive += standingBonus;
        if (tracker.IsBackingAway) positive += backingAwayBonus;

        if (tracker.IsLookingAtBear) positive += lookAtBearBonus;
        else negative += notLookingPenalty;

        if (tracker.IsBackTurned && !tracker.IsStanding) negative += backTurnedPenalty;

        float cameraSpeed = tracker.CameraAngularSpeed;
        if (cameraSpeed <= calmCameraMaxSpeed) positive += calmCameraBonus;
        else if (cameraSpeed >= chaoticCameraMinSpeed) negative += chaoticCameraPenalty;

        if (tracker.IsFlashlightOnBear) positive += flashlightBonus;

        if (tracker.IsWalking) negative += walkingPenalty;
        if (tracker.IsRunning) negative += runningPenalty;
        if (tracker.IsMovingTowardBear) negative += towardBearPenalty;

        return Mathf.Clamp01(baseScore + positive * positiveMultiplier - negative);
    }

    // ---------------------------------------------------------------
    // Level with hysteresis: to change level the value must cross the
    // border by levelHysteresis, so it does not flicker.
    // ---------------------------------------------------------------
    private void UpdateLevel()
    {
        float v = playerDominance;
        float h = levelHysteresis;
        DominanceLevel newLevel = CurrentLevel;

        switch (CurrentLevel)
        {
            case DominanceLevel.Low:
                if (v > highLevelThreshold + h) newLevel = DominanceLevel.High;
                else if (v > lowLevelThreshold + h) newLevel = DominanceLevel.Medium;
                break;

            case DominanceLevel.Medium:
                if (v < lowLevelThreshold - h) newLevel = DominanceLevel.Low;
                else if (v > highLevelThreshold + h) newLevel = DominanceLevel.High;
                break;

            case DominanceLevel.High:
                if (v < lowLevelThreshold - h) newLevel = DominanceLevel.Low;
                else if (v < highLevelThreshold - h) newLevel = DominanceLevel.Medium;
                break;
        }

        if (!allowHighLevel && newLevel == DominanceLevel.High)
            newLevel = DominanceLevel.Medium;

        if (newLevel != CurrentLevel)
        {
            if (debugLogs)
                Debug.Log($"[Dominance] LEVEL {CurrentLevel} -> {newLevel} ({v:0.00})");

            CurrentLevel = newLevel;
        }
    }

    private DominanceLevel LevelWithoutHysteresis(float v)
    {
        if (v < lowLevelThreshold) return DominanceLevel.Low;
        if (v > highLevelThreshold && allowHighLevel) return DominanceLevel.High;
        return DominanceLevel.Medium;
    }

    // ---------------------------------------------------------------
    // Debug: one line per interval with value, target and reasons.
    // Claude reads these from Editor.log.
    // ---------------------------------------------------------------
    private void WriteDebugLog(bool bearIsNear)
    {
        if (!debugLogs)
            return;

        debugTimer -= Time.deltaTime;
        if (debugTimer > 0f)
            return;

        debugTimer = debugLogInterval;

        Debug.Log(
            $"[Dominance] {playerDominance:0.00} -> {TargetDominance:0.00} [{CurrentLevel}] " +
            $"dist {tracker.DistanceToBear:0.0}{(bearIsNear ? "" : " (far)")} | " +
            $"stand {B(tracker.IsStanding)} walk {B(tracker.IsWalking)} back {B(tracker.IsBackingAway)} run {B(tracker.IsRunning)} " +
            $"look {B(tracker.IsLookingAtBear)} backTurned {B(tracker.IsBackTurned)} toward {B(tracker.IsMovingTowardBear)} | " +
            $"cam {tracker.CameraAngularSpeed:0}deg/s"
        );
    }

    private static string B(bool value) => value ? "1" : "0";
}
