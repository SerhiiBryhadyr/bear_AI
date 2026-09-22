using UnityEngine;

public class SoundEventTest : MonoBehaviour
{
    public KeyCode testKey = KeyCode.T;

    private BearHearing bearHearing;

    private void Start()
    {
        bearHearing = FindFirstObjectByType<BearHearing>();

        if (bearHearing == null)
        {
            Debug.LogError("SoundEventTest: BearHearing was not found.");
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(testKey))
        {
            SoundEvent soundEvent = new SoundEvent(
                transform.position,
                SoundType.Footstep,
                1f
            );

            Debug.Log(
                "Sound Event: " +
                "Position = " + soundEvent.Position +
                ", Type = " + soundEvent.Type +
                ", Loudness = " + soundEvent.Loudness
            );

            if (bearHearing != null)
            {
                bearHearing.HearSound(soundEvent);
            }
        }
    }
}