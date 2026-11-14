using UnityEngine;
using UnityEngine.AI;

// =====================================================================
// BearMovement - moves the bear with the NavMeshAgent: where to go,
// how fast, turning in place. States call it and never touch the
// agent directly.
//
// 14.11: the Animator moved to BearAnimator (one job per script).
// This script only says HOW the bear moves (speed, sneak); BearAnimator
// turns that into clips.
// =====================================================================
public class BearMovement : MonoBehaviour
{
    private NavMeshAgent agent;
    private float baseSpeed;          // agent speed from the Inspector

    public bool IsMoving => agent != null && agent.velocity.magnitude > 0.1f;

    // Real speed right now, m/s - not the target speed: it grows and
    // falls with the agent's Acceleration. BearAnimator picks clips by it.
    public float CurrentSpeed => agent != null ? agent.velocity.magnitude : 0f;

    // A state asked for a crouched walk (stalking). BearAnimator shows
    // it only while the bear is slow enough.
    public bool Sneaking { get; private set; }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

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

    // Exact speed in m/s (chase). SetSpeedMultiplier(1) returns to normal.
    public void SetSpeed(float metersPerSecond)
    {
        if (agent == null)
            return;

        agent.speed = metersPerSecond;
    }

    // ---------------------------------------------------------------
    // Sneak (14.11): a state turns it on when it starts and off when
    // it ends - like SetSpeedMultiplier. Only the look changes; the
    // speed is still set by the state.
    // ---------------------------------------------------------------
    public void SetSneak(bool sneak)
    {
        Sneaking = sneak;
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
}
