using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Lesson13_BearAlert : MonoBehaviour
{

    float alert = 0f;

    // Start is called before the first frame update
    void Start()
    {
        string[] sounds = { "Step", "Wind", "Branch", "Step" };
        float[] distances = { 12f, 2f, 15f, 30f };

        for (int i = 0; i < sounds.Length; i++)
        {
            Hear(sounds[i], distances[i]);
        }

        Debug.Log("Total alert: " + alert);
        if (IsAlarmed())
        {
            Debug.Log("Bear: something is out there!");
        }
        else
        {
            Debug.Log("Bear calm.");
        }
    }


    float Loudness(string sound)
    {
        if (sound == "Branch")
        {
            return 3f;
        }
        if (sound == "Step")
        {
            return 1f;
        }
        else
        return 0f;
    }

    void Hear(string sound, float distance)
    {
        float points = Loudness(sound);
        if (distance < 10f)
        {
            points = points * 2;
        }
        alert += points;
        Debug.Log (sound + " at " + distance + " m: +" + points + " -> alert " + alert);
    }

    bool IsAlarmed()
    {
        return alert >= 5f;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
