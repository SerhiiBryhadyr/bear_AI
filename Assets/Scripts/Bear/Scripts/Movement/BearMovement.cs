using UnityEngine;
using UnityEngine.AI;

public class BearMovement : MonoBehaviour
{
    private NavMeshAgent agent;

    public bool IsMoving => agent != null && agent.velocity.magnitude > 0.1f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
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
}
