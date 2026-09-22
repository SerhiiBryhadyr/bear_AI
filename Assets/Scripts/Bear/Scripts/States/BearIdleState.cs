using UnityEngine;

public class BearIdleState : MonoBehaviour
{
    public float idleTime = 6.66f;

    private BearAI bearAI;
    private float timer;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
    }

    private void OnEnable()
    {
        timer = idleTime;

        if (bearAI != null && bearAI.Movement != null)
        {
            bearAI.Movement.Stop();
        }

        Debug.Log("Bear entered IDLE");
    }

    private void Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            bearAI.StateMachine.ChangeState(BearState.Patrol);
        }
    }
}