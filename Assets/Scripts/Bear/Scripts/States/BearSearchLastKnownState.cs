using UnityEngine;

public class BearSearchLastKnownState : MonoBehaviour
{
    public float searchTime = 3f;

    private BearAI bearAI;
    private BearVision vision;

    private float timer;
    private bool arrived;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        arrived = false;
        timer = searchTime;

        if (bearAI.HasLastKnownPlayerPosition)
        {
            bearAI.Movement.MoveTo(bearAI.LastKnownPlayerPosition);

            Debug.Log("Bear searching last known position.");
        }
    }

    private void Update()
    {
        if (vision != null && vision.CanSeePlayer())
        {
            Debug.Log("Bear found player again!");

            bearAI.StateMachine.ChangeState(BearState.FollowPlayer);
            return;
        }

        if (!arrived)
        {
            if (bearAI.Movement.HasReachedDestination())
            {
                arrived = true;
                timer = searchTime;

                bearAI.Movement.Stop();

                Debug.Log("Bear reached last known position. Searching...");
            }

            return;
        }

        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            Debug.Log("Bear did not find player. Returning to patrol.");

            bearAI.StateMachine.ChangeState(BearState.Patrol);
        }
    }
}