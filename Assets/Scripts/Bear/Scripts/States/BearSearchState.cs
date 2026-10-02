using UnityEngine;

public class BearSearchState : MonoBehaviour
{
    [Header("Search")]
    public float defaultSearchRadius = 5f;
    public float searchDuration = 5f;

    private BearAI bearAI;
    private BearVision vision;

    private Vector3 searchCenter;
    private float searchRadius;
    private float searchTimer;

    private bool hasSearchArea;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        searchTimer = searchDuration;
        hasSearchArea = false;

        Debug.Log("Bear entered SEARCH");
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
            Debug.Log("Bear finished searching.");

            bearAI.StateMachine.ChangeState(
                BearState.Patrol
            );
        }
    }

    public void SetSearchArea(Vector3 center, float radius)
    {
        searchCenter = center;
        searchRadius = radius;

        hasSearchArea = true;
        searchTimer = searchDuration;

        Debug.Log(
            "Bear search area set. " +
            "Center = " + searchCenter +
            ", Radius = " + searchRadius
        );
    }
}
