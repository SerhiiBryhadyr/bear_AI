using UnityEngine;
using UnityEngine.AI;

public class BearInvestigationSystem : MonoBehaviour
{
    [Header("Localization")]
    public float localizationRadius = 5f;

    public bool TryGetInvestigationPoint(
        Vector3 soundPosition,
        out Vector3 investigationPoint)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 randomOffset =
                Random.insideUnitCircle * localizationRadius;

            Vector3 randomPoint = new Vector3(
                soundPosition.x + randomOffset.x,
                soundPosition.y,
                soundPosition.z + randomOffset.y
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