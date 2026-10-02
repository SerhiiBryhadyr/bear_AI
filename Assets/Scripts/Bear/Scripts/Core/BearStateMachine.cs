using UnityEngine;

public class BearStateMachine : MonoBehaviour
{
    public BearState CurrentState { get; private set; }

    private BearIdleState idleState;
    private BearPatrolState patrolState;
    private BearTrackingState trackingState;
    private BearInvestigateSoundState investigateSoundState;
    private BearSearchState searchState;
    private BearSearchLastKnownState searchLastKnownState;

    private bool initialized;

    private void Awake()
    {
        idleState = GetComponent<BearIdleState>();
        patrolState = GetComponent<BearPatrolState>();
        trackingState = GetComponent<BearTrackingState>();
        investigateSoundState = GetComponent<BearInvestigateSoundState>();
        searchState = GetComponent<BearSearchState>();
        searchLastKnownState = GetComponent<BearSearchLastKnownState>();
    }

    private void Start()
    {
        ChangeState(BearState.Idle);
    }

    public void ChangeState(BearState newState)
    {
        if (initialized && CurrentState == newState)
            return;

        DisableCurrentState();

        CurrentState = newState;
        initialized = true;

        EnableNewState();

        Debug.Log("Bear state changed to: " + CurrentState);
    }

    private void DisableCurrentState()
    {
        if (!initialized)
            return;

        switch (CurrentState)
        {
            case BearState.Idle:
                if (idleState != null)
                    idleState.enabled = false;
                break;

            case BearState.Patrol:
                if (patrolState != null)
                    patrolState.enabled = false;
                break;

            case BearState.Tracking:
                if (trackingState != null)
                    trackingState.enabled = false;
                break;

            case BearState.InvestigateSound:
                if (investigateSoundState != null)
                    investigateSoundState.enabled = false;
                break;

            case BearState.Search:
                if (searchState != null)
                    searchState.enabled = false;
                break;

            case BearState.SearchLastKnown:
                if (searchLastKnownState != null)
                    searchLastKnownState.enabled = false;
                break;
        }
    }

    private void EnableNewState()
    {
        switch (CurrentState)
        {
            case BearState.Idle:
                if (idleState != null)
                    idleState.enabled = true;
                break;

            case BearState.Patrol:
                if (patrolState != null)
                    patrolState.enabled = true;
                break;

            case BearState.Tracking:
                if (trackingState != null)
                    trackingState.enabled = true;
                break;

            case BearState.InvestigateSound:
                if (investigateSoundState != null)
                    investigateSoundState.enabled = true;
                break;

            case BearState.Search:
                if (searchState != null)
                    searchState.enabled = true;
                break;

            case BearState.SearchLastKnown:
                if (searchLastKnownState != null)
                    searchLastKnownState.enabled = true;
                break;
        }
    }
}