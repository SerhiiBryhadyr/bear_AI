using System;
using UnityEngine;

// What the bear knows about the player right now.
public enum KnowledgeMode
{
    Unknown,    // no exact position: only clues (last point, distant sound)
    Knows       // exact position: sees the player or heard him very close
}

// Where the last contact came from.
public enum ContactSource
{
    None,
    Vision,
    Sound
}

// =====================================================================
// BearKnowledge - the bear's memory about the player. The ONLY place
// that decides the mode "knows / does not know where the player is".
//
// Rule "the bear does not cheat": states read the player's position
// only from here, never from the player object itself. In mode Unknown
// that position is only the last known point.
//
// 1. Vision: every frame asks BearVision. Sees -> contact (Knows).
// 2. Sound (from BearHearing):
//    - while the bear SEES the player, sounds add nothing;
//    - closer than closeSoundRadius -> exact contact (Knows), even if
//                       the player is hidden behind a rock (D-19);
//    - mode Knows    -> the contact goes on, but the bear knows only an
//                       AREA around the sound: a random point in a circle
//                       that grows with distance (chase by ear, D-20);
//    - farther       -> only a clue: event SoundClue, BearBrain decides
//                       whether to investigate.
// 3. Losing the player: no sight AND no sound for loseContactTime
//    seconds -> Unknown. Last point and movement direction are kept.
// 4. Opinion "the player has noticed me" (D-16): changes only while
//    the bear SEES the player - only then it can see where he looks.
//    Reset when the player is lost.
//
// Mode changes are announced by the ModeChanged event (BearBrain
// listens). Runs after BearAI / BearStateMachine and before the states,
// so states read fresh data every frame.
// =====================================================================
[DefaultExecutionOrder(-50)]
public class BearKnowledge : MonoBehaviour
{
    [Header("Close sound (D-19)")]
    [Tooltip("A heard player sound closer than this (m) = the bear knows exactly where the player is.")]
    public float closeSoundRadius = 5f;

    [Header("Chase by ear (D-20)")]
    [Tooltip("Mode Knows, not seeing: a sound farther than closeSoundRadius gives only an area. Radius = distance x this.")]
    public float soundAreaPerMeter = 0.2f;
    public float soundAreaMin = 1.5f;
    public float soundAreaMax = 8f;
    [Tooltip("A new random point in the area at most this often (s). Steps come 2-3 per second.")]
    public float soundAreaRepickTime = 1.5f;

    [Header("Losing the player (D-20)")]
    [Tooltip("Knows -> Unknown after this many seconds without seeing AND without hearing the player.")]
    public float loseContactTime = 3f;

    [Header("Debug")]
    [Tooltip("Log every sound contact (2-3 per second while chasing).")]
    public bool logSoundContacts = false;

    // --- What other scripts read ---
    public KnowledgeMode Mode { get; private set; } = KnowledgeMode.Unknown;
    public bool IsSeeingPlayer { get; private set; }

    // While the bear sees the player this is his current position,
    // otherwise the last place he was seen or heard.
    public bool HasLastKnownPosition { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }

    // Player velocity at the last moment he was SEEN (sound gives no direction).
    public Vector3 LastKnownVelocity { get; private set; }

    public ContactSource LastContactSource { get; private set; } = ContactSource.None;

    // D-16: the bear's OPINION "the player has noticed me" - not the
    // truth (that is PlayerSightOfBear on the player). With its back to
    // the player the bear cannot know about his look.
    public bool BelievesNoticed { get; private set; }

    public float TimeSinceContact =>
        HasLastKnownPosition ? Time.time - lastContactTime : float.PositiveInfinity;

    // The player object itself - for components like PlayerDominance,
    // NOT for reading his position.
    public Transform Player => vision != null ? vision.player : null;

    // --- Events (BearBrain listens) ---
    public event Action<KnowledgeMode, KnowledgeMode> ModeChanged;  // (old, new)
    public event Action<SoundEvent> SoundClue;                      // distant sound in mode Unknown

    private BearVision vision;
    private PlayerAwareness playerAwareness;
    private PlayerSightOfBear playerSight;
    private float lastContactTime;
    private Vector3 lastAreaCenter;
    private float lastAreaPickTime = -999f;

    private void Awake()
    {
        vision = GetComponent<BearVision>();

        if (vision == null)
            Debug.LogError("BearKnowledge: BearVision is missing on the bear.");
    }

    // ---------------------------------------------------------------
    // Every frame: vision first, then "is the player lost?".
    // ---------------------------------------------------------------
    private void Update()
    {
        IsSeeingPlayer = vision != null && vision.CanSeePlayer();

        if (IsSeeingPlayer)
        {
            LastKnownVelocity = ReadPlayerVelocity();
            RegisterContact(vision.player.position, ContactSource.Vision);
            UpdateNoticedBelief();
            return;
        }

        if (Mode == KnowledgeMode.Knows && TimeSinceContact >= loseContactTime)
        {
            SetMode(KnowledgeMode.Unknown,
                "lost: no sight and no sound for " + loseContactTime + " s, last point " + LastKnownPosition);
        }
    }

