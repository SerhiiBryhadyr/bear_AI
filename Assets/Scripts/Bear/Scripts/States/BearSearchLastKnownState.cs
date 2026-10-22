using UnityEngine;

// =====================================================================
// BearSearchLastKnownState - mode Unknown, right after losing the
// player. Walks to the last known point (from BearKnowledge), waits
// searchTime seconds, then -> Patrol.
// Sees or hears the player close again -> BearKnowledge switches to
// Knows and BearBrain takes the bear back to Tracking.
// =====================================================================
public class BearSearchLastKnownState : BearStateBehaviour
{
    public override BearState Id => BearState.SearchLastKnown;

    public float searchTime = 3f;

    private float timer;
    private bool arrived;

    private void OnEnable()
    {
        arrived = false;
        timer = searchTime;

        if (!Knowledge.HasLastKnownPosition)
        {
            Debug.Log("Bear has no last known position. Returning to patrol.");
            ChangeState(BearState.Patrol);
            return;
        }

        Movement.MoveTo(Knowledge.LastKnownPosition);
        Debug.Log("Bear searching last known position: " + Knowledge.LastKnownPosition);
    }

    private void Update()
    {
        // 1. Walk to the point.
        if (!arrived)
        {
            if (Movement.HasReachedDestination())
            {
                arrived = true;
                timer = searchTime;
                Movement.Stop();

                Debug.Log("Bear reached last known position. Searching...");
            }

            return;
        }

        // 2. Look around, then give up.
        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            Debug.Log("Bear did not find player. Returning to patrol.");
            ChangeState(BearState.Patrol);
        }
    }
}
