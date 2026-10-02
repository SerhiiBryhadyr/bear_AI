using UnityEngine;

public class BearTrackingState : MonoBehaviour
{
    [Header("Tracking")]
    public float predictionTime = 0.6f;
    public float decisionInterval = 0.3f;

    private BearAI bearAI;
    private BearVision vision;
    private PlayerAwareness playerAwareness;

    private float decisionTimer;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        decisionTimer = 0f;

        if (vision != null && vision.player != null)
        {
            playerAwareness =
                vision.player.GetComponent<PlayerAwareness>();
        }

        Debug.Log("Bear entered TRACKING");
    }

    private void Update()
    {
        if (vision == null || vision.player == null)
            return;

        if (playerAwareness == null)
        {
            playerAwareness =
                vision.player.GetComponent<PlayerAwareness>();

            if (playerAwareness == null)
            {
                Debug.LogError(
                    "BearTrackingState: PlayerAwareness was not found on Player."
                );

                return;
            }
        }

        if (!vision.CanSeePlayer())
        {
            Debug.Log(
                "Bear lost player. Last known position: " +
                bearAI.LastKnownPlayerPosition
            );

            bearAI.StateMachine.ChangeState(
                BearState.SearchLastKnown
            );

            return;
        }

        bearAI.SetLastKnownPlayerPosition(
            playerAwareness.CurrentPosition
        );

        decisionTimer -= Time.deltaTime;

        if (decisionTimer > 0f)
            return;

        decisionTimer = decisionInterval;

        Vector3 trackingTarget =
            playerAwareness.CurrentPosition +
            playerAwareness.Velocity * predictionTime;

        bearAI.Movement.MoveTo(trackingTarget);

        Debug.Log(
            "Tracking target: " +
            trackingTarget +
            " | Player speed: " +
            playerAwareness.Speed
        );
    }
}