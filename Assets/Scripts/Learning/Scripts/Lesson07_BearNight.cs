using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson07_BearNight : MonoBehaviour
{
    enum BearMood {Calm, Curious, Angry};
    // Start is called before the first frame update
    void Start()
    {
        float distance = 8f;
        int steps =7;
        bool seesPlayer = true;
        GameObject flashlight = null; 
        float noise = steps*2f-distance/4f;
        BearMood mood = BearMood.Calm;

        if (seesPlayer && distance < 10f)
        {
            mood = BearMood.Angry;
        }
        else if (noise > 10 || flashlight != null)
        {
            mood = BearMood.Curious;
        }
        string action = "";
        switch (mood)
        {
            case BearMood.Calm:
            action = "Patrol";
            break;
            case BearMood.Curious:
            action = "Check the noise";
            break;
            case BearMood.Angry:
            action = "Attack";
            break;
        }
        Debug.Log("Noise: " + noise+ ", Mood: " + mood + ", Action: " + action);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
