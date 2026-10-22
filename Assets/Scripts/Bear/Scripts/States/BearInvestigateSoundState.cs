using UnityEngine;

// =====================================================================
// BearInvestigateSoundState - mode Unknown. The bear heard a distant
// sound and checks the area (canon 04, InvestigateSound).
//   X - point near the sound (given by BearBrain), wait;
//   Y, Z - random points in the second circle around X, wait at each;
//   then -> Patrol.
// Sees the player or hears him close -> BearKnowledge switches to
// Knows and BearBrain takes the bear to Tracking (no vision check here).
// =====================================================================
public class BearInvestigateSoundState : BearStateBehaviour
{
    public override BearState Id => BearState.InvestigateSound;

    [Header("First Point")]
    public float waitAtInvestigationPoint = 5f;

    [Header("Second Circle")]
    public float secondSearchRadius = 10f;
    public float waitAtSecondPoint = 5f;

    private Vector3 investigationPoint;
    private bool hasInvestigationPoint;

    private Vector3 secondPoint;
    private bool hasSecondPoint;

    private Vector3 thirdPoint;
    private bool hasThirdPoint;

    private bool reachedFirstPoint;
    private bool reachedSecondPoint;
    private bool reachedThirdPoint;

    private float waitTimer;

    private void OnEnable()
    {
        hasInvestigationPoint = false;
        hasSecondPoint = false;
        hasThirdPoint = false;

        reachedFirstPoint = false;
        reachedSecondPoint = false;
        reachedThirdPoint = false;

        waitTimer = 0f;

        Debug.Log("Bear entered INVESTIGATE SOUND");
    }

    private void Update()
    {
        // --- Walk to X ---
        if (!reachedFirstPoint)
        {
            if (!hasInvestigationPoint)
                return;

            if (Movement.HasReachedDestination())
            {
                reachedFirstPoint = true;
                waitTimer = waitAtInvestigationPoint;
                Movement.Stop();

                Debug.Log("Bear reached first investigation point X. Waiting " + waitAtInvestigationPoint + " s.");
            }

            return;
        }

        // --- Wait at X, then go to Y ---
        if (!hasSecondPoint)
        {
            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f && TryGetSecondPoint(out Vector3 point))
            {
                secondPoint = point;
                hasSecondPoint = true;
                Movement.MoveTo(secondPoint);

                Debug.Log("Second circle point Y: " + secondPoint);
            }

            return;
        }

        // --- Walk to Y ---
        if (!reachedSecondPoint)
        {
            if (Movement.HasReachedDestination())
            {
                reachedSecondPoint = true;
                waitTimer = waitAtSecondPoint;
                Movement.Stop();

                Debug.Log("Bear reached second point Y. Waiting " + waitAtSecondPoint + " s.");
            }

            return;
        }

        // --- Wait at Y, then go to Z ---
        if (!hasThirdPoint)
        {
            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f && TryGetSecondPoint(out Vector3 point))
            {
                thirdPoint = point;
                hasThirdPoint = true;
                Movement.MoveTo(thirdPoint);

                Debug.Log("Second circle point Z: " + thirdPoint);
            }

            return;
        }

        // --- Walk to Z ---
        if (!reachedThirdPoint)
        {
            if (Movement.HasReachedDestination())
            {
                reachedThirdPoint = true;
                waitTimer = waitAtSecondPoint;
                Movement.Stop();

                Debug.Log("Bear reached third point Z. Waiting " + waitAtSecondPoint + " s.");
            }

            return;
        }

        // --- Wait at Z, then give up ---
        waitTimer -= Time.deltaTime;

        if (waitTimer <= 0f)
        {
            Debug.Log("Bear finished second circle search. Returning to patrol.");
            ChangeState(BearState.Patrol);
        }
    }

    // ---------------------------------------------------------------
    // Called by BearBrain right after switching to this state.
    // ---------------------------------------------------------------
    public void SetInvestigationPoint(Vector3 point)
    {
        investigationPoint = point;
        hasInvestigationPoint = true;

        reachedFirstPoint = false;
        reachedSecondPoint = false;
        reachedThirdPoint = false;

        hasSecondPoint = false;
        hasThirdPoint = false;

        Movement.MoveTo(investigationPoint);

        Debug.Log("Bear investigating sound at X: " + investigationPoint);
    }

    // Points Y and Z: random walkable points in the second circle around X.
    private bool TryGetSecondPoint(out Vector3 point)
    {
        if (BearNavMeshUtils.TryGetRandomPoint(investigationPoint, secondSearchRadius, out point))
            return true;

        Debug.Log("Could not find valid point inside second circle.");
        return false;
    }
}
