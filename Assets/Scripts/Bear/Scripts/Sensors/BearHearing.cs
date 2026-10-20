using UnityEngine;

// -------------------------------------------------------------------
// BearHearing - decides whether the bear hears a sound and what it
// does about it.
//
// 1. Distance: a sound is heard only if the bear is closer than its
//    Loudness (Loudness = hearing radius in metres).
// 2. Tracking: while the bear sees and tracks the player, sounds are
//    ignored.
// 3. Anti-spam: footsteps come 2-3 times per second. While the bear is
//    already investigating, a new sound changes its target only if it
//    is far enough from the last one AND enough time has passed.
//    Otherwise every step would restart the search (X / Y / Z).
// 4. Heard -> point near the sound on NavMesh -> InvestigateSound.
// -------------------------------------------------------------------
public class BearHearing : MonoBehaviour
{
    [Header("While investigating")]
    [Tooltip("New sound re-targets the search only if it is at least this far from the last heard sound (m).")]
    public float retargetMinDistance = 6f;

    [Tooltip("And only if at least this many seconds passed since the last heard sound.")]
    public float retargetCooldown = 2f;

    [Header("Debug")]
    public bool logIgnoredSounds = false;   // log sounds that were too far / skipped

    private BearAI bearAI;
    private BearInvestigationSystem investigationSystem;
    private BearInvestigateSoundState investigateSoundState;

    private Vector3 lastHeardPosition;
    private float lastHeardTime = -999f;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        investigationSystem = GetComponent<BearInvestigationSystem>();
        investigateSoundState = GetComponent<BearInvestigateSoundState>();
    }

    public void HearSound(SoundEvent soundEvent)
    {
        // --- 1. Too far to hear ---
        float distance = Vector3.Distance(transform.position, soundEvent.Position);

        if (distance > soundEvent.Loudness)
        {
            if (logIgnoredSounds)
                Debug.Log("Bear did not hear " + soundEvent.Type +
                          ": distance " + distance.ToString("F1") +
                          " m > loudness " + soundEvent.Loudness + " m");
            return;
        }

        if (bearAI == null || investigationSystem == null || investigateSoundState == null)
            return;

        // --- 2. While the bear sees and tracks the player, sounds do not distract it ---
        if (bearAI.StateMachine.CurrentState == BearState.Tracking)
        {
            if (logIgnoredSounds)
                Debug.Log("Bear heard sound, ignored (tracking)");
            return;
        }

        // --- 3. Already investigating: re-target only for a new place, not too often ---
        if (bearAI.StateMachine.CurrentState == BearState.InvestigateSound)
        {
            bool tooSoon = Time.time - lastHeardTime < retargetCooldown;
            bool sameArea = Vector3.Distance(soundEvent.Position, lastHeardPosition) < retargetMinDistance;

            if (tooSoon || sameArea)
            {
                if (logIgnoredSounds)
                    Debug.Log("Bear heard sound, already investigating this area");
                return;
            }
        }

        Debug.Log(
            "Bear heard sound: " + soundEvent.Type +
            ", distance " + distance.ToString("F1") +
            " m, loudness " + soundEvent.Loudness + " m"
        );

        // --- 4. Find a point near the sound and go investigate ---
        if (!investigationSystem.TryGetInvestigationPoint(
                soundEvent.Position,
                out Vector3 investigationPoint))
        {
            Debug.Log("Bear could not find an investigation point.");
            return;
        }

        lastHeardPosition = soundEvent.Position;
        lastHeardTime = Time.time;

        bearAI.StateMachine.ChangeState(BearState.InvestigateSound);
        investigateSoundState.SetInvestigationPoint(investigationPoint);

        Debug.Log("Bear investigation point: " + investigationPoint);
    }
}
