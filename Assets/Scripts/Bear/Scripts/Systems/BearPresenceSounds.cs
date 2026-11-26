using UnityEngine;

// =====================================================================
// BearPresenceSounds - the bear is not silent (D-34, 26.11): breathing,
// sniffing, grumbling, panting after a run, a breath after a roar.
// Quiet and close only (maxDistance) - hearing it breathe means it is
// RIGHT THERE. Its own 3D AudioSource, so it never cuts a roar
// (roars and huffs play on BearVoice).
//
//   Breathing is built from single breaths (26.11: one loop of rare
//   heavy breaths sounded like a man in an oxygen mask): medium breaths
//   with a deep one now and then (deepChance), a random pause between.
//   face-off / close encounter - throaty rumbling breaths (rumbleBreaths)
//   calm (patrol, idle, sound, search) - breaths + now and then a sniff
//       or a grumble (calmSoundInterval)
//   stalking - breaths only, quieter and slower (stalkVolume): careful
//   No single breaths assigned - the old breathing loop (breathLoop).
//   after a run (faster than runSpeed for runTime s, then slow) - panting
//   after a roar - AfterRoar() (BearThreatDisplay, the face-off)
// =====================================================================
public class BearPresenceSounds : MonoBehaviour
{
    [Header("Breathing (single breaths, Assets/Audio/Bear)")]
    [Tooltip("Ordinary breaths, medium pace (Bear_Breath_*).")]
    public AudioClip[] breaths;
    [Tooltip("Deep slow breaths, now and then (Bear_BreathDeep_*).")]
    public AudioClip[] deepBreaths;
    [Tooltip("Throaty rumbling breaths - face-off, close encounter (Bear_BreathRumble_*). Empty - ordinary breaths.")]
    public AudioClip[] rumbleBreaths;
    [Tooltip("Pause between breaths (s, random), on top of the breath itself.")]
    public Vector2 breathPause = new Vector2(0.6f, 1.6f);
    [Tooltip("Share of deep breaths among ordinary ones.")]
    [Range(0f, 1f)] public float deepChance = 0.25f;
    [Tooltip("No single breaths assigned - this loop instead (Bear_BreathCalm_Loop_01).")]
    public AudioClip breathLoop;

    [Header("Other clips (Assets/Audio/Bear)")]
    public AudioClip pant;                                  // Bear_Pant_01
    public AudioClip afterRoar;                             // Bear_BreathAfterRoar_01
    public AudioClip[] calmSounds;                          // Bear_Sniff_01, Bear_Grumble_01
    [Tooltip("Calm: an ordinary roar now and then instead of a sniff - heard far (BearVoice), not a threat (26.11).")]
    public AudioClip[] calmRoars;
    [Range(0f, 1f)] public float calmRoarChance = 0.12f;

    [Header("How far and how loud")]
    [Tooltip("Full volume closer than this (m).")]
    public float minDistance = 2f;
    [Tooltip("Silent farther than this (m): only close by.")]
    public float maxDistance = 15f;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Stalking: the breathing this much quieter (x).")]
    [Range(0f, 1f)] public float stalkVolume = 0.3f;

    [Header("When")]
    [Tooltip("Calm states: a sniff / grumble every this many seconds (random).")]
    public Vector2 calmSoundInterval = new Vector2(8f, 20f);
    [Tooltip("Faster than this (m/s)...")]
    public float runSpeed = 4f;
    [Tooltip("...for this long (s), then slower than walkSpeed -> panting.")]
    public float runTime = 3f;
    public float walkSpeed = 2f;

    private AudioSource loopSource;                         // the old loop (no single breaths)
    private AudioSource breathSource;                       // single breaths
    private AudioSource shotSource;                         // pant, sniff, grumble, after roar
    private float nextBreath;
    private AudioClip lastBreath;
    private BearAI bearAI;
    private BearMovement movement;
    private float nextCalmSound;
    private float fastFor;
    private float afterRoarAt = -1f;

    private void Start()
    {
        bearAI = GetComponent<BearAI>();
        movement = GetComponent<BearMovement>();
        loopSource = MakeSource(true);
        breathSource = MakeSource(false);
        shotSource = MakeSource(false);

        if (!HasBreaths && breathLoop != null)
        {
            loopSource.clip = breathLoop;
            loopSource.time = Random.Range(0f, breathLoop.length);
            loopSource.Play();
        }
        nextCalmSound = Time.time + Random.Range(calmSoundInterval.x, calmSoundInterval.y);
    }

