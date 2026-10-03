using UnityEngine;

public class BearHearing : MonoBehaviour
{
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
        Debug.Log(
            "Bear heard sound: " +
            "Position = " + soundEvent.Position +
            ", Type = " + soundEvent.Type +
            ", Loudness = " + soundEvent.Loudness
        );

        if (bearAI == null)
            return;

        if (investigationSystem == null)
            return;

        if (investigateSoundState == null)
            return;

        if (!investigationSystem.TryGetInvestigationPoint(
                soundEvent.Position,
                out Vector3 investigationPoint))
        {
            Debug.Log(
                "Bear could not find an investigation point."
            );

            return;
        }

        bearAI.StateMachine.ChangeState(
            BearState.InvestigateSound
        );

        investigateSoundState.SetInvestigationPoint(
            investigationPoint
        );

        Debug.Log(
            "Bear investigation point: " +
            investigationPoint
        );
    }
}