using UnityEngine;

public class BearInvestigationSystem : MonoBehaviour
{
    [Header("Localization")]
    public float localizationRadius = 5f;

    // Point X: a random walkable point near the sound.
    // The bear does not know the exact source, only the area.
    public bool TryGetInvestigationPoint(
        Vector3 soundPosition,
        out Vector3 investigationPoint)
    {
        return BearNavMeshUtils.TryGetRandomPoint(
            soundPosition,
            localizationRadius,
            out investigationPoint
        );
    }
}