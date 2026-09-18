using UnityEngine;

public class BearAI : MonoBehaviour
{
    public BearStateMachine StateMachine { get; private set; }
    public BearMovement Movement { get; private set; }

    public Vector3 LastKnownPlayerPosition { get; private set; }
    public bool HasLastKnownPlayerPosition { get; private set; }

    private void Awake()
    {
        StateMachine = GetComponent<BearStateMachine>();
        Movement = GetComponent<BearMovement>();
    }

    public void SetLastKnownPlayerPosition(Vector3 position)
    {
        LastKnownPlayerPosition = position;
        HasLastKnownPlayerPosition = true;
    }
}