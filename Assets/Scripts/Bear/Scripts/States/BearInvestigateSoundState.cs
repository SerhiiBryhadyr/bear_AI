using UnityEngine;

public class BearInvestigateSoundState : MonoBehaviour
{
    private BearAI bearAI;
    private BearVision vision;

    private Vector3 investigationPoint;
    private bool hasInvestigationPoint;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        hasInvestigationPoint = false;

        Debug.Log("Bear entered INVESTIGATE SOUND");
    }

    private void Update()
    {
        if (vision != null && vision.CanSeePlayer())
        {
            bearAI.SetLastKnownPlayerPosition(vision.player.position);
            bearAI.StateMachine.ChangeState(BearState.FollowPlayer);

            return;
        }

        if (!hasInvestigationPoint)
            return;

        if (bearAI.Movement.HasReachedDestination())
        {
            bearAI.Movement.Stop();

            Debug.Log("Bear reached investigation point.");
        }
    }

    public void SetInvestigationPoint(Vector3 point)
    {
        investigationPoint = point;
        hasInvestigationPoint = true;

        bearAI.Movement.MoveTo(investigationPoint);

        Debug.Log(
            "Bear investigating sound at: " +
            investigationPoint
        );
    }
}