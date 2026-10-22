using UnityEngine;

// -------------------------------------------------------------------
// BearHearing - the bear's ears. Answers only one question:
// "did the bear hear this sound?"
//
// A sound is heard if the bear is closer than its Loudness
// (Loudness = hearing radius in metres, set by the sound source).
// Heard -> passed to BearKnowledge, which decides what it means
// (exact contact or only a clue). What to DO about it is decided by
// BearBrain, not here.
// -------------------------------------------------------------------
public class BearHearing : MonoBehaviour
{
    [Header("Debug")]
    public bool logHeardSounds = false;     // every heard sound (2-3 per second while walking)
    public bool logIgnoredSounds = false;   // sounds that were too far

    private BearKnowledge knowledge;

    private void Awake()
    {
        knowledge = GetComponent<BearKnowledge>();

        if (knowledge == null)
            Debug.LogError("BearHearing: BearKnowledge is missing on the bear.");
    }

    public void HearSound(SoundEvent soundEvent)
    {
        float distance = Vector3.Distance(transform.position, soundEvent.Position);

        if (distance > soundEvent.Loudness)
        {
            if (logIgnoredSounds)
                Debug.Log("[Hearing] not heard " + soundEvent.Type +
                          ": distance " + distance.ToString("F1") +
                          " m > loudness " + soundEvent.Loudness + " m");
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
