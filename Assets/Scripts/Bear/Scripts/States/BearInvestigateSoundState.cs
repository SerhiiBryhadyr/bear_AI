using UnityEngine;
using UnityEngine.AI;

public class BearInvestigateSoundState : MonoBehaviour
{
    [Header("First Point")]
    public float waitAtInvestigationPoint = 5f;

    [Header("Second Circle")]
    public float secondSearchRadius = 10f;
    public float waitAtSecondPoint = 5f;

    private BearAI bearAI;
    private BearVision vision;

    private Vector3 investigationPoint;
    private bool hasInvestigationPoint;

    private Vector3 secondPoint;
    private bool hasSecondPoint;

    private bool reachedFirstPoint;
    private bool reachedSecondPoint;

    private float waitTimer;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        hasInvestigationPoint = false;
        hasSecondPoint = false;

        reachedFirstPoint = false;
        reachedSecondPoint = false;

        waitTimer = 0f;

        Debug.Log("Bear entered INVESTIGATE SOUND");
    }

    private void Update()
    {
        if (vision != null && vision.CanSeePlayer())
        {
            bearAI.SetLastKnownPlayerPosition(
                vision.player.position
            );

            bearAI.StateMachine.ChangeState(
                BearState.Tracking
            );

            return;
        }

        // Перша точка X
        if (!reachedFirstPoint)
        {
            if (!hasInvestigationPoint)
                return;

            if (bearAI.Movement.HasReachedDestination())
            {
                reachedFirstPoint = true;
                waitTimer = waitAtInvestigationPoint;

                bearAI.Movement.Stop();

                Debug.Log(
                    "Bear reached first investigation point X. Idle for 5 seconds."
                );
            }

            return;
        }

        // Idle на точці X
        if (reachedFirstPoint && !hasSecondPoint)
        {
            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f)
            {
                if (TryGetSecondPoint(out Vector3 point))
                {
                    secondPoint = point;
                    hasSecondPoint = true;

                    bearAI.Movement.MoveTo(secondPoint);

                    Debug.Log(
                        "Second circle point Y: " +
                        secondPoint
                    );
                }
            }

            return;
        }

        // Рух до другої точки Y
        if (!reachedSecondPoint)
        {
            if (bearAI.Movement.HasReachedDestination())
            {
                reachedSecondPoint = true;
                waitTimer = waitAtSecondPoint;

                bearAI.Movement.Stop();

                Debug.Log(
                    "Bear reached second point Y. Idle for 5 seconds."
                );
            }

            return;
        }

        // Idle на Y
        waitTimer -= Time.deltaTime;

        if (waitTimer <= 0f)
        {
            Debug.Log(
                "Bear finished second investigation point."
            );

            // Поки що просто залишаємося в цьому стані.
            waitTimer = float.PositiveInfinity;
        }
    }

    public void SetInvestigationPoint(Vector3 point)
    {
        investigationPoint = point;
        hasInvestigationPoint = true;

        reachedFirstPoint = false;
        reachedSecondPoint = false;
        hasSecondPoint = false;

        bearAI.Movement.MoveTo(
            investigationPoint
        );

        Debug.Log(
            "Bear investigating sound at X: " +
            investigationPoint
        );
    }

    private bool TryGetSecondPoint(out Vector3 point)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 randomOffset =
                Random.insideUnitCircle * secondSearchRadius;

            Vector3 randomPoint =
                investigationPoint +
                new Vector3(
                    randomOffset.x,
                    0f,
                    randomOffset.y
                );

            if (NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                5f,
                NavMesh.AllAreas))
            {
                point = hit.position;

                Debug.Log(
                    "Second circle center X: " +
                    investigationPoint +
                    " | Radius: " +
                    secondSearchRadius
                );

                return true;
            }
        }

        point = investigationPoint;

        Debug.Log(
            "Could not find valid point inside second circle."
        );

        return false;
    }
}