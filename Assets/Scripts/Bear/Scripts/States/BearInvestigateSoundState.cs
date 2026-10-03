using UnityEngine;

public class BearInvestigateSoundState : MonoBehaviour
{
    public float waitAtInvestigationPoint = 1f;

    private BearAI bearAI;
    private BearVision vision;

    private Vector3 investigationPoint;
    private bool hasInvestigationPoint;

    private bool reachedPoint;
    private float waitTimer;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        hasInvestigationPoint = false;
        reachedPoint = false;
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

        if (!hasInvestigationPoint)
            return;

        if (!reachedPoint)
        {
            if (bearAI.Movement.HasReachedDestination())
            {
                reachedPoint = true;
                waitTimer = waitAtInvestigationPoint;

                bearAI.Movement.Stop();

                Debug.Log(
                    "Bear reached investigation point."
                );
            }

            return;
        }

        waitTimer -= Time.deltaTime;

        if (waitTimer <= 0f)
        {
            Debug.Log(
                "Bear did not find player. Returning to patrol."
            );

            bearAI.StateMachine.ChangeState(
                BearState.Patrol
            );
        }
    }

    public void SetInvestigationPoint(Vector3 point)
    {
        investigationPoint = point;
        hasInvestigationPoint = true;

        reachedPoint = false;

        bearAI.Movement.MoveTo(
            investigationPoint
        );

        Debug.Log(
            "Bear investigating sound at: " +
            investigationPoint
        );
    }
}