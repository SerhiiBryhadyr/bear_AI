using UnityEngine;

// =====================================================================
// BearAI - the bear's "passport".
// Finds the main bear components once, so every state and system takes
// them from here (bearAI.Movement, bearAI.Knowledge...).
// Runs before all other bear scripts, so the references are ready
// before anyone uses them.
// =====================================================================
[DefaultExecutionOrder(-200)]
public class BearAI : MonoBehaviour
{
    public BearStateMachine StateMachine { get; private set; }
    public BearMovement Movement { get; private set; }
    public BearKnowledge Knowledge { get; private set; }

    private void Awake()
    {
        StateMachine = GetComponent<BearStateMachine>();
        Movement = GetComponent<BearMovement>();
        Knowledge = GetComponent<BearKnowledge>();

        if (StateMachine == null) Debug.LogError("BearAI: BearStateMachine is missing on the bear.");
        if (Movement == null) Debug.LogError("BearAI: BearMovement is missing on the bear.");
        if (Knowledge == null) Debug.LogError("BearAI: BearKnowledge is missing on the bear.");
    }
}
