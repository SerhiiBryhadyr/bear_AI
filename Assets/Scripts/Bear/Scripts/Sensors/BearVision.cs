using UnityEngine;

public class BearVision : MonoBehaviour
{
    public Transform player { get; private set; }

    public float viewDistance = 15f;

    [Range(0f, 180f)]
    public float viewAngle = 90f;

    public float eyeHeight = 1.7f;
    public float playerHeight = 1.0f;

    public LayerMask obstacleMask;

    private void Start()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
        else
        {
            Debug.LogError("BearVision: Player with tag 'Player' was not found.");
        }
    }

    public bool CanSeePlayer()
    {
        if (player == null)
            return false;

        Vector3 eyePosition = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPosition = player.position + Vector3.up * playerHeight;

        Vector3 directionToPlayer = targetPosition - eyePosition;

        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > viewDistance)
            return false;

        float angleToPlayer = Vector3.Angle(
            transform.forward,
            directionToPlayer.normalized
        );

        if (angleToPlayer > viewAngle / 2f)
            return false;

        if (Physics.Raycast(
            eyePosition,
            directionToPlayer.normalized,
            distanceToPlayer,
            obstacleMask))
        {
            return false;
        }

        return true;
    }
}