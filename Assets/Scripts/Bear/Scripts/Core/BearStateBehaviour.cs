using UnityEngine;

// =====================================================================
// BearStateBehaviour - common base of every bear state.
//
// A state = one component on the bear (Idle, Patrol, Tracking...).
// BearStateMachine keeps exactly one of them enabled.
//
// Every state says WHICH state it is (Id). Thanks to that the machine
// finds all states by itself: a new state = a new script + a new name
// in the BearState enum, BearStateMachine is not touched.
//
// "abstract" = a template only: it cannot be added to the bear by
// itself, and every state MUST fill in Id.
// =====================================================================
public abstract class BearStateBehaviour : MonoBehaviour
{
    public abstract BearState Id { get; }

    protected BearAI bearAI;

    // Short names, so states write Knowledge.X instead of bearAI.Knowledge.X.
    protected BearMovement Movement => bearAI.Movement;
    protected BearKnowledge Knowledge => bearAI.Knowledge;

    // A state that needs its own Awake must call base.Awake() first.
    protected virtual void Awake()
    {
        bearAI = GetComponent<BearAI>();
    }

    protected void ChangeState(BearState newState)
    {
        bearAI.StateMachine.ChangeState(newState);
    }
}
