using UnityEngine;

// =====================================================================
// BearVoice - one place that plays the bear's sounds (D-75): huff,
// fear, roars. A 3D AudioSource on the bear: from its side, louder
// close by, heard up to maxDistance.
// No scene setup: the first script that needs it adds it (On()).
// Pattern 1 still plays its roar itself (tested code, not touched).
// =====================================================================
public class BearVoice : MonoBehaviour
{
    [Tooltip("Full volume closer than this (m).")]
    public float minDistance = 5f;
    [Tooltip("Silent farther than this (m).")]
    public float maxDistance = 60f;

    private AudioSource source;

    // The bear's voice: the existing one or a new one.
    public static BearVoice On(GameObject bear)
    {
        BearVoice voice = bear.GetComponent<BearVoice>();
        return voice != null ? voice : bear.AddComponent<BearVoice>();
    }

    public void Play(AudioClip clip, float volume)
    {
        if (clip == null)
            return;

        if (source == null)
            Setup();

        source.PlayOneShot(clip, volume);
    }

    // Uses the AudioSource already on the bear (pattern 1 adds one too),
    // otherwise adds a 3D one.
    private void Setup()
    {
        source = GetComponent<AudioSource>();
        if (source != null)
            return;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
    }
}
