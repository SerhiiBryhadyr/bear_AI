using UnityEngine;

// =====================================================================
// BearThreatDisplay - what the player SEES of the bear's threat in the
// face-off (25.11, D-76). Levels, one step at a time:
//
//   0 "I see you"    - stares (the head on him), shakes its head or
//                      rears up (to see him better, to look bigger)
//   1 "go away"      - the angry stance (head low) + huff; sometimes
//                      1-2 steps toward him first; peers, shakes its head
//   2 "last warning" - slams its paws on the ground + roar
//   3 "I'll attack"  - lunge with a paw hook + roar
//
// The face-off only says which level it wants now (Tick). This moves
// there one step at a time, holds every level at least minLevelTime,
// plays a random action of the level (not the same twice in a row) and
// more actions of the level after a pause. Good never asks above 1.
//
// The face-off's decisions (attack, chase, leave) wait while an action
// plays (IsBusy): an action is never cut (25.11). Only contact (he ran
// into the bear) cuts it - Abort().
// Animator: BearThreatAnimator; head shake: BearHeadLook; sounds: BearVoice.
// =====================================================================
public class BearThreatDisplay : MonoBehaviour
{
    [Header("Levels")]
    [Tooltip("Every level is held at least this long (s, random) before the next step.")]
    public Vector2 minLevelTime = new Vector2(2f, 3f);
    [Tooltip("Inside a level: pause after an action before the next one (s, random).")]
    public Vector2 actionPause = new Vector2(2f, 4f);

    [Header("Level 0 - rears up, shakes its head")]
    [Tooltip("Good: chance it rears up at level 0.")]
    [Range(0f, 1f)] public float goodRearChance = 0.4f;
    [Tooltip("Medium / Angry rear up only when he is farther than this (m): it takes ~9 s.")]
    public float rearMinDistance = 15f;
    [Tooltip("Medium / Angry, farther than rearMinDistance: chance it rears up.")]
    [Range(0f, 1f)] public float rearChanceFar = 0.4f;
    [Tooltip("Head shake: how long (s).")]
    public float shakeTime = 1.3f;
    [Tooltip("Head shake: angle to each side (deg, random in the range).")]
    public Vector2 shakeAngle = new Vector2(10f, 25f);

    [Header("Level 1 - steps toward him")]
    [Tooltip("Chance it first walks toward him before the angry stance.")]
    [Range(0f, 1f)] public float stepChance = 0.4f;
    [Tooltip("How far it steps (m, random).")]
    public Vector2 stepDistance = new Vector2(1f, 2f);
    [Tooltip("Steps only when he is farther than this (m).")]
    public float stepMinDistance = 7f;
    public float stepSpeed = 1.5f;

    [Header("Sounds")]
    [Tooltip("Huff at level 1. Empty - only the log.")]
    public AudioClip huffClip;
    [Tooltip("Roar at levels 2-3. Empty = the warning roar of BearRearPursuitState.")]
    public AudioClip roarClip;
    [Range(0f, 1f)] public float volume = 1f;

    [Header("Debug")]
    public bool logLevels = true;

    public int Level { get; private set; } = -1;            // -1 = not showing (walks, normal stance)
    public bool IsBusy => stepping || (anim != null && anim.IsBusy);
    public bool Moving => stepping;                         // the face-off must not stop it

    private BearThreatAnimator anim;
    private BearHeadLook headLook;
    private BearMovement movement;
    private BearMood mood;
    private BearVoice voice;
    private AudioClip roarFallback;

    private float levelSince;
    private float holdFor;
    private float nextActionAt;
    private bool wasBusy;
    private bool level0Done;
    private string lastAction = "";
    private string lastLevel0 = "";                         // across face-offs: not the same opening twice

    private bool stepping;
    private Vector3 stepPoint;
    private float stepUntil;

    private void Awake()
    {
        anim = GetComponent<BearThreatAnimator>();
        if (anim == null)
            anim = gameObject.AddComponent<BearThreatAnimator>();
        headLook = GetComponent<BearHeadLook>();
        movement = GetComponent<BearMovement>();
        mood = GetComponent<BearMood>();
        voice = BearVoice.On(gameObject);

        BearRearPursuitState pursuit = GetComponent<BearRearPursuitState>();
        roarFallback = pursuit != null ? pursuit.warningRoarClip : null;
    }

