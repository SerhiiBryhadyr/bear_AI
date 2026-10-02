using UnityEngine;

public class PlayerAwareness : MonoBehaviour
{
    public Vector3 CurrentPosition => transform.position;
    public Vector3 Velocity { get; private set; }
    public Vector3 Direction { get; private set; }
    public float Speed { get; private set; }

    private Vector3 lastPosition;

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 movement = transform.position - lastPosition;

        Velocity = movement / Mathf.Max(Time.deltaTime, 0.0001f);

        Vector3 horizontalVelocity = new Vector3(
            Velocity.x,
            0f,
            Velocity.z
        );

        Speed = horizontalVelocity.magnitude;

        if (Speed > 0.05f)
            Direction = horizontalVelocity.normalized;
        else
            Direction = Vector3.zero;

        lastPosition = transform.position;
    }
}