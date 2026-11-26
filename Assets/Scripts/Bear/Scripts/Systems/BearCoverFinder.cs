using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearCoverFinder - where to run to disappear from the player (25.11,
// D-77). Used when the face-off ends (instead of "walks away for show")
// and when the bear backs off unseen in a close encounter.
//
// Not "the nearest bush": a lone bush or rock is a bad hiding place -
// the bear's back or ears stick out and it looks silly. A place counts
// only when the AREA around it hides the bear: the point itself and 4
// points around it (neighbourRadius) are checked, so a thicket or the
// edge of the forest wins, a single bush does not.
//
// The bear stands FRONT-ON to the player there (it faces him and
// watches): head, chest and rear are on one line from him, so one dense
// bush in front can hide all of it. Rays from his eyes - the same rules
// as the eyes of both (LineOfSight: open 1, a bush 0.5, a rock 0).
//
// Candidates: rings around the bear, on the side away from the player
// (+-angleSpread), not closer than minFromPlayer to him, reachable on
// the NavMesh. Best = most hidden, then the nearest.
// No such place -> false: the caller decides (the face-off stays and
// presses him to leave, 25.11).
// =====================================================================
public class BearCoverFinder : MonoBehaviour
{
    [Header("Where to look")]
    [Tooltip("Not farther from the bear than this (m).")]
    public float maxDistance = 35f;
    [Tooltip("Not closer to the bear than this (m) - a step aside is not hiding.")]
    public float minDistance = 5f;
    [Tooltip("Not closer to the player than this (m).")]
    public float minFromPlayer = 15f;
    [Tooltip("Candidates within this angle (deg, each side) of the direction away from the player.")]
    public float angleSpread = 110f;
    [Tooltip("Rings x directions = candidates.")]
    public int rings = 5;
    public int directions = 9;

    [Header("What counts as cover")]
    [Tooltip("The point itself must hide at least this share of the bear (0-1).")]
    [Range(0f, 1f)] public float pointHidden = 0.85f;
    [Tooltip("...and the area around it (the point + 4 around) - this much on average (0-1).")]
    [Range(0f, 1f)] public float areaHidden = 0.7f;
    [Tooltip("The area: 4 points this far (m) around the candidate.")]
    public float neighbourRadius = 2.5f;

    [Header("Debug")]
    public bool logSearch = true;

    // Last result (for logs and gizmos).
    public Vector3 LastPoint { get; private set; }
    public float LastHidden { get; private set; }

    private BearVision vision;
    private BearBodyPoints body;

    private void Awake()
    {
        vision = GetComponent<BearVision>();
        body = GetComponent<BearBodyPoints>();
    }

    // ---------------------------------------------------------------
    // The best hiding place from a player standing at "player".
    // distanceWeight: how much a metre farther costs (score = area hidden
    // - distanceWeight x metres): 0.01 - the most hidden wins; 0.03 - the
    // nearest good one wins (first sight, D-83).
    // ---------------------------------------------------------------
    public bool Find(Vector3 player, out Vector3 best, float distanceWeight = 0.01f)
    {
        best = transform.position;
        if (vision == null)
            return false;

        Vector3 away = Flat(transform.position - player);
        if (away.sqrMagnitude < 0.01f)
            away = transform.forward;
        away.Normalize();

        float bestScore = float.NegativeInfinity;
        float bestHidden = 0f;
        int checkedCount = 0, goodCount = 0;
        NavMeshPath path = new NavMeshPath();

        for (int r = 0; r < rings; r++)
        {
            float distance = Mathf.Lerp(minDistance, maxDistance, rings > 1 ? (float)r / (rings - 1) : 1f);

            for (int d = 0; d < directions; d++)
            {
                float angle = Mathf.Lerp(-angleSpread, angleSpread, directions > 1 ? (float)d / (directions - 1) : 0.5f);
                Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * away * distance;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    continue;
                candidate = hit.position;
                if (FlatDistance(candidate, player) < minFromPlayer)
                    continue;

                checkedCount++;

                // 1. The point itself (cheap) - most candidates stop here.
                float hidden = 1f - VisibleFrontOn(candidate, player);
                if (hidden < pointHidden)
                    continue;

                // 2. The area around it: a lone bush does not count.
                float area = AreaHidden(candidate, player, hidden);
                if (area < areaHidden)
                    continue;

                // 3. It can get there.
                if (!NavMesh.CalculatePath(transform.position, candidate, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    continue;

                goodCount++;
                float score = area - distanceWeight * FlatDistance(candidate, transform.position);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestHidden = area;
                    best = candidate;
                }
            }
        }

        bool found = goodCount > 0;
        LastPoint = best;
        LastHidden = found ? bestHidden : 0f;

        if (logSearch)
            Debug.Log("[Cover] " + (found
                ? "found: " + FlatDistance(best, transform.position).ToString("F0") + " m from the bear, " +
                  FlatDistance(best, player).ToString("F0") + " m from him, area hidden " + Mathf.RoundToInt(bestHidden * 100f) +
                  "% (" + goodCount + " good of " + checkedCount + " checked)"
                : "NONE within " + maxDistance.ToString("F0") + " m (" + checkedCount + " checked) - no thicket around"));

        return found;
    }

    // The point and 4 around it (along and across the line to him), average.
    private float AreaHidden(Vector3 point, Vector3 player, float centre)
    {
        Vector3 toPlayer = Flat(player - point).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, toPlayer);
        Vector3[] around = { side, -side, toPlayer, -toPlayer };

        float sum = centre;
        foreach (Vector3 offset in around)
        {
            Vector3 p = point + offset * neighbourRadius;
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                p = hit.position;
            sum += 1f - VisibleFrontOn(p, player);
        }
        return sum / 5f;
    }

    // How much of a bear standing here FRONT-ON to him he could see:
    // head (toward him), chest, rear - 3 rays from his eyes, average.
    private float VisibleFrontOn(Vector3 point, Vector3 player)
    {
        Vector3 eye = player + Vector3.up * 1.6f;
        Vector3 toPlayer = Flat(player - point);
        Vector3 along = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;

        float half = body != null && body.Active ? body.HalfLength : 1.2f;
        float eyeHeight = body != null && body.Active ? body.EyeHeight : 1.6f;
        float bodyHeight = body != null && body.Active ? body.BodyHeight : 1.3f;

        Vector3 chest = point + Vector3.up * bodyHeight;
        Vector3 head = point + along * half + Vector3.up * eyeHeight;
        Vector3 rear = point - along * half + Vector3.up * bodyHeight;

        float sum = Ray(eye, head) + Ray(eye, chest) + Ray(eye, rear);
        return sum / 3f;
    }

    private float Ray(Vector3 eye, Vector3 bearPoint)
    {
        return LineOfSight.PointVisibility(eye, bearPoint, transform, vision.player,
                                           vision.blockingLayers, vision.foliageLayers, vision.foliageBlock);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // Selected bear in Play: green sphere - the last hiding place found.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || LastHidden <= 0f)
            return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(LastPoint, 1f);
    }
}
