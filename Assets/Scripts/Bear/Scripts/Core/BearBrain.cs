using UnityEngine;

// =====================================================================
// BearBrain - decisions on the border of the modes.
// Listens to BearKnowledge and says which state to enable:
//
//   Unknown -> Knows      : knowsEntryState  (Stalk: the bear first
//                           follows hidden and reads the player, D-15 /
//                           D-35; Stalk itself hands over to the
//                           Encounter (Tracking) when spotted close)
//   Knows   -> Unknown    : lostEntryState   (SearchLastKnown)
//   let go (D-52)         : releasedEntryState (Patrol - it does not
//                           search for the one it has let go)
//   distant sound (Unknown): InvestigateSound
//                           (later: reaction variants of D-12)
//   first sight close by  : BearCloseEncounterState (D-75) - it decides
//                           itself whether it applies (distance,
//                           startled). Also when the bear heard him very
//                           close (D-19) and SAW him within
//                           heardThenSawWindow: still a surprise.
//   Knows while Chase / FaceOff run: those states keep going (the
//                           contact came back because BearBoundaries
//                           ended the ignoring, D-75).
//
// Transitions INSIDE a mode (Idle <-> Patrol, X -> Y -> Z -> Patrol,
// Stalk <-> Tracking <-> Chase) stay in the states themselves - they
// are the states' own business.
//
// New sources of decisions (BearDirector D-22, mood D-13) will be
// added here, the states and sensors stay as they are.
// =====================================================================
[DefaultExecutionOrder(-40)]
public class BearBrain : MonoBehaviour
{
    [Header("Mode transitions")]
    [Tooltip("State the bear switches to when it learns exactly where the player is. Stalk = follow hidden first (D-35).")]
    public BearState knowsEntryState = BearState.Stalk;

    [Tooltip("State the bear switches to when it loses the player.")]
    public BearState lostEntryState = BearState.SearchLastKnown;

    [Tooltip("State after the bear LET the player go (D-52, BearKnowledge.Release): it does not search for him.")]
    public BearState releasedEntryState = BearState.Patrol;

    [Header("Sound clues (mode Unknown)")]
    [Tooltip("While investigating, a new sound re-targets the search only if it is at least this far from the last clue (m).")]
    public float retargetMinDistance = 6f;

    [Tooltip("And only if at least this many seconds passed since the last clue.")]
    public float retargetCooldown = 2f;

    [Header("Close encounter (D-75)")]
    [Tooltip("Heard him very close (D-19) and SAW him within this many seconds -> still a first sight for the close encounter.")]
    public float heardThenSawWindow = 1.5f;

    [Header("Debug")]
    public bool logIgnoredClues = false;

    private BearAI bearAI;
    private BearKnowledge knowledge;
    private BearInvestigationSystem investigationSystem;
    private BearInvestigateSoundState investigateSoundState;
    private BearCloseEncounterState closeEncounter;

    private float knowsSince = -999f;   // when the mode last became Knows
    private bool knowsBySound;          // ...and it was a sound, not the eyes

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
        closeEncounter = GetComponent<BearCloseEncounterState>();
    }

    // Scene check: the value saved in the scene wins over the code.
    private void Start()
    {
        bool hasStalk = GetComponent<BearStalkState>() != null;

        if (knowsEntryState == BearState.Stalk && !hasStalk)
            Debug.LogError("BearBrain: Knows Entry State = Stalk, but BearStalkState is missing on the bear. Add the component.");
        else if (knowsEntryState != BearState.Stalk && hasStalk)
            Debug.LogWarning("BearBrain: Knows Entry State = " + knowsEntryState +
                             " - stalking (D-35) is skipped on first contact. Set it to Stalk in the Inspector.");

        if (closeEncounter == null)
            Debug.LogWarning("BearBrain: BearCloseEncounterState is missing on the bear - close encounters (D-75) are off.");
    }

    private void OnEnable()
    {
        if (knowledge == null)
            return;

        knowledge.ModeChanged += HandleModeChanged;
        knowledge.SoundClue += HandleSoundClue;
        knowledge.SightStarted += HandleSightStarted;
    }

    private void OnDisable()
    {
        if (knowledge == null)
            return;

        knowledge.ModeChanged -= HandleModeChanged;
        knowledge.SoundClue -= HandleSoundClue;
        knowledge.SightStarted -= HandleSightStarted;
    }

    // ---------------------------------------------------------------
    // Mode changed -> the entry state of the new mode.
    // ---------------------------------------------------------------
    private void HandleModeChanged(KnowledgeMode oldMode, KnowledgeMode newMode)
    {
        if (newMode == KnowledgeMode.Knows)
        {
            knowsSince = Time.time;
            knowsBySound = !knowledge.IsSeeingPlayer;

            // Attack / face-off already deal with him: the contact came
            // back because the ignoring ended (BearBoundaries, D-75).
            BearState current = bearAI.StateMachine.CurrentState;
            if (current == BearState.Chase || current == BearState.FaceOff)
            {
                Debug.Log("[Brain] mode Knows -> stays in " + current);
                return;
            }

            // First sight close by -> the close encounter (D-75).
            if (knowledge.IsSeeingPlayer && closeEncounter != null && closeEncounter.TryStart("first sight"))
                return;
        }

        BearState next = newMode == KnowledgeMode.Knows ? knowsEntryState
                       : knowledge.IsIgnoringPlayer ? releasedEntryState
                       : lostEntryState;

        Debug.Log("[Brain] mode " + newMode + " -> " + next);
        bearAI.StateMachine.ChangeState(next);
    }

    // ---------------------------------------------------------------
    // D-75: heard him very close (D-19 -> Knows, stalking) and a moment
    // later SAW him - that is the same surprise, nothing has changed for
    // the bear. Later sights are not first sights (BearBoundaries).
    // ---------------------------------------------------------------
    private void HandleSightStarted()
    {
        if (closeEncounter == null || knowledge.Mode != KnowledgeMode.Knows)
            return;

        if (!knowsBySound || Time.time - knowsSince > heardThenSawWindow)
            return;

        if (bearAI.StateMachine.CurrentState != knowsEntryState)
            return;     // another state already deals with him

        closeEncounter.TryStart("heard him close, then saw him");
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
