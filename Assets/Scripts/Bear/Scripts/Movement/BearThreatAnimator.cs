using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// BearThreatAnimator - plays the threat actions of the "Threat" group in
// the Animator (25.11, D-76). The group is built by Tools -> Bear ->
// Build Threat States (BearThreatStatesBuilder).
//
// Others ask for an action by its name (Lunge, PawSlam, AngryIn...);
// this crossfades into it and says whether it is still playing
// (IsBusy). An action is never cut (25.11): whoever asked waits until it
// ends. When an action ends, the Animator goes on by itself - to the
// angry stance (AngryIdle, loops) or back to Locomotion.
// The one exception - ToLocomotion(force): he ran into the bear.
//
// Safety: the bear starts moving while it stands in the angry stance ->
// back to Locomotion (a standing pose must not slide over the ground).
// No Threat group in the Animator (the tool was not run) -> nothing
// plays, IsBusy is false: everything works as before.
// =====================================================================
[RequireComponent(typeof(BearMovement))]
public class BearThreatAnimator : MonoBehaviour
{
    [Tooltip("Empty = the Animator on this object or on a child.")]
    public Animator animator;
    [Tooltip("Speed of the threat actions: 1 = like the walk (clips x1.26 longer - the bear is bigger), 1.26 = the clip's own speed. Can be changed in Play.")]
    public float actionSpeed = 1f;
    [Tooltip("Crossfade into an action (s).")]
    public float blend = 0.25f;
    [Tooltip("Moving faster than this (m/s) in the angry stance -> back to Locomotion.")]
    public float moveSpeed = 0.4f;
    [Tooltip("Log every action: [ThreatAnim].")]
    public bool logActions = true;

    // Actions = state names in the Threat group (BearThreatStatesBuilder).
    public const string HindUp = "HindUp";
    public const string Startle = "Startle";
    public const string AngryIn = "AngryIn";
    public const string AngryPeer = "AngryPeer";
    public const string AngryOut = "AngryOut";
    public const string PawSlam = "PawSlam";
    public const string Lunge = "Lunge";

    private static readonly string[] OneShotNames = { "HindUp", "HindLook", "HindDown", "Startle", "AngryIn", "AngryPeer", "AngryOut", "PawSlam", "Lunge" };
    private static readonly string[] AngryNames = { "AngryIn", "AngryIdle", "AngryPeer", "PawSlam", "Lunge" };   // end in the stance

    private readonly HashSet<int> oneShots = new HashSet<int>();
    private readonly HashSet<int> angry = new HashSet<int>();
    private readonly HashSet<int> all = new HashSet<int>();
    private string prefix;                                  // "Base Layer.Threat."
    private int locomotionHash;
    private int speedId;
    private bool hasSpeed;
    private BearMovement movement;

    private float requestHold;                              // the Animator takes a request on its next update
    private bool requestedAngry;

    public bool Ready { get; private set; }

    // An action plays (it must not be cut).
    public bool IsBusy => Ready && (Time.time < requestHold || InState(oneShots));
    // In the angry stance or in an action that ends there.
    public bool InAngryStance => Ready && (Time.time < requestHold ? requestedAngry : InState(angry));
    // Anywhere in the Threat group.
    public bool InThreat => Ready && (Time.time < requestHold || InState(all));

    // ---------------------------------------------------------------
    // Setup: is the Threat group there? State names -> hashes.
    // ---------------------------------------------------------------
    private void Start()
    {
        movement = GetComponent<BearMovement>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator == null)
            return;

        string layer = animator.GetLayerName(0);
        prefix = layer + ".Threat.";
        locomotionHash = Animator.StringToHash(layer + ".Locomotion");

        foreach (string name in OneShotNames)
            oneShots.Add(Hash(name));
        foreach (string name in AngryNames)
            angry.Add(Hash(name));
        all.UnionWith(oneShots);
        all.UnionWith(angry);

        Ready = animator.HasState(0, Hash("AngryIdle")) && animator.HasState(0, locomotionHash);
        if (!Ready)
        {
            Debug.LogWarning("[ThreatAnim] No 'Threat' group in the Animator - run Tools -> Bear -> Build Threat States. Threats stay logs.");
            return;
        }

        speedId = Animator.StringToHash("ThreatSpeed");
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.nameHash == speedId)
                hasSpeed = true;
    }

    private void Update()
    {
        if (!Ready)
            return;

        if (hasSpeed)
            animator.SetFloat(speedId, actionSpeed);

        // Moving in a standing pose = sliding: back to Locomotion
        // (never in the middle of an action - it is not cut).
        if (movement.CurrentSpeed > moveSpeed && InThreat && !IsBusy)
            ToLocomotion();
    }

    // ---------------------------------------------------------------
    // Requests.
    // ---------------------------------------------------------------
    public void Play(string action)
    {
        if (!Ready)
            return;

        int hash = Hash(action);
        if (!animator.HasState(0, hash))
        {
            Debug.LogWarning("[ThreatAnim] state '" + action + "' is not in the Threat group - rebuild it (Tools -> Bear -> Build Threat States).");
            return;
        }

        animator.CrossFadeInFixedTime(hash, blend, 0);
        requestHold = Time.time + 0.15f;
        requestedAngry = System.Array.IndexOf(AngryNames, action) >= 0;

        if (logActions)
            Debug.Log("[ThreatAnim] " + action);
    }

    // Back to walking / standing. Not while an action plays - unless
    // force (contact: he ran into the bear, the attack cannot wait).
    public void ToLocomotion(bool force = false)
    {
        if (!InThreat || (IsBusy && !force))
            return;

        animator.CrossFadeInFixedTime(locomotionHash, blend, 0);
        requestHold = 0f;

        if (logActions)
            Debug.Log("[ThreatAnim] -> Locomotion" + (force ? " (cut: contact)" : ""));
    }

    // ---------------------------------------------------------------
    // Helpers.
    // ---------------------------------------------------------------
    private int Hash(string action)
    {
        return Animator.StringToHash(prefix + action);
    }

    // The current state, or the one it blends into, is in the set.
    private bool InState(HashSet<int> set)
    {
        if (set.Contains(animator.GetCurrentAnimatorStateInfo(0).fullPathHash))
            return true;
        return animator.IsInTransition(0) && set.Contains(animator.GetNextAnimatorStateInfo(0).fullPathHash);
    }
}
