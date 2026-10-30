using UnityEngine;

// =====================================================================
// LineOfSight - one shared rule "how well does the eye see this point"
// (D-31: the same rules for the bear and the player).
// Used by BearVision (bear -> player) and PlayerSightOfBear
// (player -> bear).
//
// One ray from the eye to the point, hits sorted from near to far:
//   - the viewer's own colliders           -> skipped
//   - the target's colliders               -> reached: visible
//   - a collider on a foliage layer (bush) -> partial cover: every bush
//                                             keeps (1 - foliageBlock)
//                                             of what is left
//   - any other trigger (game zones)       -> skipped
//   - anything else (rock, wall, trunk)    -> hidden, 0
// Nothing in the way -> fully visible, 1.
//
// Example, foliageBlock 0.5: one bush -> 0.5, two bushes -> 0.25,
// a rock -> 0 however many bushes.
//
// "static" = no component: any script calls LineOfSight.PointVisibility(...).
// =====================================================================
public static class LineOfSight
{
    // Dense forest can give many hits on one long ray.
    private const int MaxHits = 32;
    private static readonly RaycastHit[] hits = new RaycastHit[MaxHits];

    // ---------------------------------------------------------------
    // 0..1 - how visible the point is from the eye.
    // target - whose point it is (reaching it = visible);
    // viewer - who is looking (its own colliders are ignored).
    // ---------------------------------------------------------------
    public static float PointVisibility(Vector3 eye, Vector3 point, Transform target, Transform viewer,
                                        LayerMask blockingLayers, LayerMask foliageLayers, float foliageBlock)
    {
        Vector3 direction = point - eye;
        float distance = direction.magnitude;
        if (distance < 0.01f)
            return 1f;

        // Triggers are asked for on purpose: bushes are triggers
        // (they must not stop walking, only hide).
        int mask = blockingLayers.value | foliageLayers.value;
        int count = Physics.RaycastNonAlloc(eye, direction / distance, hits, distance,
                                            mask, QueryTriggerInteraction.Collide);

        SortByDistance(count);

        float visibility = 1f;

        for (int i = 0; i < count; i++)
        {
            Collider hitCollider = hits[i].collider;
            Transform hitTransform = hitCollider.transform;

            if (viewer != null && hitTransform.IsChildOf(viewer))
                continue;

            if (target != null && hitTransform.IsChildOf(target))
                return visibility;

            if (IsInMask(hitCollider.gameObject.layer, foliageLayers))
            {
                visibility *= 1f - foliageBlock;
                continue;
            }

            if (hitCollider.isTrigger)
                continue;

            return 0f;
        }

        // The target has no collider on this ray (or no collider at all):
        // the ray reached the point itself.
        return visibility;
    }

    // ---------------------------------------------------------------
    // Raycast hits come unsorted. Few hits -> simple insertion sort.
    // ---------------------------------------------------------------
    private static void SortByDistance(int count)
    {
        for (int i = 1; i < count; i++)
        {
            RaycastHit current = hits[i];
            int j = i - 1;

            while (j >= 0 && hits[j].distance > current.distance)
            {
                hits[j + 1] = hits[j];
                j--;
            }

            hits[j + 1] = current;
        }
    }

    private static bool IsInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}
