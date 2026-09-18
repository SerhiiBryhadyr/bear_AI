using UnityEngine;

public class BearPatrolState : MonoBehaviour
{
    private BearAI bearAI;
    private BearPatrolSystem patrolSystem;
    private BearVision vision;

    private Transform currentPoint;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        patrolSystem = GetComponent<BearPatrolSystem>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        ChooseNewPoint();
    }

    private void Update()
    {
        // Check if the bear sees the player.
        if (vision != null && vision.CanSeePlayer())
        {
            Debug.Log("Bear saw player!");

            bearAI.SetLastKnownPlayerPosition(vision.player.position);
            bearAI.StateMachine.ChangeState(BearState.FollowPlayer);

            return;
        }

        if (currentPoint == null)
            return;

        if (bearAI.Movement.HasReachedDestination())
        {
            bearAI.StateMachine.ChangeState(BearState.Idle);
        }
    }

    private void ChooseNewPoint()
    {
        if (patrolSystem == null)
            return;

        currentPoint = patrolSystem.GetRandomPoint();

        if (currentPoint != null)
        {
            bearAI.Movement.MoveTo(currentPoint.position);

            Debug.Log("Bear patrol target: " + currentPoint.name);
        }
    }
}