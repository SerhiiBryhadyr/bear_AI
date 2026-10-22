using UnityEngine;

// =====================================================================
// BearBrain - decisions on the border of the modes.
// Listens to BearKnowledge and says which state to enable:
//
//   Unknown -> Knows      : knowsEntryState  (now Tracking; later the
//                           analysis phase D-15 - change it in the
//                           Inspector, not in code)
//   Knows   -> Unknown    : lostEntryState   (SearchLastKnown)
//   distant sound (Unknown): InvestigateSound
//                           (later: reaction variants of D-12)
//
// Transitions INSIDE a mode (Idle <-> Patrol, X -> Y -> Z -> Patrol)
// stay in the states themselves - they are the states' own business.
//
// New sources of decisions (BearDirector D-22, mood D-13) will be
// added here, the states and sensors stay as they are.
// =====================================================================
[DefaultExecutionOrder(-40)]
public class BearBrain : MonoBehaviour
{
    [Header("Mode transitions")]
    [Tooltip("State the bear switches to when it learns exactly where the player is.")]
    public BearState knowsEntryState = BearState.Tracking;

    [Tooltip("State the bear switches to when it loses the player.")]
    public BearState lostEntryState = BearState.SearchLastKnown;

    [Header("Sound clues (mode Unknown)")]
    [Tooltip("While investigating, a new sound re-targets the search only if it is at least this far from the last clue (m).")]
    public float retargetMinDistance = 6f;

    [Tooltip("And only if at least this many seconds passed since the last clue.")]
    public float retargetCooldown = 2f;

    [Header("Debug")]
    public bool logIgnoredClues = false;

    private BearAI bearAI;
    private BearKnowledge knowledge;
    private BearInvestigationSystem investigationSystem;
    private BearInvestigateSoundState investigateSoundState;

    private Vector3 lastCluePosition;
    private float lastClueTime = -999f;

    // ---------------------------------------------------------------
    // Setup: find components, subscribe to BearKnowledge events.
    // ---------------------------------------------------------------
    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        knowledge = GetComponent<BearKnowledge>();
        investigationSystem = GetComponent<BearInvestigationSystem>();
        investigateSoundState = GetComponent<BearInvestigateSoundState>();
    }

    private void OnEnable()
    {
        if (knowledge == null)
            return;

        knowledge.ModeChanged += HandleModeChanged;
        knowledge.SoundClue += HandleSoundClue;
    }

    private void OnDisable()
    {
        if (knowledge == null)
            return;

        knowledge.ModeChanged -= HandleModeChanged;
        knowledge.SoundClue -= HandleSoundClue;
    }

    // ---------------------------------------------------------------
    // Mode changed -> the entry state of the new mode.
    // ---------------------------------------------------------------
    private void HandleModeChanged(KnowledgeMode oldMode, KnowledgeMode newMode)
    {
        BearState next = newMode == KnowledgeMode.Knows ? knowsEntryState : lostEntryState;

        Debug.Log("[Brain] mode " + newMode + " -> " + next);
        bearAI.StateMachine.ChangeState(next);
    }

    // ---------------------------------------------------------------
    // Distant sound in mode Unknown -> go check the area.
    // Anti-spam: footsteps come 2-3 times per second; while already
    // investigating, a new sound re-targets only for a new place and
    // not too often (otherwise every step restarts X / Y / Z).
    // ---------------------------------------------------------------
    private void HandleSoundClue(SoundEvent soundEvent)
    {
        if (investigationSystem == null || investigateSoundState == null)
            return;

        if (bearAI.StateMachine.CurrentState == BearState.InvestigateSound)
        {
            bool tooSoon = Time.time - lastClueTime < retargetCooldown;
            bool sameArea = Vector3.Distance(soundEvent.Position, lastCluePosition) < retargetMinDistance;

            if (tooSoon || sameArea)
            {
                if (logIgnoredClues)
                    Debug.Log("[Brain] sound clue ignored: already investigating this area");
                return;
            }
        }

        if (!investigationSystem.TryGetInvestigationPoint(soundEvent.Position, out Vector3 point))
        {
            Debug.Log("[Brain] sound clue: no walkable investigation point found");
            return;
        }

        lastCluePosition = soundEvent.Position;
        lastClueTime = Time.time;

        Debug.Log("[Brain] sound clue (" + soundEvent.Type + ") -> InvestigateSound, point X " + point);

        // Order matters: OnEnable of the state resets its points,
        // so the point is given after the switch.
        bearAI.StateMachine.ChangeState(BearState.InvestigateSound);
        investigateSoundState.SetInvestigationPoint(point);
    }
}
