using UnityEngine;

// =====================================================================
// PlayerSightOfBear - D-16, layer "FACT": does the player see the bear?
// The game knows it, the bear does not: the bear's own opinion lives in
// BearKnowledge.BelievesNoticed. Lives on the Player.
//
// 1. Visible share (0..1): 3 points of the bear (BearBodyPoints).
//    A point counts if it is inside the camera frame AND the ray from
//    the camera reaches it (LineOfSight: a rock hides, a bush hides
//    partly - the same rules as the bear's eyes).
// 2. Noticing takes time. The player has to look for
//        (baseNoticeTime + distance * noticeTimePerMeter) / visibleShare
//    seconds: 0.5 s close, 1.5 s at 20 m, 3 s at 50 m; half of the bear
//    hidden - twice as long. While the bear is not visible the progress
//    fades. A short glance at a bear in the bushes is not enough -
//    "looked, but did not notice" comes by itself, without random.
// 3. IsWatchingBear = noticed AND the bear is visible right now. Held
//    for watchHoldTime after the bear leaves the view, so a branch or
//    a quick glance aside does not switch it off (dominance reads it
//    through PlayerBehaviorTracker.IsLookingAtBear).
//
// Runs before BearKnowledge, so the bear reads a fresh fact each frame.
// =====================================================================
[DefaultExecutionOrder(-60)]
public class PlayerSightOfBear : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Leave empty - found by its BearAI component in all loaded scenes.")]
    [SerializeField] private BearBodyPoints bear;

    [Header("Obstacles (same rules as BearVision)")]
    [Tooltip("What blocks the view. Everything by default.")]
    public LayerMask blockingLayers = ~0;
    [Tooltip("Bushes: partial cover. Empty = the layer named 'Foliage'.")]
    public LayerMask foliageLayers = 0;
    [Tooltip("Every bush on the line of sight hides this share of what is left (0.5: one bush - 50% visible, two - 25%).")]
    [Range(0f, 1f)]
    public float foliageBlock = 0.5f;

    [Header("Frame")]
    [Tooltip("A point this close to the screen edge does not count yet (0.02 = 2% of the screen).")]
    [Range(0f, 0.2f)]
    public float screenMargin = 0.02f;

    [Header("Noticing")]
    [Tooltip("Seconds to notice a fully visible bear right next to you.")]
    public float baseNoticeTime = 0.5f;
    [Tooltip("Extra seconds for every metre of distance.")]
    public float noticeTimePerMeter = 0.05f;
    [Tooltip("Progress lost per second while the bear is not visible (1 = forgotten in 1 s).")]
    public float forgetSpeed = 0.5f;
    [Tooltip("Farther than this (m) the bear is never noticed.")]
    public float maxNoticeDistance = 150f;

    [Header("Watching")]
    [Tooltip("IsWatchingBear stays on this long after the bear leaves the view (s).")]
    public float watchHoldTime = 1f;

    [Header("Debug")]
    [Tooltip("Log when the player notices the bear / loses track of it.")]
    public bool logChanges = true;

    // --- Facts for other scripts ---
    public bool HasBear => bear != null;
    public float DistanceToBear { get; private set; } = float.MaxValue;
    public float VisibleShare { get; private set; }     // 0..1 right now
    public float NoticeProgress { get; private set; }   // 0..1, 1 = noticed
    public bool HasNoticedBear { get; private set; }    // reached 1, not forgotten yet
    public bool IsWatchingBear { get; private set; }    // noticed and visible now (with hold)

    private readonly float[] pointVisibility = new float[BearBodyPoints.Count];
    private Vector3 lastEyePosition;
    private float lastVisibleTime = -999f;
    private float noticeStartTime;
    private float bearSearchTimer;

    // ---------------------------------------------------------------
    // Setup: camera, foliage layer, the bear.
    // ---------------------------------------------------------------
    private void Awake()
    {
        // The player's own camera first (it sits under the player and may
        // have no "MainCamera" tag), then the main camera.
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (foliageLayers.value == 0)
            foliageLayers = LayerMask.GetMask("Foliage");
    }

    private void Start()
    {
        if (playerCamera == null)
            Debug.LogError("PlayerSightOfBear: player camera not found. Assign it in the Inspector.");

        if (bear == null)
            TryFindBear();
    }

    // ---------------------------------------------------------------
    // Every frame: what is visible -> noticing progress -> watching.
    // ---------------------------------------------------------------
    private void Update()
    {
        if (bear == null)
        {
            RetryFindBear();
            return;
        }

        if (playerCamera == null)
            return;

        VisibleShare = MeasureVisibleShare();
        UpdateNoticing();
        UpdateWatching();
    }

    // ---------------------------------------------------------------
    // Share of the 3 bear points that are in the frame and not hidden.
    // ---------------------------------------------------------------
    private float MeasureVisibleShare()
    {
        Vector3 eye = playerCamera.transform.position;
        lastEyePosition = eye;
        DistanceToBear = Vector3.Distance(eye, bear.GetPoint(1));

        float sum = 0f;

        for (int i = 0; i < BearBodyPoints.Count; i++)
        {
            Vector3 point = bear.GetPoint(i);

            pointVisibility[i] = IsInFrame(point)
                ? LineOfSight.PointVisibility(eye, point, bear.transform, transform,
                                              blockingLayers, foliageLayers, foliageBlock)
                : 0f;

            sum += pointVisibility[i];
        }

        return sum / BearBodyPoints.Count;
    }

    // In front of the camera and inside the screen (minus the margin).
    private bool IsInFrame(Vector3 point)
    {
        Vector3 v = playerCamera.WorldToViewportPoint(point);

        return v.z > 0f
            && v.x >= screenMargin && v.x <= 1f - screenMargin
            && v.y >= screenMargin && v.y <= 1f - screenMargin;
    }

    // ---------------------------------------------------------------
    // Noticing: visible -> progress grows (slower when far / partly
    // hidden); not visible -> progress fades. Reaching 1 = noticed,
    // fading to 0 = lost track.
    // ---------------------------------------------------------------
    private void UpdateNoticing()
    {
        bool canNotice = VisibleShare > 0f && DistanceToBear <= maxNoticeDistance;

        if (canNotice)
        {
            if (NoticeProgress <= 0f)
                noticeStartTime = Time.time;

            float noticeTime = (baseNoticeTime + DistanceToBear * noticeTimePerMeter) / VisibleShare;
            NoticeProgress = Mathf.Min(1f, NoticeProgress + Time.deltaTime / Mathf.Max(noticeTime, 0.01f));
            lastVisibleTime = Time.time;
        }
        else
        {
            NoticeProgress = Mathf.Max(0f, NoticeProgress - forgetSpeed * Time.deltaTime);
        }

        if (!HasNoticedBear && NoticeProgress >= 1f)
        {
            HasNoticedBear = true;

            if (logChanges)
                Debug.Log($"[Sight] player NOTICED the bear: {DistanceToBear:F1} m, visible {VisibleShare * 100f:F0}%, " +
                          $"took {Time.time - noticeStartTime:F1} s");
        }
        else if (HasNoticedBear && NoticeProgress <= 0f)
        {
            HasNoticedBear = false;

            if (logChanges)
                Debug.Log($"[Sight] player lost track of the bear ({DistanceToBear:F1} m)");
        }
    }

    // Noticed and seen recently enough.
    private void UpdateWatching()
    {
        IsWatchingBear = HasNoticedBear && Time.time - lastVisibleTime <= watchHoldTime;
    }

    // ---------------------------------------------------------------
    // Bear search: BearAI is searched in ALL loaded scenes (the bear can
    // live in a separate scene). No BearBodyPoints on it -> added with
    // default offsets, so the fact works anyway.
    // ---------------------------------------------------------------
    private void TryFindBear()
    {
        BearAI bearAI = FindFirstObjectByType<BearAI>();
        if (bearAI == null)
            return;

        bear = bearAI.GetComponent<BearBodyPoints>();

        if (bear == null)
        {
            Debug.LogWarning("PlayerSightOfBear: BearBodyPoints is missing on the bear - added with default offsets. " +
                             "Add it in the Inspector to keep its settings.");
            bear = bearAI.gameObject.AddComponent<BearBodyPoints>();
        }
    }

    private void RetryFindBear()
    {
        bearSearchTimer -= Time.deltaTime;
        if (bearSearchTimer > 0f)
            return;

        bearSearchTimer = 1f;
        TryFindBear();
    }

    // ---------------------------------------------------------------
    // Scene view (Player selected): rays to the bear's points -
    // green = visible, yellow = behind bushes, red = hidden / off screen.
    // ---------------------------------------------------------------
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || bear == null)
            return;

        for (int i = 0; i < BearBodyPoints.Count; i++)
        {
            float v = pointVisibility[i];
            Gizmos.color = v >= 0.99f ? Color.green : v > 0f ? Color.yellow : Color.red;
            Gizmos.DrawLine(lastEyePosition, bear.GetPoint(i));
        }
    }
}
