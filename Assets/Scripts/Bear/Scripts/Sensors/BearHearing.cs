using UnityEngine;

// -------------------------------------------------------------------
// BearHearing - the bear's ears. Answers only one question:
// "did the bear hear this sound?"
//
// A sound is heard if the bear is closer than its Loudness
// (Loudness = hearing radius in metres, set by the sound source),
// x RangeMultiplier while a state asks the bear to listen harder
// (D-52, 11.11: it has just lost the player and listens for him).
// Heard -> passed to BearKnowledge, which decides what it means
// (exact contact or only a clue). What to DO about it is decided by
// BearBrain, not here.
// -------------------------------------------------------------------
public class BearHearing : MonoBehaviour
{
    [Header("Debug")]
    public bool logHeardSounds = false;     // every heard sound (2-3 per second while walking)
    public bool logIgnoredSounds = false;   // sounds that were too far

    // 1 = normal ears. Set by a state, reset by the same state.
    public float RangeMultiplier { get; private set; } = 1f;

    private BearKnowledge knowledge;

    private void Awake()
    {
        knowledge = GetComponent<BearKnowledge>();

        if (knowledge == null)
            Debug.LogError("BearHearing: BearKnowledge is missing on the bear.");
    }

    // Listens harder (x multiplier) until ResetRangeMultiplier().
    public void SetRangeMultiplier(float multiplier)
    {
        RangeMultiplier = Mathf.Max(0.1f, multiplier);
    }

    public void ResetRangeMultiplier()
    {
        RangeMultiplier = 1f;
    }

    public void HearSound(SoundEvent soundEvent)
    {
        float distance = Vector3.Distance(transform.position, soundEvent.Position);
        float radius = soundEvent.Loudness * RangeMultiplier;

        if (distance > radius)
        {
            if (logIgnoredSounds)
                Debug.Log("[Hearing] not heard " + soundEvent.Type +
                          ": distance " + distance.ToString("F1") +
                          " m > radius " + radius.ToString("F1") + " m");
            return;
        }

        if (logHeardSounds)
            Debug.Log("[Hearing] heard " + soundEvent.Type +
                      ", distance " + distance.ToString("F1") +
                      " m, loudness " + soundEvent.Loudness + " m");

        if (knowledge != null)
            knowledge.ReceiveSound(soundEvent, distance);
    }
}
