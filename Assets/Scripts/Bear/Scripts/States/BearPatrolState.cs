using UnityEngine;

// =====================================================================
// BearPatrolState - mode Unknown. Walks to a random patrol point,
// on arrival -> Idle.
// =====================================================================
public class BearPatrolState : BearStateBehaviour
{
    public override BearState Id => BearState.Patrol;

    private BearPatrolSystem patrolSystem;
    private Transform currentPoint;

    protected override void Awake()
    {
        base.Awake();
        patrolSystem = GetComponent<BearPatrolSystem>();
    }

    private void OnEnable()
    {
        ChooseNewPoint();
    }

    private void Update()
    {
        if (currentPoint == null)
            return;

        if (Movement.HasReachedDestination())
            ChangeState(BearState.Idle);
    }

    private void ChooseNewPoint()
    {
        currentPoint = patrolSystem != null ? patrolSystem.GetRandomPoint() : null;

        if (currentPoint == null)
        {
            Debug.LogWarning("BearPatrolState: no patrol points - the bear stays where it is.");
            return;
        }

        Movement.MoveTo(currentPoint.position);
        Debug.Log("Bear patrol target: " + currentPoint.name);
    }
}
