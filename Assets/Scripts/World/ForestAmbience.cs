using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// ForestAmbience - the sound of the forest around the player (26.11).
//
// Two layers:
//   1. BED - a quiet steady background (wind in the pines), 2D, loops.
//      With 2+ beds it crossfades to another one now and then, so a
//      20-30 s loop is not heard repeating.
//   2. ONE-SHOTS - small sounds (owl, gust, creaking trunk, bird...)
//      at random moments, each at a random point around the player
//      (3D: from the side, quieter far away). Never the same clip twice
//      in a row, a little random pitch and volume.
// The bear's own sounds play on the bear (BearVoice) on top of this.
// Nothing here is heard by the bear's AI.
//
// Use: put it on the player (or the camera with the Audio Listener),
// drag the clips into Beds and One Shots. Keep the bed quiet - the
// bear's breathing and steps must stay audible.
// Loudness system (26.11): the files are already levelled - beds
// -30 LUFS, forest one-shots -24, bear: roar -14, startle -16, growl
// -18..-20, huff -20, breathing -22..-26. So volumes here stay near 1
// and the balance comes from the files + 3D distance.
// =====================================================================
public class ForestAmbience : MonoBehaviour
{
    [Header("Bed - steady background (2D, loops)")]
    public AudioClip[] beds;
    [Range(0f, 1f)] public float bedVolume = 0.6f;
    [Tooltip("Switches to another bed after this long (s, random). Needs 2+ beds.")]
    public Vector2 bedSwitchTime = new Vector2(60f, 120f);
    [Tooltip("Crossfade between beds (s).")]
    public float bedFade = 6f;

    [Header("One-shots - small sounds around the player (3D)")]
    public AudioClip[] oneShots;
    [Tooltip("Pause between two one-shots (s, random).")]
    public Vector2 oneShotInterval = new Vector2(6f, 20f);
    [Tooltip("How far from the player they sound (m, random).")]
    public Vector2 oneShotDistance = new Vector2(15f, 50f);
    [Tooltip("How high above the ground (m, random): birds and treetops are up.")]
    public Vector2 oneShotHeight = new Vector2(1f, 10f);
    [Range(0f, 1f)] public float oneShotVolume = 1f;
    [Tooltip("Random pitch +- this (0.05 = +-5%).")]
    public float pitchSpread = 0.05f;
    [Tooltip("Heard up to this distance (m).")]
    public float oneShotMaxDistance = 80f;

    private AudioSource bedA, bedB;                         // two, to crossfade
    private int bedIndex = -1;
    private float nextBedSwitch;
    private float fadeT = 1f;                               // 0..1, 1 = the fade is over

    private readonly List<AudioSource> shotSources = new List<AudioSource>();
    private float nextShot;
    private int lastShot = -1;

    // ---------------------------------------------------------------
    // Setup: two 2D sources for the bed, a few 3D sources for one-shots.
    // ---------------------------------------------------------------
    private void Start()
    {
        bedA = MakeSource("Ambience Bed A", false);
        bedB = MakeSource("Ambience Bed B", false);
        for (int i = 0; i < 3; i++)
            shotSources.Add(MakeSource("Ambience Shot " + i, true));

        if (beds != null && beds.Length > 0)
        {
            bedIndex = Random.Range(0, beds.Length);
            bedA.clip = beds[bedIndex];
            bedA.volume = bedVolume;
            bedA.time = Random.Range(0f, bedA.clip.length);  // not always from the start
            bedA.Play();
        }

        nextBedSwitch = Time.time + Random.Range(bedSwitchTime.x, bedSwitchTime.y);
        nextShot = Time.time + Random.Range(oneShotInterval.x, oneShotInterval.y);
    }

    private void Update()
    {
        UpdateBed();
        UpdateOneShots();
    }

    // ---------------------------------------------------------------
    // Bed: now and then the other source starts another bed and they
    // crossfade over bedFade seconds.
    // ---------------------------------------------------------------
    private void UpdateBed()
    {
        if (beds == null || beds.Length == 0)
            return;

        if (fadeT < 1f)
        {
            fadeT = Mathf.Min(1f, fadeT + Time.deltaTime / Mathf.Max(0.1f, bedFade));
            bedB.volume = bedVolume * fadeT;
            bedA.volume = bedVolume * (1f - fadeT);
            if (fadeT >= 1f)
            {
                bedA.Stop();
                (bedA, bedB) = (bedB, bedA);                // the new one is "A" now
            }
            return;
        }

        bedA.volume = bedVolume;                            // follows the slider in Play
        if (beds.Length < 2 || Time.time < nextBedSwitch)
            return;

        int next = (bedIndex + Random.Range(1, beds.Length)) % beds.Length;
        bedIndex = next;
        bedB.clip = beds[next];
        bedB.volume = 0f;
        bedB.time = Random.Range(0f, bedB.clip.length);
        bedB.Play();
        fadeT = 0f;
        nextBedSwitch = Time.time + Random.Range(bedSwitchTime.x, bedSwitchTime.y);
    }

    // ---------------------------------------------------------------
    // One-shots: a random clip (not the last one) at a random point on a
    // ring around the player.
    // ---------------------------------------------------------------
    private void UpdateOneShots()
    {
        if (oneShots == null || oneShots.Length == 0 || Time.time < nextShot)
            return;

        nextShot = Time.time + Random.Range(oneShotInterval.x, oneShotInterval.y);

        int pick = Random.Range(0, oneShots.Length);
        if (oneShots.Length > 1 && pick == lastShot)
            pick = (pick + 1) % oneShots.Length;
        lastShot = pick;
        if (oneShots[pick] == null)
            return;

        AudioSource source = FreeShotSource();
        Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(oneShotDistance.x, oneShotDistance.y);
        source.transform.position = transform.position + new Vector3(dir.x, Random.Range(oneShotHeight.x, oneShotHeight.y), dir.y);
        source.pitch = 1f + Random.Range(-pitchSpread, pitchSpread);
        source.volume = oneShotVolume * Random.Range(0.7f, 1f);
        source.clip = oneShots[pick];
        source.Play();
    }

    private AudioSource FreeShotSource()
    {
        foreach (AudioSource s in shotSources)
            if (!s.isPlaying)
                return s;
        return shotSources[0];                              // all busy - the oldest one is cut
    }

    private AudioSource MakeSource(string name, bool is3D)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = !is3D;
        s.spatialBlend = is3D ? 1f : 0f;
        if (is3D)
        {
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = 10f;
            s.maxDistance = oneShotMaxDistance;
            s.transform.SetParent(null);                    // stays where it sounded, the player walks on
        }
        return s;
    }
}
