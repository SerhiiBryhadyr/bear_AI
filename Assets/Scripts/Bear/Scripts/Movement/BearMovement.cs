using UnityEngine;
using UnityEngine.AI;

public class BearMovement : MonoBehaviour
{
    private NavMeshAgent agent;

    private Animator animator;
    private float baseSpeed;          // agent speed from the Inspector

    public bool IsMoving => agent != null && agent.velocity.magnitude > 0.1f;

    private void Awake()
{
    agent = GetComponent<NavMeshAgent>();
    animator = GetComponent<Animator>();

    if (agent != null)
        baseSpeed = agent.speed;
}

    public void MoveTo(Vector3 position)
    {
        if (agent == null)
            return;

        if (!agent.isOnNavMesh)
            return;

        agent.isStopped = false;
        agent.SetDestination(position);
    }

    public void Stop()
    {
        if (agent == null)
            return;

        agent.isStopped = true;
        agent.ResetPath();
    }

    public bool HasReachedDestination()
    {
        if (agent == null)
            return false;

        if (agent.pathPending)
            return false;

        return agent.remainingDistance <= agent.stoppingDistance + 0.1f;
    }

    // ---------------------------------------------------------------
    // Speed: 1 = normal (Inspector value), 0.5 = slow walk.
    // States that slow the bear down reset it to 1 when they end.
    // ---------------------------------------------------------------
    public void SetSpeedMultiplier(float multiplier)
    {
        if (agent == null)
            return;

        agent.speed = baseSpeed * multiplier;
    }

    // ---------------------------------------------------------------
    // Turning in place. NavMeshAgent turns the bear only while walking,
    // so standing reactions (stare, after retreat) use these.
    // ---------------------------------------------------------------
    public void FaceTowards(Vector3 point, float degreesPerSecond)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            degreesPerSecond * Time.deltaTime
        );
    }

    public bool IsFacing(Vector3 point, float maxAngle)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
            return true;

        return Vector3.Angle(transform.forward, direction) <= maxAngle;
    }

    private void Update()
{
    if (animator != null)
        animator.SetBool("IsMoving", IsMoving);
}
}