    private string MoodName => mood != null ? mood.Level.ToString() : "Good";

    // ---------------------------------------------------------------
    // The face-off began: level 0, its action comes with the first Tick.
    // ---------------------------------------------------------------
    public void Begin()
    {
        Level = 0;
        level0Done = false;
        stepping = false;
        lastAction = "";
        StartLevel();
    }

    // Every frame of the face-off: wanted = 0..3, or -1 (it walks after him).
    public void Tick(int wanted, float distance, Vector3 player)
    {
        if (stepping)
        {
            UpdateStep();
            return;
        }

        bool busy = IsBusy;
        if (wasBusy && !busy)
            nextActionAt = Time.time + Random.Range(actionPause.x, actionPause.y);
        wasBusy = busy;
        if (busy)
            return;

        if (wanted < 0)
        {
            Stop();
            return;
        }

        if (Level < 0)                                      // back from walking: no new opening
        {
            Level = 0;
            level0Done = true;
            StartLevel();
        }

        if (!level0Done)
        {
            level0Done = true;
            OpenLevel0(distance);
            return;
        }

        if (wanted != Level && Time.time - levelSince >= holdFor)
        {
            int from = Level;
            Level += wanted > Level ? 1 : -1;
            StartLevel();
            EnterLevel(from, distance, player);
            return;
        }

        if (Time.time >= nextActionAt)
            MoreOfLevel();
    }

    // Good's last warning (he comes at it): the lunge. The roar - the face-off.
    public void WarnLunge()
    {
        stepping = false;
        level0Done = true;                                  // no opening after it
        if (Level < 1)
        {
            Level = 1;                                      // the lunge ends in the angry stance
            StartLevel();
        }
        Say("LAST WARNING: lunge");
        anim.Play(BearThreatAnimator.Lunge);
        lastAction = "lunge";
    }

    // The face-off ends or the bear starts walking: back to Locomotion.
    // An action that plays is not cut - the Animator ends it itself.
    public void Stop()
    {
        stepping = false;
        if (Level >= 0)
            Say("stops showing (level " + Level + ")");
        Level = -1;
        anim.ToLocomotion();
    }

    // Contact: cut whatever plays - the attack cannot wait.
    public void Abort()
    {
        stepping = false;
        Level = -1;
        anim.ToLocomotion(true);
    }

    // ---------------------------------------------------------------
    // Levels.
    // ---------------------------------------------------------------
    private void StartLevel()
    {
        levelSince = Time.time;
        holdFor = Random.Range(minLevelTime.x, minLevelTime.y);
        nextActionAt = Time.time + Random.Range(actionPause.x, actionPause.y);
    }

    // The opening: stares, shakes its head or rears up (not as last time).
    private void OpenLevel0(float distance)
    {
        bool good = mood == null || mood.Level == BearMoodLevel.Good;
        float rear = good ? goodRearChance : (distance >= rearMinDistance ? rearChanceFar : 0f);

        string pick;
        if (lastLevel0 != "rear" && Random.value < rear)
            pick = "rear";
        else
            pick = lastLevel0 == "shake" ? "stare" : lastLevel0 == "stare" ? "shake" : (Random.value < 0.5f ? "stare" : "shake");

        lastLevel0 = pick;
        lastAction = pick;
        switch (pick)
        {
            case "rear":
                Say("level 0: REARS UP - looks at him, shows its size (" + distance.ToString("F0") + " m)");
                anim.Play(BearThreatAnimator.HindUp);
                break;
            case "shake":
                Say("level 0: shakes its head");
                ShakeHead();
                break;
            default:
                Say("level 0: stares at him");
                break;
        }
    }

