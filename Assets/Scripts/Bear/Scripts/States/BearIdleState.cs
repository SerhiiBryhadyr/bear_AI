using UnityEngine;

// =====================================================================
// BearIdleState - mode Unknown. The bear stands for idleTime seconds,
// then goes on patrol.
// Vision is no longer checked here: BearKnowledge sees the player in
// any state, and BearBrain switches the bear to Tracking.
// =====================================================================
public class BearIdleState : BearStateBehaviour
{
    public override BearState Id => BearState.Idle;

    public float idleTime = 6.66f;

    private float timer;

    private void OnEnable()
    {
        timer = idleTime;
        Movement.Stop();

        Debug.Log("Bear entered IDLE");
    }

    private void Update()
    {
        timer -= Time.deltaTime;

        if (timer <= 0f)
            ChangeState(BearState.Patrol);
    }
}
