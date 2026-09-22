using UnityEngine;

public class SoundEventTest : MonoBehaviour
{
    public KeyCode testKey = KeyCode.T;

    [Range(0f, 5f)]
    public float testLoudness = 1f;

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
                testLoudness
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