    private void EnterLevel(int from, float distance, Vector3 player)
    {
        bool up = Level > from;
        switch (Level)
        {
            case 0:
                Say("level " + from + " -> 0: calms down");
                if (anim.InAngryStance)
                    anim.Play(BearThreatAnimator.AngryOut);
                lastAction = "calm";
                break;

            case 1:
                if (up && distance > stepMinDistance && Random.value < stepChance)
                {
                    StartStep(player, distance);
                    Say("level 0 -> 1: steps toward him, then the angry stance + huff");
                }
                else
                {
                    Say("level " + from + " -> 1: angry stance + huff");
                    if (!anim.InAngryStance)
                        anim.Play(BearThreatAnimator.AngryIn);
                }
                Huff();
                lastAction = "stance";
                break;

            case 2:
                if (up)
                {
                    Say("level 1 -> 2: PAW SLAM + roar");
                    anim.Play(BearThreatAnimator.PawSlam);
                    Roar();
                    lastAction = "paw";
                }
                else
                    Say("level 3 -> 2");
                break;

            default:
                Say("level 2 -> 3: LUNGE + roar");
                anim.Play(BearThreatAnimator.Lunge);
                Roar();
                lastAction = "lunge";
                break;
        }
    }

    // The level stays: one more action of it, not the same as the last one.
    private void MoreOfLevel()
    {
        nextActionAt = Time.time + Random.Range(actionPause.x, actionPause.y);
        string pick;

        switch (Level)
        {
            case 0:
                pick = lastAction == "shake" ? "stare" : (Random.value < 0.5f ? "shake" : "stare");
                if (pick == "shake")
                    ShakeHead();
                break;

            case 1:
                pick = Pick(lastAction, "peer", "shake", "huff");
                if (pick == "peer")
                    anim.Play(BearThreatAnimator.AngryPeer);
                else if (pick == "shake")
                    ShakeHead();
                else
                    Huff();
                break;

            case 2:
                pick = lastAction == "paw" ? "shake" : "paw";
                if (pick == "paw")
                {
                    anim.Play(BearThreatAnimator.PawSlam);
                    Roar();
                }
                else
                {
                    ShakeHead();
                    Huff();
                }
                break;

            default:
                pick = lastAction == "lunge" ? "paw" : "lunge";
                anim.Play(pick == "lunge" ? BearThreatAnimator.Lunge : BearThreatAnimator.PawSlam);
                Roar();
                break;
        }

        lastAction = pick;
        Say("level " + Level + ": " + pick);
    }

    // ---------------------------------------------------------------
    // Steps toward him (level 1), then the angry stance.
    // ---------------------------------------------------------------
    private void StartStep(Vector3 player, float distance)
    {
        Vector3 toPlayer = player - transform.position;
        toPlayer.y = 0f;
        float step = Mathf.Min(Random.Range(stepDistance.x, stepDistance.y), distance - stepMinDistance + 1f);

        stepPoint = transform.position + toPlayer.normalized * Mathf.Max(0.5f, step);
        stepUntil = Time.time + 3f;
        stepping = true;
        movement.SetSpeed(stepSpeed);
        movement.MoveTo(stepPoint);
    }

    private void UpdateStep()
    {
        Vector3 left = stepPoint - transform.position;
        left.y = 0f;
        if (left.magnitude > 0.3f && Time.time < stepUntil)
            return;

        stepping = false;
        movement.Stop();
        movement.SetSpeedMultiplier(1f);                    // the step speed must not stay
        anim.Play(BearThreatAnimator.AngryIn);
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------
    private void ShakeHead()
    {
        if (headLook != null && headLook.isActiveAndEnabled)
            headLook.Shake(shakeTime, Random.Range(shakeAngle.x, shakeAngle.y));
    }

    private void Huff()
    {
        if (huffClip != null && voice != null)
            voice.Play(huffClip, volume);
        else if (logLevels)
            Debug.Log("[Threat] huff (no clip)");
    }

    private void Roar()
    {
        AudioClip clip = roarClip != null ? roarClip : roarFallback;
        if (clip != null && voice != null)
            voice.Play(clip, volume);
    }

    private static string Pick(string last, params string[] options)
    {
        string pick = options[Random.Range(0, options.Length)];
        return pick == last ? options[(System.Array.IndexOf(options, pick) + 1) % options.Length] : pick;
    }

    private void Say(string text)
    {
        if (logLevels)
            Debug.Log("[Threat] " + MoodName + " | " + text);
    }
}
