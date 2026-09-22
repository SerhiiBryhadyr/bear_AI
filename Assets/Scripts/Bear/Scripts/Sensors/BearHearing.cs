using UnityEngine;

public class BearHearing : MonoBehaviour
{
    public float hearingDistance = 10f;

    private BearAI bearAI;
    private BearInvestigationSystem investigationSystem;
    private BearInvestigateSoundState investigateSoundState;

    private void Awake()
    {
        bearAI = GetComponent<BearAI>();
        investigationSystem = GetComponent<BearInvestigationSystem>();
        investigateSoundState = GetComponent<BearInvestigateSoundState>();
    }

    public void HearSound(SoundEvent soundEvent)
    {
        float distance = Vector3.Distance(
            transform.position,
            soundEvent.Position
        );

        float effectiveHearingDistance =
            hearingDistance * soundEvent.Loudness;

        if (distance > effectiveHearingDistance)
        {
            Debug.Log(
                "Bear did NOT hear sound. " +
                "Distance = " + distance +
                ", Hearing Distance = " + effectiveHearingDistance
            );

            return;
        }

        Debug.Log(
            "Bear heard sound: " +
            "Position = " + soundEvent.Position +
            ", Type = " + soundEvent.Type +
            ", Loudness = " + soundEvent.Loudness +
            ", Distance = " + distance +
            ", Hearing Distance = " + effectiveHearingDistance
        );

        if (bearAI == null)
            return;

        if (bearAI.StateMachine.CurrentState == BearState.FollowPlayer)
            return;

        if (investigationSystem == null || investigateSoundState == null)
            return;

        if (!investigationSystem.TryGetInvestigationPoint(
                soundEvent.Position,
                out Vector3 investigationPoint))
        {
            return;
        }

        investigateSoundState.SetInvestigationPoint(investigationPoint);

        bearAI.StateMachine.ChangeState(BearState.InvestigateSound);
    }
}