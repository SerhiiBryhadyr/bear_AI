using UnityEngine;
using UnityEngine.AI;

// -------------------------------------------------------------------
// BearNavMeshUtils - shared NavMesh helpers for the bear.
// "static" = no component, no object in the scene: any script just calls
// BearNavMeshUtils.TryGetRandomPoint(...).
// -------------------------------------------------------------------
public static class BearNavMeshUtils
{
    // How far a random point may be "snapped" to the nearest walkable spot.
    private const float SnapDistance = 5f;
    private const int MaxAttempts = 10;

    // ---------------------------------------------------------------
    // Finds a random walkable point inside a circle.
    // 1. Random point in the circle (center, radius), flat on X/Z.
    // 2. NavMesh.SamplePosition moves it to the nearest place the bear
    //    can walk (the random point may land in a tree, rock or water).
    // 3. Up to 10 attempts. No luck -> returns false, point = center.
    // ---------------------------------------------------------------
    public static bool TryGetRandomPoint(Vector3 center, float radius, out Vector3 point)
    {
        for (int i = 0; i < MaxAttempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SnapDistance, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }

        point = center;
        return false;
    }
}
