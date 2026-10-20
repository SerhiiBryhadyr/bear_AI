using UnityEngine;

// -------------------------------------------------------------------
// PlayerFootsteps - turns the player's real movement into footstep
// sounds that bears can hear.
//
// How it works:
// 1. Counts horizontal distance walked on the ground.
// 2. Every "step length" metres = one step.
// 3. Each step creates a SoundEvent (Footstep) and sends it to every
//    BearHearing in the scene. Loudness depends on the move mode.
//
// Loudness = hearing radius in metres: a bear hears the step only if
// it is closer than this (the check is in BearHearing).
// Later: loudness x surface multiplier (terrain layer under the player).
// -------------------------------------------------------------------
[RequireComponent(typeof(PlayerController))]
public class PlayerFootsteps : MonoBehaviour
{
    [Header("Loudness by mode (hearing radius, m)")]
    public float crouchLoudness = 2.5f;
    public float backStepLoudness = 4f;    // S + Shift, careful retreat
    public float backWalkLoudness = 7f;    // S
    public float walkLoudness = 10f;
    public float runLoudness = 20f;

    [Header("Step length (m between two steps)")]
    public float crouchStepLength = 0.8f;
    public float walkStepLength = 1.6f;    // also used for both backward modes
    public float runStepLength = 2.4f;

    [Header("Debug")]
    public bool logSteps = false;          // write every step to the Console
    public bool drawStepRadius = true;     // Scene view: circle of the last step

    private PlayerController player;
    private CharacterController controller;
    private BearHearing[] bears;

    private Vector3 lastPosition;
    private float distanceSinceStep;

    // For the Scene view circle.
    private Vector3 lastStepPosition;
    private float lastStepLoudness;
    private float lastStepTime = -999f;

    private void Start()
    {
        player = GetComponent<PlayerController>();
        controller = player.controller;

        // All bears in the scene (now one, later maybe more).
        bears = FindObjectsByType<BearHearing>(FindObjectsSortMode.None);

        if (bears.Length == 0)
            Debug.LogWarning("PlayerFootsteps: no BearHearing found in the scene.");

        lastPosition = transform.position;
    }

    // ---------------------------------------------------------------
    // Counts walked distance and makes a step every step length.
    // Only on the ground: jumping / falling makes no steps.
    // ---------------------------------------------------------------
    private void Update()
    {
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;                       // only horizontal movement counts
        lastPosition = transform.position;

        MoveMode mode = player.CurrentMode;

        if (mode == MoveMode.Idle || !controller.isGrounded)
            return;

        distanceSinceStep += delta.magnitude;

        float stepLength = GetStepLength(mode);

        if (distanceSinceStep >= stepLength)
        {
            distanceSinceStep -= stepLength;
            MakeStep(mode);
        }
    }

    // One step: build the SoundEvent and give it to every bear.
    private void MakeStep(MoveMode mode)
    {
        float loudness = GetLoudness(mode);

        SoundEvent step = new SoundEvent(
            transform.position,
            SoundType.Footstep,
            loudness
        );

        if (logSteps)
            Debug.Log("[Footstep] " + mode + ", loudness " + loudness + " m");

        foreach (BearHearing bear in bears)
        {
            if (bear != null)
                bear.HearSound(step);
        }

        lastStepPosition = transform.position;
        lastStepLoudness = loudness;
        lastStepTime = Time.time;
    }

    private float GetLoudness(MoveMode mode)
    {
        switch (mode)
        {
            case MoveMode.Crouch:   return crouchLoudness;
            case MoveMode.BackStep: return backStepLoudness;
            case MoveMode.BackWalk: return backWalkLoudness;
            case MoveMode.Run:      return runLoudness;
            default:                return walkLoudness;
        }
    }

    private float GetStepLength(MoveMode mode)
    {
        switch (mode)
        {
            case MoveMode.Crouch: return crouchStepLength;
            case MoveMode.Run:    return runStepLength;
            default:              return walkStepLength;
        }
    }

    // Scene view only: yellow circle = how far the last step was heard.
    // Shown for 0.5 s after each step. Not visible in the Game view.
    private void OnDrawGizmos()
    {
        if (!drawStepRadius || Time.time - lastStepTime > 0.5f)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(lastStepPosition, lastStepLoudness);
    }
}
