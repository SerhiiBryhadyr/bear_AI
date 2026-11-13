using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearAreaSearch - the bear checks the area where it lost the player
// (D-52, 11.11). It only walks and looks around; what seeing or
// hearing the player means is decided by the state that uses it (now
// pattern 1, Medium; later InvestigateSound can use it too).
//
//   1. the first point (where it saw him last, or a sound) - looks around;
//   2. X, Y, Z: random walkable points within searchRadius of the first
//      point - looks around at each;
//   3. done -> IsDone (the state decides what next).
// Looking around: stands lookTime seconds and turns to new directions;
// three variants in turn (rears up, turns its head, sniffs) - logs until
// the animations exist (D-08).
// Redirect(): a quiet sound -> goes there at once (not more often than
// redirectCooldown); the remaining points stay.
// The state calls Begin(), then Tick() every frame, Stop() when it ends.
// =====================================================================
public class BearAreaSearch : MonoBehaviour
{
    [Tooltip("X, Y, Z: random points this far (m) from the first point.")]
    public float searchRadius = 10f;
    [Tooltip("Points after the first one (3 = X, Y, Z).")]
    public int extraPoints = 3;
    [Tooltip("Looks around this long (s) at every point, random in this range.")]
    public Vector2 lookTime = new Vector2(2f, 4f);
    [Tooltip("Turns to a new direction this often (s) while looking around.")]
    public float glanceInterval = 1.2f;
    [Tooltip("Walking speed while searching (x the normal speed).")]
    public float speedMultiplier = 0.8f;
    [Tooltip("Gives up a point it cannot reach: the time to walk there x1.5 + 5 s, but not less than this (s). Fix 11.11: a flat 15 s stopped it 16 m short of a far point.")]
    public float walkTimeout = 15f;
    [Tooltip("A new sound redirects the search not more often than this (s).")]
    public float redirectCooldown = 2f;
    public float turnSpeed = 90f;

    // Placeholders for the animations (D-08): used one after another.
    private static readonly string[] lookVariants =
    {
        "rears up on its hind legs, looks around",
        "turns its head left and right, listens",
        "sniffs the air"
    };

    private enum Step { Idle, Walking, Looking, Done }

    public bool IsDone => step == Step.Done;
    public bool IsActive => step == Step.Walking || step == Step.Looking;

    private BearMovement movement;
    private NavMeshAgent agent;
    private Step step = Step.Idle;
    private Vector3 center;             // the first point - X, Y, Z are around it
    private Vector3 target;
    private string targetName = "";
    private int pointsLeft;
    private int pointNumber;            // 0 - the first point, 1..3 - X, Y, Z
    private int variant;
    private float walkEnd;
    private float lookEnd;
    private float nextGlance;
    private Vector3 glanceDirection;
    private float lastRedirect = -999f;

    private void Awake()
    {
        movement = GetComponent<BearMovement>();
        agent = GetComponent<NavMeshAgent>();

        if (movement == null)
            Debug.LogError("BearAreaSearch: BearMovement is missing on the bear.");
    }

    // ---------------------------------------------------------------
    // Called by the state.
    // ---------------------------------------------------------------
    public void Begin(Vector3 firstPoint, string firstName)
    {
        center = Snap(firstPoint);
        pointsLeft = extraPoints;
        pointNumber = 0;
        variant = 0;
        lastRedirect = -999f;
        GoTo(center, firstName);
    }

    public void Stop()
    {
        step = Step.Idle;
    }

    // A sound -> goes there now; the points left stay.
    public void Redirect(Vector3 point, string why)
    {
        if (!IsActive || Time.time - lastRedirect < redirectCooldown)
            return;

        lastRedirect = Time.time;
        GoTo(Snap(point), why);
    }

    // Every frame while the state searches.
    public void Tick()
    {
        if (movement == null)
        {
            step = Step.Done;
            return;
        }

        // Walks to the point (or gives up after walkTimeout).
        if (step == Step.Walking)
        {
            if (movement.HasReachedDestination() || Time.time >= walkEnd)
                StartLooking();
            return;
        }

        if (step != Step.Looking)
            return;

        // Looks around: a new direction every glanceInterval seconds.
        if (Time.time >= nextGlance)
        {
            Vector2 random = Random.insideUnitCircle.normalized;
            glanceDirection = new Vector3(random.x, 0f, random.y);
            nextGlance = Time.time + glanceInterval;
        }

        movement.FaceTowards(transform.position + glanceDirection * 5f, turnSpeed);

        if (Time.time >= lookEnd)
            NextPoint();
    }

    // ---------------------------------------------------------------
    // Steps.
    // ---------------------------------------------------------------
    private void GoTo(Vector3 point, string name)
    {
        target = point;
        targetName = name;
        step = Step.Walking;

        movement.SetSpeedMultiplier(speedMultiplier);
        movement.MoveTo(point);

        // Time to get there at the search speed, with a margin.
        float speed = agent != null ? Mathf.Max(agent.speed, 0.5f) : 1f;
        float walkTime = Vector3.Distance(transform.position, point) / speed * 1.5f + 5f;
        walkEnd = Time.time + Mathf.Max(walkTimeout, walkTime);
        Debug.Log("[Search] goes to " + name + ", " + Vector3.Distance(transform.position, point).ToString("F0") + " m");
    }

    private void StartLooking()
    {
        step = Step.Looking;
        movement.Stop();
        lookEnd = Time.time + Random.Range(lookTime.x, lookTime.y);
        nextGlance = 0f;

        Debug.Log("[Search] at " + targetName + ": " + lookVariants[variant % lookVariants.Length] +
                  " (placeholder), " + (lookEnd - Time.time).ToString("F1") + " s");
        variant++;
    }

    private void NextPoint()
    {
        if (pointsLeft <= 0)
        {
            step = Step.Done;
            Debug.Log("[Search] nothing found");
            return;
        }

        pointsLeft--;
        pointNumber++;
        GoTo(RandomPointNear(center), "point " + PointName(pointNumber));
    }

    private static string PointName(int number)
    {
        return number >= 1 && number <= 3 ? "XYZ"[number - 1].ToString() : number.ToString();
    }

    // A random walkable point within searchRadius of the center, at least
    // 3 m from the bear (closer it would only turn in place).
    private Vector3 RandomPointNear(Vector3 around)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 random = Random.insideUnitCircle * searchRadius;
            Vector3 candidate = around + new Vector3(random.x, 0f, random.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas) &&
                Vector3.Distance(hit.position, transform.position) >= 3f)
                return hit.position;
        }

        return around;
    }

    private static Vector3 Snap(Vector3 point)
    {
        return NavMesh.SamplePosition(point, out NavMeshHit hit, 4f, NavMesh.AllAreas) ? hit.position : point;
    }

    // Selected bear in Play: orange - the point it walks to, the circle
    // where X, Y, Z are picked.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !IsActive)
            return;

        Gizmos.color = new Color(1f, 0.6f, 0f);
        Gizmos.DrawWireSphere(target, 0.8f);
        Gizmos.DrawLine(transform.position, target);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.35f);
        Gizmos.DrawWireSphere(center, searchRadius);
    }
}
