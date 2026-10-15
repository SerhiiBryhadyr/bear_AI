using UnityEngine;

public class BearIdleState : MonoBehaviour
{
    public float idleTime = 6.66f;

    private BearAI bearAI;
    private BearVision vision;
    private float timer;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
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
        // The bear is not blind while resting (bug from 13.10).
        if (vision != null && vision.CanSeePlayer())
        {
            Debug.Log("Bear saw player while idle!");

            bearAI.SetLastKnownPlayerPosition(vision.player.position);
            bearAI.StateMachine.ChangeState(BearState.Tracking);

            return;
        }

        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            bearAI.StateMachine.ChangeState(BearState.Patrol);
        }
    }
}