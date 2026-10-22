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
//    - mode Knows    -> any heard sound refreshes the contact
//                       (the bear chases by ear while out of sight, D-20);
//    - closer than closeSoundRadius -> exact contact (Knows), even if
//                       the player is hidden behind a rock (D-19);
//    - farther       -> only a clue: event SoundClue, BearBrain decides
//                       whether to investigate.
// 3. Losing the player: no sight AND no sound for loseContactTime
//    seconds -> Unknown. Last point and movement direction are kept.
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
    private float lastContactTime;

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
        if (Mode == KnowledgeMode.Knows || distance <= closeSoundRadius)
        {
            if (logSoundContacts)
                Debug.Log("[Knowledge] sound contact: " + soundEvent.Type + ", " + distance.ToString("F1") + " m");

            RegisterContact(soundEvent.Position, ContactSource.Sound);
            return;
        }

        SoundClue?.Invoke(soundEvent);
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

        ModeChanged?.Invoke(oldMode, newMode);
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