    // ---------------------------------------------------------------
    // Called by BearHearing for every sound the bear actually heard.
    // ---------------------------------------------------------------
    public void ReceiveSound(SoundEvent soundEvent, float distance)
    {
        // Sees the player -> the exact position already comes from the eyes.
        if (IsSeeingPlayer)
            return;

        // Very close -> exact position in any mode (D-19).
        if (distance <= closeSoundRadius)
        {
            if (logSoundContacts)
                Debug.Log("[Knowledge] close sound: exact, " + distance.ToString("F1") + " m");

            RegisterContact(soundEvent.Position, ContactSource.Sound);
            return;
        }

        // Chasing by ear -> only an area around the sound (D-20).
        if (Mode == KnowledgeMode.Knows)
        {
            RegisterSoundArea(soundEvent.Position, distance);
            return;
        }

        SoundClue?.Invoke(soundEvent);
    }

    // ---------------------------------------------------------------
    // Chase by ear (D-20): the contact goes on, but the bear knows only
    // the area. The radius grows with distance. A new random point is
    // picked not more often than soundAreaRepickTime (otherwise every
    // step would make the bear zigzag) or when the sound left the area.
    // ---------------------------------------------------------------
    private void RegisterSoundArea(Vector3 soundPosition, float distance)
    {
        float radius = Mathf.Clamp(distance * soundAreaPerMeter, soundAreaMin, soundAreaMax);

        bool timeToRepick = Time.time - lastAreaPickTime >= soundAreaRepickTime;
        bool soundLeftArea = Vector3.Distance(soundPosition, lastAreaCenter) > radius;
        bool firstSoundAfterSight = LastContactSource != ContactSource.Sound;

        if (timeToRepick || soundLeftArea || firstSoundAfterSight)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
            LastKnownPosition = soundPosition + new Vector3(offset.x, 0f, offset.y);
            lastAreaCenter = soundPosition;
            lastAreaPickTime = Time.time;

            if (logSoundContacts)
                Debug.Log("[Knowledge] sound area: " + distance.ToString("F1") + " m, radius " + radius.ToString("F1") + " m");
        }

        HasLastKnownPosition = true;
        LastContactSource = ContactSource.Sound;
        lastContactTime = Time.time;
    }

    // ---------------------------------------------------------------
    // The bear SEES the player moving away from it faster than minSpeed
    // (canon 03: "the player runs from the bear -> chase").
    // ---------------------------------------------------------------
    public bool IsPlayerFleeing(Vector3 bearPosition, float minSpeed)
    {
        if (!IsSeeingPlayer || LastKnownVelocity.magnitude < minSpeed)
            return false;

        Vector3 away = LastKnownPosition - bearPosition;
        away.y = 0f;
        return Vector3.Dot(LastKnownVelocity, away) > 0f;
    }

    // ---------------------------------------------------------------
    // Contact = the bear knows exactly where the player is right now.
    // ---------------------------------------------------------------
    private void RegisterContact(Vector3 position, ContactSource source)
    {
        LastKnownPosition = position;
        HasLastKnownPosition = true;
        LastContactSource = source;
        lastContactTime = Time.time;

        if (Mode != KnowledgeMode.Knows)
            SetMode(KnowledgeMode.Knows, source.ToString().ToLower());
    }

    private void SetMode(KnowledgeMode newMode, string reason)
    {
        KnowledgeMode oldMode = Mode;
        Mode = newMode;

        Debug.Log("[Knowledge] " + oldMode + " -> " + newMode + " (" + reason + ")");

        // Lost the player -> he no longer knows where the bear is either.
        if (newMode == KnowledgeMode.Unknown && BelievesNoticed)
        {
            BelievesNoticed = false;
            Debug.Log("[Knowledge] belief reset: the bear lost the player");
        }

        ModeChanged?.Invoke(oldMode, newMode);
    }

    // ---------------------------------------------------------------
    // D-16 opinion. Called only while the bear SEES the player.
    // The player is looking at the bear right now (the fact from
    // PlayerSightOfBear) -> "he has noticed me". Otherwise the opinion
    // stays as it was.
    // ---------------------------------------------------------------
    private void UpdateNoticedBelief()
    {
        if (BelievesNoticed)
            return;

        if (playerSight == null && vision.player != null)
            playerSight = vision.player.GetComponent<PlayerSightOfBear>();

        if (playerSight == null || !playerSight.IsWatchingBear)
            return;

        BelievesNoticed = true;
        Debug.Log("[Knowledge] bear believes it is NOTICED: it sees the player looking at it, " +
                  playerSight.DistanceToBear.ToString("F1") + " m");
    }

    // The bear sees the player, so it may see how he moves.
    private Vector3 ReadPlayerVelocity()
    {
        if (playerAwareness == null && vision.player != null)
            playerAwareness = vision.player.GetComponent<PlayerAwareness>();

        if (playerAwareness == null)
            return Vector3.zero;

        Vector3 velocity = playerAwareness.Velocity;
        velocity.y = 0f;
        return velocity;
    }
}