    // A breath right after a roar (call with the roar's length).
    public void AfterRoar(float roarLength)
    {
        if (afterRoar != null)
            afterRoarAt = Time.time + Mathf.Max(0f, roarLength - 0.2f);
    }

    private void Update()
    {
        BearState state = bearAI != null && bearAI.StateMachine != null ? bearAI.StateMachine.CurrentState : BearState.Idle;
        bool stalking = state == BearState.Stalk;
        bool calm = state == BearState.Idle || state == BearState.Patrol ||
                    state == BearState.InvestigateSound || state == BearState.SearchLastKnown;

        // Breathing: quiet while stalking, silent while a one-shot breathes for it.
        loopSource.volume = shotSource.isPlaying ? 0f : volume * (stalking ? stalkVolume : 1f);
        bool threat = state == BearState.FaceOff || state == BearState.CloseEncounter;
        UpdateBreathing(stalking, threat);

        // After a roar - the breath.
        if (afterRoarAt > 0f && Time.time >= afterRoarAt)
        {
            afterRoarAt = -1f;
            PlayShot(afterRoar);
        }

        // After a run - panting.
        float speed = movement != null ? movement.CurrentSpeed : 0f;
        if (speed > runSpeed)
            fastFor += Time.deltaTime;
        else if (speed < walkSpeed)
        {
            if (fastFor >= runTime && !stalking)
                PlayShot(pant);
            fastFor = 0f;
        }

        // Calm: a sniff or a grumble now and then.
        if (calm && Time.time >= nextCalmSound)
        {
            nextCalmSound = Time.time + Random.Range(calmSoundInterval.x, calmSoundInterval.y);
            if (calmRoars != null && calmRoars.Length > 0 && Random.value < calmRoarChance)
            {
                AudioClip roar = calmRoars[Random.Range(0, calmRoars.Length)];
                BearVoice.On(gameObject).Play(roar, volume);
                AfterRoar(roar != null ? roar.length : 0f);
            }
            else if (calmSounds != null && calmSounds.Length > 0 && !shotSource.isPlaying)
                PlayShot(calmSounds[Random.Range(0, calmSounds.Length)]);
        }
    }

    private bool HasBreaths => breaths != null && breaths.Length > 0;

    // One breath at a time: the next starts after this one + a pause.
    // Face-off - throaty rumbles; else ordinary, a deep one now and then.
    private void UpdateBreathing(bool stalking, bool threat)
    {
        if (!HasBreaths || Time.time < nextBreath || shotSource.isPlaying)
            return;

        AudioClip[] set = threat && rumbleBreaths != null && rumbleBreaths.Length > 0 ? rumbleBreaths
                        : deepBreaths != null && deepBreaths.Length > 0 && Random.value < deepChance ? deepBreaths
                        : breaths;
        AudioClip clip = set[Random.Range(0, set.Length)];
        if (clip == lastBreath && set.Length > 1)
            clip = set[(System.Array.IndexOf(set, clip) + 1) % set.Length];
        lastBreath = clip;
        if (clip == null)
            return;

        breathSource.clip = clip;
        breathSource.volume = volume * (stalking ? stalkVolume : 1f);
        breathSource.pitch = Random.Range(0.95f, 1.05f);
        breathSource.Play();

        float pause = Random.Range(breathPause.x, breathPause.y) * (stalking ? 1.5f : threat ? 0.7f : 1f);
        nextBreath = Time.time + clip.length / breathSource.pitch + pause;
    }

    private void PlayShot(AudioClip clip)
    {
        if (clip == null)
            return;
        shotSource.clip = clip;
        shotSource.volume = volume;
        shotSource.pitch = Random.Range(0.96f, 1.04f);
        shotSource.Play();
    }

    private AudioSource MakeSource(bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 1f;
        s.rolloffMode = AudioRolloffMode.Linear;            // really silent past maxDistance
        s.minDistance = minDistance;
        s.maxDistance = maxDistance;
        return s;
    }
}
