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
//    While seeing, it also remembers where the player was, how he moved
//    and where he looked - only then the bear can know it.
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
//    seconds -> Unknown. A state may ask for a longer memory (stalking
//    hidden behind bushes, D-44). Last point and movement direction
//    are kept.
// 4. Opinion "the player has noticed me" (D-16, D-42, D-43): changes
//    only while the bear SEES the player - only then it sees where he
//    looks. The player must look STRAIGHT at the bear (a glance with
//    the corner of the eye does not count) long enough, and the bear
//    must not be fully hidden from him. The opinion fades after
//    noticedForgetTime seconds without such a look: the bear decides it
//    was not seen after all. Reset when the player is lost. It is only
//    a guess and can be wrong.
// 5. Estimate (D-44): without sight - where the player probably is
//    now: a fresh sound, or the last seen point + last seen velocity.
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
    [Tooltip("Knows -> Unknown after this many seconds without seeing AND without hearing the player. A state may ask for longer (stalking, D-44).")]
    public float loseContactTime = 3f;

    [Header("Opinion 'the player has noticed me' (D-16, D-42, D-43)")]
    [Tooltip("The player's look must point at the bear within this angle (deg). A glance with the corner of the eye does not count.")]
    public float noticedLookAngle = 20f;
    [Tooltip("...for this long (s), plus noticedLookTimePerMeter for every meter of distance.")]
    public float noticedLookTime = 1f;
    public float noticedLookTimePerMeter = 0.03f;
    [Tooltip("This many seconds without such a look -> the bear decides it was not seen after all (D-43).")]
    public float noticedForgetTime = 10f;

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

    // The last moment the bear SAW the player: where he stood and where
    // he looked (flat direction of his body).
    public Vector3 LastSeenPosition { get; private set; }
    public Vector3 PlayerLookDirection { get; private set; }
    public float TimeSinceSeen => hasSeen ? Time.time - lastSeenTime : float.PositiveInfinity;

    // D-16 / D-42: the bear's OPINION "the player has noticed me" - not
    // the truth (that is PlayerSightOfBear on the player).
    public bool BelievesNoticed { get; private set; }
    public int NoticedCount { get; private set; }       // how many times the opinion turned on
    public float TimeSinceNoticed => NoticedCount > 0 ? Time.time - lastNoticedTime : float.PositiveInfinity;

    public float TimeSinceContact =>
        HasLastKnownPosition ? Time.time - lastContactTime : float.PositiveInfinity;

    // Lose time now: the normal one or the one a state asked for.
    public float CurrentLoseContactTime => loseContactOverride > 0f ? loseContactOverride : loseContactTime;

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

    private bool hasSeen;
    private float lastSeenTime;
    private float lastNoticedTime;
    private float lookTimer;
    private float loseContactOverride;

    private void Awake()
    {
        vision = GetComponent<BearVision>();

        if (vision == null)
            Debug.LogError("BearKnowledge: BearVision is missing on the bear.");
    }

    // ---------------------------------------------------------------
    // Every frame: vision first, then the opinion, then "is the player
    // lost?".
    // ---------------------------------------------------------------
    private void Update()
    {
        IsSeeingPlayer = vision != null && vision.CanSeePlayer();

        if (IsSeeingPlayer)
        {
            LastKnownVelocity = ReadPlayerVelocity();
            LastSeenPosition = vision.player.position;
            PlayerLookDirection = Flat(vision.player.forward).normalized;
            lastSeenTime = Time.time;
            hasSeen = true;

            RegisterContact(vision.player.position, ContactSource.Vision);
            UpdateNoticedBelief();
        }
        else
        {
            lookTimer = 0f;
        }

        FadeNoticedBelief();

        if (!IsSeeingPlayer && Mode == KnowledgeMode.Knows && TimeSinceContact >= CurrentLoseContactTime)
        {
            SetMode(KnowledgeMode.Unknown,
                "lost: no sight and no sound for " + CurrentLoseContactTime + " s, last point " + LastKnownPosition);
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
    // Stalking (D-44): the bear follows hidden behind bushes, so it may
    // go longer without seeing the player. The state that asks must
    // reset it when it ends.
    // ---------------------------------------------------------------
    public void SetLoseContactTime(float seconds)
    {
        loseContactOverride = seconds;
    }

    public void ResetLoseContactTime()
    {
        loseContactOverride = 0f;
    }

    // ---------------------------------------------------------------
    // Where the player probably is now (D-44). Seeing -> exactly.
    // A sound newer than the last sight -> the sound point / area.
    // Otherwise -> the last seen point moved by the last seen velocity,
    // but not longer than maxPredictionTime (an old guess gets worse,
    // so the bear has to go and check).
    // ---------------------------------------------------------------
    public Vector3 EstimatePlayerPosition(float maxPredictionTime)
    {
        if (IsSeeingPlayer || !hasSeen || LastContactSource == ContactSource.Sound)
            return LastKnownPosition;

        return LastSeenPosition + LastKnownVelocity * Mathf.Min(TimeSinceSeen, maxPredictionTime);
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
        if (newMode == KnowledgeMode.Unknown)
        {
            lookTimer = 0f;

            if (BelievesNoticed)
            {
                BelievesNoticed = false;
                Debug.Log("[Knowledge] belief reset: the bear lost the player");
            }
        }

        ModeChanged?.Invoke(oldMode, newMode);
    }

    // ---------------------------------------------------------------
    // Opinion (D-16, D-42). Called only while the bear SEES the player.
    // The player looks straight at the bear long enough -> "he has
    // noticed me". Every such moment refreshes the opinion, so it lasts
    // while he keeps looking.
    // ---------------------------------------------------------------
    private void UpdateNoticedBelief()
    {
        if (!IsPlayerLookingAtBear())
        {
            lookTimer = 0f;
            return;
        }

        lookTimer += Time.deltaTime;

        float distance = Flat(transform.position - vision.player.position).magnitude;
        float needed = noticedLookTime + noticedLookTimePerMeter * distance;

        if (lookTimer < needed)
            return;

        lastNoticedTime = Time.time;

        if (BelievesNoticed)
            return;

        BelievesNoticed = true;
        NoticedCount++;
        Debug.Log("[Knowledge] bear believes it is NOTICED: the player looks straight at it (" +
                  needed.ToString("F1") + " s, " + distance.ToString("F1") + " m)");
    }

    // The player's body faces the bear (within noticedLookAngle) and the
    // bear is not fully hidden from him - it knows whether it stands in
    // the open, behind a bush or behind a rock.
    private bool IsPlayerLookingAtBear()
    {
        Vector3 toBear = Flat(transform.position - vision.player.position);

        if (toBear.sqrMagnitude < 0.01f)
            return true;

        if (PlayerLookDirection == Vector3.zero || Vector3.Angle(PlayerLookDirection, toBear) > noticedLookAngle)
            return false;

        if (playerSight == null)
            playerSight = vision.player.GetComponent<PlayerSightOfBear>();

        return playerSight == null || playerSight.VisibleShare > 0f;
    }

    // D-43: no straight look for noticedForgetTime -> "he did not see me".
    private void FadeNoticedBelief()
    {
        if (!BelievesNoticed || Time.time - lastNoticedTime < noticedForgetTime)
            return;

        BelievesNoticed = false;
        Debug.Log("[Knowledge] belief faded: " + noticedForgetTime +
                  " s without the player looking at the bear - it thinks it was not seen (D-43)");
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

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }
}
