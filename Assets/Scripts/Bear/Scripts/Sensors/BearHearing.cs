using UnityEngine;

public class BearHearing : MonoBehaviour
{
    public void HearSound(SoundEvent soundEvent)
    {
        Debug.Log(
            "Bear heard sound: " +
            "Position = " + soundEvent.Position +
            ", Type = " + soundEvent.Type +
            ", Loudness = " + soundEvent.Loudness
        );
    }
}
