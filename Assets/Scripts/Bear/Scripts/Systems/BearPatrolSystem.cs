using UnityEngine;

public class BearPatrolSystem : MonoBehaviour
{
    public Transform[] patrolPoints;

    public Transform GetRandomPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
            return null;

        int randomIndex = Random.Range(0, patrolPoints.Length);

        return patrolPoints[randomIndex];
    }
}