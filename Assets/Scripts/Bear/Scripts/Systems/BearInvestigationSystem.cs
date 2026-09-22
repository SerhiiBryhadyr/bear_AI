using UnityEngine;
using UnityEngine.AI;

public class BearInvestigationSystem : MonoBehaviour
{
    public float investigationRadius = 5f;

    public bool TryGetInvestigationPoint(
        Vector3 soundPosition,
        out Vector3 investigationPoint)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 randomCircle = Random.insideUnitCircle * investigationRadius;

            Vector3 randomPoint = new Vector3(
                soundPosition.x + randomCircle.x,
                soundPosition.y,
                soundPosition.z + randomCircle.y
            );

            if (NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                5f,
                NavMesh.AllAreas))
            {
                investigationPoint = hit.position;
                return true;
            }
        }

        investigationPoint = soundPosition;
        return false;
    }
}