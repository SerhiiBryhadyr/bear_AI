using UnityEngine;
using UnityEngine.AI;

public class BearSearchState : MonoBehaviour
{
    [Header("Search")]
    public float defaultSearchRadius = 5f;
    public float searchDuration = 5f;

    [Header("Search Movement")]
    public float searchPointSampleDistance = 5f;

    private BearAI bearAI;
    private BearVision vision;

    private Vector3 searchCenter;
    private float searchRadius;
    private float searchTimer;

    private bool hasSearchArea;
    private bool hasSearchPoint;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        searchTimer = searchDuration;
        hasSearchPoint = false;

        Debug.Log("Bear entered SEARCH");

        if (hasSearchArea)
        {
            Debug.Log(
                "Bear search area active. " +
                "Center = " + searchCenter +
                ", Radius = " + searchRadius
            );

            ChooseNewSearchPoint();
        }
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

        if (!hasSearchArea)
            return;

        searchTimer -= Time.deltaTime;

        if (searchTimer <= 0f)
        {
            Debug.Log(
                "Bear finished searching."
            );

            hasSearchArea = false;
            hasSearchPoint = false;

            bearAI.Movement.Stop();

            bearAI.StateMachine.ChangeState(
                BearState.Patrol
            );

            return;
        }

        if (!hasSearchPoint)
        {
            ChooseNewSearchPoint();
            return;
        }

        if (bearAI.Movement.HasReachedDestination())
        {
            ChooseNewSearchPoint();
        }
    }

    public void SetSearchArea(
        Vector3 center,
        float radius)
    {
        searchCenter = center;
        searchRadius = radius;

        hasSearchArea = true;

        Debug.Log(
            "Bear search area set. " +
            "Center = " + searchCenter +
            ", Radius = " + searchRadius
        );
    }

    private void ChooseNewSearchPoint()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 randomOffset =
                Random.insideUnitCircle * searchRadius;

            Vector3 randomPoint =
                searchCenter +
                new Vector3(
                    randomOffset.x,
                    0f,
                    randomOffset.y
                );

            if (NavMesh.SamplePosition(
                randomPoint,
                out NavMeshHit hit,
                searchPointSampleDistance,
                NavMesh.AllAreas))
            {
                hasSearchPoint = true;

                bearAI.Movement.MoveTo(hit.position);

                Debug.Log(
                    "Bear search target: " +
                    hit.position
                );

                return;
            }
        }

        Debug.Log(
            "Bear could not find a valid search point."
        );

        hasSearchPoint = false;
    }
}