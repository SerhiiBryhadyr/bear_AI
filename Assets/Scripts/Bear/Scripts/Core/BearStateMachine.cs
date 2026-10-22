using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// BearStateMachine - the switch: exactly one state is enabled.
//
// Awake: collects every BearStateBehaviour on the bear into a dictionary
//        "name -> component" and disables them all (whatever the
//        Inspector checkboxes say).
// ChangeState: disables the current state, enables the new one.
//
// The machine does not know the concrete states. A new state needs no
// changes here (the old version had three switch blocks).
//
// Runs right after BearAI and before the states, so all states are
// disabled before any of them can run OnEnable.
// =====================================================================
[DefaultExecutionOrder(-100)]
public class BearStateMachine : MonoBehaviour
{
    [Tooltip("State the bear starts in.")]
    public BearState initialState = BearState.Idle;

    public BearState CurrentState { get; private set; }

    private readonly Dictionary<BearState, BearStateBehaviour> states =
        new Dictionary<BearState, BearStateBehaviour>();

    private BearStateBehaviour current;

    // ---------------------------------------------------------------
    // Find all states on the bear.
    // ---------------------------------------------------------------
    private void Awake()
    {
        foreach (BearStateBehaviour state in GetComponents<BearStateBehaviour>())
        {
            state.enabled = false;

            if (states.ContainsKey(state.Id))
            {
                Debug.LogError("[StateMachine] Two components claim state " + state.Id +
                               ". The second one (" + state.GetType().Name + ") is ignored.");
                continue;
            }

            states.Add(state.Id, state);
        }
    }

    private void Start()
    {
        ChangeState(initialState);
    }

    // ---------------------------------------------------------------
    // Switch: old state off -> new state on.
    // Switching to the state that is already active does nothing.
    // ---------------------------------------------------------------
    public void ChangeState(BearState newState)
    {
        if (current != null && CurrentState == newState)
            return;

        if (!states.TryGetValue(newState, out BearStateBehaviour next))
        {
            Debug.LogError("[StateMachine] No component for state " + newState + " on the bear.");
            return;
        }

        BearState previous = CurrentState;
        bool isFirstState = current == null;

        if (current != null)
            current.enabled = false;

        current = next;
        CurrentState = newState;
        current.enabled = true;     // runs the new state's OnEnable

        Debug.Log(isFirstState
            ? "[StateMachine] start: " + newState
            : "[StateMachine] " + previous + " -> " + newState);
    }
}
