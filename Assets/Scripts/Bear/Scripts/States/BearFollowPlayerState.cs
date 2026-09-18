using UnityEngine;

public class BearFollowPlayerState : MonoBehaviour
{
    private BearAI bearAI;
    private BearVision vision;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        vision = GetComponent<BearVision>();
    }

    private void OnEnable()
    {
        Debug.Log("Bear entered FOLLOW PLAYER");
    }

    private void Update()
    {
        if (vision == null || vision.player == null)
            return;

        if (vision.CanSeePlayer())
        {
            // The player is visible.
            // Continuously remember the last position where the bear saw the player.
            bearAI.SetLastKnownPlayerPosition(vision.player.position);

            // Keep following the player.
            bearAI.Movement.MoveTo(vision.player.position);

            return;
        }

        // The player is no longer visible.
        Debug.Log(
            "Bear lost player. Last known position: " +
            bearAI.LastKnownPlayerPosition
        );

        bearAI.StateMachine.ChangeState(BearState.SearchLastKnown);
    }
}